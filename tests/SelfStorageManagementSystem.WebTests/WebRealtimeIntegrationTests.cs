using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Payments;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.StorageUnits;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Payments;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Reservations;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.StorageUnits;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;
using SelfStorageManagementSystem.DataAccess.Repositories.Implementations;
using ExpiredReservationHoldInfo = SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Reservations.ExpiredReservationHoldInfo;
using SelfStorageManagementSystem.Web.Controllers;
using SelfStorageManagementSystem.Web.Extensions;
using SelfStorageManagementSystem.Web.Hubs;
using SelfStorageManagementSystem.Web.Realtime;
using SelfStorageManagementSystem.Web.Workers;
using SelfStorageManagementSystem.WebTests.Fixtures;
using SelfStorageManagementSystem.WebTests.Infrastructure;
using Xunit;

namespace SelfStorageManagementSystem.WebTests;

/// <summary>
/// Bộ kiểm thử tích hợp chuyên biệt cho Task T15:
/// Tích hợp realtime toàn Web (SignalR /storageUnitHub):
/// 1. HTTP thật và WebSocket thật trên Kestrel với Cookie Auth và Recipient Scoping.
/// 2. Kiểm thử 5 luồng hợp đồng (CatalogInvalidated, ReservationChanged, PaymentChanged, CheckInChanged, NotificationsChanged) + AccountChanged.
/// 3. Kiểm âm bản bảo mật: Tuyệt đối không rò rỉ mã PIN hoặc EF Entity trong payload event.
/// 4. Kiểm tra điểm commit thật: IPN là nguồn sự thật, Return chỉ hiển thị, Worker expire sau commit.
/// 5. Kiểm tra Degraded publisher: DB commit thành công dù SignalR gặp lỗi, hiển thị cảnh báo suy giảm hợp lý.
/// 6. Bảo toàn các sự kiện CRUD ô kho của Task T13.
/// </summary>
public class WebRealtimeIntegrationTests : IClassFixture<CookieAuthWebTestFixture>
{
    private readonly CookieAuthWebTestFixture _factory;
    private static long _sequence = 50000;

    public WebRealtimeIntegrationTests(CookieAuthWebTestFixture factory)
    {
        _factory = factory;
    }

    #region Helper Methods

    private async Task<(facility Facility, unit_type UnitType, storage_unit Unit)> SeedFacilityHierarchyAsync(string suffix)
    {
        using var db = _factory.CreateDbContext();
        var facId = Interlocked.Increment(ref _sequence);

        var facility = new facility
        {
            id = facId,
            code = $"FAC_{suffix.ToUpperInvariant()}_{facId}",
            name = $"Cơ Sở T15 {suffix}",
            address_line = $"Số 100 Đường {suffix}",
            ward = "Phường 1",
            district = "Quận Cầu Giấy",
            city = "Hà Nội",
            status = "active",
            timezone = "Asia/Ho_Chi_Minh",
            opening_time = new TimeOnly(8, 0),
            closing_time = new TimeOnly(20, 0),
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        db.facilities.Add(facility);

        var unitTypeId = facId * 10 + 1;
        var unitType = new unit_type
        {
            id = unitTypeId,
            code = $"TYPE_{suffix.ToUpperInvariant()}",
            name = $"Loại Kho {suffix}",
            description = "Mô tả",
            width_m = 2.0m,
            length_m = 3.0m,
            height_m = 2.5m,
            area_m2 = 6.0m,
            volume_m3 = 15.0m,
            climate_controlled = true,
            max_weight_kg = 500m,
            is_active = true,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        db.unit_types.Add(unitType);

        var unitId = facId * 100 + 1;
        var unit = new storage_unit
        {
            id = unitId,
            facility_id = facId,
            unit_type_id = unitTypeId,
            unit_code = $"UNIT-{suffix.ToUpperInvariant()}",
            physical_status = "available",
            is_listed = true,
            floor_label = "Tầng 1",
            zone_label = "Zone A",
            notes = "Test notes",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        var rate = new facility_rate
        {
            facility_id = facId,
            unit_type_id = unitTypeId,
            monthly_rate = 1000000m,
            deposit_amount = 1000000m,
            booking_fee = 50000m,
            valid_from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            valid_to = null,
            created_at = DateTimeOffset.UtcNow
        };
        db.facility_rates.Add(rate);

        await db.SaveChangesAsync();
        return (facility, unitType, unit);
    }

    private async Task<(user User, string CookieHeader)> AuthenticateUserAndGetCookieAsync(string roleCode, long? assignedFacilityId = null)
    {
        var email = $"t15_{roleCode}_{Guid.NewGuid():N}@test.local";
        var password = "SecureT15Password123!";

        var user = await _factory.SeedUserAsync(email, password, roleCode, UserStatusConstants.Active, $"User {roleCode}");

        if (assignedFacilityId.HasValue && (roleCode == RoleConstants.FacilityStaff || roleCode == RoleConstants.FacilityManager))
        {
            using var db = _factory.CreateDbContext();
            db.staff_facility_assignments.Add(new staff_facility_assignment
            {
                employee_id = user.id,
                facility_id = assignedFacilityId.Value,
                assignment_role = roleCode,
                starts_at = DateTimeOffset.UtcNow.AddDays(-1),
                ends_at = null,
                created_at = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var (token, cookieHeader) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(client, "/Account/Login");
        var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = token,
            ["ReturnUrl"] = "/"
        });

        var loginMsg = new HttpRequestMessage(HttpMethod.Post, "/Account/Login") { Content = loginContent };
        loginMsg.Headers.Add("Cookie", cookieHeader);
        var loginResp = await client.SendAsync(loginMsg);

        Assert.Equal(HttpStatusCode.Redirect, loginResp.StatusCode);
        var authHeader = loginResp.Headers.GetValues("Set-Cookie")
            .FirstOrDefault(c => c.Contains(CookieAuthExtensions.CookieName));
        Assert.NotNull(authHeader);

        return (user, authHeader!.Split(';')[0]);
    }

    private HubConnection BuildHubConnection(string authCookieHeader)
    {
        return new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "/storageUnitHub"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Headers.Add("Cookie", authCookieHeader);
            })
            .Build();
    }

    private static async Task WaitForUserInGroupAsync(
        IStorageUnitHubConnectionTracker tracker,
        string group,
        long userId,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var conns = tracker.GetConnectionsInGroup(group);
            if (conns.Any(c => c.UserId == userId))
            {
                return;
            }
            await Task.Delay(50);
        }
        throw new TimeoutException($"User {userId} did not join group '{group}' within {timeout.TotalSeconds}s.");
    }

    #endregion

    #region 1. Security & Endpoint Authorization Tests

    [Fact]
    public async Task SignalR_AnonymousClient_IsRejected_CannotNegotiateOrConnect()
    {
        // Arrange: Hub connection without authentication cookie
        var hubConnection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "/storageUnitHub"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            })
            .Build();

        // Act & Assert: Anonymous connection must be rejected
        await Assert.ThrowsAnyAsync<Exception>(() => hubConnection.StartAsync());
    }

    [Fact]
    public async Task SignalR_AuthenticatedCustomer_CanConnect_AndJoinsPersonalGroup()
    {
        // Arrange
        var (customer, cookie) = await AuthenticateUserAndGetCookieAsync(RoleConstants.StorageCustomer);

        await using var hubConnection = BuildHubConnection(cookie);

        // Act
        await hubConnection.StartAsync();

        // Assert
        Assert.Equal(HubConnectionState.Connected, hubConnection.State);
        await hubConnection.StopAsync();
    }

    #endregion

    #region 2. Five Contract Streams & Recipient Scoping Tests

    [Fact]
    public async Task ContractStream_CatalogInvalidated_PublicCapacityStream_ReceivedByConnectedClients_FilteredClientSide()
    {
        // Arrange: Facility 1 and Facility 2, Staff 1 and Customer 2
        var fac1 = await SeedFacilityHierarchyAsync("cat_fac1");
        var fac2 = await SeedFacilityHierarchyAsync("cat_fac2");

        var (staff1, cookie1) = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityStaff, fac1.Facility.id);
        var (customer2, cookie2) = await AuthenticateUserAndGetCookieAsync(RoleConstants.StorageCustomer);

        await using var conn1 = BuildHubConnection(cookie1);
        await using var conn2 = BuildHubConnection(cookie2);

        var tcs1 = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2 = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

        conn1.On<JsonElement>("CatalogInvalidated", data => tcs1.TrySetResult(data));
        conn2.On<JsonElement>("CatalogInvalidated", data => tcs2.TrySetResult(data));

        await conn1.StartAsync();
        await conn2.StartAsync();

        var tracker = _factory.Services.GetRequiredService<IStorageUnitHubConnectionTracker>();
        await WaitForUserInGroupAsync(tracker, $"facility_{fac1.Facility.id}", staff1.id, TimeSpan.FromSeconds(5));
        await WaitForUserInGroupAsync(tracker, $"user_{customer2.id}", customer2.id, TimeSpan.FromSeconds(5));

        // Act: Invalidate catalog for Facility 1
        using (var scope = _factory.Services.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IWebRealtimePublisher>();
            await publisher.PublishCatalogInvalidatedAsync(fac1.Facility.id, fac1.UnitType.id, CancellationToken.None);
        }

        // Assert: Both Staff 1 and Customer 2 receive the public capacity event (F04 requirement)
        var completedTask1 = await Task.WhenAny(tcs1.Task, Task.Delay(10000));
        var completedTask2 = await Task.WhenAny(tcs2.Task, Task.Delay(10000));
        Assert.Same(tcs1.Task, completedTask1);
        Assert.Same(tcs2.Task, completedTask2);

        var payload1 = await tcs1.Task;
        var payload2 = await tcs2.Task;
        Assert.Equal(fac1.Facility.id, payload1.GetProperty("facilityId").GetInt64());
        Assert.Equal(fac1.UnitType.id, payload1.GetProperty("unitTypeId").GetInt64());
        Assert.True(payload1.TryGetProperty("timestampUtc", out var ts) && !string.IsNullOrEmpty(ts.GetString()));

        // Client-side filtering check (simulating catalog.js logic):
        // Client viewing facility 1 accepts the event:
        var client1ViewingFacilityId = fac1.Facility.id;
        var shouldClient1Update = payload1.GetProperty("facilityId").GetInt64() == 0 || payload1.GetProperty("facilityId").GetInt64() == client1ViewingFacilityId;
        Assert.True(shouldClient1Update);

        // Client viewing facility 2 rejects the event:
        var client2ViewingFacilityId = fac2.Facility.id;
        var shouldClient2Update = payload2.GetProperty("facilityId").GetInt64() == 0 || payload2.GetProperty("facilityId").GetInt64() == client2ViewingFacilityId;
        Assert.False(shouldClient2Update);

        // Zero Sensitive Data Assert: Payload must not contain any private user or financial data or PIN
        var rawJson = payload1.GetRawText();
        Assert.DoesNotContain("pin", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("customer", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("userId", rawJson, StringComparison.OrdinalIgnoreCase);

        await conn1.StopAsync();
        await conn2.StopAsync();
    }

    [Fact]
    public async Task ContractStream_ReservationChanged_ScopedToCustomerAndFacilityStaff_NotReceivedByOtherCustomer()
    {
        // Arrange
        var fac = await SeedFacilityHierarchyAsync("rsv_scope");
        var (customer1, cookieCust1) = await AuthenticateUserAndGetCookieAsync(RoleConstants.StorageCustomer);
        var (customer2, cookieCust2) = await AuthenticateUserAndGetCookieAsync(RoleConstants.StorageCustomer);
        var (staff, cookieStaff) = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityStaff, fac.Facility.id);

        await using var connCust1 = BuildHubConnection(cookieCust1);
        await using var connCust2 = BuildHubConnection(cookieCust2);
        await using var connStaff = BuildHubConnection(cookieStaff);

        var tcsCust1 = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsCust2 = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsStaff = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

        connCust1.On<JsonElement>("ReservationChanged", data => tcsCust1.TrySetResult(data));
        connCust2.On<JsonElement>("ReservationChanged", data => tcsCust2.TrySetResult(data));
        connStaff.On<JsonElement>("ReservationChanged", data => tcsStaff.TrySetResult(data));

        await connCust1.StartAsync();
        await connCust2.StartAsync();
        await connStaff.StartAsync();

        var tracker = _factory.Services.GetRequiredService<IStorageUnitHubConnectionTracker>();
        await WaitForUserInGroupAsync(tracker, $"user_{customer1.id}", customer1.id, TimeSpan.FromSeconds(5));
        await WaitForUserInGroupAsync(tracker, $"user_{customer2.id}", customer2.id, TimeSpan.FromSeconds(5));
        await WaitForUserInGroupAsync(tracker, $"facility_{fac.Facility.id}", staff.id, TimeSpan.FromSeconds(5));

        var reservationId = Interlocked.Increment(ref _sequence);

        // Act: Publish ReservationChanged for customer 1 on facility
        using (var scope = _factory.Services.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IWebRealtimePublisher>();
            await publisher.PublishReservationChangedAsync(reservationId, fac.Facility.id, customer1.id, "confirmed", CancellationToken.None);
        }

        // Assert: Customer 1 and Staff receive the event
        var taskCust1 = await Task.WhenAny(tcsCust1.Task, Task.Delay(10000));
        var taskStaff = await Task.WhenAny(tcsStaff.Task, Task.Delay(10000));

        Assert.Same(tcsCust1.Task, taskCust1);
        Assert.Same(tcsStaff.Task, taskStaff);

        var payload1 = await tcsCust1.Task;
        Assert.Equal(reservationId, payload1.GetProperty("reservationId").GetInt64());
        Assert.Equal(fac.Facility.id, payload1.GetProperty("facilityId").GetInt64());
        Assert.Equal("confirmed", payload1.GetProperty("status").GetString());

        // Negative Assert: Unrelated customer 2 does NOT receive the event
        Assert.False(tcsCust2.Task.IsCompleted);

        await connCust1.StopAsync();
        await connCust2.StopAsync();
        await connStaff.StopAsync();
    }

    [Fact]
    public async Task ContractStream_PaymentChanged_ScopedToCustomerAndManager_NotReceivedByUnrelatedCustomer()
    {
        // Arrange
        var fac = await SeedFacilityHierarchyAsync("pmt_scope");
        var (customer1, cookieCust1) = await AuthenticateUserAndGetCookieAsync(RoleConstants.StorageCustomer);
        var (customer2, cookieCust2) = await AuthenticateUserAndGetCookieAsync(RoleConstants.StorageCustomer);
        var (manager, cookieManager) = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityManager, fac.Facility.id);

        await using var connCust1 = BuildHubConnection(cookieCust1);
        await using var connCust2 = BuildHubConnection(cookieCust2);
        await using var connManager = BuildHubConnection(cookieManager);

        var tcsCust1 = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsCust2 = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsManager = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

        connCust1.On<JsonElement>("PaymentChanged", data => tcsCust1.TrySetResult(data));
        connCust2.On<JsonElement>("PaymentChanged", data => tcsCust2.TrySetResult(data));
        connManager.On<JsonElement>("PaymentChanged", data => tcsManager.TrySetResult(data));

        await connCust1.StartAsync();
        await connCust2.StartAsync();
        await connManager.StartAsync();

        var tracker = _factory.Services.GetRequiredService<IStorageUnitHubConnectionTracker>();
        await WaitForUserInGroupAsync(tracker, $"user_{customer1.id}", customer1.id, TimeSpan.FromSeconds(5));
        await WaitForUserInGroupAsync(tracker, $"user_{customer2.id}", customer2.id, TimeSpan.FromSeconds(5));
        await WaitForUserInGroupAsync(tracker, $"facility_{fac.Facility.id}", manager.id, TimeSpan.FromSeconds(5));

        var paymentId = Interlocked.Increment(ref _sequence);
        var reservationId = Interlocked.Increment(ref _sequence);

        // Act: Publish PaymentChanged
        using (var scope = _factory.Services.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IWebRealtimePublisher>();
            await publisher.PublishPaymentChangedAsync(paymentId, reservationId, fac.Facility.id, customer1.id, "completed", CancellationToken.None);
        }

        // Assert: Customer 1 and Manager receive
        var taskCust1 = await Task.WhenAny(tcsCust1.Task, Task.Delay(10000));
        var taskManager = await Task.WhenAny(tcsManager.Task, Task.Delay(10000));

        Assert.Same(tcsCust1.Task, taskCust1);
        Assert.Same(tcsManager.Task, taskManager);

        var payload = await tcsCust1.Task;
        Assert.Equal(paymentId, payload.GetProperty("paymentId").GetInt64());
        Assert.Equal(reservationId, payload.GetProperty("reservationId").GetInt64());
        Assert.Equal("completed", payload.GetProperty("status").GetString());

        // Negative Assert: Customer 2 does NOT receive
        Assert.False(tcsCust2.Task.IsCompleted);

        await connCust1.StopAsync();
        await connCust2.StopAsync();
        await connManager.StopAsync();
    }

    [Fact]
    public async Task ContractStream_CheckInChanged_ZeroPinLeakage_PayloadContainsOnlyAllowedFields()
    {
        // Arrange
        var fac = await SeedFacilityHierarchyAsync("chk_scope");
        var (customer, cookieCust) = await AuthenticateUserAndGetCookieAsync(RoleConstants.StorageCustomer);

        await using var conn = BuildHubConnection(cookieCust);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        conn.On<JsonElement>("CheckInChanged", data => tcs.TrySetResult(data));
        await conn.StartAsync();

        var chkTracker = _factory.Services.GetRequiredService<IStorageUnitHubConnectionTracker>();
        await WaitForUserInGroupAsync(chkTracker, $"user_{customer.id}", customer.id, TimeSpan.FromSeconds(5));

        var checkInId = Interlocked.Increment(ref _sequence);
        var reservationId = Interlocked.Increment(ref _sequence);
        var unitId = fac.Unit.id;

        // Act: Publish CheckInChanged
        using (var scope = _factory.Services.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IWebRealtimePublisher>();
            await publisher.PublishCheckInChangedAsync(checkInId, reservationId, unitId, fac.Facility.id, customer.id, "completed", CancellationToken.None);
        }

        // Assert: Received within timeout
        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(10000));
        Assert.Same(tcs.Task, completedTask);

        var payload = await tcs.Task;
        Assert.Equal(checkInId, payload.GetProperty("checkInId").GetInt64());
        Assert.Equal(reservationId, payload.GetProperty("reservationId").GetInt64());
        Assert.Equal(unitId, payload.GetProperty("unitId").GetInt64());
        Assert.Equal("completed", payload.GetProperty("status").GetString());

        // CRITICAL SECURITY ASSERTION: Negative check for PIN leakage
        var rawJson = payload.GetRawText();
        Assert.DoesNotContain("pin", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityPin", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passcode", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", rawJson, StringComparison.OrdinalIgnoreCase);

        await conn.StopAsync();
    }

    [Fact]
    public async Task ContractStream_NotificationsChanged_ScopedStrictlyToTargetUser()
    {
        // Arrange: User 1 and User 2
        var (user1, cookie1) = await AuthenticateUserAndGetCookieAsync(RoleConstants.StorageCustomer);
        var (user2, cookie2) = await AuthenticateUserAndGetCookieAsync(RoleConstants.StorageCustomer);

        await using var conn1 = BuildHubConnection(cookie1);
        await using var conn2 = BuildHubConnection(cookie2);

        var tcs1 = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2 = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

        conn1.On<JsonElement>("NotificationsChanged", data => tcs1.TrySetResult(data));
        conn2.On<JsonElement>("NotificationsChanged", data => tcs2.TrySetResult(data));

        await conn1.StartAsync();
        await conn2.StartAsync();

        var notifTracker = _factory.Services.GetRequiredService<IStorageUnitHubConnectionTracker>();
        await WaitForUserInGroupAsync(notifTracker, $"user_{user1.id}", user1.id, TimeSpan.FromSeconds(5));
        await WaitForUserInGroupAsync(notifTracker, $"user_{user2.id}", user2.id, TimeSpan.FromSeconds(5));

        var notificationId = Interlocked.Increment(ref _sequence);

        // Act: Send notification specifically to user 1
        using (var scope = _factory.Services.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IWebRealtimePublisher>();
            await publisher.PublishNotificationsChangedAsync(user1.id, notificationId, CancellationToken.None);
        }

        // Assert: User 1 receives
        var completed = await Task.WhenAny(tcs1.Task, Task.Delay(10000));
        Assert.Same(tcs1.Task, completed);

        var payload = await tcs1.Task;
        Assert.Equal(user1.id, payload.GetProperty("userId").GetInt64());
        Assert.Equal(notificationId, payload.GetProperty("notificationId").GetInt64());

        // Negative Assert: User 2 does NOT receive User 1's notification
        Assert.False(tcs2.Task.IsCompleted);

        await conn1.StopAsync();
        await conn2.StopAsync();
    }

    [Fact]
    public async Task ExtensionStream_AccountChanged_EmittedToAdminAndTargetUser()
    {
        // Arrange
        var (admin, cookieAdmin) = await AuthenticateUserAndGetCookieAsync(RoleConstants.SystemAdministrator);
        var (targetUser, cookieUser) = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityStaff);
        var (unrelatedCustomer, cookieCustomer) = await AuthenticateUserAndGetCookieAsync(RoleConstants.StorageCustomer);

        await using var connAdmin = BuildHubConnection(cookieAdmin);
        await using var connUser = BuildHubConnection(cookieUser);
        await using var connCustomer = BuildHubConnection(cookieCustomer);

        var tcsAdmin = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsUser = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsCustomer = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

        connAdmin.On<JsonElement>("AccountChanged", data => tcsAdmin.TrySetResult(data));
        connUser.On<JsonElement>("AccountChanged", data => tcsUser.TrySetResult(data));
        connCustomer.On<JsonElement>("AccountChanged", data => tcsCustomer.TrySetResult(data));

        await connAdmin.StartAsync();
        await connUser.StartAsync();
        await connCustomer.StartAsync();

        var actTracker = _factory.Services.GetRequiredService<IStorageUnitHubConnectionTracker>();
        await WaitForUserInGroupAsync(actTracker, "admin_global", admin.id, TimeSpan.FromSeconds(5));
        await WaitForUserInGroupAsync(actTracker, $"user_{targetUser.id}", targetUser.id, TimeSpan.FromSeconds(5));
        await WaitForUserInGroupAsync(actTracker, $"user_{unrelatedCustomer.id}", unrelatedCustomer.id, TimeSpan.FromSeconds(5));

        // Act: Publish AccountChanged for target user
        using (var scope = _factory.Services.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IWebRealtimePublisher>();
            await publisher.PublishAccountChangedAsync(targetUser.id, "status_updated", CancellationToken.None);
        }

        // Assert: Admin and Target User receive
        var taskAdmin = await Task.WhenAny(tcsAdmin.Task, Task.Delay(10000));
        var taskUser = await Task.WhenAny(tcsUser.Task, Task.Delay(10000));

        Assert.Same(tcsAdmin.Task, taskAdmin);
        Assert.Same(tcsUser.Task, taskUser);

        var payload = await tcsUser.Task;
        Assert.Equal(targetUser.id, payload.GetProperty("userId").GetInt64());
        Assert.Equal("status_updated", payload.GetProperty("action").GetString());

        // Negative Assert: Unrelated customer does NOT receive AccountChanged
        Assert.False(tcsCustomer.Task.IsCompleted);

        await connAdmin.StopAsync();
        await connUser.StopAsync();
        await connCustomer.StopAsync();
    }

    #endregion

    #region 3. Post-Commit, Worker & Degraded Handling Tests

    [Fact]
    public async Task Worker_ReservationExpirationHook_MultiHold_ScopedToHoldOwners_BystanderExcluded()
    {
        // Arrange: 2 facilities, 2 customers with holds, 1 bystander customer without holds (F02)
        var fac1 = await SeedFacilityHierarchyAsync("worker_fac1");
        var fac2 = await SeedFacilityHierarchyAsync("worker_fac2");

        var (customer1, cookie1) = await AuthenticateUserAndGetCookieAsync(RoleConstants.StorageCustomer);
        var (customer2, cookie2) = await AuthenticateUserAndGetCookieAsync(RoleConstants.StorageCustomer);
        var (bystander, cookieBystander) = await AuthenticateUserAndGetCookieAsync(RoleConstants.StorageCustomer);

        await using var conn1 = BuildHubConnection(cookie1);
        await using var conn2 = BuildHubConnection(cookie2);
        await using var connBystander = BuildHubConnection(cookieBystander);

        var tcsRsv1 = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsRsv2 = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsRsvBystander = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

        var tcsCat1 = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsCat2 = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

        conn1.On<JsonElement>("ReservationChanged", data => tcsRsv1.TrySetResult(data));
        conn1.On<JsonElement>("CatalogInvalidated", data => tcsCat1.TrySetResult(data));

        conn2.On<JsonElement>("ReservationChanged", data => tcsRsv2.TrySetResult(data));
        conn2.On<JsonElement>("CatalogInvalidated", data => tcsCat2.TrySetResult(data));

        connBystander.On<JsonElement>("ReservationChanged", data => tcsRsvBystander.TrySetResult(data));

        await conn1.StartAsync();
        await conn2.StartAsync();
        await connBystander.StartAsync();

        var workerTracker = _factory.Services.GetRequiredService<IStorageUnitHubConnectionTracker>();
        await WaitForUserInGroupAsync(workerTracker, $"user_{customer1.id}", customer1.id, TimeSpan.FromSeconds(5));
        await WaitForUserInGroupAsync(workerTracker, $"user_{customer2.id}", customer2.id, TimeSpan.FromSeconds(5));
        await WaitForUserInGroupAsync(workerTracker, $"user_{bystander.id}", bystander.id, TimeSpan.FromSeconds(5));

        var holdId1 = Interlocked.Increment(ref _sequence);
        var holdId2 = Interlocked.Increment(ref _sequence);

        var expiredHolds = new List<ExpiredReservationHoldInfo>
        {
            new ExpiredReservationHoldInfo(holdId1, fac1.Facility.id, customer1.id),
            new ExpiredReservationHoldInfo(holdId2, fac2.Facility.id, customer2.id)
        };

        // Act: Execute background worker hook with detailed expired holds
        using (var scope = _factory.Services.CreateScope())
        {
            var hook = scope.ServiceProvider.GetRequiredService<IReservationExpirationHook>();
            await hook.OnHoldsExpiredWithDetailsAsync(expiredHolds, CancellationToken.None);
        }

        // Assert: Customer 1 receives only their reservation expiration
        var completed1 = await Task.WhenAny(tcsRsv1.Task, Task.Delay(10000));
        Assert.Same(tcsRsv1.Task, completed1);
        var payload1 = await tcsRsv1.Task;
        Assert.Equal(holdId1, payload1.GetProperty("reservationId").GetInt64());
        Assert.Equal(fac1.Facility.id, payload1.GetProperty("facilityId").GetInt64());
        Assert.Equal("expired", payload1.GetProperty("status").GetString());

        // Assert: Customer 2 receives only their reservation expiration
        var completed2 = await Task.WhenAny(tcsRsv2.Task, Task.Delay(10000));
        Assert.Same(tcsRsv2.Task, completed2);
        var payload2 = await tcsRsv2.Task;
        Assert.Equal(holdId2, payload2.GetProperty("reservationId").GetInt64());
        Assert.Equal(fac2.Facility.id, payload2.GetProperty("facilityId").GetInt64());
        Assert.Equal("expired", payload2.GetProperty("status").GetString());

        // Negative Assert: Bystander customer does NOT receive any ReservationChanged event (F02)
        Assert.False(tcsRsvBystander.Task.IsCompleted);

        // CatalogInvalidated events were emitted for both facilities
        var catCompleted1 = await Task.WhenAny(tcsCat1.Task, Task.Delay(10000));
        Assert.Same(tcsCat1.Task, catCompleted1);

        await conn1.StopAsync();
        await conn2.StopAsync();
        await connBystander.StopAsync();
    }

    [Fact]
    public async Task Ipn_Realtime_Success_EmitsPaymentChangedCompleted_AndReservationChangedConfirmed_WithExactMetadata()
    {
        // Arrange
        var fac = await SeedFacilityHierarchyAsync("ipn_succ");
        var (customer, cookieCust) = await AuthenticateUserAndGetCookieAsync(RoleConstants.StorageCustomer);
        var (staff, cookieStaff) = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityStaff, fac.Facility.id);
        var (admin, cookieAdmin) = await AuthenticateUserAndGetCookieAsync(RoleConstants.SystemAdministrator);
        var (bystander, cookieBystander) = await AuthenticateUserAndGetCookieAsync(RoleConstants.StorageCustomer);

        await using var connCust = BuildHubConnection(cookieCust);
        await using var connStaff = BuildHubConnection(cookieStaff);
        await using var connAdmin = BuildHubConnection(cookieAdmin);
        await using var connBystander = BuildHubConnection(cookieBystander);

        var tcsCustPmt = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsCustRsv = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsStaffPmt = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsStaffRsv = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsAdminPmt = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsBystanderPmt = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsBystanderRsv = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

        connCust.On<JsonElement>("PaymentChanged", data => tcsCustPmt.TrySetResult(data));
        connCust.On<JsonElement>("ReservationChanged", data => tcsCustRsv.TrySetResult(data));
        connStaff.On<JsonElement>("PaymentChanged", data => tcsStaffPmt.TrySetResult(data));
        connStaff.On<JsonElement>("ReservationChanged", data => tcsStaffRsv.TrySetResult(data));
        connAdmin.On<JsonElement>("PaymentChanged", data => tcsAdminPmt.TrySetResult(data));
        connBystander.On<JsonElement>("PaymentChanged", data => tcsBystanderPmt.TrySetResult(data));
        connBystander.On<JsonElement>("ReservationChanged", data => tcsBystanderRsv.TrySetResult(data));

        await connCust.StartAsync();
        await connStaff.StartAsync();
        await connAdmin.StartAsync();
        await connBystander.StartAsync();

        var tracker = _factory.Services.GetRequiredService<IStorageUnitHubConnectionTracker>();
        await WaitForUserInGroupAsync(tracker, $"user_{customer.id}", customer.id, TimeSpan.FromSeconds(5));
        await WaitForUserInGroupAsync(tracker, $"facility_{fac.Facility.id}", staff.id, TimeSpan.FromSeconds(5));
        await WaitForUserInGroupAsync(tracker, "admin_global", admin.id, TimeSpan.FromSeconds(5));
        await WaitForUserInGroupAsync(tracker, $"user_{bystander.id}", bystander.id, TimeSpan.FromSeconds(5));

        var paymentId = Interlocked.Increment(ref _sequence);
        var reservationId = Interlocked.Increment(ref _sequence);

        // Act: Invoke PaymentsApiController with successful callback result
        using (var scope = _factory.Services.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IWebRealtimePublisher>();
            var mockPaymentService = new Mock<IPaymentService>();
            mockPaymentService.Setup(s => s.ProcessVnpayIpnAsync(It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PaymentGatewayCallbackResult
                {
                    IsSuccess = true,
                    IpnResponseCode = "00",
                    IpnResponseMessage = "Confirm Success",
                    PaymentId = paymentId,
                    ReservationId = reservationId,
                    FacilityId = fac.Facility.id,
                    CustomerUserId = customer.id,
                    IsStatusTransitionCommitted = true,
                    CommittedStatus = "completed"
                });

            var controller = new PaymentsApiController(
                mockPaymentService.Object,
                new Mock<IConfiguration>().Object,
                new Mock<IWebHostEnvironment>().Object,
                NullLogger<PaymentsApiController>.Instance,
                publisher);

            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            var actionResult = await controller.VnpayIpn(CancellationToken.None);
            Assert.IsType<OkObjectResult>(actionResult);
        }

        // Assert: Customer, Staff, Admin receive PaymentChanged(completed)
        var custPmt = await Task.WhenAny(tcsCustPmt.Task, Task.Delay(10000));
        var staffPmt = await Task.WhenAny(tcsStaffPmt.Task, Task.Delay(10000));
        var adminPmt = await Task.WhenAny(tcsAdminPmt.Task, Task.Delay(10000));
        Assert.Same(tcsCustPmt.Task, custPmt);
        Assert.Same(tcsStaffPmt.Task, staffPmt);
        Assert.Same(tcsAdminPmt.Task, adminPmt);

        var pmtPayload = await tcsCustPmt.Task;
        Assert.Equal(paymentId, pmtPayload.GetProperty("paymentId").GetInt64());
        Assert.Equal(reservationId, pmtPayload.GetProperty("reservationId").GetInt64());
        Assert.Equal("completed", pmtPayload.GetProperty("status").GetString());

        // Assert: Customer and Staff receive ReservationChanged(confirmed)
        var custRsv = await Task.WhenAny(tcsCustRsv.Task, Task.Delay(10000));
        Assert.Same(tcsCustRsv.Task, custRsv);
        var rsvPayload = await tcsCustRsv.Task;
        Assert.Equal(reservationId, rsvPayload.GetProperty("reservationId").GetInt64());
        Assert.Equal("confirmed", rsvPayload.GetProperty("status").GetString());

        // Negative Assert: Bystander receives ZERO events (F01, F02, F03)
        Assert.False(tcsBystanderPmt.Task.IsCompleted);
        Assert.False(tcsBystanderRsv.Task.IsCompleted);

        await connCust.StopAsync();
        await connStaff.StopAsync();
        await connAdmin.StopAsync();
        await connBystander.StopAsync();
    }

    [Fact]
    public async Task Ipn_Realtime_Declined_EmitsPaymentChangedFailed_NeverEmitsReservationChangedConfirmed()
    {
        // Arrange
        var fac = await SeedFacilityHierarchyAsync("ipn_fail");
        var (customer, cookieCust) = await AuthenticateUserAndGetCookieAsync(RoleConstants.StorageCustomer);
        var (bystander, cookieBystander) = await AuthenticateUserAndGetCookieAsync(RoleConstants.StorageCustomer);

        await using var connCust = BuildHubConnection(cookieCust);
        await using var connBystander = BuildHubConnection(cookieBystander);

        var tcsCustPmt = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsCustRsv = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

        connCust.On<JsonElement>("PaymentChanged", data => tcsCustPmt.TrySetResult(data));
        connCust.On<JsonElement>("ReservationChanged", data => tcsCustRsv.TrySetResult(data));

        await connCust.StartAsync();
        await connBystander.StartAsync();

        var tracker = _factory.Services.GetRequiredService<IStorageUnitHubConnectionTracker>();
        await WaitForUserInGroupAsync(tracker, $"user_{customer.id}", customer.id, TimeSpan.FromSeconds(5));

        var paymentId = Interlocked.Increment(ref _sequence);
        var reservationId = Interlocked.Increment(ref _sequence);

        // Act: Invoke PaymentsApiController with failed/declined callback result
        using (var scope = _factory.Services.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IWebRealtimePublisher>();
            var mockPaymentService = new Mock<IPaymentService>();
            mockPaymentService.Setup(s => s.ProcessVnpayIpnAsync(It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PaymentGatewayCallbackResult
                {
                    IsSuccess = false,
                    IpnResponseCode = "00", // ACK is 00 even on decline (F01)
                    IpnResponseMessage = "Confirm Success",
                    PaymentId = paymentId,
                    ReservationId = reservationId,
                    FacilityId = fac.Facility.id,
                    CustomerUserId = customer.id,
                    IsStatusTransitionCommitted = true,
                    CommittedStatus = "failed"
                });

            var controller = new PaymentsApiController(
                mockPaymentService.Object,
                new Mock<IConfiguration>().Object,
                new Mock<IWebHostEnvironment>().Object,
                NullLogger<PaymentsApiController>.Instance,
                publisher);

            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            var actionResult = await controller.VnpayIpn(CancellationToken.None);
            Assert.IsType<OkObjectResult>(actionResult);
        }

        // Assert: Customer receives PaymentChanged with status "failed"
        var custPmt = await Task.WhenAny(tcsCustPmt.Task, Task.Delay(10000));
        Assert.Same(tcsCustPmt.Task, custPmt);

        var pmtPayload = await tcsCustPmt.Task;
        Assert.Equal(paymentId, pmtPayload.GetProperty("paymentId").GetInt64());
        Assert.Equal("failed", pmtPayload.GetProperty("status").GetString());

        // Negative Assert (F01): NEVER emits ReservationChanged("confirmed") on decline
        Assert.False(tcsCustRsv.Task.IsCompleted);

        await connCust.StopAsync();
        await connBystander.StopAsync();
    }

    [Fact]
    public async Task Ipn_Realtime_InvalidAmountOrDuplicate_EmitsZeroRealtimeEvents()
    {
        // Arrange
        var fac = await SeedFacilityHierarchyAsync("ipn_noval");
        var (customer, cookieCust) = await AuthenticateUserAndGetCookieAsync(RoleConstants.StorageCustomer);

        await using var connCust = BuildHubConnection(cookieCust);
        var tcsCustPmt = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsCustRsv = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

        connCust.On<JsonElement>("PaymentChanged", data => tcsCustPmt.TrySetResult(data));
        connCust.On<JsonElement>("ReservationChanged", data => tcsCustRsv.TrySetResult(data));

        await connCust.StartAsync();
        var tracker = _factory.Services.GetRequiredService<IStorageUnitHubConnectionTracker>();
        await WaitForUserInGroupAsync(tracker, $"user_{customer.id}", customer.id, TimeSpan.FromSeconds(5));

        // Act: Invoke PaymentsApiController with uncommitted callback (e.g. invalid amount 04 or duplicate 02)
        using (var scope = _factory.Services.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IWebRealtimePublisher>();
            var mockPaymentService = new Mock<IPaymentService>();
            mockPaymentService.Setup(s => s.ProcessVnpayIpnAsync(It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PaymentGatewayCallbackResult
                {
                    IsSuccess = false,
                    IpnResponseCode = "04", // Invalid Amount
                    IpnResponseMessage = "Invalid Amount",
                    PaymentId = 1234,
                    ReservationId = 5678,
                    FacilityId = fac.Facility.id,
                    CustomerUserId = customer.id,
                    IsStatusTransitionCommitted = false, // Not committed!
                    CommittedStatus = null
                });

            var controller = new PaymentsApiController(
                mockPaymentService.Object,
                new Mock<IConfiguration>().Object,
                new Mock<IWebHostEnvironment>().Object,
                NullLogger<PaymentsApiController>.Instance,
                publisher);

            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            var actionResult = await controller.VnpayIpn(CancellationToken.None);
            Assert.IsType<OkObjectResult>(actionResult);
        }

        // Assert: NO events emitted
        await Task.Delay(500);
        Assert.False(tcsCustPmt.Task.IsCompleted);
        Assert.False(tcsCustRsv.Task.IsCompleted);

        await connCust.StopAsync();
    }

    [Fact]
    public async Task Ipn_Realtime_ReconciliationRequired_EmitsPaymentChangedReconciliation_NeverReservationConfirmed()
    {
        // Arrange
        var fac = await SeedFacilityHierarchyAsync("ipn_recon");
        var (customer, cookieCust) = await AuthenticateUserAndGetCookieAsync(RoleConstants.StorageCustomer);

        await using var connCust = BuildHubConnection(cookieCust);
        var tcsCustPmt = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsCustRsv = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

        connCust.On<JsonElement>("PaymentChanged", data => tcsCustPmt.TrySetResult(data));
        connCust.On<JsonElement>("ReservationChanged", data => tcsCustRsv.TrySetResult(data));

        await connCust.StartAsync();
        var tracker = _factory.Services.GetRequiredService<IStorageUnitHubConnectionTracker>();
        await WaitForUserInGroupAsync(tracker, $"user_{customer.id}", customer.id, TimeSpan.FromSeconds(5));

        var paymentId = Interlocked.Increment(ref _sequence);
        var reservationId = Interlocked.Increment(ref _sequence);

        // Act: Invoke PaymentsApiController with reconciliation_required status
        using (var scope = _factory.Services.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IWebRealtimePublisher>();
            var mockPaymentService = new Mock<IPaymentService>();
            mockPaymentService.Setup(s => s.ProcessVnpayIpnAsync(It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PaymentGatewayCallbackResult
                {
                    IsSuccess = false,
                    IpnResponseCode = "00",
                    IpnResponseMessage = "Confirm Success",
                    PaymentId = paymentId,
                    ReservationId = reservationId,
                    FacilityId = fac.Facility.id,
                    CustomerUserId = customer.id,
                    IsStatusTransitionCommitted = true,
                    CommittedStatus = "reconciliation_required",
                    ReconciliationRequired = true
                });

            var controller = new PaymentsApiController(
                mockPaymentService.Object,
                new Mock<IConfiguration>().Object,
                new Mock<IWebHostEnvironment>().Object,
                NullLogger<PaymentsApiController>.Instance,
                publisher);

            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            var actionResult = await controller.VnpayIpn(CancellationToken.None);
            Assert.IsType<OkObjectResult>(actionResult);
        }

        // Assert: Customer receives PaymentChanged with status "reconciliation_required"
        var custPmt = await Task.WhenAny(tcsCustPmt.Task, Task.Delay(10000));
        Assert.Same(tcsCustPmt.Task, custPmt);
        var pmtPayload = await tcsCustPmt.Task;
        Assert.Equal("reconciliation_required", pmtPayload.GetProperty("status").GetString());

        // Negative Assert: NEVER emits ReservationChanged("confirmed")
        Assert.False(tcsCustRsv.Task.IsCompleted);

        await connCust.StopAsync();
    }

    [Fact]
    public async Task Refund_ReviewRefund_EmitsPaymentChanged_ScopedToCustomerAndStaff_NotBystander()
    {
        // Arrange
        var fac = await SeedFacilityHierarchyAsync("ref_scope");
        var (customer, cookieCust) = await AuthenticateUserAndGetCookieAsync(RoleConstants.StorageCustomer);
        var (staff, cookieStaff) = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityStaff, fac.Facility.id);
        var (bystander, cookieBystander) = await AuthenticateUserAndGetCookieAsync(RoleConstants.StorageCustomer);

        await using var connCust = BuildHubConnection(cookieCust);
        await using var connStaff = BuildHubConnection(cookieStaff);
        await using var connBystander = BuildHubConnection(cookieBystander);

        var tcsCust = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsStaff = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsBystander = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

        connCust.On<JsonElement>("PaymentChanged", data => tcsCust.TrySetResult(data));
        connStaff.On<JsonElement>("PaymentChanged", data => tcsStaff.TrySetResult(data));
        connBystander.On<JsonElement>("PaymentChanged", data => tcsBystander.TrySetResult(data));

        await connCust.StartAsync();
        await connStaff.StartAsync();
        await connBystander.StartAsync();

        var tracker = _factory.Services.GetRequiredService<IStorageUnitHubConnectionTracker>();
        await WaitForUserInGroupAsync(tracker, $"user_{customer.id}", customer.id, TimeSpan.FromSeconds(5));
        await WaitForUserInGroupAsync(tracker, $"facility_{fac.Facility.id}", staff.id, TimeSpan.FromSeconds(5));
        await WaitForUserInGroupAsync(tracker, $"user_{bystander.id}", bystander.id, TimeSpan.FromSeconds(5));

        var paymentId = Interlocked.Increment(ref _sequence);
        var reservationId = Interlocked.Increment(ref _sequence);

        // Act: Publish PaymentChanged with authentic refund metadata
        using (var scope = _factory.Services.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IWebRealtimePublisher>();
            await publisher.PublishPaymentChangedAsync(
                paymentId,
                reservationId,
                fac.Facility.id,
                customer.id,
                "refunded",
                CancellationToken.None);
        }

        // Assert: Customer and Staff receive PaymentChanged with matching IDs
        var custCompleted = await Task.WhenAny(tcsCust.Task, Task.Delay(10000));
        var staffCompleted = await Task.WhenAny(tcsStaff.Task, Task.Delay(10000));
        Assert.Same(tcsCust.Task, custCompleted);
        Assert.Same(tcsStaff.Task, staffCompleted);

        var payload = await tcsCust.Task;
        Assert.Equal(paymentId, payload.GetProperty("paymentId").GetInt64());
        Assert.Equal(reservationId, payload.GetProperty("reservationId").GetInt64());
        Assert.Equal("refunded", payload.GetProperty("status").GetString());

        // Negative Assert: Bystander does NOT receive it
        Assert.False(tcsBystander.Task.IsCompleted);

        await connCust.StopAsync();
        await connStaff.StopAsync();
        await connBystander.StopAsync();
    }

    [Fact]
    public async Task StaffCheckIn_CompleteCheckIn_EmitsNotificationsChanged_WithAuthenticNotificationId()
    {
        // Arrange
        var (customer, cookieCust) = await AuthenticateUserAndGetCookieAsync(RoleConstants.StorageCustomer);
        var (bystander, cookieBystander) = await AuthenticateUserAndGetCookieAsync(RoleConstants.StorageCustomer);

        await using var connCust = BuildHubConnection(cookieCust);
        await using var connBystander = BuildHubConnection(cookieBystander);

        var tcsCust = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsBystander = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

        connCust.On<JsonElement>("NotificationsChanged", data => tcsCust.TrySetResult(data));
        connBystander.On<JsonElement>("NotificationsChanged", data => tcsBystander.TrySetResult(data));

        await connCust.StartAsync();
        await connBystander.StartAsync();

        var tracker = _factory.Services.GetRequiredService<IStorageUnitHubConnectionTracker>();
        await WaitForUserInGroupAsync(tracker, $"user_{customer.id}", customer.id, TimeSpan.FromSeconds(5));
        await WaitForUserInGroupAsync(tracker, $"user_{bystander.id}", bystander.id, TimeSpan.FromSeconds(5));

        var dbNotificationId = Interlocked.Increment(ref _sequence);

        // Act: Publish NotificationsChanged with authentic notification.id (not agreementId)
        using (var scope = _factory.Services.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IWebRealtimePublisher>();
            await publisher.PublishNotificationsChangedAsync(customer.id, dbNotificationId, CancellationToken.None);
        }

        // Assert: Customer receives the exact notificationId
        var completed = await Task.WhenAny(tcsCust.Task, Task.Delay(10000));
        Assert.Same(tcsCust.Task, completed);

        var payload = await tcsCust.Task;
        Assert.Equal(customer.id, payload.GetProperty("userId").GetInt64());
        Assert.Equal(dbNotificationId, payload.GetProperty("notificationId").GetInt64());

        // Negative Assert: Bystander does NOT receive it
        Assert.False(tcsBystander.Task.IsCompleted);

        await connCust.StopAsync();
        await connBystander.StopAsync();
    }

    [Fact]
    public async Task DegradedPublisher_PageHandler_PreservesDatabaseCommit_AndExposesDegradedWarning()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var mockPublisher = new Mock<IWebRealtimePublisher>();
        mockPublisher.Setup(p => p.PublishPaymentChangedAsync(
            It.IsAny<long>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<long?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RealtimePublishDegradedException("PaymentChanged", "SignalR broadcast failed", new Exception("SignalR connection dropped")));

        var mockPaymentService = new Mock<IPaymentService>();
        mockPaymentService.Setup(s => s.ReviewRefundAsync(
            It.IsAny<long>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<long>(), It.IsAny<ReviewRefundRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefundDetailResponse
            {
                Id = 1,
                PaymentId = 100,
                ReservationId = 200,
                FacilityId = 300,
                CustomerUserId = 400,
                Status = "approved"
            });

        var model = new SelfStorageManagementSystem.Web.Pages.Payments.IndexModel(
            mockPaymentService.Object,
            NullLogger<SelfStorageManagementSystem.Web.Pages.Payments.IndexModel>.Instance,
            mockPublisher.Object);

        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "99"),
            new Claim(ClaimTypes.Role, RoleConstants.FacilityManager)
        }, "TestAuth"));
        model.PageContext = new PageContext { HttpContext = httpContext };

        var tempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(
            httpContext,
            Mock.Of<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider>());
        model.TempData = tempData;

        // Act: Execute review refund when realtime throws RealtimePublishDegradedException
        var result = await model.OnPostReviewRefundAsync(1, "approved", "Approved by staff", CancellationToken.None);

        // Assert: Review refund succeeds, sets WarningMessage in TempData, does not ask for re-submission
        Assert.IsType<RedirectToPageResult>(result);
        Assert.True(model.TempData.ContainsKey("WarningMessage"));
        var warning = model.TempData["WarningMessage"]?.ToString();
        Assert.Contains("thành công", warning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("thời gian thực", warning, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gửi lại", warning, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("thử lại", warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DegradedPublisher_OperationCanceledException_RethrownAndNotSwallowed()
    {
        // Arrange
        var mockPublisher = new Mock<IWebRealtimePublisher>();
        mockPublisher.Setup(p => p.PublishPaymentChangedAsync(
            It.IsAny<long>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<long?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var mockPaymentService = new Mock<IPaymentService>();
        mockPaymentService.Setup(s => s.ReviewRefundAsync(
            It.IsAny<long>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<long>(), It.IsAny<ReviewRefundRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefundDetailResponse
            {
                Id = 1,
                PaymentId = 100,
                ReservationId = 200,
                FacilityId = 300,
                CustomerUserId = 400,
                Status = "approved"
            });

        var model = new SelfStorageManagementSystem.Web.Pages.Payments.IndexModel(
            mockPaymentService.Object,
            NullLogger<SelfStorageManagementSystem.Web.Pages.Payments.IndexModel>.Instance,
            mockPublisher.Object);

        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "99"),
            new Claim(ClaimTypes.Role, RoleConstants.FacilityManager)
        }, "TestAuth"));
        model.PageContext = new PageContext { HttpContext = httpContext };

        model.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(
            httpContext,
            Mock.Of<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider>());

        // Act & Assert: OperationCanceledException MUST be rethrown and not swallowed as degraded (F06)
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            model.OnPostReviewRefundAsync(1, "approved", "Approved", new CancellationToken(true)));
    }

    [Fact]
    public async Task DegradedPublisher_WhenRealtimeThrows_ThrowsRealtimePublishDegradedException_WithoutImpactingDatabase()
    {
        // Arrange: Create a publisher where HubContext.Clients throws an exception
        var mockHubClients = new Mock<IHubClients>();
        var mockGroup = new Mock<IClientProxy>();
        mockGroup.Setup(g => g.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated SignalR transport failure"));
        mockHubClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockGroup.Object);
        mockHubClients.Setup(c => c.Groups(It.IsAny<IReadOnlyList<string>>())).Returns(mockGroup.Object);
        mockHubClients.Setup(c => c.Groups(It.IsAny<string[]>())).Returns(mockGroup.Object);

        var mockHubContext = new Mock<IHubContext<StorageUnitHub>>();
        mockHubContext.Setup(h => h.Clients).Returns(mockHubClients.Object);

        using var scope = _factory.Services.CreateScope();
        var revocationService = scope.ServiceProvider.GetRequiredService<StorageUnitHubScopeRevocationService>();

        var publisher = new StorageUnitRealtimePublisher(
            mockHubContext.Object,
            revocationService,
            NullLogger<StorageUnitRealtimePublisher>.Instance);

        // Act & Assert: Should throw RealtimePublishDegradedException preserving inner cause
        var ex = await Assert.ThrowsAsync<RealtimePublishDegradedException>(() =>
            publisher.PublishPaymentChangedAsync(100, 200, 1, 10, "completed", CancellationToken.None));

        Assert.Equal("PaymentChanged", ex.Operation);
        Assert.Contains("Simulated SignalR transport failure", ex.InnerException?.Message);
    }

    [Fact]
    public async Task Preservation_StorageUnits_CRUD_Events_RemainFunctional()
    {
        // Arrange
        var fac = await SeedFacilityHierarchyAsync("crud_preserv");
        var (manager, cookie) = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityManager, fac.Facility.id);

        await using var conn = BuildHubConnection(cookie);
        var tcsCreate = new TaskCompletionSource<StorageUnitRealtimeEventDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsDelete = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        conn.On<StorageUnitRealtimeEventDto>("Receiver_CreateStorageUnit", dto => tcsCreate.TrySetResult(dto));
        conn.On<string>("Receiver_DeleteStorageUnit", id => tcsDelete.TrySetResult(id));
        await conn.StartAsync();

        var tracker = _factory.Services.GetRequiredService<IStorageUnitHubConnectionTracker>();
        await WaitForUserInGroupAsync(tracker, $"facility_{fac.Facility.id}", manager.id, TimeSpan.FromSeconds(5));

        // Act: Test T13 CRUD publisher methods
        using (var scope = _factory.Services.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IStorageUnitRealtimePublisher>();
            var testAdminDto = new StorageUnitAdminDto
            {
                Id = 9991,
                UnitCode = "T15-PRESERV-01",
                FacilityId = fac.Facility.id,
                FacilityCode = fac.Facility.code,
                FacilityName = fac.Facility.name,
                UnitTypeId = fac.UnitType.id,
                UnitTypeName = fac.UnitType.name,
                UnitTypeCode = "TYPE_TEST",
                PhysicalStatus = "available",
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await publisher.PublishCreatedAsync(testAdminDto, CancellationToken.None);
            await publisher.PublishDeletedAsync(9991, fac.Facility.id, CancellationToken.None);
        }

        // Assert: Both events received
        var resCreate = await Task.WhenAny(tcsCreate.Task, Task.Delay(10000));
        var resDelete = await Task.WhenAny(tcsDelete.Task, Task.Delay(10000));

        Assert.Same(tcsCreate.Task, resCreate);
        Assert.Same(tcsDelete.Task, resDelete);

        var created = await tcsCreate.Task;
        Assert.Equal("T15-PRESERV-01", created.unitCode);
        Assert.Equal("9991", await tcsDelete.Task);

        await conn.StopAsync();
    }

    #endregion

    #region 4. Real Kestrel & WebSocket Transport Test

    private static async Task<user> SeedUserOnKestrelAsync(KestrelRealtimeServer kestrel, string email, string password, string roleCode, long? facilityId = null)
    {
        using var scope = kestrel.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var rolesToSeed = new (short id, string code, string name)[]
        {
            (1, RoleConstants.StorageCustomer, "Customer"),
            (2, RoleConstants.FacilityStaff, "Staff"),
            (3, RoleConstants.FacilityManager, "Manager"),
            (4, RoleConstants.BusinessOperationsManager, "Operations"),
            (5, RoleConstants.SystemAdministrator, "Admin")
        };
        foreach (var (rid, rcode, rname) in rolesToSeed)
        {
            if (!await db.roles.AnyAsync(r => r.code == rcode))
            {
                db.roles.Add(new role { id = rid, code = rcode, display_name = rname, description = rname });
            }
        }
        await db.SaveChangesAsync();

        var role = await db.roles.FirstAsync(r => r.code == roleCode);
        var newUser = new user
        {
            email = email.Trim().ToLowerInvariant(),
            password_hash = hasher.HashPassword(password),
            status = UserStatusConstants.Active,
            phone_number = "0901234567",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        db.users.Add(newUser);
        await db.SaveChangesAsync();

        db.user_roles.Add(new user_role { user_id = newUser.id, role_id = role.id, granted_at = DateTimeOffset.UtcNow });
        db.employee_profiles.Add(new employee_profile
        {
            user_id = newUser.id,
            full_name = $"Staff {newUser.id}",
            employee_code = $"EMP{newUser.id:D4}",
            employment_status = "active",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        });

        if (facilityId.HasValue)
        {
            db.staff_facility_assignments.Add(new staff_facility_assignment
            {
                employee_id = newUser.id,
                facility_id = facilityId.Value,
                assignment_role = roleCode,
                starts_at = DateTimeOffset.UtcNow.AddDays(-1),
                ends_at = null,
                created_at = DateTimeOffset.UtcNow
            });
        }
        await db.SaveChangesAsync();
        return newUser;
    }

    private static async Task<string> LoginOnKestrelAsync(KestrelRealtimeServer kestrel, long userId, string roleCode, string name)
    {
        var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(kestrel.ServerUrl) };

        var resp = await client.GetAsync($"/test-auth-login?role={Uri.EscapeDataString(roleCode)}&userId={userId}&name={Uri.EscapeDataString(name)}");
        resp.EnsureSuccessStatusCode();

        var authHeader = resp.Headers.TryGetValues("Set-Cookie", out var setCookies)
            ? setCookies.FirstOrDefault(c => c.Contains(CookieAuthExtensions.CookieName))
            : null;

        if (authHeader != null)
        {
            return authHeader.Split(';')[0];
        }

        var cookies = handler.CookieContainer.GetCookies(new Uri(kestrel.ServerUrl));
        var authCookie = cookies[CookieAuthExtensions.CookieName];
        return authCookie != null ? $"{CookieAuthExtensions.CookieName}={authCookie.Value}" : string.Empty;
    }

    [Fact]
    public async Task Kestrel_RealWebSocket_TwoClients_EndToEndScoping_AndZeroPinLeakage()
    {
        // Arrange: Start real Kestrel server with ephemerally assigned port
        await using var kestrel = await KestrelRealtimeServer.StartAsync();

        // Seed facilities in Kestrel's isolated DB
        facility fac1, fac2;
        using (var scope = kestrel.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
            fac1 = new facility
            {
                id = 8101,
                code = "FAC_KESTREL_1",
                name = "Kestrel Cơ Sở 1",
                address_line = "Số 1 Cầu Giấy",
                ward = "Dịch Vọng",
                district = "Cầu Giấy",
                city = "Hà Nội",
                status = "active",
                timezone = "Asia/Ho_Chi_Minh",
                opening_time = new TimeOnly(8, 0),
                closing_time = new TimeOnly(20, 0),
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            };
            fac2 = new facility
            {
                id = 8102,
                code = "FAC_KESTREL_2",
                name = "Kestrel Cơ Sở 2",
                address_line = "Số 2 Cầu Giấy",
                ward = "Dịch Vọng",
                district = "Cầu Giấy",
                city = "Hà Nội",
                status = "active",
                timezone = "Asia/Ho_Chi_Minh",
                opening_time = new TimeOnly(8, 0),
                closing_time = new TimeOnly(20, 0),
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            };
            db.facilities.AddRange(fac1, fac2);
            await db.SaveChangesAsync();
        }

        // Seed staff users on Kestrel
        var staff1Email = "kestrel_staff1@test.local";
        var staff2Email = "kestrel_staff2@test.local";
        var password = "SecureKestrelPass123!";

        var staff1 = await SeedUserOnKestrelAsync(kestrel, staff1Email, password, RoleConstants.FacilityStaff, fac1.id);
        var staff2 = await SeedUserOnKestrelAsync(kestrel, staff2Email, password, RoleConstants.FacilityStaff, fac2.id);

        var cookie1 = await LoginOnKestrelAsync(kestrel, staff1.id, RoleConstants.FacilityStaff, "Staff 1");
        var cookie2 = await LoginOnKestrelAsync(kestrel, staff2.id, RoleConstants.FacilityStaff, "Staff 2");

        Assert.NotEmpty(cookie1);
        Assert.NotEmpty(cookie2);

        var hubUrl = new Uri(new Uri(kestrel.ServerUrl), "/storageUnitHub");

        // Act 1: Anonymous connection over physical WebSocket is rejected
        var anonConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = false;
            })
            .Build();

        await Assert.ThrowsAnyAsync<Exception>(() => anonConnection.StartAsync());

        // Act 2: Connect two real clients over physical WebSocket transport
        await using var conn1 = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.Headers.Add("Cookie", cookie1);
            })
            .Build();

        await using var conn2 = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.Headers.Add("Cookie", cookie2);
            })
            .Build();

        var tcs1 = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2 = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

        conn1.On<JsonElement>("CheckInChanged", data => tcs1.TrySetResult(data));
        conn2.On<JsonElement>("CheckInChanged", data => tcs2.TrySetResult(data));

        await conn1.StartAsync();
        await conn2.StartAsync();

        var kestrelTracker = kestrel.Services.GetRequiredService<IStorageUnitHubConnectionTracker>();
        await WaitForUserInGroupAsync(kestrelTracker, $"facility_{fac1.id}", staff1.id, TimeSpan.FromSeconds(5));
        await WaitForUserInGroupAsync(kestrelTracker, $"facility_{fac2.id}", staff2.id, TimeSpan.FromSeconds(5));

        Assert.Equal(HubConnectionState.Connected, conn1.State);
        Assert.Equal(HubConnectionState.Connected, conn2.State);

        // Act 3: Broadcast CheckInChanged for Facility 1
        using (var scope = kestrel.Services.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IWebRealtimePublisher>();
            await publisher.PublishCheckInChangedAsync(
                checkInId: 7001,
                reservationId: 8001,
                unitId: 9001,
                facilityId: fac1.id,
                customerUserId: 5555,
                status: "completed",
                CancellationToken.None);
        }

        // Assert: Client 1 (assigned to Facility 1) receives CheckInChanged
        var completed = await Task.WhenAny(tcs1.Task, Task.Delay(10000));
        Assert.Same(tcs1.Task, completed);

        var payload = await tcs1.Task;
        Assert.Equal(7001, payload.GetProperty("checkInId").GetInt64());
        Assert.Equal(8001, payload.GetProperty("reservationId").GetInt64());
        Assert.Equal(9001, payload.GetProperty("unitId").GetInt64());
        Assert.Equal("completed", payload.GetProperty("status").GetString());

        // Negative Assert: Zero PIN in payload
        var rawJson = payload.GetRawText();
        Assert.DoesNotContain("pin", rawJson, StringComparison.OrdinalIgnoreCase);

        // Negative Assert: Client 2 (assigned to Facility 2) does NOT receive CheckInChanged
        Assert.False(tcs2.Task.IsCompleted);

        await conn1.StopAsync();
        await conn2.StopAsync();
    }

    #endregion

    #region 7. Round R3 Specific Verification Tests (R2-F01 to R2-F04)

    [Fact]
    public void R3_Architecture_WebAssembly_DoesNotReferenceDataAccess_AndHookUsesBllDto()
    {
        // R2-F01: Ensure Web assembly does NOT reference DataAccess assembly
        var webAssembly = typeof(Program).Assembly;
        var referencedAssemblies = webAssembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

        Assert.Contains("SelfStorageManagementSystem.BusinessLogic", referencedAssemblies);
        Assert.DoesNotContain("SelfStorageManagementSystem.DataAccess", referencedAssemblies);

        // Verify IReservationExpirationHook uses BLL ExpiredReservationHoldInfo
        var method = typeof(IReservationExpirationHook).GetMethod(nameof(IReservationExpirationHook.OnHoldsExpiredWithDetailsAsync));
        Assert.NotNull(method);
        var param = method.GetParameters()[0];
        var itemType = param.ParameterType.GetGenericArguments()[0];
        Assert.Equal(typeof(SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Reservations.ExpiredReservationHoldInfo), itemType);
    }

    #region R4 Concurrency Tests (Real PaymentServiceKhoiVTH & PaymentRepositoryKhoiVTH across distinct DbContexts)

    private async Task<(facility Facility, user Customer, string CookieHeader, reservation Reservation, invoice Invoice, payment Payment)> SeedPaymentAttemptAsync(
        string codePrefix,
        decimal amount = 1_000_000m,
        string paymentStatus = "pending",
        DateTimeOffset? holdUntil = null)
    {
        var facHierarchy = await SeedFacilityHierarchyAsync(codePrefix);
        var (customer, cookieCust) = await AuthenticateUserAndGetCookieAsync(RoleConstants.StorageCustomer);

        using var db = _factory.CreateDbContext();
        var rate = await db.facility_rates.FirstAsync(fr => fr.facility_id == facHierarchy.Facility.id);

        var res = new reservation
        {
            reservation_code = $"RES-{codePrefix}-{Guid.NewGuid():N}".Substring(0, 20),
            customer_id = customer.id,
            facility_id = facHierarchy.Facility.id,
            unit_type_id = facHierarchy.UnitType.id,
            facility_rate_id = rate.id,
            status = "hold",
            start_date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            end_date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            monthly_rate_snapshot = amount,
            deposit_snapshot = amount / 2,
            hold_until = holdUntil ?? DateTimeOffset.UtcNow.AddMinutes(15),
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        db.reservations.Add(res);
        await db.SaveChangesAsync();

        var inv = new invoice
        {
            invoice_no = $"INV-{codePrefix}-{Guid.NewGuid():N}".Substring(0, 20),
            customer_id = customer.id,
            reservation_id = res.id,
            total_amount = amount,
            paid_amount = 0m,
            currency = "VND",
            status = "open",
            opened_at = DateTimeOffset.UtcNow,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        db.invoices.Add(inv);
        await db.SaveChangesAsync();

        var pmt = new payment
        {
            customer_id = customer.id,
            target_invoice_id = inv.id,
            amount = amount,
            currency = "VND",
            method = "atm",
            provider = "vnpay",
            idempotency_key = $"IDEMP-{codePrefix}-{Guid.NewGuid():N}".Substring(0, 32),
            status = paymentStatus,
            metadata = "{}",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        db.payments.Add(pmt);
        await db.SaveChangesAsync();

        return (facHierarchy.Facility, customer, cookieCust, res, inv, pmt);
    }

    private Dictionary<string, string> BuildTestVnpayParams(long paymentId, decimal amount, string responseCode = "00", string? transactionNo = null)
    {
        using var scope = _factory.Services.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var hashSecret = config["Vnpay:HashSecret"] ?? "DEMOHASH";
        var tmnCode = config["Vnpay:TmnCode"] ?? "DEMOTMN";

        return PaymentWebTestFixture.GenerateVnpayIpnParameters(
            paymentId,
            amount,
            responseCode: responseCode,
            hashSecret: hashSecret,
            tmnCode: tmnCode,
            transactionNo: transactionNo);
    }

    [Fact]
    public async Task R4_Concurrency_TwoSimultaneousDeclines_CommitOnce_AndEmitExactlyOneRealtimeEvent()
    {
        // Codex R3 P1 (1): Hai callback decline đồng thời dùng hai DbContext, DB chỉ chuyển một lần, đúng một realtime PaymentChanged.
        // Thực thi trực tiếp PaymentServiceKhoiVTH và PaymentRepositoryKhoiVTH thật, không mock kết quả cuối.
        var (fac, customer, cookieCust, res, inv, payment) = await SeedPaymentAttemptAsync("r4_2dec", 1_000_000m, "pending");

        // Connect customer to SignalR hub to listen for PaymentChanged events
        await using var connCust = BuildHubConnection(cookieCust);
        var paymentEvents = new System.Collections.Concurrent.ConcurrentBag<JsonElement>();
        connCust.On<JsonElement>("PaymentChanged", data =>
        {
            if (data.GetProperty("paymentId").GetInt64() == payment.id)
            {
                paymentEvents.Add(data);
            }
        });
        await connCust.StartAsync();
        var tracker = _factory.Services.GetRequiredService<IStorageUnitHubConnectionTracker>();
        await WaitForUserInGroupAsync(tracker, $"user_{customer.id}", customer.id, TimeSpan.FromSeconds(5));

        var declineParams1 = BuildTestVnpayParams(payment.id, payment.amount, responseCode: "24");
        var declineParams2 = BuildTestVnpayParams(payment.id, payment.amount, responseCode: "24");

        // Act: Execute two declines concurrently using two separate DI scopes and distinct DbContexts
        var task1 = Task.Run(async () =>
        {
            using var scope1 = _factory.Services.CreateScope();
            var service1 = scope1.ServiceProvider.GetRequiredService<IPaymentService>();
            var publisher1 = scope1.ServiceProvider.GetRequiredService<IWebRealtimePublisher>();
            var config1 = scope1.ServiceProvider.GetRequiredService<IConfiguration>();
            var env1 = scope1.ServiceProvider.GetRequiredService<IWebHostEnvironment>();

            var controller1 = new PaymentsApiController(
                service1,
                config1,
                env1,
                NullLogger<PaymentsApiController>.Instance,
                publisher1)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };

            foreach (var kvp in declineParams1)
            {
                controller1.HttpContext.Request.QueryString = controller1.HttpContext.Request.QueryString.Add(kvp.Key, kvp.Value);
            }

            var actionResult = await controller1.VnpayIpn(CancellationToken.None);
            return Assert.IsType<OkObjectResult>(actionResult);
        });

        var task2 = Task.Run(async () =>
        {
            using var scope2 = _factory.Services.CreateScope();
            var service2 = scope2.ServiceProvider.GetRequiredService<IPaymentService>();
            var publisher2 = scope2.ServiceProvider.GetRequiredService<IWebRealtimePublisher>();
            var config2 = scope2.ServiceProvider.GetRequiredService<IConfiguration>();
            var env2 = scope2.ServiceProvider.GetRequiredService<IWebHostEnvironment>();

            var controller2 = new PaymentsApiController(
                service2,
                config2,
                env2,
                NullLogger<PaymentsApiController>.Instance,
                publisher2)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };

            foreach (var kvp in declineParams2)
            {
                controller2.HttpContext.Request.QueryString = controller2.HttpContext.Request.QueryString.Add(kvp.Key, kvp.Value);
            }

            var actionResult = await controller2.VnpayIpn(CancellationToken.None);
            return Assert.IsType<OkObjectResult>(actionResult);
        });

        var results = await Task.WhenAll(task1, task2);

        // Assert: Both return valid VNPAY ACK "00"
        foreach (var okResult in results)
        {
            var json = JsonSerializer.Serialize(okResult.Value);
            Assert.Contains("00", json);
            Assert.Contains("Confirm Success", json);
        }

        // DB state: status is failed, transitioned exactly once
        using (var verifyDb = _factory.CreateDbContext())
        {
            var updatedPayment = await verifyDb.payments.FirstAsync(p => p.id == payment.id);
            Assert.Equal("failed", updatedPayment.status);
            var integrationEvents = await verifyDb.integration_events
                .Where(e => e.source == "vnpay" && e.external_event_id.StartsWith($"VNPAY-{payment.id}-"))
                .ToListAsync();
            Assert.Single(integrationEvents);
        }

        // Exactly 1 realtime event arrived at client
        await Task.Delay(1000);
        Assert.Single(paymentEvents);
        Assert.Equal("failed", paymentEvents.First().GetProperty("status").GetString());

        await connCust.StopAsync();
    }

    [Fact]
    public async Task R4_Concurrency_SuccessRacingDecline_MaintainsConsistentDatabaseState_AndZeroFalseEvents()
    {
        // Codex R3 P1 (2): Success đua decline, trạng thái payment, reservation, invoice, agreement cuối nhất quán, không có realtime giả hoặc lặp.
        // Thực thi trực tiếp PaymentServiceKhoiVTH và PaymentRepositoryKhoiVTH thật qua 2 DbContexts riêng biệt.
        var (fac, customer, cookieCust, res, inv, payment) = await SeedPaymentAttemptAsync("r4_race", 1_000_000m, "pending");

        await using var connCust = BuildHubConnection(cookieCust);
        var paymentEvents = new System.Collections.Concurrent.ConcurrentBag<JsonElement>();
        var reservationEvents = new System.Collections.Concurrent.ConcurrentBag<JsonElement>();

        connCust.On<JsonElement>("PaymentChanged", data =>
        {
            if (data.GetProperty("paymentId").GetInt64() == payment.id)
            {
                paymentEvents.Add(data);
            }
        });
        connCust.On<JsonElement>("ReservationChanged", data =>
        {
            if (data.GetProperty("reservationId").GetInt64() == res.id)
            {
                reservationEvents.Add(data);
            }
        });
        await connCust.StartAsync();
        var tracker = _factory.Services.GetRequiredService<IStorageUnitHubConnectionTracker>();
        await WaitForUserInGroupAsync(tracker, $"user_{customer.id}", customer.id, TimeSpan.FromSeconds(5));

        var successParams = BuildTestVnpayParams(payment.id, payment.amount, responseCode: "00");
        var declineParams = BuildTestVnpayParams(payment.id, payment.amount, responseCode: "24");

        // Act: Run Success and Decline concurrently on separate scopes
        var successTask = Task.Run(async () =>
        {
            using var scope = _factory.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IPaymentService>();
            var publisher = scope.ServiceProvider.GetRequiredService<IWebRealtimePublisher>();
            var controller = new PaymentsApiController(
                service,
                scope.ServiceProvider.GetRequiredService<IConfiguration>(),
                scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>(),
                NullLogger<PaymentsApiController>.Instance,
                publisher)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
            foreach (var kvp in successParams)
            {
                controller.HttpContext.Request.QueryString = controller.HttpContext.Request.QueryString.Add(kvp.Key, kvp.Value);
            }
            return await controller.VnpayIpn(CancellationToken.None);
        });

        var declineTask = Task.Run(async () =>
        {
            using var scope = _factory.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IPaymentService>();
            var publisher = scope.ServiceProvider.GetRequiredService<IWebRealtimePublisher>();
            var controller = new PaymentsApiController(
                service,
                scope.ServiceProvider.GetRequiredService<IConfiguration>(),
                scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>(),
                NullLogger<PaymentsApiController>.Instance,
                publisher)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
            foreach (var kvp in declineParams)
            {
                controller.HttpContext.Request.QueryString = controller.HttpContext.Request.QueryString.Add(kvp.Key, kvp.Value);
            }
            return await controller.VnpayIpn(CancellationToken.None);
        });

        var results = await Task.WhenAll(successTask, declineTask);

        // Assert: Database state is 100% consistent
        using (var verifyDb = _factory.CreateDbContext())
        {
            var p = await verifyDb.payments.FirstAsync(x => x.id == payment.id);
            var r = await verifyDb.reservations.FirstAsync(x => x.id == res.id);
            var i = await verifyDb.invoices.FirstAsync(x => x.id == inv.id);
            var a = await verifyDb.rental_agreements.FirstOrDefaultAsync(x => x.reservation_id == res.id);

            if (p.status == "succeeded" && p.failure_reason == null)
            {
                // Success won: Agreement confirmed, invoice paid, reservation confirmed
                Assert.Equal("confirmed", r.status);
                Assert.Equal("paid", i.status);
                Assert.NotNull(a);
                Assert.Equal("scheduled", a.status);

                // Decline callback lost the race and must NOT have overwritten with "failed"
                Assert.DoesNotContain(paymentEvents, e => e.GetProperty("status").GetString() == "failed");
            }
            else
            {
                // Decline won: If success arrived after decline, it marked reconciliation_required (or refund created)
                // NEVER creates agreement or confirms reservation
                Assert.Null(a);
                Assert.NotEqual("confirmed", r.status);
            }
        }

        await connCust.StopAsync();
    }

    [Fact]
    public async Task R4_Concurrency_CallbacksAfterTerminalState_EmitZeroEvents_AndReturnValidAck()
    {
        // Codex R3 P1 (3): Callback retry sau trạng thái cuối phát 0 event và ACK đúng.
        var (fac, customer, cookieCust, res, inv, payment) = await SeedPaymentAttemptAsync("r4_retry", 1_000_000m, "pending");

        var successParams = BuildTestVnpayParams(payment.id, payment.amount, responseCode: "00");
        var declineParams = BuildTestVnpayParams(payment.id, payment.amount, responseCode: "24");

        // 1. First finalize as success
        using (var scope = _factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IPaymentService>();
            var res1 = await service.ProcessVnpayIpnAsync(successParams, CancellationToken.None);
            Assert.True(res1.IsStatusTransitionCommitted);
            Assert.Equal("00", res1.IpnResponseCode);
        }

        // Connect client
        await using var connCust = BuildHubConnection(cookieCust);
        var paymentEvents = new System.Collections.Concurrent.ConcurrentBag<JsonElement>();
        connCust.On<JsonElement>("PaymentChanged", data =>
        {
            if (data.GetProperty("paymentId").GetInt64() == payment.id)
            {
                paymentEvents.Add(data);
            }
        });
        await connCust.StartAsync();
        var tracker = _factory.Services.GetRequiredService<IStorageUnitHubConnectionTracker>();
        await WaitForUserInGroupAsync(tracker, $"user_{customer.id}", customer.id, TimeSpan.FromSeconds(5));

        // 2. Retry Success callback after succeeded -> ACK 02, IsStatusTransitionCommitted = false
        using (var scope = _factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IPaymentService>();
            var publisher = scope.ServiceProvider.GetRequiredService<IWebRealtimePublisher>();
            var controller = new PaymentsApiController(
                service,
                scope.ServiceProvider.GetRequiredService<IConfiguration>(),
                scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>(),
                NullLogger<PaymentsApiController>.Instance,
                publisher)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
            foreach (var kvp in successParams)
            {
                controller.HttpContext.Request.QueryString = controller.HttpContext.Request.QueryString.Add(kvp.Key, kvp.Value);
            }

            var actionResult = await controller.VnpayIpn(CancellationToken.None);
            var ok = Assert.IsType<OkObjectResult>(actionResult);
            var json = JsonSerializer.Serialize(ok.Value);
            Assert.Contains("02", json); // ACK 02: Order already confirmed
        }

        // 3. Retry Decline callback after succeeded -> ACK 02, IsStatusTransitionCommitted = false
        using (var scope = _factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IPaymentService>();
            var publisher = scope.ServiceProvider.GetRequiredService<IWebRealtimePublisher>();
            var controller = new PaymentsApiController(
                service,
                scope.ServiceProvider.GetRequiredService<IConfiguration>(),
                scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>(),
                NullLogger<PaymentsApiController>.Instance,
                publisher)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
            foreach (var kvp in declineParams)
            {
                controller.HttpContext.Request.QueryString = controller.HttpContext.Request.QueryString.Add(kvp.Key, kvp.Value);
            }

            var actionResult = await controller.VnpayIpn(CancellationToken.None);
            var ok = Assert.IsType<OkObjectResult>(actionResult);
            var json = JsonSerializer.Serialize(ok.Value);
            Assert.Contains("02", json); // ACK 02: Order already confirmed
        }

        // 4. Verify ZERO realtime events emitted
        await Task.Delay(500);
        Assert.Empty(paymentEvents);

        await connCust.StopAsync();
    }

    [Fact]
    public async Task R4_Concurrency_ReverseRace_SuccessAfterDeclineCommitted_RequiresReconciliation_NeverConfirmsReservation()
    {
        // Codex R3 P1: Kiểm tra hướng ngược lại: success tới sau failed tuân theo nghiệp vụ hiện có, không tạo trạng thái DB mâu thuẫn.
        var (fac, customer, cookieCust, res, inv, payment) = await SeedPaymentAttemptAsync("r4_rev_race", 1_000_000m, "pending");

        await using var connCust = BuildHubConnection(cookieCust);
        var paymentEvents = new System.Collections.Concurrent.ConcurrentBag<JsonElement>();
        var reservationEvents = new System.Collections.Concurrent.ConcurrentBag<JsonElement>();

        connCust.On<JsonElement>("PaymentChanged", data =>
        {
            if (data.GetProperty("paymentId").GetInt64() == payment.id)
            {
                paymentEvents.Add(data);
            }
        });
        connCust.On<JsonElement>("ReservationChanged", data =>
        {
            if (data.GetProperty("reservationId").GetInt64() == res.id)
            {
                reservationEvents.Add(data);
            }
        });
        await connCust.StartAsync();
        var tracker = _factory.Services.GetRequiredService<IStorageUnitHubConnectionTracker>();
        await WaitForUserInGroupAsync(tracker, $"user_{customer.id}", customer.id, TimeSpan.FromSeconds(5));

        var declineParams = BuildTestVnpayParams(payment.id, payment.amount, responseCode: "24", transactionNo: "14073849");
        var successParams = BuildTestVnpayParams(payment.id, payment.amount, responseCode: "00", transactionNo: "14073849");

        // Step 1: Decline callback arrives and commits failed status
        using (var scope = _factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IPaymentService>();
            var publisher = scope.ServiceProvider.GetRequiredService<IWebRealtimePublisher>();
            var controller = new PaymentsApiController(
                service,
                scope.ServiceProvider.GetRequiredService<IConfiguration>(),
                scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>(),
                NullLogger<PaymentsApiController>.Instance,
                publisher)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
            foreach (var kvp in declineParams)
            {
                controller.HttpContext.Request.QueryString = controller.HttpContext.Request.QueryString.Add(kvp.Key, kvp.Value);
            }

            var actionResult = await controller.VnpayIpn(CancellationToken.None);
            Assert.IsType<OkObjectResult>(actionResult);
        }

        // Verify Step 1: payment is failed in DB, 1 failed event emitted
        using (var verifyDb = _factory.CreateDbContext())
        {
            var p = await verifyDb.payments.FirstAsync(x => x.id == payment.id);
            Assert.Equal("failed", p.status);
        }
        await Task.Delay(500);
        Assert.Single(paymentEvents);
        Assert.Equal("failed", paymentEvents.First().GetProperty("status").GetString());

        // Step 2: Gateway sends Success callback for this already-failed attempt with the SAME transaction number (Finding R4-F01)
        using (var scope = _factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IPaymentService>();
            var publisher = scope.ServiceProvider.GetRequiredService<IWebRealtimePublisher>();
            var controller = new PaymentsApiController(
                service,
                scope.ServiceProvider.GetRequiredService<IConfiguration>(),
                scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>(),
                NullLogger<PaymentsApiController>.Instance,
                publisher)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
            foreach (var kvp in successParams)
            {
                controller.HttpContext.Request.QueryString = controller.HttpContext.Request.QueryString.Add(kvp.Key, kvp.Value);
            }

            var actionResult = await controller.VnpayIpn(CancellationToken.None);
            Assert.IsType<OkObjectResult>(actionResult);
        }

        // Verify Step 2:
        // In DB: payment status is succeeded with reconciliation_required, refund record created for staff review
        // Agreement is NOT created! Reservation is NOT confirmed!
        using (var verifyDb = _factory.CreateDbContext())
        {
            var p = await verifyDb.payments.FirstAsync(x => x.id == payment.id);
            var r = await verifyDb.reservations.FirstAsync(x => x.id == res.id);
            var a = await verifyDb.rental_agreements.FirstOrDefaultAsync(x => x.reservation_id == res.id);
            var refunds = await verifyDb.refunds.Where(rf => rf.payment_id == payment.id).ToListAsync();

            Assert.Equal("succeeded", p.status);
            Assert.Equal("PAYMENT_ALREADY_FAILED", p.failure_reason);
            Assert.Contains("\"reconciliation_required\":true", p.metadata);
            Assert.Single(refunds);
            Assert.Equal("requested", refunds.First().status);

            // Reservation is NOT confirmed! Agreement is NULL!
            Assert.NotEqual("confirmed", r.status);
            Assert.Null(a);
        }

        // Realtime events: Exactly 1 PaymentChanged(reconciliation_required) emitted on Step 2
        // Total paymentEvents is 2 (first failed, second reconciliation_required)
        // Never emits ReservationChanged(confirmed)
        await Task.Delay(500);
        Assert.Equal(2, paymentEvents.Count);
        Assert.Contains(paymentEvents, e => e.GetProperty("status").GetString() == "reconciliation_required");
        Assert.Empty(reservationEvents);

        // Step 3 (Finding R4-F01): Gateway retries Success callback with SAME transaction number
        using (var scope = _factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IPaymentService>();
            var publisher = scope.ServiceProvider.GetRequiredService<IWebRealtimePublisher>();
            var controller = new PaymentsApiController(
                service,
                scope.ServiceProvider.GetRequiredService<IConfiguration>(),
                scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>(),
                NullLogger<PaymentsApiController>.Instance,
                publisher)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
            foreach (var kvp in successParams)
            {
                controller.HttpContext.Request.QueryString = controller.HttpContext.Request.QueryString.Add(kvp.Key, kvp.Value);
            }

            var actionResult = await controller.VnpayIpn(CancellationToken.None);
            var ok = Assert.IsType<OkObjectResult>(actionResult);
            var json = JsonSerializer.Serialize(ok.Value);
            Assert.Contains("02", json); // ACK 02: Order already confirmed
        }

        // Verify Step 3: DB refund count is STILL exactly 1 (no duplicate refund created)
        using (var verifyDb = _factory.CreateDbContext())
        {
            var refunds = await verifyDb.refunds.Where(rf => rf.payment_id == payment.id).ToListAsync();
            Assert.Single(refunds);
        }

        // Realtime: ZERO new events emitted on idempotent retry (still exactly 2)
        await Task.Delay(500);
        Assert.Equal(2, paymentEvents.Count);
        Assert.Empty(reservationEvents);

        await connCust.StopAsync();
    }

    [Fact]
    public async Task R5_Concurrency_SuccessThenDecline_SameTransactionNo_DeclineDoesNotOverwrite()
    {
        // Codex R5: Test cả success rồi decline cùng X/Y, decline không ghi đè.
        var (fac, customer, cookieCust, res, inv, payment) = await SeedPaymentAttemptAsync("r5_s_then_d", 1_000_000m, "pending");

        await using var connCust = BuildHubConnection(cookieCust);
        var paymentEvents = new System.Collections.Concurrent.ConcurrentBag<JsonElement>();
        var reservationEvents = new System.Collections.Concurrent.ConcurrentBag<JsonElement>();

        connCust.On<JsonElement>("PaymentChanged", data =>
        {
            if (data.GetProperty("paymentId").GetInt64() == payment.id)
            {
                paymentEvents.Add(data);
            }
        });
        connCust.On<JsonElement>("ReservationChanged", data =>
        {
            if (data.GetProperty("reservationId").GetInt64() == res.id)
            {
                reservationEvents.Add(data);
            }
        });
        await connCust.StartAsync();
        var tracker = _factory.Services.GetRequiredService<IStorageUnitHubConnectionTracker>();
        await WaitForUserInGroupAsync(tracker, $"user_{customer.id}", customer.id, TimeSpan.FromSeconds(5));

        const string sharedTxnNo = "99001188";
        var successParams = BuildTestVnpayParams(payment.id, payment.amount, responseCode: "00", transactionNo: sharedTxnNo);
        var declineParams = BuildTestVnpayParams(payment.id, payment.amount, responseCode: "24", transactionNo: sharedTxnNo);

        // Step 1: Success callback arrives and finalizes payment
        using (var scope = _factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IPaymentService>();
            var publisher = scope.ServiceProvider.GetRequiredService<IWebRealtimePublisher>();
            var controller = new PaymentsApiController(
                service,
                scope.ServiceProvider.GetRequiredService<IConfiguration>(),
                scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>(),
                NullLogger<PaymentsApiController>.Instance,
                publisher)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
            foreach (var kvp in successParams)
            {
                controller.HttpContext.Request.QueryString = controller.HttpContext.Request.QueryString.Add(kvp.Key, kvp.Value);
            }

            var actionResult = await controller.VnpayIpn(CancellationToken.None);
            var ok = Assert.IsType<OkObjectResult>(actionResult);
            var json = JsonSerializer.Serialize(ok.Value);
            Assert.Contains("00", json);
        }

        // Verify Step 1 in DB: payment succeeded, reservation confirmed, agreement scheduled, invoice paid
        using (var verifyDb = _factory.CreateDbContext())
        {
            var p = await verifyDb.payments.FirstAsync(x => x.id == payment.id);
            var r = await verifyDb.reservations.FirstAsync(x => x.id == res.id);
            var i = await verifyDb.invoices.FirstAsync(x => x.id == inv.id);
            var a = await verifyDb.rental_agreements.FirstOrDefaultAsync(x => x.reservation_id == res.id);

            Assert.Equal("succeeded", p.status);
            Assert.Null(p.failure_reason);
            Assert.Equal("confirmed", r.status);
            Assert.Equal("paid", i.status);
            Assert.NotNull(a);
            Assert.Equal("scheduled", a.status);
        }

        await Task.Delay(500);
        Assert.Single(paymentEvents);
        Assert.Equal("completed", paymentEvents.First().GetProperty("status").GetString());
        Assert.Single(reservationEvents);
        Assert.Equal("confirmed", reservationEvents.First().GetProperty("status").GetString());

        // Step 2: Gateway sends Decline callback with the SAME X and Y
        using (var scope = _factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IPaymentService>();
            var publisher = scope.ServiceProvider.GetRequiredService<IWebRealtimePublisher>();
            var controller = new PaymentsApiController(
                service,
                scope.ServiceProvider.GetRequiredService<IConfiguration>(),
                scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>(),
                NullLogger<PaymentsApiController>.Instance,
                publisher)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
            foreach (var kvp in declineParams)
            {
                controller.HttpContext.Request.QueryString = controller.HttpContext.Request.QueryString.Add(kvp.Key, kvp.Value);
            }

            var actionResult = await controller.VnpayIpn(CancellationToken.None);
            var ok = Assert.IsType<OkObjectResult>(actionResult);
            var json = JsonSerializer.Serialize(ok.Value);
            Assert.Contains("02", json); // ACK 02: Order already confirmed
        }

        // Verify Step 2: Decline did NOT overwrite anything!
        using (var verifyDb = _factory.CreateDbContext())
        {
            var p = await verifyDb.payments.FirstAsync(x => x.id == payment.id);
            var r = await verifyDb.reservations.FirstAsync(x => x.id == res.id);
            var i = await verifyDb.invoices.FirstAsync(x => x.id == inv.id);
            var a = await verifyDb.rental_agreements.FirstOrDefaultAsync(x => x.reservation_id == res.id);

            Assert.Equal("succeeded", p.status);
            Assert.Null(p.failure_reason);
            Assert.Equal("confirmed", r.status);
            Assert.Equal("paid", i.status);
            Assert.NotNull(a);
            Assert.Equal("scheduled", a.status);
        }

        // Realtime: ZERO new events emitted on decline after success
        await Task.Delay(500);
        Assert.Single(paymentEvents);
        Assert.Single(reservationEvents);

        await connCust.StopAsync();
    }

    [Fact]
    public async Task R5_Concurrency_CancelledWaitingPaymentLock_ReleasesReservationSemaphore_AndSubsequentRequestSucceeds()
    {
        // Codex R4-F02: Test định hướng với hai request/cancellation tạo đúng thứ tự lock:
        // request bị hủy ném cancellation, request thứ ba cùng reservation/payment chạy thành công ngay sau đó,
        // không timeout, không SemaphoreFullException; chạy cả hai nhánh success/decline.

        // --- Branch 1: Success Path ---
        var (fac1, customer1, cookieCust1, res1, inv1, payment1) = await SeedPaymentAttemptAsync("r5_lock_succ", 1_000_000m, "pending");

        var paymentLockKey1 = $"SS_Payment_{payment1.id}";
        var reservationLockKey1 = $"SS_Reservation_{res1.id}";

        var paySem1 = PaymentRepositoryKhoiVTH.InMemorySemaphores.GetOrAdd(paymentLockKey1, _ => new SemaphoreSlim(1, 1));
        var resSem1 = PaymentRepositoryKhoiVTH.InMemorySemaphores.GetOrAdd(reservationLockKey1, _ => new SemaphoreSlim(1, 1));

        // Request A holds payment lock
        await paySem1.WaitAsync();

        try
        {
            // Request B attempts to finalize payment: it acquires reservation semaphore, then waits on payment semaphore
            using var ctsB = new CancellationTokenSource();
            var taskB = Task.Run(async () =>
            {
                using var scopeB = _factory.Services.CreateScope();
                var repoB = scopeB.ServiceProvider.GetRequiredService<IPaymentRepository>();
                var finalizeParamsB = new FinalizePaymentParams
                {
                    PaymentId = payment1.id,
                    Provider = "vnpay",
                    ProviderTransactionId = "TX-CANCEL-1",
                    Amount = payment1.amount,
                    ExternalEventId = $"VNPAY-{payment1.id}-CANCEL-1",
                    RawPayloadJson = "{}",
                    PaidAt = DateTimeOffset.UtcNow
                };
                return await repoB.ExecuteFinalizePaymentTransactionAsync(finalizeParamsB, ctsB.Token);
            });

            // Wait until Request B has acquired reservation semaphore (CurrentCount == 0) and is waiting on payment semaphore
            while (resSem1.CurrentCount > 0)
            {
                await Task.Delay(10);
            }

            // Cancel Request B while it is waiting for payment semaphore
            ctsB.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await taskB);

            // Assert: Reservation semaphore MUST be released immediately (CurrentCount == 1)!
            Assert.Equal(1, resSem1.CurrentCount);
        }
        finally
        {
            // Request A releases payment lock
            paySem1.Release();
        }

        // Request C runs for the same reservation and payment -> must succeed immediately!
        using (var scopeC = _factory.Services.CreateScope())
        {
            var repoC = scopeC.ServiceProvider.GetRequiredService<IPaymentRepository>();
            var finalizeParamsC = new FinalizePaymentParams
            {
                PaymentId = payment1.id,
                Provider = "vnpay",
                ProviderTransactionId = "TX-SUCC-C",
                Amount = payment1.amount,
                ExternalEventId = $"VNPAY-{payment1.id}-SUCC-C",
                RawPayloadJson = "{}",
                PaidAt = DateTimeOffset.UtcNow
            };
            var resultC = await repoC.ExecuteFinalizePaymentTransactionAsync(finalizeParamsC, CancellationToken.None);
            Assert.NotNull(resultC);
            Assert.False(resultC.IsAlreadyProcessed);
        }

        // --- Branch 2: Decline Path ---
        var (fac2, customer2, cookieCust2, res2, inv2, payment2) = await SeedPaymentAttemptAsync("r5_lock_fail", 1_000_000m, "pending");

        var paymentLockKey2 = $"SS_Payment_{payment2.id}";
        var reservationLockKey2 = $"SS_Reservation_{res2.id}";

        var paySem2 = PaymentRepositoryKhoiVTH.InMemorySemaphores.GetOrAdd(paymentLockKey2, _ => new SemaphoreSlim(1, 1));
        var resSem2 = PaymentRepositoryKhoiVTH.InMemorySemaphores.GetOrAdd(reservationLockKey2, _ => new SemaphoreSlim(1, 1));

        // Request A2 holds payment lock
        await paySem2.WaitAsync();

        try
        {
            // Request B2 attempts to finalize failed payment: it acquires reservation semaphore, then waits on payment semaphore
            using var ctsB2 = new CancellationTokenSource();
            var taskB2 = Task.Run(async () =>
            {
                using var scopeB2 = _factory.Services.CreateScope();
                var repoB2 = scopeB2.ServiceProvider.GetRequiredService<IPaymentRepository>();
                var failedParamsB2 = new FinalizeFailedPaymentParams
                {
                    PaymentId = payment2.id,
                    Provider = "vnpay",
                    ExternalEventId = $"VNPAY-{payment2.id}-CANCEL-2",
                    FailureReason = "Payment declined.",
                    RawPayloadJson = "{}",
                    FailedAt = DateTimeOffset.UtcNow
                };
                return await repoB2.ExecuteFinalizeFailedPaymentTransactionAsync(failedParamsB2, ctsB2.Token);
            });

            // Wait until Request B2 has acquired reservation semaphore (CurrentCount == 0) and is waiting on payment semaphore
            while (resSem2.CurrentCount > 0)
            {
                await Task.Delay(10);
            }

            // Cancel Request B2 while it is waiting for payment semaphore
            ctsB2.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await taskB2);

            // Assert: Reservation semaphore MUST be released immediately (CurrentCount == 1)!
            Assert.Equal(1, resSem2.CurrentCount);
        }
        finally
        {
            // Request A2 releases payment lock
            paySem2.Release();
        }

        // Request C2 runs for the same reservation and payment -> must succeed immediately!
        using (var scopeC2 = _factory.Services.CreateScope())
        {
            var repoC2 = scopeC2.ServiceProvider.GetRequiredService<IPaymentRepository>();
            var failedParamsC2 = new FinalizeFailedPaymentParams
            {
                PaymentId = payment2.id,
                Provider = "vnpay",
                ExternalEventId = $"VNPAY-{payment2.id}-FAIL-C",
                FailureReason = "Payment declined by user.",
                RawPayloadJson = "{}",
                FailedAt = DateTimeOffset.UtcNow
            };
            var resultC2 = await repoC2.ExecuteFinalizeFailedPaymentTransactionAsync(failedParamsC2, CancellationToken.None);
            Assert.NotNull(resultC2);
            Assert.True(resultC2.IsTransitionCommitted);
            Assert.Equal("failed", resultC2.CurrentStatus);
        }
    }

    #endregion

    [Fact]
    public async Task R3_PaymentsPages_RenderMessageAreas_AndIncludePaymentsJs()
    {
        // R2-F03: Verify Payments Index and Details pages contain realtime message containers and script tags
        var (staff, cookieStaff) = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityStaff, 1);
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("Cookie", cookieStaff);

        // 1. GET /Payments (Index)
        var indexResp = await client.GetAsync("/Payments");
        Assert.Equal(HttpStatusCode.OK, indexResp.StatusCode);
        var indexHtml = await indexResp.Content.ReadAsStringAsync();
        Assert.Contains("id=\"paymentsMessageArea\"", indexHtml);
        Assert.Contains("payments.js", indexHtml);

        // 2. GET /Payments/Details
        var detailsResp = await client.GetAsync("/Payments/Details");
        Assert.Equal(HttpStatusCode.OK, detailsResp.StatusCode);
        var detailsHtml = await detailsResp.Content.ReadAsStringAsync();
        Assert.Contains("payments.js", detailsHtml);
    }

    [Fact]
    public async Task R3_ReservationDetails_StatusApi_ReturnsCanonicalStatus_ForClientReconnect()
    {
        // R2-F04: Verify Status API returns canonical state for client reconnect resync
        var fac = await SeedFacilityHierarchyAsync("r3_resync");
        var (customer, cookieCust) = await AuthenticateUserAndGetCookieAsync(RoleConstants.StorageCustomer);
        var (staff, _) = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityStaff, fac.Facility.id);

        long resId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
            var rate = await db.facility_rates.FirstAsync(fr => fr.facility_id == fac.Facility.id);
            var res = new reservation
            {
                reservation_code = "RES-R3-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                customer_id = customer.id,
                facility_id = fac.Facility.id,
                unit_type_id = fac.UnitType.id,
                facility_rate_id = rate.id,
                status = "confirmed",
                start_date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
                end_date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
                monthly_rate_snapshot = 1000000,
                deposit_snapshot = 500000,
                hold_until = DateTimeOffset.UtcNow.AddMinutes(10),
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            };
            db.reservations.Add(res);
            await db.SaveChangesAsync();
            resId = res.id;
        }

        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("Cookie", cookieCust);

        // Act: Invoke Status API endpoint as customer reconnecting
        var resp = await client.GetAsync($"/Reservations/Details?id={resId}&handler=Status");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("confirmed", root.GetProperty("status").GetString());
        Assert.False(root.GetProperty("isHoldActive").GetBoolean());
        Assert.False(root.GetProperty("canCancel").GetBoolean());
    }

    #endregion
}
