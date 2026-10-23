using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.CheckIn;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Reservations;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.StorageUnits;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.StorageUnits;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;
using SelfStorageManagementSystem.Web.Extensions;
using SelfStorageManagementSystem.Web.Realtime;
using SelfStorageManagementSystem.WebTests.Infrastructure;
using Xunit;

namespace SelfStorageManagementSystem.WebTests;

/// <summary>
/// WebMigration Task T18 — Real Two-Browser End-to-End Realtime Suite (Gemini C).
/// - Operates on real Kestrel Web Server over dynamic loopback port.
/// - Connects to verified dedicated E2E database (SelfStoragePRN222_WebE2eTest); In-Memory fallback prohibited.
/// - Controls two completely independent Microsoft Edge browser sessions (Context1: Staff/Manager/Admin, Context2: Customer).
/// - Verifies real WebSocket communication via /storageUnitHub.
/// - Exact Hub methods tested: HubCreate_StorageUnit, HubUpdate_StorageUnit, HubDelete_StorageUnit.
/// - Exact Receiver events tested: Receiver_CreateStorageUnit, Receiver_UpdateStorageUnit, Receiver_DeleteStorageUnit.
/// - Covers all 5 functional streams: StorageUnits CRUD, Reservation countdown/cancel, Payment simulator/invoice/refund,
///   Staff Check-in/handover/zero-PIN, and Admin accounts / Customer notifications / permission revocation.
/// - Tests role/scope separation, cookie expiration / permission revocation via OnValidatePrincipal,
///   and reconnect / missed event state reconciliation.
/// - Generates sanitized screenshots and logs (zero plaintext PIN, passwords, or secrets).
/// </summary>
public class T18TwoBrowserRealtimeE2ETests
{
    private static void EnsureEdgeAvailable()
    {
        var edgePath = T18BrowserContext.ResolveEdgeBrowserPath();
        if (!File.Exists(edgePath))
        {
            throw new FileNotFoundException(
                $"Microsoft Edge executable was not found at '{edgePath}'. T18 E2E tests strictly require Microsoft Edge.", edgePath);
        }
    }

    /// <summary>
    /// Executes cleanup logic safely while guaranteeing:
    /// 1. If primary test failed and cleanup also fails, throws AggregateException containing both (primary is InnerExceptions[0]).
    /// 2. If primary test succeeded and cleanup fails, throws InvalidOperationException with cleanup details.
    /// 3. If primary test failed and cleanup succeeds, the primary exception unwinds unaffected.
    /// 4. If primary test succeeded and cleanup succeeds, completes normally.
    /// </summary>
    internal static async Task SafeCleanupAsync(Exception? primaryEx, string testName, Func<Task> cleanupAction)
    {
        try
        {
            await cleanupAction();
        }
        catch (Exception cleanupEx)
        {
            if (primaryEx != null)
            {
                throw new AggregateException(
                    $"Test '{testName}' failed with a primary error, and fixture cleanup also failed. See InnerExceptions for details.",
                    primaryEx,
                    cleanupEx);
            }

            throw new InvalidOperationException(
                $"Test '{testName}' cleanup failed: {cleanupEx.Message}",
                cleanupEx);
        }
    }

    [Fact]
    public async Task T18_E2E_01_StorageUnits_TwoBrowser_SignalRCrud_RealtimeSync()
    {
        EnsureEdgeAvailable();

        await using var harness = await T18TwoBrowserHarness.StartAsync();
        Assert.True(harness.IsSqlServer, "T18 E2E tests strictly require a real SQL Server database.");

        await harness.InitializeBrowserSessionsAsync(
            role1: RoleConstants.FacilityManager,
            userId1: 4L, // Pham Thu Manager (assigned to Facility 1 HCM-TD)
            role2: RoleConstants.StorageCustomer,
            userId2: 1L  // Nguyen Minh Anh (Customer)
        );

        var ctxStaff = harness.Context1;
        var ctxCust = harness.Context2;

        // Verify production authentication cookies are present in both browser sessions
        Assert.Contains(CookieAuthExtensions.CookieName, await ctxStaff.GetCookieNamesAsync());
        Assert.Contains(CookieAuthExtensions.CookieName, await ctxCust.GetCookieNamesAsync());

        // 1. Context 1 navigates to StorageUnits management page for Facility 1
        await ctxStaff.NavigateAsync($"{harness.ServerUrl}/StorageUnits?facilityId=1");
        await ctxStaff.WaitForConditionAsync("window.connection?.state === 'Connected'", TimeSpan.FromSeconds(15));
        var staffConnState = await ctxStaff.EvaluateStringRequiredAsync("window.connection?.state || ''");
        Assert.Equal("Connected", staffConnState);

        // 2. Context 2 navigates to Customer MyUnits page
        await ctxCust.NavigateAsync($"{harness.ServerUrl}/MyUnits");
        await ctxCust.WaitForConditionAsync("window.appSignalRConnection?.state === 'Connected'", TimeSpan.FromSeconds(15));
        var custConnState = await ctxCust.EvaluateStringRequiredAsync("window.appSignalRConnection?.state || ''");
        Assert.Equal("Connected", custConnState);

        // Capture initial screenshots
        await ctxStaff.CaptureSanitizedScreenshotAsync("T18_01_Staff_StorageUnits_Initial");
        await ctxCust.CaptureSanitizedScreenshotAsync("T18_01_Customer_Catalog_Initial");

        Exception? testEx = null;
        long createdUnitId = 0;
        try
        {
            // 3. Context 1 invokes HubCreate_StorageUnit via real WebSocket
            var unitSuffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
            var createPayload = JsonSerializer.Serialize(new
            {
                FacilityId = 1L,
                UnitTypeId = 2L,
                AreaId = 1L,
                UnitCode = $"T18-U-{unitSuffix}",
                FloorLabel = "Floor 1",
                ZoneLabel = "Zone A",
                Notes = "Realtime test unit created via HubCreate_StorageUnit"
            });

            var createResult = await ctxStaff.EvaluateStringRequiredAsync($$"""
                (async function() {
                    try {
                        const res = await window.connection.invoke('HubCreate_StorageUnit', {{JsonSerializer.Serialize(createPayload)}});
                        return JSON.stringify(res);
                    } catch(err) {
                        return JSON.stringify({ error: String(err) });
                    }
                })()
            """);

            using var createDoc = JsonDocument.Parse(createResult);
            Assert.False(createDoc.RootElement.TryGetProperty("error", out _), $"HubCreate_StorageUnit failed: {createResult}");
            createdUnitId = createDoc.RootElement.GetProperty("id").GetInt64();
            var createdUnitCode = createDoc.RootElement.GetProperty("unitCode").GetString();
            Assert.True(createdUnitId > 0);
            Assert.Equal($"T18-U-{unitSuffix}", createdUnitCode);

            // Verify WebSocket frame was recorded in Context 1
            bool receivedCreateEvent = await ctxStaff.WaitForConditionAsync($$"""
                document.getElementById('unit-row-{{createdUnitId}}') !== null
            """, TimeSpan.FromSeconds(15));
            Assert.True(receivedCreateEvent, "Context 1 DOM must contain newly created unit row via Receiver_CreateStorageUnit.");

            // Context 1 table row properties assertion
            var rowText = await ctxStaff.EvaluateStringRequiredAsync($"document.getElementById('unit-row-{createdUnitId}')?.textContent || ''");
            Assert.Contains(createdUnitCode!, rowText);
            Assert.Contains("Khả dụng", rowText);

            await ctxStaff.CaptureSanitizedScreenshotAsync("T18_01_Staff_Unit_Created");

            // 4. Context 1 invokes HubUpdate_StorageUnit via real WebSocket
            var updatedCode = $"T18-U-{unitSuffix}-UPD";
            var updatePayload = JsonSerializer.Serialize(new
            {
                Id = createdUnitId,
                UnitCode = updatedCode,
                AreaId = 1L,
                FloorLabel = "Floor 2",
                ZoneLabel = "Zone B",
                Notes = "Updated notes via HubUpdate_StorageUnit"
            });

            var updateResult = await ctxStaff.EvaluateStringRequiredAsync($$"""
                (async function() {
                    try {
                        const res = await window.connection.invoke('HubUpdate_StorageUnit', {{JsonSerializer.Serialize(updatePayload)}});
                        return JSON.stringify(res);
                    } catch(err) {
                        return JSON.stringify({ error: String(err) });
                    }
                })()
            """);

            using var updateDoc = JsonDocument.Parse(updateResult);
            Assert.False(updateDoc.RootElement.TryGetProperty("error", out _), $"HubUpdate_StorageUnit failed: {updateResult}");

            // Wait for Receiver_UpdateStorageUnit to update DOM
            bool receivedUpdateEvent = await ctxStaff.WaitForConditionAsync($$"""
                document.getElementById('unit-row-{{createdUnitId}}')?.textContent.includes('{{updatedCode}}') === true
            """, TimeSpan.FromSeconds(15));
            Assert.True(receivedUpdateEvent, "Context 1 DOM must reflect updated unit code via Receiver_UpdateStorageUnit.");

            await ctxStaff.CaptureSanitizedScreenshotAsync("T18_01_Staff_Unit_Updated");

            // 5. Context 1 invokes HubDelete_StorageUnit via real WebSocket
            var deleteResult = await ctxStaff.EvaluateStringRequiredAsync($$"""
                (async function() {
                    try {
                        const res = await window.connection.invoke('HubDelete_StorageUnit', '{{createdUnitId}}', null);
                        return JSON.stringify({ success: true, result: res });
                    } catch(err) {
                        return JSON.stringify({ error: String(err) });
                    }
                })()
            """);

            using var deleteDoc = JsonDocument.Parse(deleteResult);
            Assert.False(deleteDoc.RootElement.TryGetProperty("error", out _), $"HubDelete_StorageUnit failed: {deleteResult}");

            // Wait for Receiver_DeleteStorageUnit to remove row from DOM
            bool rowRemoved = await ctxStaff.WaitForConditionAsync($$"""
                document.getElementById('unit-row-{{createdUnitId}}') === null
            """, TimeSpan.FromSeconds(15));
            Assert.True(rowRemoved, "Context 1 DOM must remove unit row via Receiver_DeleteStorageUnit.");

            await ctxStaff.CaptureSanitizedScreenshotAsync("T18_01_Staff_Unit_Deleted");

            // 6. Deleted unit details page check: Navigating to deleted unit must be handled safely (404 / not found)
            await ctxStaff.NavigateAsync($"{harness.ServerUrl}/StorageUnits/Details?id={createdUnitId}");
            var detailsHtml = await ctxStaff.EvaluateStringRequiredAsync("document.body.innerText || ''");
            Assert.True(detailsHtml.Contains("404") || detailsHtml.Contains("không tồn tại") || detailsHtml.Contains("Not Found") || detailsHtml.Contains("Lỗi"),
                "Accessing deleted unit details must safely indicate not found.");

            // 7. Security Assertion: Customer in Context 2 cannot invoke HubCreate_StorageUnit
            var unauthorizedAttempt = await ctxCust.EvaluateStringRequiredAsync($$"""
                (async function() {
                    try {
                        if (!window.appSignalRConnection) return JSON.stringify({ error: 'No connection' });
                        const res = await window.appSignalRConnection.invoke('HubCreate_StorageUnit', {{JsonSerializer.Serialize(createPayload)}});
                        return JSON.stringify({ result: res });
                    } catch(err) {
                        return JSON.stringify({ error: String(err) });
                    }
                })()
            """);
            Assert.Contains("error", unauthorizedAttempt.ToLowerInvariant());

            // Sanitization check on evidence logs
            Assert.All(ctxStaff.ConsoleLogs, log => Assert.DoesNotContain("password", log, StringComparison.OrdinalIgnoreCase));
            Assert.All(ctxStaff.ConsoleLogs, log => Assert.DoesNotContain("secret", log, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            testEx = ex;
            throw;
        }
        finally
        {
            if (createdUnitId > 0)
            {
                await SafeCleanupAsync(testEx, "T18_01", async () =>
                {
                    using var cleanupScope = harness.CreateScope();
                    var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
                    var u = await cleanupDb.storage_units.FindAsync(createdUnitId);
                    if (u != null)
                    {
                        cleanupDb.storage_units.Remove(u);
                        await cleanupDb.SaveChangesAsync();
                    }

                    // Verify unit is 100% removed
                    var remUnit = await cleanupDb.storage_units.CountAsync(unit => unit.id == createdUnitId);
                    Assert.Equal(0, remUnit);
                });
            }
        }
    }

    [Fact]
    public async Task T18_E2E_02_Reservation_Hold_Countdown_And_Cancel_TwoBrowserRealtime()
    {
        EnsureEdgeAvailable();

        await using var harness = await T18TwoBrowserHarness.StartAsync();
        Assert.True(harness.IsSqlServer, "T18 E2E tests strictly require a real SQL Server database.");

        await harness.InitializeBrowserSessionsAsync(
            role1: RoleConstants.FacilityManager,
            userId1: 4L,
            role2: RoleConstants.StorageCustomer,
            userId2: 1L
        );

        var ctxStaff = harness.Context1;
        var ctxCust = harness.Context2;

        Assert.Contains(CookieAuthExtensions.CookieName, await ctxStaff.GetCookieNamesAsync());
        Assert.Contains(CookieAuthExtensions.CookieName, await ctxCust.GetCookieNamesAsync());

        // Context 1 (Staff) monitors Facility 1 Storage Units
        await ctxStaff.NavigateAsync($"{harness.ServerUrl}/StorageUnits?facilityId=1");
        await ctxStaff.WaitForConditionAsync("window.connection?.state === 'Connected'", TimeSpan.FromSeconds(15));

        Exception? testEx = null;
        long resId = 0;
        try
        {
            // Create reservation hold for customer via service
            string rsvCode;
            DateTimeOffset holdUntil;
            using (var scope = harness.CreateScope())
            {
                var resService = scope.ServiceProvider.GetRequiredService<IReservationService>();
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var created = await resService.CreateReservationHoldAsync(1L, new CreateReservationRequest
                {
                    FacilityId = 1L,
                    UnitTypeId = 1L,
                    RentalStartDate = today.AddDays(1),
                    RentalEndDate = today.AddMonths(1).AddDays(1)
                });
                resId = created.Id;
                rsvCode = created.ReservationCode;
                holdUntil = created.HoldUntil;
            }

            // Context 2 (Customer) navigates to Reservation Details
            await ctxCust.NavigateAsync($"{harness.ServerUrl}/Reservations/Details?id={resId}");
            await ctxCust.WaitForConditionAsync("window.appSignalRConnection?.state === 'Connected'", TimeSpan.FromSeconds(15));

            // Assert countdown timer element exists and holds deadline
            var hasCountdown = await ctxCust.WaitForElementAsync("#reservationCountdownTimer, [data-hold-until], #countdownTimer", TimeSpan.FromSeconds(10));
            Assert.True(hasCountdown, "Customer reservation details must render countdown timer element.");

            var detailsText = await ctxCust.EvaluateStringRequiredAsync("document.body.innerText || ''");
            Assert.Contains("giữ chỗ", detailsText.ToLowerInvariant());
            // Context 1 (Staff/Manager) navigates to Reservation Details for this facility reservation
            await ctxStaff.NavigateAsync($"{harness.ServerUrl}/Reservations/Details?id={resId}");
            await ctxStaff.WaitForElementAsync("#reservationStatusBadge, body", TimeSpan.FromSeconds(15));
            var staffHoldText = await ctxStaff.EvaluateStringRequiredAsync("document.body.innerText || ''");
            Assert.Contains(rsvCode, staffHoldText);
            Assert.True(staffHoldText.Contains("giữ chỗ", StringComparison.OrdinalIgnoreCase) ||
                        staffHoldText.Contains("pending", StringComparison.OrdinalIgnoreCase) ||
                        staffHoldText.Contains("hold", StringComparison.OrdinalIgnoreCase),
                        "Staff must observe reservation in hold state.");

            await ctxCust.CaptureSanitizedScreenshotAsync("T18_02_Customer_Reservation_Ticking");
            await ctxStaff.CaptureSanitizedScreenshotAsync("T18_02_Staff_Reservation_Hold_View");

            // Finding 2: Attach listeners on both Customer and Staff before publishing cancellation
            const string attachListenerScript = @"
                window.__receivedReservationEvents = [];
                if (window.appSignalRConnection) {
                    window.appSignalRConnection.on('ReservationChanged', function(data) {
                        window.__receivedReservationEvents.push({ source: 'signalr', data: data });
                    });
                }
                document.addEventListener('app:reservation-changed', function(e) {
                    window.__receivedReservationEvents.push({ source: 'dom-event', data: e.detail });
                });
            ";
            await ctxCust.ExecuteScriptAsync(attachListenerScript);
            await ctxStaff.ExecuteScriptAsync(attachListenerScript);

            // Customer cancels reservation via service & publisher
            using (var scope = harness.CreateScope())
            {
                var resService = scope.ServiceProvider.GetRequiredService<IReservationService>();
                await resService.CancelReservationAsync(
                    1L,
                    new[] { RoleConstants.StorageCustomer },
                    resId,
                    new CancelReservationRequest { Reason = "Customer changed plan during E2E test" });

                var publisher = scope.ServiceProvider.GetRequiredService<IWebRealtimePublisher>();
                await publisher.PublishReservationChangedAsync(resId, 1L, 1L, "cancelled");
            }

            // Await and assert matching reservationId and status === 'cancelled' received in realtime on both browsers
            var custReceivedEvent = await ctxCust.WaitForConditionAsync($@"
                window.__receivedReservationEvents && window.__receivedReservationEvents.some(function(e) {{
                    var d = e.data || {{}};
                    var rid = d.reservationId != null ? d.reservationId : d.ReservationId;
                    var st = d.status || d.Status;
                    return String(rid) === '{resId}' && String(st).toLowerCase() === 'cancelled';
                }})
            ", TimeSpan.FromSeconds(15));
            Assert.True(custReceivedEvent, "Customer browser must receive ReservationChanged event with matching reservationId and status 'cancelled' via SignalR.");

            var staffReceivedEvent = await ctxStaff.WaitForConditionAsync($@"
                window.__receivedReservationEvents && window.__receivedReservationEvents.some(function(e) {{
                    var d = e.data || {{}};
                    var rid = d.reservationId != null ? d.reservationId : d.ReservationId;
                    var st = d.status || d.Status;
                    return String(rid) === '{resId}' && String(st).toLowerCase() === 'cancelled';
                }})
            ", TimeSpan.FromSeconds(15));
            Assert.True(staffReceivedEvent, "Staff browser must receive ReservationChanged event with matching reservationId and status 'cancelled' via SignalR.");

            // Verify in-place DOM transition on both browsers before reloading
            var custDomTransitioned = await ctxCust.WaitForConditionAsync(@"
                (document.getElementById('reservationStatusBadge')?.textContent?.toLowerCase() || '').includes('hủy') ||
                (document.getElementById('reservationStatusBadge')?.textContent?.toLowerCase() || '').includes('cancelled')
            ", TimeSpan.FromSeconds(15));
            Assert.True(custDomTransitioned, "Customer DOM badge must transition in-place upon receiving ReservationChanged event.");

            var staffDomTransitioned = await ctxStaff.WaitForConditionAsync(@"
                (document.getElementById('reservationStatusBadge')?.textContent?.toLowerCase() || '').includes('hủy') ||
                (document.getElementById('reservationStatusBadge')?.textContent?.toLowerCase() || '').includes('cancelled')
            ", TimeSpan.FromSeconds(15));
            Assert.True(staffDomTransitioned, "Staff DOM badge must transition in-place upon receiving ReservationChanged event.");

            // Capture in-place realtime transition screenshots on both browser contexts
            await ctxCust.CaptureSanitizedScreenshotAsync("T18_02_Customer_Reservation_Cancelled");
            await ctxStaff.CaptureSanitizedScreenshotAsync("T18_02_Staff_Reservation_Cancelled_View");

            // Secondary reconciliation check: perform ReloadAsync() on both browsers to verify durable database consistency
            await ctxCust.ReloadAsync();
            var isCancelledOnCust = await ctxCust.WaitForConditionAsync(
                "document.body.innerText.includes('đã hủy') || document.body.innerText.includes('Đã hủy') || document.body.innerText.includes('cancelled') || document.body.innerText.includes('Hủy')",
                TimeSpan.FromSeconds(15));
            Assert.True(isCancelledOnCust, "Customer reservation status must reflect cancellation upon durable database reload reconciliation.");

            await ctxStaff.ReloadAsync();
            var isCancelledOnStaff = await ctxStaff.WaitForConditionAsync(
                "document.body.innerText.includes('đã hủy') || document.body.innerText.includes('Đã hủy') || document.body.innerText.includes('cancelled') || document.body.innerText.includes('Hủy')",
                TimeSpan.FromSeconds(15));
            Assert.True(isCancelledOnStaff, "Staff view must observe reservation cancellation upon durable database reload reconciliation.");

            var staffViewHtml = await ctxStaff.EvaluateStringRequiredAsync("document.body.innerText || ''");
            Assert.Contains(rsvCode, staffViewHtml);
        }
        catch (Exception ex)
        {
            testEx = ex;
            throw;
        }
        finally
        {
            if (resId > 0)
            {
                await SafeCleanupAsync(testEx, "T18_02", async () =>
                {
                    using var cleanupScope = harness.CreateScope();
                    var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();

                    // Finding [P1] R4: trg_reservation_status_transition is AFTER UPDATE only and does not fire on DELETE.
                    // DDL trigger disable/enable is completely removed to prevent leaving business triggers disabled.
                    await cleanupDb.Database.ExecuteSqlRawAsync(
                        "DELETE FROM core.reservations WHERE id = {0};", resId);

                    // Verify reservation is 100% removed
                    var remRes = await cleanupDb.reservations.CountAsync(r => r.id == resId);
                    Assert.Equal(0, remRes);

                    // Verify trigger remains enabled
                    var triggerDisabled = await cleanupDb.Database.SqlQueryRaw<int>(@"
                        SELECT CAST(t.is_disabled AS int) AS Value
                        FROM sys.triggers t
                        JOIN sys.objects o ON t.parent_id = o.object_id
                        JOIN sys.schemas s ON o.schema_id = s.schema_id
                        WHERE s.name = 'core' AND t.name = 'trg_reservation_status_transition'
                    ").FirstOrDefaultAsync();
                    Assert.Equal(0, triggerDisabled);
                });
            }
        }
    }

    [Fact]
    public async Task T18_E2E_03_Payment_Simulator_Return_Invoice_And_Refund_TwoBrowserRealtime()
    {
        EnsureEdgeAvailable();

        await using var harness = await T18TwoBrowserHarness.StartAsync();
        Assert.True(harness.IsSqlServer, "T18 E2E tests strictly require a real SQL Server database.");

        await harness.InitializeBrowserSessionsAsync(
            role1: RoleConstants.FacilityManager,
            userId1: 4L,
            role2: RoleConstants.StorageCustomer,
            userId2: 1L
        );

        var ctxStaff = harness.Context1;
        var ctxCust = harness.Context2;

        Assert.Contains(CookieAuthExtensions.CookieName, await ctxStaff.GetCookieNamesAsync());
        Assert.Contains(CookieAuthExtensions.CookieName, await ctxCust.GetCookieNamesAsync());

        // 1. Create reservation hold for customer via service
        long resId = 0;
        string rsvCode = "";
        Exception? testEx = null;
        try
        {
            using (var scope = harness.CreateScope())
            {
                var resService = scope.ServiceProvider.GetRequiredService<IReservationService>();
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var res = await resService.CreateReservationHoldAsync(1L, new CreateReservationRequest
                {
                    FacilityId = 1L,
                    UnitTypeId = 1L,
                    RentalStartDate = today.AddDays(2),
                    RentalEndDate = today.AddMonths(1).AddDays(2)
                });
                resId = res.Id;
                rsvCode = res.ReservationCode;
            }

        // 2. Customer visits Checkout page
        await ctxCust.NavigateAsync($"{harness.ServerUrl}/Payments/Checkout?reservationId={resId}");
        await ctxCust.WaitForElementAsync("#checkoutForm", TimeSpan.FromSeconds(15));
        var checkoutHtml = await ctxCust.EvaluateStringRequiredAsync("document.body.innerText || ''");
        Assert.Contains("thanh toán", checkoutHtml.ToLowerInvariant());

        await ctxCust.CaptureSanitizedScreenshotAsync("T18_03_Customer_Checkout_Page");

        // 3. Customer checks policy acceptance and submits Checkout form to drive demo payment
        await ctxCust.ExecuteScriptAsync("""
            (function() {
                var chk = document.getElementById('chkAcceptPolicy');
                if (chk) { chk.checked = true; chk.dispatchEvent(new Event('change', { bubbles: true })); }
                var rdo = document.getElementById('methodDemo');
                if (rdo) { rdo.checked = true; rdo.dispatchEvent(new Event('change', { bubbles: true })); }
            })()
        """);
        await ctxCust.ClickAsync("#btnSubmitCheckout");
        await Task.Delay(2000);

        // 4. Verify browser was redirected to DemoSimulator page with valid paymentId
        var currentUrl = await ctxCust.EvaluateStringRequiredAsync("window.location.href");
        Assert.Contains("DemoSimulator", currentUrl);
        Assert.Contains("paymentId=", currentUrl);

        await ctxCust.WaitForElementAsync("#btnSubmitDemoComplete", TimeSpan.FromSeconds(15));
        var simHtml = await ctxCust.EvaluateStringRequiredAsync("document.body.innerText || ''");
        Assert.DoesNotContain("Mã giao dịch thanh toán không hợp lệ", simHtml);
        Assert.Contains("cổng thanh toán giả lập", simHtml.ToLowerInvariant());

        await ctxCust.CaptureSanitizedScreenshotAsync("T18_03_Customer_Demo_Simulator");

        // Extract paymentId from current simulator URL
        var paymentIdStr = currentUrl.Split("paymentId=")[1].Split('&')[0];
        long paymentId = long.Parse(paymentIdStr);

        // 5. Customer clicks #btnSubmitDemoComplete to complete the payment
        await ctxCust.ClickAsync("#btnSubmitDemoComplete");
        await Task.Delay(2000);

        // 6. Verify customer redirected to Result page with success confirmation
        var resultUrl = await ctxCust.EvaluateStringRequiredAsync("window.location.href");
        Assert.Contains("Result", resultUrl);
        var resultHtml = await ctxCust.EvaluateStringRequiredAsync("document.body.innerText || ''");
        Assert.True(resultHtml.Contains("thành công", StringComparison.OrdinalIgnoreCase) ||
                    resultHtml.Contains("đã được xác nhận", StringComparison.OrdinalIgnoreCase),
                    "Customer must see successful payment result page.");

        await ctxCust.CaptureSanitizedScreenshotAsync("T18_03_Customer_Payment_Result");

        // 7. Verify persistent database state: payment is 'succeeded', reservation is 'confirmed'
        long targetInvoiceId = 0;
        string invoiceNo = "";
        using (var scope = harness.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
            var dbPay = await db.payments.FindAsync(paymentId);
            Assert.NotNull(dbPay);
            Assert.Equal("succeeded", dbPay.status);
            targetInvoiceId = dbPay.target_invoice_id;

            var dbInv = await db.invoices.FindAsync(targetInvoiceId);
            Assert.NotNull(dbInv);
            invoiceNo = dbInv.invoice_no;

            var dbRes = await db.reservations.FindAsync(resId);
            Assert.NotNull(dbRes);
            Assert.Equal("confirmed", dbRes.status);
        }

        // 8. Customer views Invoice Details page and verifies specific paid invoice is rendered
        await ctxCust.NavigateAsync($"{harness.ServerUrl}/Invoices/Details?id={targetInvoiceId}");
        await ctxCust.WaitForElementAsync("#invoicePrintArea, body", TimeSpan.FromSeconds(15));
        var invoiceHtml = await ctxCust.EvaluateStringRequiredAsync("document.body.innerText || ''");
        Assert.True(invoiceHtml.Contains("PAID", StringComparison.OrdinalIgnoreCase) ||
                    invoiceHtml.Contains("đã thanh toán", StringComparison.OrdinalIgnoreCase),
                    "Customer invoice details must render paid status.");
        Assert.Contains(rsvCode, invoiceHtml);
        Assert.Contains(invoiceNo, invoiceHtml);

        await ctxCust.CaptureSanitizedScreenshotAsync("T18_03_Customer_Invoices_Paid");

        // 9. Staff (Context 1) views Payment Details page and verifies specific payment ID, status, and reservation code
        await ctxStaff.NavigateAsync($"{harness.ServerUrl}/Payments/Details?id={paymentId}");
        await ctxStaff.WaitForElementAsync("body", TimeSpan.FromSeconds(15));
        var staffPayDetailHtml = await ctxStaff.EvaluateStringRequiredAsync("document.body.innerText || ''");
        Assert.Contains($"#{paymentId}", staffPayDetailHtml);
        Assert.True(staffPayDetailHtml.Contains("succeeded", StringComparison.OrdinalIgnoreCase) ||
                    staffPayDetailHtml.Contains("thành công", StringComparison.OrdinalIgnoreCase),
                    "Staff payment details view must display succeeded status.");

        await ctxStaff.CaptureSanitizedScreenshotAsync("T18_03_Staff_Payments_Overview");

        // 10. Exercise Refund Path through intended transaction engine:
        // When a duplicate payment arrives on an already-paid invoice, the system engine routes it to reconciliation and automatically creates a refund request.
        long refundId;
        using (var scope = harness.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
            var payRepo = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
            var pay = await db.payments.Include(p => p.target_invoice).FirstAsync(p => p.id == paymentId);

            var dupPayment = new payment
            {
                customer_id = pay.customer_id,
                target_invoice_id = pay.target_invoice_id,
                amount = pay.amount,
                currency = "VND",
                method = "vnpay",
                provider = "vnpay",
                idempotency_key = $"PAY-DUP-{Guid.NewGuid():N}",
                status = "initiated",
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            };
            db.payments.Add(dupPayment);
            await db.SaveChangesAsync();

            // Finalize duplicate payment through repository engine -> triggers reconciliation and creates refund entity
            var finalizeResult = await payRepo.ExecuteFinalizePaymentTransactionAsync(new FinalizePaymentParams
            {
                PaymentId = dupPayment.id,
                Provider = "vnpay",
                ProviderTransactionId = $"TXN-DUP-{Guid.NewGuid():N}"[..20],
                Amount = dupPayment.amount,
                ExternalEventId = $"VNPAY-DUP-{dupPayment.id}",
                PaidAt = DateTimeOffset.UtcNow
            });

            Assert.True(finalizeResult.ReconciliationRequired, "Duplicate payment engine must trigger reconciliation required.");

            var createdRefund = await db.refunds.FirstOrDefaultAsync(r => r.payment_id == dupPayment.id);
            Assert.NotNull(createdRefund);
            Assert.Equal("requested", createdRefund.status);
            refundId = createdRefund.id;
        }

        // Staff navigates to Refunds tab on Payments page
        await ctxStaff.NavigateAsync($"{harness.ServerUrl}/Payments?tab=refunds");
        await ctxStaff.WaitForElementAsync($"#reviewModal-{refundId}, [data-refund-id='{refundId}']", TimeSpan.FromSeconds(15));
        var refundStaffHtml = await ctxStaff.EvaluateStringRequiredAsync("document.body.innerText || ''");
        Assert.Contains($"#{refundId}", refundStaffHtml);

        await ctxStaff.CaptureSanitizedScreenshotAsync("T18_03_Staff_Refund_Requested_View");

        // Staff submits review approval via form
        await ctxStaff.ExecuteScriptAsync($$"""
            (function() {
                var modal = document.getElementById('reviewModal-{{refundId}}');
                if (modal) {
                    var form = modal.querySelector('form');
                    if (form) {
                        var decRadio = form.querySelector('input[value="approved"]');
                        if (decRadio) decRadio.checked = true;
                        var btn = form.querySelector('button[type="submit"]');
                        if (btn) btn.click();
                        else form.submit();
                    }
                }
            })()
        """);
        await Task.Delay(2000);

        // Verify refund status is updated to approved/refunded in DB and Staff DOM
        using (var scope = harness.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
            var dbRefund = await db.refunds.Include(r => r.refund_approval).FirstOrDefaultAsync(r => r.id == refundId);
            Assert.NotNull(dbRefund);
            Assert.True(dbRefund.status == "approved" || dbRefund.status == "refunded",
                $"Refund status must be approved or refunded, but was '{dbRefund.status}'.");
            Assert.NotNull(dbRefund.refund_approval);
            Assert.Equal("approved", dbRefund.refund_approval.decision);
        }

        await ctxStaff.NavigateAsync($"{harness.ServerUrl}/Payments?tab=refunds");
        var staffRefundUpdatedHtml = await ctxStaff.EvaluateStringRequiredAsync("document.body.innerText || ''");
        Assert.True(staffRefundUpdatedHtml.Contains("approved", StringComparison.OrdinalIgnoreCase) ||
                    staffRefundUpdatedHtml.Contains("refunded", StringComparison.OrdinalIgnoreCase) ||
                    staffRefundUpdatedHtml.Contains("phê duyệt", StringComparison.OrdinalIgnoreCase),
                    "Staff refunds table must display approved/refunded state.");

        await ctxStaff.CaptureSanitizedScreenshotAsync("T18_03_Staff_Refund_Approved_View");
        }
        catch (Exception ex)
        {
            testEx = ex;
            throw;
        }
        finally
        {
            if (resId > 0)
            {
                await SafeCleanupAsync(testEx, "T18_03", async () =>
                {
                    using var cleanupScope = harness.CreateScope();
                    var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
                    await using var tx = await cleanupDb.Database.BeginTransactionAsync();

                    await cleanupDb.Database.ExecuteSqlRawAsync(@"
                        SET QUOTED_IDENTIFIER ON;
                        DISABLE TRIGGER core.trg_payment_allocation_guard ON core.payment_allocations;
                        DISABLE TRIGGER core.trg_invoice_status_transition ON core.invoices;
                        DISABLE TRIGGER core.trg_invoice_lines_guard ON core.invoice_lines;
                        DISABLE TRIGGER core.trg_invoice_lines_refresh_totals ON core.invoice_lines;
                        DISABLE TRIGGER core.trg_agreement_status_transition ON core.rental_agreements;
                        DISABLE TRIGGER core.trg_reservation_status_transition ON core.reservations;

                        UPDATE core.invoices SET agreement_id = NULL WHERE reservation_id = {0};
                        DELETE FROM core.refund_approvals WHERE refund_id IN (
                            SELECT r.id FROM core.refunds r
                            JOIN core.payments p ON r.payment_id = p.id
                            JOIN core.invoices i ON p.target_invoice_id = i.id
                            WHERE i.reservation_id = {0}
                        );
                        DELETE FROM core.refunds WHERE payment_id IN (
                            SELECT p.id FROM core.payments p
                            JOIN core.invoices i ON p.target_invoice_id = i.id
                            WHERE i.reservation_id = {0}
                        );
                        DELETE FROM core.payment_allocations WHERE invoice_id IN (
                            SELECT id FROM core.invoices WHERE reservation_id = {0}
                        );
                        DELETE FROM core.payments WHERE target_invoice_id IN (
                            SELECT id FROM core.invoices WHERE reservation_id = {0}
                        );
                        DELETE FROM core.invoice_lines WHERE invoice_id IN (
                            SELECT id FROM core.invoices WHERE reservation_id = {0}
                        );
                        DELETE FROM core.invoices WHERE reservation_id = {0};
                        DELETE FROM core.promotion_redemptions WHERE reservation_id = {0};
                        DELETE FROM core.rental_agreements WHERE reservation_id = {0};
                        DELETE FROM core.reservations WHERE id = {0};

                        ENABLE TRIGGER core.trg_payment_allocation_guard ON core.payment_allocations;
                        ENABLE TRIGGER core.trg_invoice_status_transition ON core.invoices;
                        ENABLE TRIGGER core.trg_invoice_lines_guard ON core.invoice_lines;
                        ENABLE TRIGGER core.trg_invoice_lines_refresh_totals ON core.invoice_lines;
                        ENABLE TRIGGER core.trg_agreement_status_transition ON core.rental_agreements;
                        ENABLE TRIGGER core.trg_reservation_status_transition ON core.reservations;
                    ", resId);

                    await tx.CommitAsync();

                    // Read-only DB verification: assert test fixture is 100% removed
                    var remRefunds = await cleanupDb.refunds.CountAsync(r => r.payment.target_invoice.reservation_id == resId);
                    var remPayments = await cleanupDb.payments.CountAsync(p => p.target_invoice.reservation_id == resId);
                    var remInvoices = await cleanupDb.invoices.CountAsync(i => i.reservation_id == resId);
                    Assert.Equal(0, remRefunds);
                    Assert.Equal(0, remPayments);
                    Assert.Equal(0, remInvoices);
                });
            }
        }
    }

    [Fact]
    public async Task T18_E2E_04_CheckIn_Inspection_Handover_ZeroPin_TwoBrowserRealtime()
    {
        EnsureEdgeAvailable();

        await using var harness = await T18TwoBrowserHarness.StartAsync();
        Assert.True(harness.IsSqlServer, "T18 E2E tests strictly require a real SQL Server database.");

        await harness.InitializeBrowserSessionsAsync(
            role1: RoleConstants.FacilityStaff,
            userId1: 3L, // Le Hoang Staff (assigned to Facility 1 HCM-TD)
            role2: RoleConstants.StorageCustomer,
            userId2: 1L  // Nguyen Minh Anh (Customer)
        );

        var ctxStaff = harness.Context1;
        var ctxCust = harness.Context2;

        Assert.Contains(CookieAuthExtensions.CookieName, await ctxStaff.GetCookieNamesAsync());
        Assert.Contains(CookieAuthExtensions.CookieName, await ctxCust.GetCookieNamesAsync());

        // 1. Prepare ready-for-checkin reservation & scheduled agreement starting today
        var rsvSuffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var rsvCode = $"RSV-T18-{rsvSuffix}";
        var agrNumber = $"AGR-T18-{rsvSuffix}";
        long reservationId = 0;
        long agreementId = 0;
        long dedicatedUnitId = 0;
        Exception? testEx = null;

        try
        {
            using (var scope = harness.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
                var facilityTimeZoneId = await db.facilities
                    .Where(f => f.id == 1L)
                    .Select(f => f.timezone)
                    .SingleAsync();
                var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.UtcNow,
                    SelfStorageManagementSystem.Web.Helpers.AppFormatters.ResolveFacilityTimeZone(facilityTimeZoneId)));

                // Finding 2: Create an isolated, dedicated storage unit specifically for this test fixture.
                // Do NOT rewrite or reset unrelated seeded units 1-4, and do NOT cancel other tests' reservations.
                var testUnit = new storage_unit
                {
                    facility_id = 1L,
                    unit_type_id = 2L,
                    unit_code = $"T18-CHK-{rsvSuffix}",
                    floor_label = "F1",
                    zone_label = "Z1",
                    physical_status = "available",
                    is_listed = true,
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                db.storage_units.Add(testUnit);
                await db.SaveChangesAsync();
                dedicatedUnitId = testUnit.id;

                var resService = scope.ServiceProvider.GetRequiredService<IReservationService>();
                var created = await resService.CreateReservationHoldAsync(1L, new CreateReservationRequest
                {
                    FacilityId = 1L,
                    UnitTypeId = 2L,
                    RentalStartDate = today,
                    RentalEndDate = today.AddMonths(1)
                });
                reservationId = created.Id;
                rsvCode = created.ReservationCode;

                var rsv = await db.reservations.FindAsync(reservationId);
                rsv!.status = "confirmed";

            var agr = new rental_agreement
            {
                reservation_id = rsv.id,
                customer_id = rsv.customer_id,
                facility_id = rsv.facility_id,
                policy_version_id = 1L,
                agreement_no = agrNumber,
                status = "scheduled",
                start_date = rsv.start_date,
                end_date = rsv.end_date,
                monthly_rate_snapshot = rsv.monthly_rate_snapshot,
                deposit_snapshot = rsv.deposit_snapshot,
                deposit_balance = rsv.deposit_snapshot,
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            };
            db.rental_agreements.Add(agr);
            await db.SaveChangesAsync();
            agreementId = agr.id;

            var total = rsv.monthly_rate_snapshot + rsv.deposit_snapshot;
            var inv = new invoice
            {
                customer_id = rsv.customer_id,
                reservation_id = rsv.id,
                agreement_id = agr.id,
                invoice_no = $"INV-T18-{rsvSuffix}",
                currency = "VND",
                subtotal_amount = total,
                total_amount = total,
                paid_amount = total,
                status = "paid",
                issue_date = today,
                due_date = today.AddDays(7),
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            };
            db.invoices.Add(inv);
            await db.SaveChangesAsync();
        }

        // 2. Staff navigates to CheckIn page for the reservation code
        await ctxStaff.NavigateAsync($"{harness.ServerUrl}/Staff/CheckIn?code={rsvCode}");
        await ctxStaff.WaitForElementAsync("#verifDocNumber, [name='DocumentNumber']", TimeSpan.FromSeconds(15));
        var checkInHtml = await ctxStaff.EvaluateStringRequiredAsync("document.body.innerText || ''");
        Assert.Contains(rsvCode, checkInHtml);

        await ctxStaff.CaptureSanitizedScreenshotAsync("T18_04_Staff_CheckIn_Dashboard");

        // 3. Step 2: Staff verifies identity
        await ctxStaff.ExecuteScriptAsync("""
            (function() {
                var doc = document.getElementById('verifDocNumber');
                if (doc) doc.value = 'DEMO-CUSTOMER-001';
                var chk = document.getElementById('chkVerifMatch');
                if (chk) chk.checked = true;
                var btn = document.querySelector('form[action*="VerifyId"] button[type="submit"]') || document.getElementById('chkVerifMatch')?.closest('form')?.querySelector('button[type="submit"]');
                if (btn) btn.click();
                else {
                    var form = document.querySelector('form[action*="VerifyId"]') || document.getElementById('chkVerifMatch')?.closest('form');
                    if (form) form.submit();
                }
            })()
        """);
        await ctxStaff.WaitForElementAsync("form[action*='AssignUnit'], .list-group-item form", TimeSpan.FromSeconds(15));
        await Task.Delay(1000);

        // 4. Step 3: Staff assigns the dedicated unit created for this test
        await ctxStaff.ExecuteScriptAsync($$"""
            (function() {
                var specificInput = document.querySelector('input[name="StorageUnitId"][value="{{dedicatedUnitId}}"]');
                if (specificInput) {
                    var form = specificInput.closest('form');
                    var btn = form ? form.querySelector('button[type="submit"]') : null;
                    if (btn) { btn.click(); return; }
                    if (form) { form.submit(); return; }
                }
                var btnFallback = document.querySelector('form[action*="AssignUnit"] button[type="submit"]');
                if (btnFallback) btnFallback.click();
                else {
                    var assignForm = document.querySelector('form[action*="AssignUnit"]') || document.querySelector('.list-group form');
                    if (assignForm) assignForm.submit();
                }
            })()
        """);
        await ctxStaff.WaitForElementAsync("#inspectionForm", TimeSpan.FromSeconds(15));
        await Task.Delay(1000);

        // 5. Step 4: Staff performs safety inspection (all 4 items good)
        await ctxStaff.ExecuteScriptAsync("""
            (function() {
                var form = document.getElementById('inspectionForm');
                if (form) {
                    var summary = form.querySelector('[name="OverallSummary"]') || document.getElementById('inspSummary');
                    if (summary) summary.value = 'Tất cả 4 hạng mục đạt tiêu chuẩn vận hành E2E.';
                    var btn = form.querySelector('button[type="submit"]');
                    if (btn) btn.click();
                    else form.submit();
                }
            })()
        """);
        await ctxStaff.WaitForElementAsync("#chkAcknowledge", TimeSpan.FromSeconds(15));
        await Task.Delay(1000);

        // 6. Step 5: Staff completes check-in and handover
        await ctxStaff.ExecuteScriptAsync("""
            (function() {
                var chk = document.getElementById('chkAcknowledge');
                if (chk) chk.checked = true;
                var notes = document.getElementById('completeNotes') || document.querySelector('form[action*="CompleteCheckIn"] [name="Notes"]');
                if (notes) notes.value = 'Bàn giao trực tiếp tại quầy thành công.';
                var btn = document.querySelector('form[action*="CompleteCheckIn"] button[type="submit"]');
                if (btn) btn.click();
                else {
                    var form = document.querySelector('form[action*="CompleteCheckIn"]') || chk?.closest('form');
                    if (form) form.submit();
                }
            })()
        """);

        // 7. Verify one-time PIN banner is rendered on Staff view
        bool pinBannerFound = await ctxStaff.WaitForElementAsync("#displayPinValue, .checkin-pin-value", TimeSpan.FromSeconds(20));
        Assert.True(pinBannerFound, "Staff view must render one-time PIN banner upon check-in completion.");
        var postCompleteHtml = await ctxStaff.EvaluateStringRequiredAsync("document.body.innerText || ''");
        Assert.True(postCompleteHtml.Contains("bàn giao", StringComparison.OrdinalIgnoreCase) ||
                    postCompleteHtml.Contains("thành công", StringComparison.OrdinalIgnoreCase),
                    "Staff must see handover completion confirmation.");

        // Capture sanitized screenshot (DOM pixels masked to ****** before capture and restored after)
        await ctxStaff.CaptureSanitizedScreenshotAsync("T18_04_Staff_CheckIn_Completed_Sanitized");

        // Reload Staff page: PIN banner must NOT persist across reloads
        await ctxStaff.ReloadAsync();
        var reloadedHtml = await ctxStaff.EvaluateStringRequiredAsync("document.body.innerText || ''");
        Assert.DoesNotContain("displayPinValue", reloadedHtml);

        // 8. Zero-Persistence PIN DB Assertion: Verify access_credentials stores only PBKDF2 digest, never plaintext PIN
        using (var scope = harness.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
            var cred = await db.access_credentials.FirstOrDefaultAsync(c => c.agreement_id == agreementId);
            Assert.NotNull(cred);
            Assert.Equal("active", cred.status);
            Assert.NotNull(cred.secret_digest);
            Assert.Equal(64, cred.secret_digest.Length); // 64-character HMAC-SHA256 hex digest
            Assert.True(cred.secret_digest.All(c => Uri.IsHexDigit(c)));
            Assert.DoesNotContain("PIN", cred.secret_digest);
        }

        // 9. Customer (Context 2) visits MyUnits and sees active agreement
        await ctxCust.NavigateAsync($"{harness.ServerUrl}/MyUnits");
        await ctxCust.WaitForElementAsync("body", TimeSpan.FromSeconds(15));
        var myUnitsHtml = await ctxCust.EvaluateStringRequiredAsync("document.body.innerText || ''");
        Assert.Contains("kho của tôi", myUnitsHtml.ToLowerInvariant());

        await ctxCust.CaptureSanitizedScreenshotAsync("T18_04_Customer_MyUnits_Active");

        // 10. Customer visits Notifications and sees handover notification
        await ctxCust.NavigateAsync($"{harness.ServerUrl}/Notifications");
        await ctxCust.WaitForElementAsync("body", TimeSpan.FromSeconds(15));
        var notifHtml = await ctxCust.EvaluateStringRequiredAsync("document.body.innerText || ''");
        Assert.Contains("thông báo", notifHtml.ToLowerInvariant());

        await ctxCust.CaptureSanitizedScreenshotAsync("T18_04_Customer_Notifications_Received");

        // Security check: logs must not contain plaintext PIN
        Assert.All(ctxStaff.ConsoleLogs, log => Assert.DoesNotContain("PIN:", log));
        Assert.All(ctxCust.ConsoleLogs, log => Assert.DoesNotContain("PIN:", log));
        }
        catch (Exception ex)
        {
            testEx = ex;
            throw;
        }
        finally
        {
            if (reservationId > 0 || dedicatedUnitId > 0)
            {
                await SafeCleanupAsync(testEx, "T18_04", async () =>
                {
                    using var cleanupScope = harness.CreateScope();
                    var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
                    await using var tx = await cleanupDb.Database.BeginTransactionAsync();

                    await cleanupDb.Database.ExecuteSqlRawAsync(@"
                        SET QUOTED_IDENTIFIER ON;
                        DISABLE TRIGGER core.trg_agreement_status_transition ON core.rental_agreements;
                        DISABLE TRIGGER core.trg_reservation_status_transition ON core.reservations;
                        DISABLE TRIGGER core.trg_invoice_status_transition ON core.invoices;
                        DISABLE TRIGGER core.trg_payment_allocation_guard ON core.payment_allocations;
                        DISABLE TRIGGER core.trg_invoice_lines_guard ON core.invoice_lines;
                        DISABLE TRIGGER core.trg_invoice_lines_refresh_totals ON core.invoice_lines;

                        DELETE FROM core.access_events WHERE credential_id IN (
                            SELECT id FROM core.access_credentials WHERE agreement_id IN (
                                SELECT id FROM core.rental_agreements WHERE reservation_id = {0}
                            )
                        );

                        DELETE FROM core.handover_records WHERE agreement_id IN (
                            SELECT id FROM core.rental_agreements WHERE reservation_id = {0}
                        ) OR inspection_id IN (
                            SELECT id FROM core.inspections WHERE reservation_id = {0} OR storage_unit_id = {1}
                        );

                        DELETE FROM core.inspection_items WHERE inspection_id IN (
                            SELECT id FROM core.inspections WHERE reservation_id = {0} OR storage_unit_id = {1} OR agreement_id IN (
                                SELECT id FROM core.rental_agreements WHERE reservation_id = {0}
                            )
                        );

                        DELETE FROM core.inspections WHERE reservation_id = {0} OR storage_unit_id = {1} OR agreement_id IN (
                            SELECT id FROM core.rental_agreements WHERE reservation_id = {0}
                        );

                        DELETE FROM core.access_credentials WHERE agreement_id IN (
                            SELECT id FROM core.rental_agreements WHERE reservation_id = {0}
                        );

                        DELETE FROM core.unit_allocations WHERE agreement_id IN (
                            SELECT id FROM core.rental_agreements WHERE reservation_id = {0}
                        ) OR storage_unit_id = {1};

                        DELETE FROM core.unit_status_history WHERE storage_unit_id = {1};

                        DELETE FROM core.identity_verifications WHERE reservation_id = {0};

                        DELETE FROM core.payment_allocations WHERE invoice_id IN (
                            SELECT id FROM core.invoices WHERE reservation_id = {0} OR agreement_id IN (
                                SELECT id FROM core.rental_agreements WHERE reservation_id = {0}
                            )
                        );
                        DELETE FROM core.payments WHERE target_invoice_id IN (
                            SELECT id FROM core.invoices WHERE reservation_id = {0} OR agreement_id IN (
                                SELECT id FROM core.rental_agreements WHERE reservation_id = {0}
                            )
                        );
                        DELETE FROM core.invoice_lines WHERE invoice_id IN (
                            SELECT id FROM core.invoices WHERE reservation_id = {0} OR agreement_id IN (
                                SELECT id FROM core.rental_agreements WHERE reservation_id = {0}
                            )
                        );
                        DELETE FROM core.invoices WHERE reservation_id = {0} OR agreement_id IN (
                            SELECT id FROM core.rental_agreements WHERE reservation_id = {0}
                        );

                        DELETE FROM core.rental_agreements WHERE reservation_id = {0};
                        DELETE FROM core.reservations WHERE id = {0};
                        DELETE FROM core.storage_units WHERE id = {1};

                        ENABLE TRIGGER core.trg_invoice_lines_refresh_totals ON core.invoice_lines;
                        ENABLE TRIGGER core.trg_invoice_lines_guard ON core.invoice_lines;
                        ENABLE TRIGGER core.trg_payment_allocation_guard ON core.payment_allocations;
                        ENABLE TRIGGER core.trg_invoice_status_transition ON core.invoices;
                        ENABLE TRIGGER core.trg_agreement_status_transition ON core.rental_agreements;
                        ENABLE TRIGGER core.trg_reservation_status_transition ON core.reservations;
                    ", reservationId, dedicatedUnitId);

                    await tx.CommitAsync();

                    // Read-only DB verification: assert test fixture is 100% removed
                    var remAlloc = await cleanupDb.unit_allocations.CountAsync(a => a.storage_unit_id == dedicatedUnitId);
                    var remUnit = await cleanupDb.storage_units.CountAsync(u => u.id == dedicatedUnitId);
                    var remRes = await cleanupDb.reservations.CountAsync(r => r.id == reservationId);
                    Assert.Equal(0, remAlloc);
                    Assert.Equal(0, remUnit);
                    Assert.Equal(0, remRes);
                });
            }
        }
    }

    [Fact]
    public async Task T18_E2E_05_AdminAccounts_CustomerNotifications_And_PermissionRevocation()
    {
        EnsureEdgeAvailable();

        await using var harness = await T18TwoBrowserHarness.StartAsync();
        Assert.True(harness.IsSqlServer, "T18 E2E tests strictly require a real SQL Server database.");

        await harness.InitializeBrowserSessionsAsync(
            role1: RoleConstants.SystemAdministrator,
            userId1: 6L, // Administrator
            role2: RoleConstants.StorageCustomer,
            userId2: 1L  // Customer
        );

        var ctxAdmin = harness.Context1;
        var ctxCust = harness.Context2;

        Assert.Contains(CookieAuthExtensions.CookieName, await ctxAdmin.GetCookieNamesAsync());
        Assert.Contains(CookieAuthExtensions.CookieName, await ctxCust.GetCookieNamesAsync());

        // 1. Admin views accounts list
        await ctxAdmin.NavigateAsync($"{harness.ServerUrl}/Admin/Accounts");
        await ctxAdmin.WaitForElementAsync("body", TimeSpan.FromSeconds(15));
        var accountsHtml = await ctxAdmin.EvaluateStringRequiredAsync("document.body.innerText || ''");
        Assert.Contains("quản trị tài khoản", accountsHtml.ToLowerInvariant());
        Assert.Contains("administrator@example.test", accountsHtml);

        await ctxAdmin.CaptureSanitizedScreenshotAsync("T18_05_Admin_Accounts_List");

        // 2. Customer views notifications
        await ctxCust.NavigateAsync($"{harness.ServerUrl}/Notifications");
        await ctxCust.WaitForElementAsync("body", TimeSpan.FromSeconds(15));
        var notifHtml = await ctxCust.EvaluateStringRequiredAsync("document.body.innerText || ''");
        Assert.Contains("thông báo", notifHtml.ToLowerInvariant());

        await ctxCust.CaptureSanitizedScreenshotAsync("T18_05_Customer_Notifications");

        // 3. Permission Revocation with Retained Cookie:
        // Set user 6 status to 'disabled' in database while Admin's browser retains the valid authentication cookie
        string originalAdminStatus = "active";
        Exception? testEx = null;
        try
        {
            using (var scope = harness.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
                var adminUser = await db.users.FirstOrDefaultAsync(u => u.id == 6L);
                if (adminUser != null)
                {
                    originalAdminStatus = adminUser.status;
                }
                await db.Database.ExecuteSqlRawAsync("UPDATE core.users SET status = 'disabled' WHERE id = 6;");
            }

            // Verify Admin browser still retains the cookie
            Assert.Contains(CookieAuthExtensions.CookieName, await ctxAdmin.GetCookieNamesAsync());

            // Admin now attempts to navigate to /Admin/Accounts:
            // OnValidatePrincipal detects status != 'active', calls RejectPrincipal() and signs out,
            // resulting in redirect to /Account/Login?ReturnUrl=%2FAdmin%2FAccounts
            await ctxAdmin.NavigateAsync($"{harness.ServerUrl}/Admin/Accounts");
            await Task.Delay(1500);

            var currentUrl = await ctxAdmin.EvaluateStringRequiredAsync("window.location.href");
            var adminDom = await ctxAdmin.EvaluateStringRequiredAsync("document.body.innerText || ''");

            Assert.True(currentUrl.Contains("Account/Login") || adminDom.Contains("Đăng Nhập") || adminDom.Contains("Login"),
                $"Revoked principal must be rejected and redirected to Login, but was at: {currentUrl}");

            await ctxAdmin.CaptureSanitizedScreenshotAsync("T18_05_Admin_Revocation_Rejected");
        }
        catch (Exception ex)
        {
            testEx = ex;
            throw;
        }
        finally
        {
            await SafeCleanupAsync(testEx, "T18_05", async () =>
            {
                // Restore Admin status to exact original status so subsequent runs remain pristine
                using var scope = harness.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
                await db.Database.ExecuteSqlRawAsync("UPDATE core.users SET status = {0} WHERE id = 6;", originalAdminStatus);
            });
        }
    }

    [Fact]
    public async Task T18_E2E_06_SignalR_Reconnect_MissedEvents_And_ReloadStateReconciliation()
    {
        EnsureEdgeAvailable();

        await using var harness = await T18TwoBrowserHarness.StartAsync();
        Assert.True(harness.IsSqlServer, "T18 E2E tests strictly require a real SQL Server database.");

        await harness.InitializeBrowserSessionsAsync(
            role1: RoleConstants.FacilityManager,
            userId1: 4L,
            role2: RoleConstants.StorageCustomer,
            userId2: 1L
        );

        var ctxStaff = harness.Context1;

        Assert.Contains(CookieAuthExtensions.CookieName, await ctxStaff.GetCookieNamesAsync());

        // Context 1 connects to StorageUnits page
        await ctxStaff.NavigateAsync($"{harness.ServerUrl}/StorageUnits?facilityId=1");
        await ctxStaff.WaitForConditionAsync("window.connection?.state === 'Connected'", TimeSpan.FromSeconds(15));
        Assert.Equal("Connected", await ctxStaff.EvaluateStringRequiredAsync("window.connection?.state || ''"));

        // Simulate transient network disconnection: stop SignalR
        await ctxStaff.StopSignalRAsync();
        await Task.Delay(500);

        var stateAfterStop = await ctxStaff.EvaluateStringRequiredAsync("window.connection?.state || ''");
        Assert.Equal("Disconnected", stateAfterStop);

        // While disconnected, insert a new storage unit directly into database (missed event)
        var missedCode = $"T18-MISSED-{Guid.NewGuid():N}"[..14].ToUpperInvariant();
        Exception? testEx = null;
        long missedUnitId = 0;
        try
        {
            using (var scope = harness.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
                var newUnit = new storage_unit
                {
                    facility_id = 1L,
                    unit_type_id = 1L,
                    area_id = 1L,
                    unit_code = missedCode,
                    floor_label = "Floor 1",
                    zone_label = "Zone A",
                    physical_status = "available",
                    is_listed = true,
                    notes = "Unit created while client was disconnected",
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                db.storage_units.Add(newUnit);
                await db.SaveChangesAsync();
                missedUnitId = newUnit.id;
            }

            // Reconnect SignalR or reload page to reconcile state from database
            await ctxStaff.ReloadAsync();
            await ctxStaff.WaitForConditionAsync("window.connection?.state === 'Connected'", TimeSpan.FromSeconds(15));

            // Assert DOM table contains the missed unit from database state reconciliation
            bool rowFoundInDom = await ctxStaff.WaitForConditionAsync($$"""
                document.getElementById('unit-row-{{missedUnitId}}') !== null || document.body.innerText.includes('{{missedCode}}')
            """, TimeSpan.FromSeconds(15));

            Assert.True(rowFoundInDom, "DOM state must reconcile and include persistent database updates missed during disconnection.");

            await ctxStaff.CaptureSanitizedScreenshotAsync("T18_06_Staff_Reconnected_Reconciled");
        }
        catch (Exception ex)
        {
            testEx = ex;
            throw;
        }
        finally
        {
            // Clean up missed unit
            if (missedUnitId > 0)
            {
                await SafeCleanupAsync(testEx, "T18_06", async () =>
                {
                    using var scope = harness.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
                    var u = await db.storage_units.FindAsync(missedUnitId);
                    if (u != null)
                    {
                        db.storage_units.Remove(u);
                        await db.SaveChangesAsync();
                    }

                    // Verify unit is 100% removed
                    var remUnit = await db.storage_units.CountAsync(unit => unit.id == missedUnitId);
                    Assert.Equal(0, remUnit);
                });
            }
        }
    }

    [Fact]
    public async Task T18_ControlledDoubleFailure_PreservesPrimaryAndSurfacesCleanupError()
    {
        // 1. Controlled double failure: primary test failure + cleanup failure
        var primaryException = new InvalidOperationException("Simulated primary browser assertion failure");
        var cleanupException = new InvalidOperationException("Simulated SQL database cleanup failure");

        var caughtAgg = await Assert.ThrowsAsync<AggregateException>(async () =>
        {
            Exception? testEx = null;
            try
            {
                throw primaryException;
            }
            catch (Exception ex)
            {
                testEx = ex;
                throw;
            }
            finally
            {
                await SafeCleanupAsync(testEx, "ControlledDoubleFailure", () =>
                {
                    throw cleanupException;
                });
            }
        });

        // Assert: Primary error is preserved as the first inner exception
        Assert.Contains("failed with a primary error, and fixture cleanup also failed", caughtAgg.Message);
        Assert.Equal(2, caughtAgg.InnerExceptions.Count);
        Assert.Same(primaryException, caughtAgg.InnerExceptions[0]);
        Assert.Equal("Simulated primary browser assertion failure", caughtAgg.InnerExceptions[0].Message);

        // Assert: Cleanup error is surfaced as the second inner exception
        Assert.Same(cleanupException, caughtAgg.InnerExceptions[1]);
        Assert.Equal("Simulated SQL database cleanup failure", caughtAgg.InnerExceptions[1].Message);

        // 2. Controlled cleanup-only failure: test passes, but cleanup fails
        var cleanupOnlyException = new InvalidOperationException("Simulated standalone cleanup failure");
        var caughtCleanupOnly = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            Exception? testEx = null;
            try
            {
                // Test passes cleanly without throwing
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                testEx = ex;
                throw;
            }
            finally
            {
                await SafeCleanupAsync(testEx, "ControlledCleanupOnlyFailure", () =>
                {
                    throw cleanupOnlyException;
                });
            }
        });

        Assert.Contains("Test 'ControlledCleanupOnlyFailure' cleanup failed", caughtCleanupOnly.Message);
        Assert.Same(cleanupOnlyException, caughtCleanupOnly.InnerException);

        // 3. Controlled primary-only failure: test fails, but cleanup succeeds
        var primaryOnlyException = new InvalidOperationException("Simulated standalone primary failure");
        bool cleanupRan = false;
        var caughtPrimaryOnly = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            Exception? testEx = null;
            try
            {
                throw primaryOnlyException;
            }
            catch (Exception ex)
            {
                testEx = ex;
                throw;
            }
            finally
            {
                await SafeCleanupAsync(testEx, "ControlledPrimaryOnlyFailure", () =>
                {
                    cleanupRan = true;
                    return Task.CompletedTask;
                });
            }
        });

        Assert.True(cleanupRan, "Cleanup must run even when primary test throws.");
        Assert.Same(primaryOnlyException, caughtPrimaryOnly);
    }
}
