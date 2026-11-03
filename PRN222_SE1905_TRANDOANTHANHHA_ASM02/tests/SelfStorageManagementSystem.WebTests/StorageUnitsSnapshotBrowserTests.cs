using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.WebTests.Infrastructure;
using Xunit;

namespace SelfStorageManagementSystem.WebTests;

public class StorageUnitsSnapshotBrowserTests
{
    [Fact]
    public async Task RazorPages_TwoBrowsers_CrudWithCommittedBooking_SynchronizesWithoutHtmlFetchOrDebugLogs()
    {
        var edgePath = T18BrowserContext.ResolveEdgeBrowserPath();
        Assert.True(File.Exists(edgePath), "Microsoft Edge is required for this browser regression test.");
        await using var server = await KestrelRealtimeServer.StartAsync();
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        using (var scope = server.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
            db.roles.Add(new role { id = 5, code = RoleConstants.SystemAdministrator, display_name = "Admin" });
            db.users.Add(new user
            {
                id = 1001, email = "snapshot-admin@example.test", password_hash = "test-only",
                status = "active", created_at = now, updated_at = now
            });
            db.user_roles.Add(new user_role { user_id = 1001, role_id = 5, granted_at = now });
            db.facilities.Add(new facility
            {
                id = 10, code = "SNAPSHOT", name = "Snapshot facility", address_line = "Test address",
                ward = "Test ward", district = "Test district", city = "Test city", status = "active",
                timezone = "Asia/Ho_Chi_Minh", opening_time = new TimeOnly(8, 0), closing_time = new TimeOnly(20, 0),
                created_at = now, updated_at = now
            });
            db.unit_types.Add(new unit_type
            {
                id = 20, code = "SNAPSHOT-M", name = "Medium", width_m = 2, length_m = 3, height_m = 2,
                is_active = true, created_at = now, updated_at = now
            });
            db.facility_rates.Add(new facility_rate
            {
                id = 30, facility_id = 10, unit_type_id = 20, monthly_rate = 1000000,
                deposit_amount = 1000000, booking_fee = 100000,
                valid_from = today.AddDays(-10), created_at = now
            });
            db.storage_units.Add(new storage_unit
            {
                id = 40, facility_id = 10, unit_type_id = 20, unit_code = "BASELINE",
                physical_status = "available", is_listed = true, created_at = now, updated_at = now
            });
            db.reservations.Add(new reservation
            {
                id = 50, customer_id = 2001, facility_id = 10, unit_type_id = 20,
                reservation_code = "CONFIRMED-SNAPSHOT", status = "confirmed",
                start_date = today, end_date = today.AddMonths(1), created_at = now, updated_at = now
            });
            await db.SaveChangesAsync();
        }

        await using var first = await T18BrowserContext.LaunchAsync("asm02_snapshot_first", RoleConstants.SystemAdministrator, 1001, server.ServerUrl, edgePath);
        await using var second = await T18BrowserContext.LaunchAsync("asm02_snapshot_second", RoleConstants.SystemAdministrator, 1001, server.ServerUrl, edgePath);
        foreach (var browser in new[] { first, second })
        {
            await browser.NavigateAsync(server.ServerUrl + "/test-auth-login?role=system_administrator&userId=1001");
            await browser.NavigateAsync(server.ServerUrl + "/StorageUnits/Index");
            await WaitForAsync(browser, "window.connection?.state === 'Connected' && document.getElementById('unit-row-40') !== null");
            await browser.SendCommandAsync("Runtime.evaluate", new
            {
                expression = "window.__snapshotHttpCalls = []; window.__originalSnapshotFetch = window.fetch; window.fetch = function(...args) { window.__snapshotHttpCalls.push(String(args[0])); return window.__originalSnapshotFetch.apply(this, args); };"
            });
        }

        // Two candidates for one confirmed booking: deleting the new unit remains safe.
        var createJson = JsonSerializer.Serialize(new { FacilityId = 10, UnitTypeId = 20, UnitCode = "BROWSER-CREATED", IsListed = true });
        await InvokeAsync(first, "HubCreate_StorageUnit", JsonSerializer.Serialize(createJson));
        await WaitForAsync(second, "document.getElementById('storageUnitsTableBody').textContent.includes('BROWSER-CREATED') && document.getElementById('storageUnitsTotalCount').textContent.trim() === '2'");

        long createdId;
        using (var scope = server.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
            createdId = db.storage_units.Single(u => u.unit_code == "BROWSER-CREATED").id;
        }
        var updateJson = JsonSerializer.Serialize(new { Id = createdId, UnitCode = "BROWSER-UPDATED" });
        await InvokeAsync(first, "HubUpdate_StorageUnit", JsonSerializer.Serialize(updateJson));
        await WaitForAsync(second, $"document.getElementById('unit-row-{createdId}')?.textContent.includes('BROWSER-UPDATED') === true");

        await InvokeAsync(first, "HubDelete_StorageUnit", JsonSerializer.Serialize(createdId.ToString()), "null");
        await WaitForAsync(second, $"document.getElementById('unit-row-{createdId}') === null && document.getElementById('storageUnitsTotalCount').textContent.trim() === '1'");
        using (var scope = server.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
            Assert.Null(await db.storage_units.FindAsync(createdId));
            Assert.NotNull(await db.reservations.FindAsync(50L));
            Assert.NotNull(await db.storage_units.FindAsync(40L));
        }
        foreach (var browser in new[] { first, second })
        {
            Assert.True(await browser.EvaluateBooleanAsync("window.__snapshotHttpCalls.length === 0"), "Realtime CRUD must not refetch page HTML.");
            Assert.DoesNotContain(browser.ConsoleLogs, log => log.Contains("[Realtime]", StringComparison.Ordinal) || log.Contains("Receiver_", StringComparison.Ordinal));
            Assert.NotEmpty(browser.WebSocketFrames);
        }
    }

    private static async Task InvokeAsync(T18BrowserContext browser, string method, params string[] jsonArguments)
    {
        var arguments = string.Join(",", new[] { JsonSerializer.Serialize(method) }.Concat(jsonArguments));
        var result = await browser.SendCommandAsync("Runtime.evaluate", new
        {
            expression = "window.connection.invoke(" + arguments + ")", awaitPromise = true, returnByValue = true
        });
        Assert.False(result.TryGetProperty("exceptionDetails", out _), result.ToString());
    }

    private static async Task WaitForAsync(T18BrowserContext browser, string expression)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (await browser.EvaluateBooleanAsync(expression)) return;
            await Task.Delay(100);
        }
        Assert.Fail("Browser condition was not reached: " + expression);
    }
}
