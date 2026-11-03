using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.Web.Components;
using SelfStorageManagementSystem.Web.Realtime;
using SelfStorageManagementSystem.WebTests.Fixtures;
using Xunit;

namespace SelfStorageManagementSystem.WebTests;

public class TestAuthenticationStateProvider : AuthenticationStateProvider
{
    private ClaimsPrincipal _principal;
    public TestAuthenticationStateProvider(ClaimsPrincipal principal) => _principal = principal;
    public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
        Task.FromResult(new AuthenticationState(_principal));
    public void SetUser(ClaimsPrincipal principal)
    {
        _principal = principal;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}

public class BlazorStorageUnitsTests : IClassFixture<CookieAuthWebTestFixture>
{
    private readonly CookieAuthWebTestFixture _factory;
    public BlazorStorageUnitsTests(CookieAuthWebTestFixture factory) => _factory = factory;

    [Fact]
    public async Task Anonymous_IsChallengedBeforeBlazorRendering()
    {
        using var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var response = await client.GetAsync("/Blazor/StorageUnits");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.OriginalString);
        using var negotiate = new HttpRequestMessage(HttpMethod.Post, "/_blazor/negotiate?negotiateVersion=1");
        negotiate.Headers.Accept.ParseAdd("application/json");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(negotiate)).StatusCode);
    }

    [Theory]
    [InlineData(RoleConstants.StorageCustomer, false)]
    [InlineData(RoleConstants.FacilityStaff, true)]
    [InlineData(RoleConstants.SystemAdministrator, true)]
    public async Task Host_EnforcesRoleAndRendersInteractiveComponent(string role, bool allowed)
    {
        var email = $"blazor_{Guid.NewGuid():N}@example.test";
        const string password = "BlazorIntegrationTest123!";
        await _factory.SeedUserAsync(email, password, role);
        using var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var loginHtml = await client.GetStringAsync("/Account/Login");
        var match = Regex.Match(loginHtml, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success);
        var loginResponse = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value)
        }));
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        var response = await client.GetAsync("/Blazor/StorageUnits");
        using var negotiate = new HttpRequestMessage(HttpMethod.Post, "/_blazor/negotiate?negotiateVersion=1");
        negotiate.Headers.Accept.ParseAdd("application/json");
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden,
            (await client.SendAsync(negotiate)).StatusCode);
        if (!allowed)
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/Account/Forbidden", response.Headers.Location?.OriginalString);
            return;
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("blazor-storage-units", html);
        Assert.Contains("blazor.server.js", html);
        Assert.Contains("blazor-search", html);
        Assert.Contains("base href=\"/\"", html);
        Assert.DoesNotContain("@onclick", html);
        if (role == RoleConstants.SystemAdministrator) Assert.Contains("Thêm ô kho", html);
        else Assert.DoesNotContain("Thêm ô kho", html);
    }

    private async Task<(facility Facility, unit_type UnitType)> SeedFacilityAndTypeAsync(string suffix)
    {
        using var db = _factory.CreateDbContext();
        var facId = (long)(Math.Abs(Guid.NewGuid().GetHashCode()) % 80000 + 1000);
        var fac = new facility
        {
            id = facId,
            code = $"BLZ_{suffix.ToUpperInvariant()}",
            name = $"Blazor Cơ Sở {suffix}",
            address_line = "123 Đường Blazor",
            ward = "P1", district = "Q1", city = "HCM",
            status = "active", timezone = "Asia/Ho_Chi_Minh",
            opening_time = new TimeOnly(8, 0), closing_time = new TimeOnly(20, 0),
            created_at = DateTimeOffset.UtcNow, updated_at = DateTimeOffset.UtcNow
        };
        db.facilities.Add(fac);

        var typeId = facId * 10 + 2;
        var ut = new unit_type
        {
            id = typeId,
            code = $"BLZT_{suffix.ToUpperInvariant()}",
            name = $"Blazor Type {suffix}",
            width_m = 2m, length_m = 2m, height_m = 2m,
            area_m2 = 4m, volume_m3 = 8m,
            climate_controlled = false, max_weight_kg = 200m,
            is_active = true,
            created_at = DateTimeOffset.UtcNow, updated_at = DateTimeOffset.UtcNow
        };
        db.unit_types.Add(ut);

        var rate = new facility_rate
        {
            id = facId * 10 + 3,
            facility_id = facId,
            unit_type_id = typeId,
            monthly_rate = 1200000m,
            deposit_amount = 1200000m,
            booking_fee = 50000m,
            valid_from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            created_at = DateTimeOffset.UtcNow
        };
        db.facility_rates.Add(rate);

        await db.SaveChangesAsync();
        return (fac, ut);
    }

    private async Task<(user User, StorageUnitManager Component)> CreateComponentForUserAsync(string role, long? assignToFacilityId = null)
    {
        var email = $"blazor_comp_{Guid.NewGuid():N}@example.test";
        var user = await _factory.SeedUserAsync(email, "BlazorPassword123!", role);

        if (assignToFacilityId.HasValue && (role == RoleConstants.FacilityStaff || role == RoleConstants.FacilityManager))
        {
            using var db = _factory.CreateDbContext();
            db.staff_facility_assignments.Add(new staff_facility_assignment
            {
                employee_id = user.id,
                facility_id = assignToFacilityId.Value,
                assignment_role = role,
                starts_at = DateTimeOffset.UtcNow.AddDays(-5),
                ends_at = null,
                created_at = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.id.ToString()),
            new Claim(ClaimTypes.Email, user.email),
            new Claim(ClaimTypes.Name, user.email),
            new Claim(ClaimTypes.Role, role)
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "CookieAuth"));
        var authProvider = new TestAuthenticationStateProvider(principal);
        var scopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();
        var logger = _factory.Services.GetRequiredService<ILogger<StorageUnitManager>>();
        var notificationService = _factory.Services.GetRequiredService<IStorageUnitCircuitNotificationService>();

        var component = new StorageUnitManager();
        component.InitializeForTest(scopeFactory, authProvider, logger, notificationService);
        return (user, component);
    }

    [Fact]
    public async Task Component_StaffRole_IsReadOnly_CannotManage()
    {
        var (user, component) = await CreateComponentForUserAsync(RoleConstants.FacilityStaff);
        await component.TestInitAsync();

        Assert.False(component.TestCanManage);
        Assert.False(component.TestIsBusy);
        await component.TestStartCreateAsync();
        Assert.True(component.TestIsError);
        Assert.Contains("Bạn chưa có cơ sở", component.TestMessage);
    }

    [Fact]
    public async Task Component_AdminRole_FullCrudLifecycle_ExecutesSuccessfully()
    {
        var suffix = Guid.NewGuid().ToString("N")[..4];
        var (fac, ut) = await SeedFacilityAndTypeAsync(suffix);
        var (user, component) = await CreateComponentForUserAsync(RoleConstants.SystemAdministrator);

        // 1. Initialize
        await component.TestInitAsync();
        Assert.True(component.TestCanManage);
        Assert.False(component.TestIsError);

        // 2. Start create
        await component.TestStartCreateAsync();
        Assert.NotNull(component.TestCreateRequest);

        // 3. Create unit
        var unitCode = $"BLZ-{suffix.ToUpperInvariant()}-01";
        await component.TestCreateAsync(unitCode, ut.id, fac.id);
        Assert.Equal("Đã tạo ô kho.", component.TestMessage);
        Assert.False(component.TestIsError);

        // 4. Verify created unit in list
        var created = component.TestItems.FirstOrDefault(u => u.UnitCode == unitCode);
        Assert.NotNull(created);
        Assert.Equal("available", created.PhysicalStatus);

        // 5. Start edit & save
        await component.TestStartEditAsync(created);
        Assert.NotNull(component.TestEditRequest);
        var updatedCode = $"BLZ-{suffix.ToUpperInvariant()}-01U";
        await component.TestSaveAsync(updatedCode, "Ghi chú cập nhật");
        Assert.Equal("Đã lưu thay đổi.", component.TestMessage);
        Assert.False(component.TestIsError);

        var updated = component.TestItems.FirstOrDefault(u => u.UnitCode == updatedCode);
        Assert.NotNull(updated);

        // 6. Delete clean unit (hard delete)
        await component.TestStartDeleteAsync(updated);
        Assert.NotNull(component.TestDeleteDetail);
        Assert.False(component.TestDeleteDetail.HasAnyReferences);

        await component.TestDeleteAsync();
        Assert.Contains("Đã xóa vĩnh viễn", component.TestMessage);
        Assert.Null(component.TestItems.FirstOrDefault(u => u.UnitCode == updatedCode));

        component.Dispose();
    }

    [Fact]
    public async Task Component_Delete_WithReferences_RetainsRecordAndUnlists()
    {
        var suffix = Guid.NewGuid().ToString("N")[..4];
        var (fac, ut) = await SeedFacilityAndTypeAsync(suffix);
        var (user, component) = await CreateComponentForUserAsync(RoleConstants.SystemAdministrator);

        await component.TestInitAsync();
        var unitCode = $"BLZ-{suffix.ToUpperInvariant()}-REF";
        await component.TestStartCreateAsync();
        await component.TestCreateAsync(unitCode, ut.id);

        var created = component.TestItems.First(u => u.UnitCode == unitCode);

        // Add a status history reference
        using (var db = _factory.CreateDbContext())
        {
            db.unit_status_histories.Add(new unit_status_history
            {
                storage_unit_id = created.Id,
                old_status = "available",
                new_status = "cleaning",
                changed_by = user.id,
                reason = "Audit test reference",
                changed_at = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Start delete and verify HasAnyReferences is true
        await component.TestStartDeleteAsync(created);
        Assert.NotNull(component.TestDeleteDetail);
        Assert.True(component.TestDeleteDetail.HasAnyReferences);

        // Execute delete -> unlists record
        await component.TestDeleteAsync();
        Assert.Contains("ngừng niêm yết", component.TestMessage);

        // Verify DB retains row and is_listed is false
        using (var db = _factory.CreateDbContext())
        {
            var retained = await db.storage_units.FindAsync(created.Id);
            Assert.NotNull(retained);
            Assert.False(retained.is_listed);
        }

        component.Dispose();
    }

    [Fact]
    public async Task Component_CrossCircuitNotification_SyncsIndependentSessionWithoutHtmlFetch()
    {
        var suffix = Guid.NewGuid().ToString("N")[..4];
        var (fac, ut) = await SeedFacilityAndTypeAsync(suffix);

        // Session 1: Admin
        var (adminUser, adminComponent) = await CreateComponentForUserAsync(RoleConstants.SystemAdministrator);
        // Session 2: Staff assigned to the facility
        var (staffUser, staffComponent) = await CreateComponentForUserAsync(RoleConstants.FacilityStaff, fac.id);

        await adminComponent.TestInitAsync();
        await staffComponent.TestInitAsync();

        var staffInitialCount = staffComponent.TestItemCount;

        // Admin creates a unit in Session 1
        var unitCode = $"BLZ-{suffix.ToUpperInvariant()}-CROSS";
        await adminComponent.TestStartCreateAsync();
        await adminComponent.TestCreateAsync(unitCode, ut.id, fac.id);

        // Emulate notification received by Session 2 circuit (dispatched automatically by publisher)
        var notificationService = _factory.Services.GetRequiredService<IStorageUnitCircuitNotificationService>();
        await notificationService.NotifyChangedAsync(new StorageUnitCircuitNotification
        {
            FacilityId = fac.id,
            UnitId = 9999,
            Action = "Created"
        });

        // Staff component refreshed state via its own scoped service without full-page HTML fetch
        Assert.True(staffComponent.TestItemCount > staffInitialCount);
        Assert.NotNull(staffComponent.TestItems.FirstOrDefault(u => u.UnitCode == unitCode));

        adminComponent.Dispose();
        staffComponent.Dispose();
    }

    [Fact]
    public async Task Component_Dispose_CleansUpSubscription_NoMoreUpdates()
    {
        var (staffUser, staffComponent) = await CreateComponentForUserAsync(RoleConstants.FacilityStaff);
        await staffComponent.TestInitAsync();

        staffComponent.Dispose();

        var notificationService = _factory.Services.GetRequiredService<IStorageUnitCircuitNotificationService>();
        // Firing notification after disposal should not throw or update disposed component
        var ex = await Record.ExceptionAsync(() => notificationService.NotifyChangedAsync(new StorageUnitCircuitNotification
        {
            FacilityId = 1,
            UnitId = 1,
            Action = "Updated"
        }));

        Assert.Null(ex);
    }
}
