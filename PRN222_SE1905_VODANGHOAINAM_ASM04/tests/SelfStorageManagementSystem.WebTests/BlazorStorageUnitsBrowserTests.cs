using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.WebTests.Infrastructure;

namespace SelfStorageManagementSystem.WebTests;

public class BlazorStorageUnitsBrowserTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Crud_BroadcastsToIndependentConnection_AndPreservesReferencedUnits(bool referenced)
    {
        await using var server = await KestrelRealtimeServer.StartAsync();
        var now = DateTimeOffset.UtcNow;
        using (var scope = server.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
            db.facilities.Add(new facility { id = 10, code = "BLZ", name = "Blazor facility", address_line = "Test", city = "Test", status = "active", timezone = "UTC", created_at = now, updated_at = now });
            db.unit_types.Add(new unit_type { id = 20, code = "BLZ", name = "Test type", is_active = true, width_m = 2, length_m = 2, height_m = 2, created_at = now, updated_at = now });
            db.facility_rates.Add(new facility_rate { facility_id = 10, unit_type_id = 20, monthly_rate = 1000000, deposit_amount = 1000000, valid_from = DateOnly.FromDateTime(now.UtcDateTime.AddDays(-1)), created_at = now });
            db.users.Add(new user { id = 1001, email = "blazor@test.local", password_hash = "test-only", status = UserStatusConstants.Active, created_at = now, updated_at = now });
            db.roles.Add(new role { id = 1, code = RoleConstants.FacilityManager, display_name = "Manager" });
            db.user_roles.Add(new user_role { user_id = 1001, role_id = 1, granted_at = now });
            db.employee_profiles.Add(new employee_profile { user_id = 1001, employee_code = "BLZ", full_name = "Blazor manager", employment_status = EmploymentStatusConstants.Active, hire_date = DateOnly.FromDateTime(now.UtcDateTime.AddYears(-1)), created_at = now, updated_at = now });
            db.staff_facility_assignments.Add(new staff_facility_assignment { employee_id = 1001, facility_id = 10, assignment_role = RoleConstants.FacilityManager, starts_at = now.AddDays(-1), created_at = now });
            await db.SaveChangesAsync();
        }

        var loginUrl = $"{server.ServerUrl}/test-auth-login?role={RoleConstants.FacilityManager}&userId=1001";
        using var handler = new HttpClientHandler { CookieContainer = new CookieContainer() };
        using var client = new HttpClient(handler);
        (await client.GetAsync(loginUrl)).EnsureSuccessStatusCode();
        await using var observer = new HubConnectionBuilder()
            .WithUrl($"{server.ServerUrl}/storageUnitHub", options => options.Cookies = handler.CookieContainer)
            .Build();
        var created = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var updated = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var unlisted = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var deleted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        observer.On<JsonElement>("Receiver_CreateStorageUnit", data => created.TrySetResult(data));
        observer.On<JsonElement>("Receiver_UpdateStorageUnit", data =>
        {
            if (data.GetProperty("isListed").GetBoolean()) updated.TrySetResult(data);
            else unlisted.TrySetResult(data);
        });
        observer.On<string>("Receiver_DeleteStorageUnit", id => deleted.TrySetResult(id));
        await observer.StartAsync();

        await using var browser = await HeadlessEdgeSession.LaunchAsync();
        await browser.SendCommandAsync("Emulation.setDeviceMetricsOverride", new { width = 1280, height = 800, deviceScaleFactor = 1, mobile = false });
        await browser.SendCommandAsync("Page.addScriptToEvaluateOnNewDocument", new { source = "window.__qaErrors = []; window.addEventListener('error', e => window.__qaErrors.push(e.message)); window.addEventListener('unhandledrejection', e => window.__qaErrors.push(String(e.reason))); const originalError = console.error; console.error = (...args) => { window.__qaErrors.push(args.join(' ')); originalError.apply(console, args); };" });
        await browser.NavigateAsync(loginUrl);
        await browser.NavigateAsync($"{server.ServerUrl}/Blazor/StorageUnits");
        await WaitForAsync(browser, "document.querySelector('fieldset')?.disabled === false");
        Assert.True(await browser.EvaluateBoolAsync("location.pathname === '/Blazor/StorageUnits' && document.title.includes('Blazor')"));
        await browser.ExecuteScriptAsync("Array.from(document.querySelectorAll('button')).find(b => b.textContent.includes('Thêm ô kho')).click()");
        await WaitForAsync(browser, "!!document.querySelector('#blazor-create-code')");
        await browser.ExecuteScriptAsync("const type = document.querySelector('#blazor-create-type'); type.value = '20'; type.dispatchEvent(new Event('change', { bubbles: true })); const code = document.querySelector('#blazor-create-code'); code.value = 'BLZ-NEW'; code.dispatchEvent(new Event('change', { bubbles: true }));");
        await browser.ExecuteScriptAsync("document.querySelector('#blazor-create-code').closest('form').requestSubmit()");
        var createEvent = await created.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var unitId = long.Parse(createEvent.GetProperty("id").ToString());
        await WaitForAsync(browser, "!!document.querySelector('[data-unit-code=\"BLZ-NEW\"]') && !document.querySelector('fieldset').disabled");

        await browser.ExecuteScriptAsync("Array.from(document.querySelector('[data-unit-code=\"BLZ-NEW\"]').querySelectorAll('button')).find(b => b.textContent === 'Sửa').click()");
        await WaitForAsync(browser, "!!document.querySelector('#blazor-edit-code')");
        await browser.ExecuteScriptAsync("const edit = document.querySelector('#blazor-edit-code'); edit.value = 'BLZ-EDITED'; edit.dispatchEvent(new Event('change', { bubbles: true }));");
        await browser.ExecuteScriptAsync("document.querySelector('#blazor-edit-code').closest('form').requestSubmit()");
        var updateEvent = await updated.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal("BLZ-EDITED", updateEvent.GetProperty("unitCode").GetString());

        // The current search still targets the original code; clear it before deleting.
        await WaitForAsync(browser, "!document.querySelector('fieldset').disabled");
        await browser.ExecuteScriptAsync("const search = document.querySelector('#blazor-search'); search.value = ''; search.dispatchEvent(new Event('change', { bubbles: true }));");
        await browser.ExecuteScriptAsync("document.querySelector('#blazor-search').closest('form').requestSubmit()");
        await WaitForAsync(browser, "!!document.querySelector('[data-unit-code=\"BLZ-EDITED\"]') && !document.querySelector('fieldset').disabled");
        var screenshot = await browser.SendCommandAsync("Page.captureScreenshot", new { format = "png" });
        await File.WriteAllBytesAsync(Path.Combine(Path.GetTempPath(), $"asm-review-blazor-{referenced}.png"), Convert.FromBase64String(screenshot.GetProperty("data").GetString()!));
        if (referenced)
        {
            using var scope = server.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
            db.unit_status_histories.Add(new unit_status_history { storage_unit_id = unitId, old_status = "available", new_status = "available", reason = "Retain history", changed_by = 1001, changed_at = now });
            await db.SaveChangesAsync();
        }
        await browser.ExecuteScriptAsync("Array.from(document.querySelector('[data-unit-code=\"BLZ-EDITED\"]').querySelectorAll('button')).find(b => b.textContent === 'Xóa').click()");
        await WaitForAsync(browser, "Array.from(document.querySelectorAll('button')).some(b => b.textContent === 'Xác nhận xóa')");
        await browser.ExecuteScriptAsync("Array.from(document.querySelectorAll('button')).find(b => b.textContent === 'Xác nhận xóa').click()");
        if (referenced)
        {
            var unlistEvent = await unlisted.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(unitId.ToString(), unlistEvent.GetProperty("id").ToString());
            Assert.False(deleted.Task.IsCompleted);
        }
        else
        {
            Assert.Equal(unitId.ToString(), await deleted.Task.WaitAsync(TimeSpan.FromSeconds(20)));
        }
        using (var scope = server.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
            var persisted = await db.storage_units.FindAsync(unitId);
            if (referenced) Assert.False(Assert.IsType<storage_unit>(persisted).is_listed);
            else Assert.Null(persisted);
        }
        await WaitForAsync(browser, "!document.querySelector('fieldset').disabled");
        var message = await browser.EvaluateStringRequiredAsync("document.querySelector('[role=alert]').textContent");
        Assert.Contains(referenced ? "ngừng niêm yết" : "Đã xóa ô kho", message);
        Assert.DoesNotContain("STORAGE_UNIT_", message);
        Assert.Equal("[]", await browser.EvaluateStringRequiredAsync("JSON.stringify(window.__qaErrors)"));
    }

    private static async Task WaitForAsync(HeadlessEdgeSession browser, string condition)
    {
        for (var attempt = 0; attempt < 80; attempt++)
        {
            if (await browser.EvaluateBoolAsync(condition)) return;
            await Task.Delay(250);
        }
        Assert.Fail($"Browser condition did not become true: {condition}. Page: {await browser.EvaluateStringRequiredAsync("document.body.innerText")}");
    }
}
