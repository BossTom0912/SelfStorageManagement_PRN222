using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.Web.Extensions;
using SelfStorageManagementSystem.WebTests.Fixtures;
using Xunit;

namespace SelfStorageManagementSystem.WebTests;

/// <summary>
/// Bộ kiểm thử tích hợp toàn diện cho Task T09:
/// Quản trị danh mục ô kho Razor Pages CRUD bằng Form:
/// - Index: Bảng dữ liệu phân trang, lọc theo cơ sở/loại/trạng thái, ID dòng ổn định unit-row-id và message area cho T13.
/// - Create: POST form kèm Antiforgery token, validation, lookups theo scope cơ sở, duplicate code conflict, chặn staff ghi.
/// - Edit: Thuộc tính FacilityId và UnitTypeId bất biến, truyền ExpectedUpdatedAt round-trip, stale concurrency conflict.
/// - Details: Hiển thị đầy đủ quy cách, kích thước, biểu giá và thống kê tham chiếu.
/// - Delete: GET chỉ xác nhận (không xóa dữ liệu), POST xóa an toàn với ExpectedUpdatedAt, chặn xóa ô có tham chiếu hoặc occupied.
/// </summary>
public class StorageUnitsRazorPagesTests : IClassFixture<CookieAuthWebTestFixture>
{
    private readonly CookieAuthWebTestFixture _factory;

    public StorageUnitsRazorPagesTests(CookieAuthWebTestFixture factory)
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

    private static string ExtractHiddenInputValue(string html, string inputName)
    {
        var escaped = Regex.Escape(inputName);
        var match = Regex.Match(html, $@"name=""{escaped}""[^>]*value=""([^""]*)""");
        if (!match.Success)
        {
            match = Regex.Match(html, $@"value=""([^""]*)""[^>]*name=""{escaped}""");
        }
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : string.Empty;
    }

    private static string ExtractToken(string html)
    {
        return ExtractHiddenInputValue(html, "__RequestVerificationToken");
    }

    private async Task<(facility Facility, unit_type UnitType, facility_area Area, storage_unit Unit)> SeedStorageUnitHierarchyAsync(
        string suffix,
        string physicalStatus = "available",
        bool isListed = true)
    {
        using var db = _factory.CreateDbContext();

        // Sử dụng khoảng giá trị an toàn tránh tràn số nguyên khi nhân 10
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

    #region 1. Index Page Tests: Quyền hạn, ID ổn định & Vùng Message

    [Fact]
    public async Task Index_AnonymousUser_RedirectsToLogin()
    {
        var client = CreateClient(allowAutoRedirect: false);
        var response = await client.GetAsync("/StorageUnits");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Index_CustomerRole_IsForbidden()
    {
        var (client, authCookie, _) = await LoginAsRoleAsync(RoleConstants.StorageCustomer);

        var req = new HttpRequestMessage(HttpMethod.Get, "/StorageUnits");
        req.Headers.Add("Cookie", authCookie);

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Forbidden", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Index_StaffUser_RendersTable_WithStableRowId_AndMessageArea()
    {
        var (facility, _, _, unit) = await SeedStorageUnitHierarchyAsync("IDX1");
        var (client, authCookie, _) = await LoginAsRoleAsync(RoleConstants.FacilityStaff, facility.id, RoleConstants.FacilityStaff);

        var req = new HttpRequestMessage(HttpMethod.Get, $"/StorageUnits?facilityId={facility.id}");
        req.Headers.Add("Cookie", authCookie);

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        var decodedHtml = WebUtility.HtmlDecode(html);

        // 1. Chuẩn bị ID dòng ổn định cho T13
        Assert.Contains($"id=\"unit-row-{unit.id}\"", html);
        Assert.Contains($"data-unit-id=\"{unit.id}\"", html);

        // 2. Chuẩn bị vùng message cho T13
        Assert.Contains("id=\"storageUnitsMessageArea\"", html);

        // 3. Hiển thị thông tin ô kho và cơ sở
        Assert.Contains(unit.unit_code, html);
        Assert.Contains(facility.name, decodedHtml);
    }

    [Fact]
    public async Task Index_FilterByStatus_ReturnsOnlyMatchingUnits()
    {
        var (facility, _, _, unit1) = await SeedStorageUnitHierarchyAsync("FLT1", physicalStatus: "available");
        var (_, _, _, unit2) = await SeedStorageUnitHierarchyAsync("FLT2", physicalStatus: "maintenance");

        var (client, authCookie, _) = await LoginAsRoleAsync(RoleConstants.SystemAdministrator);

        var req = new HttpRequestMessage(HttpMethod.Get, $"/StorageUnits?physicalStatus=maintenance");
        req.Headers.Add("Cookie", authCookie);

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains(unit2.unit_code, html);
        Assert.DoesNotContain(unit1.unit_code, html);
    }

    #endregion

    #region 2. Create Page Tests: GET form, POST, Antiforgery, Conflict & Scope

    [Fact]
    public async Task Create_Get_Manager_RendersFormWithLookups()
    {
        var (facility, unitType, area, _) = await SeedStorageUnitHierarchyAsync("CR1");
        var (client, authCookie, _) = await LoginAsRoleAsync(RoleConstants.FacilityManager, facility.id);

        var req = new HttpRequestMessage(HttpMethod.Get, $"/StorageUnits/Create?facilityId={facility.id}");
        req.Headers.Add("Cookie", authCookie);

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        var decodedHtml = WebUtility.HtmlDecode(html);

        Assert.Contains("__RequestVerificationToken", html);
        Assert.Contains(facility.name, decodedHtml);
        Assert.Contains(unitType.name, decodedHtml);
        Assert.Contains("btnSubmitCreate", html);
    }

    [Fact]
    public async Task Create_Post_Manager_ValidData_CreatesUnit_AndRedirectsWithSuccessMessage()
    {
        var (facility, unitType, area, _) = await SeedStorageUnitHierarchyAsync("CR2");
        var (client, authCookie, _) = await LoginAsRoleAsync(RoleConstants.FacilityManager, facility.id);

        // Lấy Antiforgery Token từ GET Create
        var getReq = new HttpRequestMessage(HttpMethod.Get, $"/StorageUnits/Create?facilityId={facility.id}");
        getReq.Headers.Add("Cookie", authCookie);
        var getResp = await client.SendAsync(getReq);
        var getHtml = await getResp.Content.ReadAsStringAsync();

        var token = ExtractToken(getHtml);
        Assert.False(string.IsNullOrEmpty(token));

        var newUnitCode = $"HN_{Guid.NewGuid():N}"[..12].ToUpperInvariant();

        var postReq = new HttpRequestMessage(HttpMethod.Post, "/StorageUnits/Create");
        postReq.Headers.Add("Cookie", authCookie);
        postReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.FacilityId"] = facility.id.ToString(),
            ["Input.UnitTypeId"] = unitType.id.ToString(),
            ["Input.AreaId"] = area.id.ToString(),
            ["Input.UnitCode"] = newUnitCode,
            ["Input.FloorLabel"] = "Tầng 2",
            ["Input.ZoneLabel"] = "Dãy B",
            ["Input.Notes"] = "Ghi chú tạo mới test"
        });

        var postResp = await client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.Redirect, postResp.StatusCode);
        Assert.Contains("/StorageUnits", postResp.Headers.Location?.OriginalString);

        // Xác thực trong CSDL ô kho đã được tạo thành công
        using var db = _factory.CreateDbContext();
        var created = await db.storage_units.FirstOrDefaultAsync(u => u.unit_code == newUnitCode);
        Assert.NotNull(created);
        Assert.Equal("available", created!.physical_status);
        Assert.True(created.is_listed);
        Assert.Equal(facility.id, created.facility_id);
    }

    [Fact]
    public async Task Create_Post_DuplicateUnitCode_ReturnsConflictError_AndPreservesInput()
    {
        var (facility, unitType, area, existingUnit) = await SeedStorageUnitHierarchyAsync("CR3");
        var (client, authCookie, _) = await LoginAsRoleAsync(RoleConstants.FacilityManager, facility.id);

        var getReq = new HttpRequestMessage(HttpMethod.Get, $"/StorageUnits/Create?facilityId={facility.id}");
        getReq.Headers.Add("Cookie", authCookie);
        var getResp = await client.SendAsync(getReq);
        var getHtml = await getResp.Content.ReadAsStringAsync();

        var token = ExtractToken(getHtml);
        Assert.False(string.IsNullOrEmpty(token));

        // Gửi trùng unit_code
        var postReq = new HttpRequestMessage(HttpMethod.Post, "/StorageUnits/Create");
        postReq.Headers.Add("Cookie", authCookie);
        postReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.FacilityId"] = facility.id.ToString(),
            ["Input.UnitTypeId"] = unitType.id.ToString(),
            ["Input.AreaId"] = area.id.ToString(),
            ["Input.UnitCode"] = existingUnit.unit_code,
            ["Input.FloorLabel"] = "Tầng 1",
            ["Input.ZoneLabel"] = "Dãy A"
        });

        var postResp = await client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.OK, postResp.StatusCode);

        var html = await postResp.Content.ReadAsStringAsync();
        var decodedHtml = WebUtility.HtmlDecode(html);
        Assert.Contains(existingUnit.unit_code, html);
        Assert.True(decodedHtml.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
                    decodedHtml.Contains("đã tồn tại", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Create_Post_ManagerOutsideFacilityScope_ReturnsForbiddenError()
    {
        var (fac1, _, _, _) = await SeedStorageUnitHierarchyAsync("FAC1");
        var (fac2, type2, area2, _) = await SeedStorageUnitHierarchyAsync("FAC2");

        // Manager chỉ được gán vào fac1
        var (client, authCookie, _) = await LoginAsRoleAsync(RoleConstants.FacilityManager, fac1.id);

        var getReq = new HttpRequestMessage(HttpMethod.Get, $"/StorageUnits/Create?facilityId={fac1.id}");
        getReq.Headers.Add("Cookie", authCookie);
        var getResp = await client.SendAsync(getReq);
        var getHtml = await getResp.Content.ReadAsStringAsync();
        var token = ExtractToken(getHtml);
        Assert.False(string.IsNullOrEmpty(token));

        // Cố tình gửi tạo ô kho tại fac2
        var postReq = new HttpRequestMessage(HttpMethod.Post, "/StorageUnits/Create");
        postReq.Headers.Add("Cookie", authCookie);
        postReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.FacilityId"] = fac2.id.ToString(),
            ["Input.UnitTypeId"] = type2.id.ToString(),
            ["Input.AreaId"] = area2.id.ToString(),
            ["Input.UnitCode"] = "ILLEGAL_UNIT",
            ["Input.FloorLabel"] = "Tầng 1"
        });

        var postResp = await client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.Redirect, postResp.StatusCode);
        Assert.Contains("/Account/Forbidden", postResp.Headers.Location?.OriginalString);
        using var db = _factory.CreateDbContext();
        Assert.False(await db.storage_units.AnyAsync(u => u.unit_code == "ILLEGAL_UNIT"));
    }

    [Fact]
    public async Task Create_Post_StaffUser_IsReadOnly_CannotCreateUnit()
    {
        var (facility, unitType, area, _) = await SeedStorageUnitHierarchyAsync("CR4");
        var (client, authCookie, _) = await LoginAsRoleAsync(RoleConstants.FacilityStaff, facility.id, RoleConstants.FacilityStaff);

        var getReq = new HttpRequestMessage(HttpMethod.Get, $"/StorageUnits/Create?facilityId={facility.id}");
        getReq.Headers.Add("Cookie", authCookie);
        var getResp = await client.SendAsync(getReq);
        var getHtml = await getResp.Content.ReadAsStringAsync();
        var token = ExtractToken(getHtml);
        Assert.False(string.IsNullOrEmpty(token));

        var postReq = new HttpRequestMessage(HttpMethod.Post, "/StorageUnits/Create");
        postReq.Headers.Add("Cookie", authCookie);
        postReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.FacilityId"] = facility.id.ToString(),
            ["Input.UnitTypeId"] = unitType.id.ToString(),
            ["Input.AreaId"] = area.id.ToString(),
            ["Input.UnitCode"] = "STAFF_FAIL_UNIT",
            ["Input.FloorLabel"] = "Tầng 1"
        });

        var postResp = await client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.Redirect, postResp.StatusCode);
        Assert.Contains("/Account/Forbidden", postResp.Headers.Location?.OriginalString);
        using var db = _factory.CreateDbContext();
        Assert.False(await db.storage_units.AnyAsync(u => u.unit_code == "STAFF_FAIL_UNIT"));
    }

    [Fact]
    public async Task Create_Get_FromIndex_WithFacilityId_PreservesFacility_RendersMatchingAreas_AndAjaxLookupsWork()
    {
        var (fac1, type1, area1, _) = await SeedStorageUnitHierarchyAsync("CRFLO1");
        var (fac2, type2, area2, _) = await SeedStorageUnitHierarchyAsync("CRFLO2");
        var (client, authCookie, _) = await LoginAsRoleAsync(RoleConstants.FacilityManager, fac1.id);

        // 1. Kiểm tra từ Index khi đang chọn facilityId thì link mở Create bảo toàn query parameter facilityId
        var indexReq = new HttpRequestMessage(HttpMethod.Get, $"/StorageUnits?facilityId={fac1.id}");
        indexReq.Headers.Add("Cookie", authCookie);
        var indexResp = await client.SendAsync(indexReq);
        Assert.Equal(HttpStatusCode.OK, indexResp.StatusCode);
        var indexHtml = await indexResp.Content.ReadAsStringAsync();
        Assert.Contains($"/StorageUnits/Create?facilityId={fac1.id}", indexHtml);

        // 2. Mở trang Create kèm facilityId: tự động chọn cơ sở và nạp đúng Areas thuộc cơ sở đó
        var createReq = new HttpRequestMessage(HttpMethod.Get, $"/StorageUnits/Create?facilityId={fac1.id}");
        createReq.Headers.Add("Cookie", authCookie);
        var createResp = await client.SendAsync(createReq);
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode);
        var createHtml = await createResp.Content.ReadAsStringAsync();
        var decodedCreateHtml = WebUtility.HtmlDecode(createHtml);

        Assert.Contains(fac1.name, decodedCreateHtml);
        Assert.Contains(area1.name, decodedCreateHtml); // Dropdown khu vực không rỗng
        Assert.Contains($"value=\"{area1.id}\"", createHtml);
        Assert.DoesNotContain(area2.name, decodedCreateHtml); // Không lẫn khu vực của cơ sở khác

        // 3. Kiểm tra handler AJAX nạp động lookups khi đổi cơ sở
        var ajaxReq = new HttpRequestMessage(HttpMethod.Get, $"/StorageUnits/Create?handler=FacilityLookups&facilityId={fac1.id}");
        ajaxReq.Headers.Add("Cookie", authCookie);
        var ajaxResp = await client.SendAsync(ajaxReq);
        var ajaxJson = await ajaxResp.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(ajaxJson);
        var areasArray = doc.RootElement.GetProperty("areas");
        Assert.True(areasArray.GetArrayLength() > 0);
        Assert.Equal(area1.name, areasArray[0].GetProperty("name").GetString());
        Assert.Equal(area1.code, areasArray[0].GetProperty("code").GetString());
        var unitTypesArray = doc.RootElement.GetProperty("unitTypes");
        Assert.True(unitTypesArray.GetArrayLength() > 0);
        Assert.Equal(type1.code, unitTypesArray[0].GetProperty("code").GetString());
    }

    #endregion

    #region 3. Edit Page Tests: Bất biến Facility/Type, Round-trip ExpectedUpdatedAt & Concurrency

    [Fact]
    public async Task Edit_Get_Manager_RendersImmutableFields_AndRoundTripExpectedUpdatedAt()
    {
        var (facility, unitType, area, unit) = await SeedStorageUnitHierarchyAsync("ED1");
        var (client, authCookie, _) = await LoginAsRoleAsync(RoleConstants.FacilityManager, facility.id);

        var req = new HttpRequestMessage(HttpMethod.Get, $"/StorageUnits/Edit?id={unit.id}");
        req.Headers.Add("Cookie", authCookie);

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        var decodedHtml = WebUtility.HtmlDecode(html);

        // 1. Facility và UnitType hiển thị dạng nhãn/thông tin bất biến (không có input editable)
        Assert.Contains(facility.name, decodedHtml);
        Assert.Contains(unitType.name, decodedHtml);
        Assert.DoesNotContain("<select name=\"Input.FacilityId\"", html);
        Assert.DoesNotContain("<select name=\"Input.UnitTypeId\"", html);

        // 2. ExpectedUpdatedAt được truyền dạng hidden round-trip
        Assert.Contains("name=\"Input.ExpectedUpdatedAt\"", html);
        Assert.Contains(unit.updated_at.ToString("yyyy-MM-dd"), html);
    }

    [Fact]
    public async Task Edit_Post_ValidData_UpdatesUnit_PreservesFacilityAndType_AndRedirects()
    {
        var (facility, unitType, area, unit) = await SeedStorageUnitHierarchyAsync("ED2");
        var (client, authCookie, _) = await LoginAsRoleAsync(RoleConstants.FacilityManager, facility.id);

        var getReq = new HttpRequestMessage(HttpMethod.Get, $"/StorageUnits/Edit?id={unit.id}");
        getReq.Headers.Add("Cookie", authCookie);
        var getResp = await client.SendAsync(getReq);
        var getHtml = await getResp.Content.ReadAsStringAsync();

        var token = ExtractToken(getHtml);
        Assert.False(string.IsNullOrEmpty(token));

        var expectedUpdatedAt = ExtractHiddenInputValue(getHtml, "Input.ExpectedUpdatedAt");
        Assert.False(string.IsNullOrEmpty(expectedUpdatedAt));

        var updatedCode = $"HN_{Guid.NewGuid():N}"[..12].ToUpperInvariant();

        var postReq = new HttpRequestMessage(HttpMethod.Post, "/StorageUnits/Edit");
        postReq.Headers.Add("Cookie", authCookie);
        postReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Id"] = unit.id.ToString(),
            ["Input.ExpectedUpdatedAt"] = expectedUpdatedAt,
            ["Input.UnitCode"] = updatedCode,
            ["Input.AreaId"] = area.id.ToString(),
            ["Input.FloorLabel"] = "Tầng 3",
            ["Input.ZoneLabel"] = "Dãy C",
            ["Input.Notes"] = "Cập nhật thành công qua form"
        });

        var postResp = await client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.Redirect, postResp.StatusCode);

        // Kiểm tra cơ sở dữ liệu
        using var db = _factory.CreateDbContext();
        var reloaded = await db.storage_units.FindAsync(unit.id);
        Assert.NotNull(reloaded);
        Assert.Equal(updatedCode, reloaded!.unit_code);
        Assert.Equal("Tầng 3", reloaded.floor_label);
        Assert.Equal(facility.id, reloaded.facility_id); // Facility giữ nguyên
        Assert.Equal(unitType.id, reloaded.unit_type_id); // UnitType giữ nguyên
    }

    [Fact]
    public async Task Edit_Post_StaleExpectedUpdatedAt_ReturnsConcurrencyConflictAlert_WithRefreshOption()
    {
        var (facility, unitType, area, unit) = await SeedStorageUnitHierarchyAsync("ED3");
        var (client, authCookie, _) = await LoginAsRoleAsync(RoleConstants.FacilityManager, facility.id);

        var getReq = new HttpRequestMessage(HttpMethod.Get, $"/StorageUnits/Edit?id={unit.id}");
        getReq.Headers.Add("Cookie", authCookie);
        var getResp = await client.SendAsync(getReq);
        var getHtml = await getResp.Content.ReadAsStringAsync();
        var token = ExtractToken(getHtml);
        Assert.False(string.IsNullOrEmpty(token));

        // Giả lập phiên khác đã cập nhật bản ghi này trong DB trước đó
        using (var db = _factory.CreateDbContext())
        {
            var target = await db.storage_units.FindAsync(unit.id);
            target!.updated_at = DateTimeOffset.UtcNow.AddMinutes(5);
            await db.SaveChangesAsync();
        }

        // Gửi form với mốc thời gian cũ (stale)
        var postReq = new HttpRequestMessage(HttpMethod.Post, "/StorageUnits/Edit");
        postReq.Headers.Add("Cookie", authCookie);
        postReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Id"] = unit.id.ToString(),
            ["Input.ExpectedUpdatedAt"] = unit.updated_at.ToString("O"), // Mốc cũ!
            ["Input.UnitCode"] = "CONCURRENT_CONFLICT_CODE",
            ["Input.FloorLabel"] = "Tầng 1"
        });

        var postResp = await client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.OK, postResp.StatusCode);

        var html = await postResp.Content.ReadAsStringAsync();
        var decodedHtml = WebUtility.HtmlDecode(html);
        Assert.Contains("Phát hiện xung đột đồng thời", decodedHtml);
        Assert.Contains("Tải Lại Dữ Liệu Mới Nhất", decodedHtml);
    }

    #endregion

    #region 4. Details Page Tests: Hiển thị đầy đủ thông số & Tham chiếu

    [Fact]
    public async Task Details_Get_Staff_RendersComprehensiveUnitSpecsAndReferenceSummary()
    {
        var (facility, unitType, area, unit) = await SeedStorageUnitHierarchyAsync("DT1");
        var (client, authCookie, _) = await LoginAsRoleAsync(RoleConstants.FacilityStaff, facility.id, RoleConstants.FacilityStaff);

        var req = new HttpRequestMessage(HttpMethod.Get, $"/StorageUnits/Details?id={unit.id}");
        req.Headers.Add("Cookie", authCookie);

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        var decodedHtml = WebUtility.HtmlDecode(html);
        Assert.Contains(unit.unit_code, html);
        Assert.Contains(facility.name, decodedHtml);
        Assert.Contains(unitType.name, decodedHtml);
        Assert.Contains("Thông Tin Vị Trí & Cơ Sở", decodedHtml);
        Assert.Contains("Loại Ô Kho & Quy Cách Kỹ Thuật", decodedHtml);
        Assert.Contains("Thống Kê Tham Chiếu & Lịch Sử", decodedHtml);
    }

    #endregion

    #region 5. Delete Page Tests: GET chỉ xác nhận, POST an toàn & Ràng buộc tham chiếu

    [Fact]
    public async Task Delete_Get_OnlyShowsConfirmation_DoesNotDeleteUnit()
    {
        var (facility, unitType, area, unit) = await SeedStorageUnitHierarchyAsync("DEL1");
        var (client, authCookie, _) = await LoginAsRoleAsync(RoleConstants.FacilityManager, facility.id);

        var req = new HttpRequestMessage(HttpMethod.Get, $"/StorageUnits/Delete?id={unit.id}");
        req.Headers.Add("Cookie", authCookie);

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        var decodedHtml = WebUtility.HtmlDecode(html);
        Assert.Contains("Xác Nhận Xóa Ô Kho", decodedHtml);
        Assert.Contains(unit.unit_code, html);
        Assert.Contains("btnSubmitDelete", html);

        // Chứng minh GET không làm thay đổi hay xóa dữ liệu trong DB
        using var db = _factory.CreateDbContext();
        var stillExists = await db.storage_units.FindAsync(unit.id);
        Assert.NotNull(stillExists);
    }

    [Fact]
    public async Task Delete_Post_AvailableUnitWithoutReferences_DeletesSuccessfully()
    {
        var (facility, unitType, area, unit) = await SeedStorageUnitHierarchyAsync("DEL2");
        var (client, authCookie, _) = await LoginAsRoleAsync(RoleConstants.FacilityManager, facility.id);

        var getReq = new HttpRequestMessage(HttpMethod.Get, $"/StorageUnits/Delete?id={unit.id}");
        getReq.Headers.Add("Cookie", authCookie);
        var getResp = await client.SendAsync(getReq);
        var getHtml = await getResp.Content.ReadAsStringAsync();

        var token = ExtractToken(getHtml);
        Assert.False(string.IsNullOrEmpty(token));

        var expectedUpdatedAt = ExtractHiddenInputValue(getHtml, "ExpectedUpdatedAt");
        Assert.False(string.IsNullOrEmpty(expectedUpdatedAt));

        var postReq = new HttpRequestMessage(HttpMethod.Post, "/StorageUnits/Delete");
        postReq.Headers.Add("Cookie", authCookie);
        postReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Id"] = unit.id.ToString(),
            ["ExpectedUpdatedAt"] = expectedUpdatedAt
        });

        var postResp = await client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.Redirect, postResp.StatusCode);

        // Xác thực ô kho đã được xóa sạch khỏi cơ sở dữ liệu
        using var db = _factory.CreateDbContext();
        var deleted = await db.storage_units.FindAsync(unit.id);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task Delete_Post_UnitWithAllocationsOrOccupied_IsBlockedByPolicy()
    {
        var (facility, unitType, area, unit) = await SeedStorageUnitHierarchyAsync("DEL3", physicalStatus: "occupied");

        // Thêm bản ghi gán thuê (relational reference)
        using (var db = _factory.CreateDbContext())
        {
            db.unit_allocations.Add(new unit_allocation
            {
                storage_unit_id = unit.id,
                allocation_kind = "regular",
                allocation_start_date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)),
                allocation_end_date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
                status = "active",
                created_at = DateTimeOffset.UtcNow.AddDays(-2)
            });
            await db.SaveChangesAsync();
        }

        var (client, authCookie, _) = await LoginAsRoleAsync(RoleConstants.FacilityManager, facility.id);

        // 1. GET Delete phải hiển thị cảnh báo chặn xóa
        var getReq = new HttpRequestMessage(HttpMethod.Get, $"/StorageUnits/Delete?id={unit.id}");
        getReq.Headers.Add("Cookie", authCookie);
        var getResp = await client.SendAsync(getReq);
        var getHtml = await getResp.Content.ReadAsStringAsync();
        var decodedGetHtml = WebUtility.HtmlDecode(getHtml);

        Assert.Contains("Không thể xóa ô kho này", decodedGetHtml);
        Assert.DoesNotContain("<button type=\"submit\" id=\"btnSubmitDelete\"", getHtml); // Nút xác nhận xóa bị ẩn khi không đủ điều kiện

        var token = ExtractToken(getHtml);
        Assert.False(string.IsNullOrEmpty(token));

        // 2. Cố tình gửi POST qua request trực tiếp
        var postReq = new HttpRequestMessage(HttpMethod.Post, "/StorageUnits/Delete");
        postReq.Headers.Add("Cookie", authCookie);
        postReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Id"] = unit.id.ToString(),
            ["ExpectedUpdatedAt"] = unit.updated_at.ToString("O")
        });

        var postResp = await client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.OK, postResp.StatusCode);

        var postHtml = await postResp.Content.ReadAsStringAsync();
        var decodedPostHtml = WebUtility.HtmlDecode(postHtml);
        Assert.True(decodedPostHtml.Contains("cannot be deleted", StringComparison.OrdinalIgnoreCase) ||
                    decodedPostHtml.Contains("Không thể xóa", StringComparison.OrdinalIgnoreCase));

        // Xác thực ô kho vẫn tồn tại trong CSDL
        using (var db = _factory.CreateDbContext())
        {
            var stillThere = await db.storage_units.FindAsync(unit.id);
            Assert.NotNull(stillThere);
        }
    }

    [Fact]
    public async Task Delete_Post_ManagerOutsideFacilityScope_ReturnsForbiddenControlled_DoesNotReturn500_AndDoesNotDeleteData()
    {
        var (fac1, _, _, unit1) = await SeedStorageUnitHierarchyAsync("DLSCP1");
        var (fac2, _, _, unit2) = await SeedStorageUnitHierarchyAsync("DLSCP2");

        // Manager chỉ được phân công tại fac1, KHÔNG có quyền tại fac2
        var (client, authCookie, _) = await LoginAsRoleAsync(RoleConstants.FacilityManager, fac1.id);

        // Lấy Antiforgery token hợp lệ từ ô trong fac1
        var getReq = new HttpRequestMessage(HttpMethod.Get, $"/StorageUnits/Delete?id={unit1.id}");
        getReq.Headers.Add("Cookie", authCookie);
        var getResp = await client.SendAsync(getReq);
        var getHtml = await getResp.Content.ReadAsStringAsync();
        var token = ExtractToken(getHtml);
        Assert.False(string.IsNullOrEmpty(token));

        // Manager cố tình gửi POST xóa ô unit2 thuộc fac2 (ngoài phạm vi phân công)
        var postReq = new HttpRequestMessage(HttpMethod.Post, "/StorageUnits/Delete");
        postReq.Headers.Add("Cookie", authCookie);
        postReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Id"] = unit2.id.ToString(),
            ["ExpectedUpdatedAt"] = unit2.updated_at.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ")
        });

        var postResp = await client.SendAsync(postReq);

        // MVC forbids the operation through the configured access-denied redirect.
        Assert.Equal(HttpStatusCode.Redirect, postResp.StatusCode);
        Assert.Contains("/Account/Forbidden", postResp.Headers.Location?.OriginalString);
        // Xác thực ô kho unit2 KHÔNG bị xóa và vẫn tồn tại trong CSDL
        using var db = _factory.CreateDbContext();
        var stillExists = await db.storage_units.FindAsync(unit2.id);
        Assert.NotNull(stillExists);
    }

    #endregion
}
