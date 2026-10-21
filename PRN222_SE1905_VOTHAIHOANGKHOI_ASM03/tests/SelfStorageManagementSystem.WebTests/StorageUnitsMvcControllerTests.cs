using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.Web.Controllers;
using SelfStorageManagementSystem.Web.Extensions;
using SelfStorageManagementSystem.WebTests.Fixtures;
using Xunit;

namespace SelfStorageManagementSystem.WebTests;

/// <summary>
/// Targeted tests specifically verifying MVC StorageUnitsController architecture,
/// actions, views, DI integrity (IStorageUnitAdminService only, no DbContext in controller),
/// pagination, role/facility checks, anti-forgery protection, and async/await contracts for ASM03.
/// </summary>
public class StorageUnitsMvcControllerTests : IClassFixture<CookieAuthWebTestFixture>
{
    private readonly CookieAuthWebTestFixture _factory;

    public StorageUnitsMvcControllerTests(CookieAuthWebTestFixture factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient(bool allowAutoRedirect = false)
    {
        return _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect
        });
    }

    [Fact]
    public void StorageUnitsController_DoesNotInjectOrReferenceDbContext()
    {
        var controllerType = typeof(StorageUnitsController);
        Assert.True(typeof(Controller).IsAssignableFrom(controllerType), "StorageUnitsController must inherit from Microsoft.AspNetCore.Mvc.Controller.");

        var ctors = controllerType.GetConstructors();
        Assert.Single(ctors);

        var parameters = ctors[0].GetParameters();
        var paramTypes = parameters.Select(p => p.ParameterType.FullName).ToList();

        // Must NOT have DbContext or any DAL types
        Assert.DoesNotContain(paramTypes, t => t != null && t.Contains("DbContext"));
        Assert.DoesNotContain(paramTypes, t => t != null && t.Contains("SelfStorageManagementSystem.DataAccess"));

        // Must inject IStorageUnitAdminService
        Assert.Contains(paramTypes, t => t != null && t.Contains("IStorageUnitAdminService"));
    }

    [Fact]
    public void StorageUnitsController_PostActions_HaveValidateAntiForgeryToken()
    {
        var controllerType = typeof(StorageUnitsController);
        var postMethods = controllerType.GetMethods()
            .Where(m => m.GetCustomAttributes(typeof(HttpPostAttribute), false).Any())
            .ToList();

        Assert.NotEmpty(postMethods);

        foreach (var method in postMethods)
        {
            var hasAntiCsrf = method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), false).Any() ||
                              controllerType.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), false).Any() ||
                              controllerType.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), false).Any();
            Assert.True(hasAntiCsrf, $"Action {method.Name} with [HttpPost] must enforce [ValidateAntiForgeryToken].");
        }
    }

    [Fact]
    public async Task AnonymousAccess_ToStorageUnits_RedirectsToLogin()
    {
        using var client = CreateClient(allowAutoRedirect: false);

        var indexResp = await client.GetAsync("/StorageUnits");
        Assert.Equal(HttpStatusCode.Redirect, indexResp.StatusCode);
        Assert.Contains("/Account/Login", indexResp.Headers.Location?.ToString());

        var createResp = await client.GetAsync("/StorageUnits/Create");
        Assert.Equal(HttpStatusCode.Redirect, createResp.StatusCode);
        Assert.Contains("/Account/Login", createResp.Headers.Location?.ToString());

        var editResp = await client.GetAsync("/StorageUnits/Edit?id=1");
        Assert.Equal(HttpStatusCode.Redirect, editResp.StatusCode);
        Assert.Contains("/Account/Login", editResp.Headers.Location?.ToString());

        var deleteResp = await client.GetAsync("/StorageUnits/Delete?id=1");
        Assert.Equal(HttpStatusCode.Redirect, deleteResp.StatusCode);
        Assert.Contains("/Account/Login", deleteResp.Headers.Location?.ToString());
    }

    [Fact]
    public async Task CustomerRole_AccessStorageUnits_IsForbidden()
    {
        var (client, authCookie, _) = await LoginAsRoleAsync(RoleConstants.StorageCustomer);

        var req = new HttpRequestMessage(HttpMethod.Get, "/StorageUnits");
        req.Headers.Add("Cookie", authCookie);

        var resp = await client.SendAsync(req);
        Assert.True(resp.StatusCode == HttpStatusCode.Forbidden || resp.StatusCode == HttpStatusCode.Redirect);
        if (resp.StatusCode == HttpStatusCode.Redirect)
        {
            Assert.Contains("Forbidden", resp.Headers.Location?.ToString() ?? "");
        }
    }

    [Fact]
    public async Task StorageUnits_Index_PaginationAndSearch_RenderProperly()
    {
        var (facility, unitType, area, unit) = await SeedStorageUnitHierarchyAsync("MVC_SRCH");
        var (client, authCookie, _) = await LoginAsRoleAsync(RoleConstants.FacilityManager, facility.id);

        var req = new HttpRequestMessage(HttpMethod.Get, $"/StorageUnits?facilityId={facility.id}&searchTerm={unit.unit_code}&pageNumber=1&pageSize=10");
        req.Headers.Add("Cookie", authCookie);

        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var html = await resp.Content.ReadAsStringAsync();
        Assert.Contains("Quản Trị Danh Mục Ô Kho", html);
        Assert.Contains(unit.unit_code, html);
        Assert.Contains("storageUnitsTableBody", html);
        Assert.Contains("storageUnitsFooterContainer", html);
    }

    private async Task<(HttpClient Client, string AuthCookie, user User)> LoginAsRoleAsync(
        string roleCode,
        long? assignToFacilityId = null,
        string assignmentRole = "facility_manager")
    {
        var email = $"user_{roleCode}_{Guid.NewGuid():N}@example.test";
        var password = "SecurePassword123!";
        var user = await _factory.SeedUserAsync(email, password, roleCode, UserStatusConstants.Active, $"Test {roleCode}");

        if (assignToFacilityId.HasValue && (roleCode == RoleConstants.FacilityStaff || roleCode == RoleConstants.FacilityManager))
        {
            using var db = _factory.CreateDbContext();
            db.staff_facility_assignments.Add(new staff_facility_assignment
            {
                employee_id = user.id,
                facility_id = assignToFacilityId.Value,
                assignment_role = assignmentRole,
                starts_at = DateTimeOffset.UtcNow.AddDays(-5),
                ends_at = null,
                created_at = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var client = CreateClient(allowAutoRedirect: false);
        var (token, afCookie) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(client, "/Account/Login");

        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/Account/Login");
        loginReq.Headers.Add("Cookie", afCookie);
        loginReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = token,
            ["ReturnUrl"] = "/"
        });

        var loginResp = await client.SendAsync(loginReq);
        Assert.Equal(HttpStatusCode.Redirect, loginResp.StatusCode);

        var authHeader = loginResp.Headers.GetValues("Set-Cookie")
            .FirstOrDefault(c => c.Contains(CookieAuthExtensions.CookieName));
        Assert.NotNull(authHeader);
        var authCookie = authHeader!.Split(';')[0];

        return (client, authCookie, user);
    }

    private async Task<(facility Facility, unit_type UnitType, facility_area Area, storage_unit Unit)> SeedStorageUnitHierarchyAsync(
        string suffix,
        string physicalStatus = "available",
        bool isListed = true)
    {
        using var db = _factory.CreateDbContext();
        var facId = (long)(Math.Abs(Guid.NewGuid().GetHashCode()) % 80000 + 1000);
        var facility = new facility
        {
            id = facId,
            code = $"FAC_{suffix.ToUpperInvariant()}",
            name = $"Cơ Sở Test {suffix}",
            address_line = $"Số 100 Phố {suffix}",
            ward = "Phường 1",
            district = "Quận Ba Đình",
            city = "Hà Nội",
            status = "active",
            timezone = "Asia/Ho_Chi_Minh",
            opening_time = new TimeOnly(8, 0),
            closing_time = new TimeOnly(20, 0),
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        db.facilities.Add(facility);

        var areaId = facId * 10 + 1;
        var area = new facility_area
        {
            id = areaId,
            facility_id = facId,
            code = $"AREA_{suffix.ToUpperInvariant()}",
            name = $"Khu Vực {suffix} - Tầng 1",
            area_type = "floor",
            display_order = 1,
            map_metadata = "{}",
            is_active = true
        };
        db.facility_areas.Add(area);

        var typeId = facId * 10 + 2;
        var unitType = new unit_type
        {
            id = typeId,
            code = $"TYPE_{suffix.ToUpperInvariant()}",
            name = $"Loại Kho {suffix}",
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

        var rateId = facId * 10 + 3;
        var rate = new facility_rate
        {
            id = rateId,
            facility_id = facId,
            unit_type_id = typeId,
            monthly_rate = 1500000m,
            deposit_amount = 1500000m,
            booking_fee = 200000m,
            valid_from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
            valid_to = null,
            created_at = DateTimeOffset.UtcNow
        };
        db.facility_rates.Add(rate);

        var unitId = facId * 10 + 4;
        var unit = new storage_unit
        {
            id = unitId,
            facility_id = facId,
            unit_type_id = typeId,
            area_id = areaId,
            unit_code = $"UNIT_{suffix.ToUpperInvariant()}",
            floor_label = "Tầng 1",
            zone_label = "Dãy A",
            physical_status = physicalStatus,
            is_listed = isListed,
            notes = "Ô kho kiểm thử",
            created_at = DateTimeOffset.UtcNow.AddDays(-5),
            updated_at = DateTimeOffset.UtcNow.AddDays(-1)
        };
        db.storage_units.Add(unit);

        await db.SaveChangesAsync();
        return (facility, unitType, area, unit);
    }
}
