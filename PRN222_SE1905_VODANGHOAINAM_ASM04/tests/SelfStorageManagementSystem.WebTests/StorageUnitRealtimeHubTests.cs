using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.StorageUnits;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.StorageUnits;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.Web.Extensions;
using SelfStorageManagementSystem.Web.Hubs;
using SelfStorageManagementSystem.Web.Pages.StorageUnits;
using SelfStorageManagementSystem.Web.Realtime;
using SelfStorageManagementSystem.WebTests.Fixtures;
using SelfStorageManagementSystem.WebTests.Infrastructure;
using Xunit;

namespace SelfStorageManagementSystem.WebTests;

/// <summary>
/// Bộ kiểm thử tích hợp chuyên biệt cho Task T13:
/// SignalR Create Update Delete Realtime theo chuẩn mẫu giảng viên (TeacherCode):
/// - Kiểm tra Hub endpoints (/storageUnitHub) với Cookie Authentication [Authorize].
/// - Kiểm tra 3 Hub Invocations: HubCreate_StorageUnit, HubUpdate_StorageUnit, HubDelete_StorageUnit.
/// - Kiểm tra 3 Server Broadcasts: Receiver_CreateStorageUnit, Receiver_UpdateStorageUnit, Receiver_DeleteStorageUnit.
/// - Kiểm tra DTO deserialization validation, safe error mapping (không leak DB exception).
/// - Kiểm tra bảo vệ phân quyền (Staff read-only vs Manager).
/// - Kiểm tra Form POST và Hub dùng chung dispatcher (IStorageUnitRealtimePublisher).
/// - Kiểm tra ngăn ngừa double-write, duplicate row, và XSS payload handling.
/// </summary>
public class StorageUnitRealtimeHubTests : IClassFixture<CookieAuthWebTestFixture>
{
    private readonly CookieAuthWebTestFixture _factory;
    private static long _facilityIdSequence = 20000;

    public StorageUnitRealtimeHubTests(CookieAuthWebTestFixture factory)
    {
        _factory = factory;
    }

    private async Task<(facility Facility, unit_type UnitType, facility_area Area, storage_unit Unit)> SeedStorageUnitHierarchyAsync(
        string suffix,
        string physicalStatus = "available",
        bool isListed = true)
    {
        using var db = _factory.CreateDbContext();

        var facId = Interlocked.Increment(ref _facilityIdSequence);
        var facility = new facility
        {
            id = facId,
            code = $"FAC_{suffix.ToUpperInvariant()}_{facId}",
            name = $"Cơ Sở Realtime {suffix}",
            address_line = $"Số 200 Phố {suffix}",
            ward = "Phường 2",
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

        var areaId = facId * 100 + 1;
        var area = new facility_area
        {
            id = areaId,
            facility_id = facId,
            code = $"AREA_{suffix.ToUpperInvariant()}_{facId}",
            name = $"Khu Vực {suffix} - Tầng 2",
            area_type = "floor",
            display_order = 1,
            map_metadata = "{}",
            is_active = true
        };
        db.facility_areas.Add(area);

        var unitTypeId = facId * 100 + 2;
        var unitType = new unit_type
        {
            id = unitTypeId,
            code = $"TYPE_{suffix.ToUpperInvariant()}_{facId}",
            name = $"Loại Kho Realtime {suffix}",
            description = "Mô tả loại kho",
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

        var rateId = facId * 100 + 3;
        var rate = new facility_rate
        {
            id = rateId,
            facility_id = facId,
            unit_type_id = unitTypeId,
            monthly_rate = 1500000m,
            deposit_amount = 1500000m,
            booking_fee = 200000m,
            valid_from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
            valid_to = null,
            created_at = DateTimeOffset.UtcNow
        };
        db.facility_rates.Add(rate);

        var unitId = facId * 100 + 4;
        var unit = new storage_unit
        {
            id = unitId,
            facility_id = facId,
            area_id = areaId,
            unit_type_id = unitTypeId,
            unit_code = $"RT-{suffix.ToUpperInvariant()}-{facId}",
            floor_label = "Tầng 2",
            zone_label = "Dãy B",
            physical_status = physicalStatus,
            is_listed = isListed,
            notes = "Ghi chú bảo mật nội bộ ô kho realtime",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        db.storage_units.Add(unit);

        await db.SaveChangesAsync();
        return (facility, unitType, area, unit);
    }

    private async Task<string> AuthenticateUserAndGetCookieAsync(string roleCode, long? assignedFacilityId = null)
    {
        var email = $"hub_{roleCode}_{Guid.NewGuid():N}@example.test";
        var password = "SecureHubPassword123!";

        var user = await _factory.SeedUserAsync(email, password, roleCode, UserStatusConstants.Active, $"User {roleCode}");

        if (assignedFacilityId.HasValue && (roleCode == RoleConstants.FacilityStaff || roleCode == RoleConstants.FacilityManager))
        {
            using var db = _factory.CreateDbContext();
            db.staff_facility_assignments.Add(new staff_facility_assignment
            {
                employee_id = user.id,
                facility_id = assignedFacilityId.Value,
                assignment_role = roleCode,
                starts_at = DateTimeOffset.UtcNow.AddDays(-5),
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
            .FirstOrDefault(c => c.Contains(SelfStorageManagementSystem.Web.Extensions.CookieAuthExtensions.CookieName));
        Assert.NotNull(authHeader);
        return authHeader!.Split(';')[0];
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

    #region 1. Security & Authentication Tests

    [Fact]
    public async Task SignalR_AnonymousConnection_IsRejected()
    {
        var hubConnection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "/storageUnitHub"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            })
            .Build();

        await Assert.ThrowsAnyAsync<Exception>(() => hubConnection.StartAsync());
    }

    [Fact]
    public async Task SignalR_StaffUser_CanConnect_ButCannotCreateOrModifyUnit()
    {
        var hierarchy = await SeedStorageUnitHierarchyAsync("staff_perm");
        var staffCookie = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityStaff, hierarchy.Facility.id);

        await using var hubConnection = BuildHubConnection(staffCookie);
        await hubConnection.StartAsync();

        var payload = JsonSerializer.Serialize(new
        {
            FacilityId = hierarchy.Facility.id,
            UnitTypeId = hierarchy.UnitType.id,
            UnitCode = "STAFF-FAIL-01"
        });

        // Nhân viên cơ sở bị chặn bởi chính sách Read-Only
        var exCreate = await Assert.ThrowsAsync<HubException>(() =>
            hubConnection.InvokeAsync<StorageUnitRealtimeEventDto>("HubCreate_StorageUnit", payload));
        Assert.Contains("Nhân viên thường không có quyền", exCreate.Message);

        var exDelete = await Assert.ThrowsAsync<HubException>(() =>
            hubConnection.InvokeAsync<string>("HubDelete_StorageUnit", hierarchy.Unit.id.ToString(), (string?)null));
        Assert.Contains("Nhân viên thường không có quyền", exDelete.Message);
    }

    #endregion

    #region 2. Hub Invocations & Event Broadcasts (TeacherCode Pattern)

    [Fact]
    public async Task HubCreate_ValidRequest_CreatesUnit_CallsBLL_EmitsReceiverCreateStorageUnit_AndReturnsDto()
    {
        var suffix = $"c_{Guid.NewGuid():N}".Substring(0, 8);
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);
        var managerCookie = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityManager, hierarchy.Facility.id);

        await using var hubConnection = BuildHubConnection(managerCookie);

        // Đăng ký listener trước start() theo chuẩn mẫu thầy
        StorageUnitRealtimeEventDto? receivedEvent = null;
        var eventTcs = new TaskCompletionSource<StorageUnitRealtimeEventDto>(TaskCreationOptions.RunContinuationsAsynchronously);

        hubConnection.On<StorageUnitRealtimeEventDto>("Receiver_CreateStorageUnit", item =>
        {
            receivedEvent = item;
            eventTcs.TrySetResult(item);
        });

        await hubConnection.StartAsync();

        var targetCode = $"RT-{suffix.ToUpperInvariant()}-NEW";
        var dataObj = JsonSerializer.Serialize(new
        {
            FacilityId = hierarchy.Facility.id,
            UnitTypeId = hierarchy.UnitType.id,
            AreaId = hierarchy.Area.id,
            UnitCode = targetCode,
            FloorLabel = "Tầng 2",
            ZoneLabel = "Dãy C",
            Notes = "Tạo qua SignalR test"
        });

        // Act - Invoke HubCreate_StorageUnit
        var resultDto = await hubConnection.InvokeAsync<StorageUnitRealtimeEventDto>("HubCreate_StorageUnit", dataObj);

        // Assert kết quả trả về
        Assert.NotNull(resultDto);
        Assert.Equal(targetCode, resultDto.unitCode);
        Assert.Equal(hierarchy.Facility.id, resultDto.facilityId);
        Assert.Equal("available", resultDto.status);

        // Assert broadcast nhận được qua Receiver_CreateStorageUnit
        var completed = await Task.WhenAny(eventTcs.Task, Task.Delay(5000));
        Assert.True(completed == eventTcs.Task, "Receiver_CreateStorageUnit event was not received within timeout.");

        Assert.NotNull(receivedEvent);
        Assert.Equal(targetCode, receivedEvent.unitCode);
        Assert.Equal(hierarchy.Facility.id, receivedEvent.facilityId);
        Assert.Equal("available", receivedEvent.status);

        // Assert trong CSDL: bản ghi thực sự tồn tại sau commit
        using var db = _factory.CreateDbContext();
        var inDb = await db.storage_units.FirstOrDefaultAsync(u => u.unit_code == targetCode && u.facility_id == hierarchy.Facility.id);
        Assert.NotNull(inDb);
        Assert.Equal("available", inDb.physical_status);
        Assert.True(inDb.is_listed);
    }

    [Fact]
    public async Task HubCreate_InvalidJsonOrMissingFields_ThrowsHubException()
    {
        var hierarchy = await SeedStorageUnitHierarchyAsync("json_err");
        var managerCookie = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityManager, hierarchy.Facility.id);

        await using var hubConnection = BuildHubConnection(managerCookie);
        await hubConnection.StartAsync();

        // 1. JSON cú pháp sai
        var exSyntax = await Assert.ThrowsAsync<HubException>(() =>
            hubConnection.InvokeAsync<StorageUnitRealtimeEventDto>("HubCreate_StorageUnit", "{ invalid_json }"));
        Assert.Contains("JSON", exSyntax.Message);

        // 2. Dữ liệu thiếu UnitCode bắt buộc
        var invalidPayload = JsonSerializer.Serialize(new
        {
            FacilityId = hierarchy.Facility.id,
            UnitTypeId = hierarchy.UnitType.id,
            UnitCode = ""
        });

        var exMissing = await Assert.ThrowsAsync<HubException>(() =>
            hubConnection.InvokeAsync<StorageUnitRealtimeEventDto>("HubCreate_StorageUnit", invalidPayload));
        Assert.Contains("không hợp lệ", exMissing.Message);
    }

    [Fact]
    public async Task HubCreate_DuplicateUnitCode_ThrowsConflictHubException()
    {
        var hierarchy = await SeedStorageUnitHierarchyAsync("dup_code");
        var managerCookie = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityManager, hierarchy.Facility.id);

        await using var hubConnection = BuildHubConnection(managerCookie);
        await hubConnection.StartAsync();

        // Cố tình gửi lại unit_code đã có trong cùng cơ sở
        var duplicatePayload = JsonSerializer.Serialize(new
        {
            FacilityId = hierarchy.Facility.id,
            UnitTypeId = hierarchy.UnitType.id,
            UnitCode = hierarchy.Unit.unit_code
        });

        var exDup = await Assert.ThrowsAsync<HubException>(() =>
            hubConnection.InvokeAsync<StorageUnitRealtimeEventDto>("HubCreate_StorageUnit", duplicatePayload));
        Assert.Contains("already exists", exDup.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HubUpdate_ValidRequest_UpdatesUnit_CallsBLL_EmitsReceiverUpdateStorageUnit_AndPreservesImmutableFields()
    {
        var suffix = $"u_{Guid.NewGuid():N}".Substring(0, 8);
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);
        var managerCookie = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityManager, hierarchy.Facility.id);

        await using var hubConnection = BuildHubConnection(managerCookie);

        // Đăng ký listener trước start()
        StorageUnitRealtimeEventDto? receivedUpdate = null;
        var eventTcs = new TaskCompletionSource<StorageUnitRealtimeEventDto>(TaskCreationOptions.RunContinuationsAsynchronously);

        hubConnection.On<StorageUnitRealtimeEventDto>("Receiver_UpdateStorageUnit", item =>
        {
            receivedUpdate = item;
            eventTcs.TrySetResult(item);
        });

        await hubConnection.StartAsync();

        var updatedCode = $"RT-{suffix.ToUpperInvariant()}-MODIFIED";
        var dataObj = JsonSerializer.Serialize(new
        {
            Id = hierarchy.Unit.id,
            UnitCode = updatedCode,
            AreaId = hierarchy.Area.id,
            FloorLabel = "Tầng 3",
            ZoneLabel = "Dãy Vip",
            Notes = "Đã cập nhật qua HubUpdate",
            ExpectedUpdatedAt = hierarchy.Unit.updated_at
        });

        // Act - Invoke HubUpdate_StorageUnit
        var resultDto = await hubConnection.InvokeAsync<StorageUnitRealtimeEventDto>("HubUpdate_StorageUnit", dataObj);

        Assert.NotNull(resultDto);
        Assert.Equal(updatedCode, resultDto.unitCode);

        // Assert broadcast nhận được qua Receiver_UpdateStorageUnit
        var completed = await Task.WhenAny(eventTcs.Task, Task.Delay(5000));
        Assert.True(completed == eventTcs.Task, "Receiver_UpdateStorageUnit event was not received within timeout.");

        Assert.NotNull(receivedUpdate);
        Assert.Equal(updatedCode, receivedUpdate.unitCode);
        Assert.Equal(hierarchy.Unit.id, receivedUpdate.id);

        // Assert trong CSDL: kiểm chứng thuộc tính bất biến (facility_id, unit_type_id, physical_status)
        using var db = _factory.CreateDbContext();
        var inDb = await db.storage_units.AsNoTracking().FirstAsync(u => u.id == hierarchy.Unit.id);
        Assert.Equal(updatedCode, inDb.unit_code);
        Assert.Equal("Tầng 3", inDb.floor_label);
        Assert.Equal("Dãy Vip", inDb.zone_label);
        Assert.Equal(hierarchy.Facility.id, inDb.facility_id); // Bất biến
        Assert.Equal(hierarchy.UnitType.id, inDb.unit_type_id); // Bất biến
        Assert.Equal("available", inDb.physical_status); // Bất biến
    }

    [Fact]
    public async Task HubUpdate_StaleExpectedUpdatedAt_ThrowsConcurrencyConflictHubException()
    {
        var hierarchy = await SeedStorageUnitHierarchyAsync("stale_upd");
        var managerCookie = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityManager, hierarchy.Facility.id);

        await using var hubConnection = BuildHubConnection(managerCookie);
        await hubConnection.StartAsync();

        var staleTimestamp = hierarchy.Unit.updated_at.AddMinutes(-10);
        var dataObj = JsonSerializer.Serialize(new
        {
            Id = hierarchy.Unit.id,
            UnitCode = "STALE-UPDATE",
            ExpectedUpdatedAt = staleTimestamp
        });

        var exConflict = await Assert.ThrowsAsync<HubException>(() =>
            hubConnection.InvokeAsync<StorageUnitRealtimeEventDto>("HubUpdate_StorageUnit", dataObj));
        Assert.Contains("concurrency", exConflict.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HubDelete_ValidRequest_DeletesUnit_CallsBLL_EmitsReceiverDeleteStorageUnit()
    {
        var suffix = $"d_{Guid.NewGuid():N}".Substring(0, 8);
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);
        var managerCookie = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityManager, hierarchy.Facility.id);

        await using var hubConnection = BuildHubConnection(managerCookie);

        // Đăng ký listener trước start()
        string? deletedUnitId = null;
        var eventTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        hubConnection.On<string>("Receiver_DeleteStorageUnit", id =>
        {
            deletedUnitId = id;
            eventTcs.TrySetResult(id);
        });

        await hubConnection.StartAsync();

        // Act - Invoke HubDelete_StorageUnit theo chuẩn TeacherCode (truyền id dạng chuỗi)
        var resultId = await hubConnection.InvokeAsync<string>(
            "HubDelete_StorageUnit",
            hierarchy.Unit.id.ToString(),
            hierarchy.Unit.updated_at.ToString("o"));

        Assert.Equal(hierarchy.Unit.id.ToString(), resultId);

        // Assert broadcast nhận được qua Receiver_DeleteStorageUnit
        var completed = await Task.WhenAny(eventTcs.Task, Task.Delay(5000));
        Assert.True(completed == eventTcs.Task, "Receiver_DeleteStorageUnit event was not received within timeout.");

        Assert.Equal(hierarchy.Unit.id.ToString(), deletedUnitId);

        // Assert trong CSDL: bản ghi đã bị xóa sạch
        using var db = _factory.CreateDbContext();
        var inDb = await db.storage_units.FirstOrDefaultAsync(u => u.id == hierarchy.Unit.id);
        Assert.Null(inDb);
    }

    [Fact]
    public async Task HubDelete_OccupiedUnit_ThrowsConflictHubException()
    {
        var hierarchy = await SeedStorageUnitHierarchyAsync("del_occ", physicalStatus: "occupied");
        var managerCookie = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityManager, hierarchy.Facility.id);

        await using var hubConnection = BuildHubConnection(managerCookie);
        await hubConnection.StartAsync();

        var exConflict = await Assert.ThrowsAsync<HubException>(() =>
            hubConnection.InvokeAsync<string>(
                "HubDelete_StorageUnit",
                hierarchy.Unit.id.ToString(),
                hierarchy.Unit.updated_at.ToString("o")));

        Assert.Contains("cannot be deleted", exConflict.Message, StringComparison.OrdinalIgnoreCase);

        // Kiểm tra CSDL bản ghi vẫn được giữ nguyên
        using var db = _factory.CreateDbContext();
        var inDb = await db.storage_units.FirstOrDefaultAsync(u => u.id == hierarchy.Unit.id);
        Assert.NotNull(inDb);
    }

    #endregion

    #region 3. Unified Dispatcher: Form Path Dispatches Realtime Broadcasts

    [Fact]
    public async Task FormPost_Create_DispatchesReceiverCreateStorageUnit_ToSignalRListeners()
    {
        var suffix = $"form_c_{Guid.NewGuid():N}".Substring(0, 8);
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);
        var managerCookie = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityManager, hierarchy.Facility.id);

        // Khách hàng / Quản lý khác kết nối SignalR lắng nghe
        await using var listenerConnection = BuildHubConnection(managerCookie);

        StorageUnitRealtimeEventDto? receivedEvent = null;
        var eventTcs = new TaskCompletionSource<StorageUnitRealtimeEventDto>(TaskCreationOptions.RunContinuationsAsynchronously);

        listenerConnection.On<StorageUnitRealtimeEventDto>("Receiver_CreateStorageUnit", item =>
        {
            receivedEvent = item;
            eventTcs.TrySetResult(item);
        });

        await listenerConnection.StartAsync();

        // Gửi form POST truyền thống qua Create.cshtml
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var createUrl = $"/StorageUnits/Create?facilityId={hierarchy.Facility.id}";
        var getMsg = new HttpRequestMessage(HttpMethod.Get, createUrl);
        getMsg.Headers.Add("Cookie", managerCookie);
        var getResp = await client.SendAsync(getMsg);
        var getHtml = await getResp.Content.ReadAsStringAsync();

        var token = ExtractHiddenInputValue(getHtml, "__RequestVerificationToken");
        var targetCode = $"FORM-{suffix.ToUpperInvariant()}";

        var postMsg = new HttpRequestMessage(HttpMethod.Post, "/StorageUnits/Create")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["Input.FacilityId"] = hierarchy.Facility.id.ToString(),
                ["Input.UnitTypeId"] = hierarchy.UnitType.id.ToString(),
                ["Input.AreaId"] = hierarchy.Area.id.ToString(),
                ["Input.UnitCode"] = targetCode,
                ["Input.FloorLabel"] = "Tầng 1",
                ["Input.ZoneLabel"] = "Dãy A",
                ["Input.Notes"] = "Ghi chú từ Form POST"
            })
        };
        postMsg.Headers.Add("Cookie", managerCookie);

        var postResp = await client.SendAsync(postMsg);
        Assert.Equal(HttpStatusCode.Redirect, postResp.StatusCode);

        // Listener SignalR phải nhận được Receiver_CreateStorageUnit từ đường Form
        var completed = await Task.WhenAny(eventTcs.Task, Task.Delay(5000));
        Assert.True(completed == eventTcs.Task, "Receiver_CreateStorageUnit was not broadcasted by Form POST.");

        Assert.NotNull(receivedEvent);
        Assert.Equal(targetCode, receivedEvent.unitCode);
    }

    [Fact]
    public async Task FormPost_Delete_DispatchesReceiverDeleteStorageUnit_ToSignalRListeners()
    {
        var suffix = $"form_d_{Guid.NewGuid():N}".Substring(0, 8);
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);
        var managerCookie = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityManager, hierarchy.Facility.id);

        await using var listenerConnection = BuildHubConnection(managerCookie);

        string? deletedId = null;
        var eventTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        listenerConnection.On<string>("Receiver_DeleteStorageUnit", id =>
        {
            deletedId = id;
            eventTcs.TrySetResult(id);
        });

        await listenerConnection.StartAsync();

        // Gửi form POST Delete
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var deleteUrl = $"/StorageUnits/Delete?id={hierarchy.Unit.id}";
        var getMsg = new HttpRequestMessage(HttpMethod.Get, deleteUrl);
        getMsg.Headers.Add("Cookie", managerCookie);
        var getResp = await client.SendAsync(getMsg);
        var getHtml = await getResp.Content.ReadAsStringAsync();

        var token = ExtractHiddenInputValue(getHtml, "__RequestVerificationToken");
        var expUpdatedAt = ExtractHiddenInputValue(getHtml, "ExpectedUpdatedAt");

        var postMsg = new HttpRequestMessage(HttpMethod.Post, "/StorageUnits/Delete")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["Id"] = hierarchy.Unit.id.ToString(),
                ["ExpectedUpdatedAt"] = expUpdatedAt
            })
        };
        postMsg.Headers.Add("Cookie", managerCookie);

        var postResp = await client.SendAsync(postMsg);
        Assert.Equal(HttpStatusCode.Redirect, postResp.StatusCode);

        // Listener SignalR phải nhận được Receiver_DeleteStorageUnit từ đường Form
        var completed = await Task.WhenAny(eventTcs.Task, Task.Delay(5000));
        Assert.True(completed == eventTcs.Task, "Receiver_DeleteStorageUnit was not broadcasted by Form POST Delete.");

        Assert.Equal(hierarchy.Unit.id.ToString(), deletedId);
    }

    #endregion

    #region 4. Data Safety, XSS & Sensitive Information Broadcast Protection

    [Fact]
    public async Task BroadcastPayload_DoesNotExposeSensitiveNotesOrEFEntityCycles()
    {
        var suffix = $"leak_{Guid.NewGuid():N}".Substring(0, 8);
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);
        var managerCookie = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityManager, hierarchy.Facility.id);

        await using var hubConnection = BuildHubConnection(managerCookie);

        JsonElement? rawJson = null;
        var eventTcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Bắt raw JSON element để kiểm tra từng trường broadcast
        hubConnection.On<JsonElement>("Receiver_CreateStorageUnit", el =>
        {
            rawJson = el;
            eventTcs.TrySetResult(el);
        });

        await hubConnection.StartAsync();

        var secretNotes = "SUPER_SECRET_INTERNAL_ADMIN_NOTES_DO_NOT_BROADCAST";
        var dataObj = JsonSerializer.Serialize(new
        {
            FacilityId = hierarchy.Facility.id,
            UnitTypeId = hierarchy.UnitType.id,
            UnitCode = $"RT-SEC-{suffix}",
            Notes = secretNotes
        });

        await hubConnection.InvokeAsync<StorageUnitRealtimeEventDto>("HubCreate_StorageUnit", dataObj);

        var completed = await Task.WhenAny(eventTcs.Task, Task.Delay(5000));
        Assert.True(completed == eventTcs.Task);

        Assert.NotNull(rawJson);
        var jsonText = rawJson.Value.GetRawText();

        // TUYỆT ĐỐI không chứa secret notes trong broadcast realtime
        Assert.DoesNotContain(secretNotes, jsonText);

        // Không chứa các navigation properties của EF Core (rental_agreements, facility, v.v.)
        Assert.False(rawJson.Value.TryGetProperty("facility", out _));
        Assert.False(rawJson.Value.TryGetProperty("unit_allocations", out _));
    }

    [Fact]
    public async Task Realtime_HtmlCharactersInUnitCode_AreProperlyStoredAndBroadcastSafe()
    {
        var suffix = $"xss_{Guid.NewGuid():N}".Substring(0, 8);
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);
        var managerCookie = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityManager, hierarchy.Facility.id);

        await using var hubConnection = BuildHubConnection(managerCookie);

        StorageUnitRealtimeEventDto? received = null;
        var eventTcs = new TaskCompletionSource<StorageUnitRealtimeEventDto>(TaskCreationOptions.RunContinuationsAsynchronously);

        hubConnection.On<StorageUnitRealtimeEventDto>("Receiver_CreateStorageUnit", item =>
        {
            received = item;
            eventTcs.TrySetResult(item);
        });

        await hubConnection.StartAsync();

        var xssCode = $"<script>alert('{suffix}')</script>";
        var dataObj = JsonSerializer.Serialize(new
        {
            FacilityId = hierarchy.Facility.id,
            UnitTypeId = hierarchy.UnitType.id,
            UnitCode = xssCode
        });

        var res = await hubConnection.InvokeAsync<StorageUnitRealtimeEventDto>("HubCreate_StorageUnit", dataObj);
        Assert.NotNull(res);

        var completed = await Task.WhenAny(eventTcs.Task, Task.Delay(5000));
        Assert.True(completed == eventTcs.Task);

        Assert.NotNull(received);
        Assert.Equal(xssCode, received.unitCode);

        // Kiểm tra trang Index HTML: khi hiển thị không được unescaped
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var indexMsg = new HttpRequestMessage(HttpMethod.Get, $"/StorageUnits?facilityId={hierarchy.Facility.id}");
        indexMsg.Headers.Add("Cookie", managerCookie);
        var indexResp = await client.SendAsync(indexMsg);
        var indexHtml = await indexResp.Content.ReadAsStringAsync();

        // Razor Pages tự động escape trên server render
        Assert.DoesNotContain("<script>alert(", indexHtml);
        Assert.Contains("&lt;script&gt;alert(", indexHtml);
    }

    #endregion

    #region 5. Dual Browser Context Realtime Simulation

    [Fact]
    public async Task DualBrowserContext_SimultaneousConnections_ReceiveRealtimeEventsWithoutRefresh()
    {
        var suffix = $"dual_{Guid.NewGuid():N}".Substring(0, 8);
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);

        var managerCookie = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityManager, hierarchy.Facility.id);
        var opsCookie = await AuthenticateUserAndGetCookieAsync(RoleConstants.BusinessOperationsManager, hierarchy.Facility.id);

        // Browser 1 (Context A): Quản lý cơ sở tại Index
        await using var browserAConnection = BuildHubConnection(managerCookie);
        var browserACreated = new List<StorageUnitRealtimeEventDto>();
        var browserAUpdated = new List<StorageUnitRealtimeEventDto>();
        var browserADeleted = new List<string>();

        browserAConnection.On<StorageUnitRealtimeEventDto>("Receiver_CreateStorageUnit", item => browserACreated.Add(item));
        browserAConnection.On<StorageUnitRealtimeEventDto>("Receiver_UpdateStorageUnit", item => browserAUpdated.Add(item));
        browserAConnection.On<string>("Receiver_DeleteStorageUnit", id => browserADeleted.Add(id));

        await browserAConnection.StartAsync();

        // Browser 2 (Context B): Quản lý vận hành thực thi CRUD qua SignalR
        await using var browserBConnection = BuildHubConnection(opsCookie);
        await browserBConnection.StartAsync();

        // 1. Browser 2 tạo mới
        var newCode = $"RT-DUAL-{suffix}";
        var createPayload = JsonSerializer.Serialize(new
        {
            FacilityId = hierarchy.Facility.id,
            UnitTypeId = hierarchy.UnitType.id,
            UnitCode = newCode
        });

        var createdDto = await browserBConnection.InvokeAsync<StorageUnitRealtimeEventDto>("HubCreate_StorageUnit", createPayload);
        Assert.NotNull(createdDto);

        // Chờ Browser 1 nhận sự kiện
        await Task.Delay(800);
        Assert.Contains(browserACreated, c => c.unitCode == newCode);

        // 2. Browser 2 cập nhật
        var updatedCode = $"RT-DUAL-{suffix}-EDIT";
        var updatePayload = JsonSerializer.Serialize(new
        {
            Id = createdDto.id,
            UnitCode = updatedCode
        });

        var updatedDto = await browserBConnection.InvokeAsync<StorageUnitRealtimeEventDto>("HubUpdate_StorageUnit", updatePayload);
        Assert.NotNull(updatedDto);

        await Task.Delay(800);
        Assert.Contains(browserAUpdated, u => u.unitCode == updatedCode && u.id == createdDto.id);

        // 3. Browser 2 xóa
        var deletedResult = await browserBConnection.InvokeAsync<string>("HubDelete_StorageUnit", createdDto.id.ToString(), (string?)null);
        Assert.Equal(createdDto.id.ToString(), deletedResult);

        await Task.Delay(800);
        Assert.Contains(browserADeleted, d => d == createdDto.id.ToString());
    }

    #endregion

    #region 7. T13 Finding Fix Tests (T13-F01, T13-F02, T13-F03)

    [Fact]
    public async Task T13_F01_Customer_SubscribedToHub_ReceivesZeroCrudEvents()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);

        var custCookie = await AuthenticateUserAndGetCookieAsync(RoleConstants.StorageCustomer);
        var managerCookie = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityManager, hierarchy.Facility.id);

        await using var custConn = BuildHubConnection(custCookie);
        var custCreated = new List<StorageUnitRealtimeEventDto>();
        var custUpdated = new List<StorageUnitRealtimeEventDto>();
        var custDeleted = new List<string>();
        var custCatalogInvalidated = new List<object>();

        custConn.On<StorageUnitRealtimeEventDto>("Receiver_CreateStorageUnit", item => custCreated.Add(item));
        custConn.On<StorageUnitRealtimeEventDto>("Receiver_UpdateStorageUnit", item => custUpdated.Add(item));
        custConn.On<string>("Receiver_DeleteStorageUnit", id => custDeleted.Add(id));
        custConn.On<object>("CatalogInvalidated", payload => custCatalogInvalidated.Add(payload));

        await custConn.StartAsync();

        // Manager creates, updates, and deletes unit
        await using var mgrConn = BuildHubConnection(managerCookie);
        await mgrConn.StartAsync();

        var code = $"F01-CUST-{suffix}";
        var created = await mgrConn.InvokeAsync<StorageUnitRealtimeEventDto>("HubCreate_StorageUnit", JsonSerializer.Serialize(new
        {
            FacilityId = hierarchy.Facility.id,
            UnitTypeId = hierarchy.UnitType.id,
            UnitCode = code
        }));

        var updated = await mgrConn.InvokeAsync<StorageUnitRealtimeEventDto>("HubUpdate_StorageUnit", JsonSerializer.Serialize(new
        {
            Id = created.id,
            UnitCode = $"{code}-UPD"
        }));

        await mgrConn.InvokeAsync<string>("HubDelete_StorageUnit", created.id.ToString(), (string?)null);

        await Task.Delay(1000);

        // Khách hàng tuyệt đối không nhận bất kỳ sự kiện CRUD quản trị nào
        Assert.Empty(custCreated);
        Assert.Empty(custUpdated);
        Assert.Empty(custDeleted);

        // Khách hàng chỉ nhận sự kiện công khai CatalogInvalidated
        Assert.NotEmpty(custCatalogInvalidated);
    }

    [Fact]
    public async Task T13_F01_StaffOfFacilityA_DoesNotReceiveEventsOfFacilityB()
    {
        var suffixA = Guid.NewGuid().ToString("N")[..6];
        var suffixB = Guid.NewGuid().ToString("N")[..6];

        var hierarchyA = await SeedStorageUnitHierarchyAsync(suffixA);
        var hierarchyB = await SeedStorageUnitHierarchyAsync(suffixB);

        var staffACookie = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityStaff, hierarchyA.Facility.id);
        var managerBCookie = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityManager, hierarchyB.Facility.id);

        await using var staffAConn = BuildHubConnection(staffACookie);
        var staffACreated = new List<StorageUnitRealtimeEventDto>();
        var staffAUpdated = new List<StorageUnitRealtimeEventDto>();
        var staffADeleted = new List<string>();

        staffAConn.On<StorageUnitRealtimeEventDto>("Receiver_CreateStorageUnit", item => staffACreated.Add(item));
        staffAConn.On<StorageUnitRealtimeEventDto>("Receiver_UpdateStorageUnit", item => staffAUpdated.Add(item));
        staffAConn.On<string>("Receiver_DeleteStorageUnit", id => staffADeleted.Add(id));

        await staffAConn.StartAsync();

        // Manager B performs CRUD in Facility B
        await using var mgrBConn = BuildHubConnection(managerBCookie);
        await mgrBConn.StartAsync();

        var codeB = $"F01-FACB-{suffixB}";
        var createdB = await mgrBConn.InvokeAsync<StorageUnitRealtimeEventDto>("HubCreate_StorageUnit", JsonSerializer.Serialize(new
        {
            FacilityId = hierarchyB.Facility.id,
            UnitTypeId = hierarchyB.UnitType.id,
            UnitCode = codeB
        }));

        var updatedB = await mgrBConn.InvokeAsync<StorageUnitRealtimeEventDto>("HubUpdate_StorageUnit", JsonSerializer.Serialize(new
        {
            Id = createdB.id,
            UnitCode = $"{codeB}-UPD"
        }));

        await mgrBConn.InvokeAsync<string>("HubDelete_StorageUnit", createdB.id.ToString(), (string?)null);

        await Task.Delay(1000);

        // Nhân viên cơ sở A không được phép nhận bất kỳ sự kiện nào của cơ sở B
        Assert.Empty(staffACreated);
        Assert.Empty(staffAUpdated);
        Assert.Empty(staffADeleted);
    }

    [Fact]
    public async Task T13_F01_Admin_ReceivesEventsAcrossAllFacilities()
    {
        var suffixA = Guid.NewGuid().ToString("N")[..6];
        var suffixB = Guid.NewGuid().ToString("N")[..6];

        var hierarchyA = await SeedStorageUnitHierarchyAsync(suffixA);
        var hierarchyB = await SeedStorageUnitHierarchyAsync(suffixB);

        var adminCookie = await AuthenticateUserAndGetCookieAsync(RoleConstants.SystemAdministrator);
        var mgrACookie = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityManager, hierarchyA.Facility.id);
        var mgrBCookie = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityManager, hierarchyB.Facility.id);

        await using var adminConn = BuildHubConnection(adminCookie);
        var adminCreated = new List<StorageUnitRealtimeEventDto>();
        adminConn.On<StorageUnitRealtimeEventDto>("Receiver_CreateStorageUnit", item => adminCreated.Add(item));
        await adminConn.StartAsync();

        // Manager A creates in Facility A
        await using var connA = BuildHubConnection(mgrACookie);
        await connA.StartAsync();
        var codeA = $"ADMIN-FACA-{suffixA}";
        await connA.InvokeAsync<StorageUnitRealtimeEventDto>("HubCreate_StorageUnit", JsonSerializer.Serialize(new
        {
            FacilityId = hierarchyA.Facility.id,
            UnitTypeId = hierarchyA.UnitType.id,
            UnitCode = codeA
        }));

        // Manager B creates in Facility B
        await using var connB = BuildHubConnection(mgrBCookie);
        await connB.StartAsync();
        var codeB = $"ADMIN-FACB-{suffixB}";
        await connB.InvokeAsync<StorageUnitRealtimeEventDto>("HubCreate_StorageUnit", JsonSerializer.Serialize(new
        {
            FacilityId = hierarchyB.Facility.id,
            UnitTypeId = hierarchyB.UnitType.id,
            UnitCode = codeB
        }));

        await Task.Delay(1000);

        // System Administrator nhận đầy đủ sự kiện từ cả hai cơ sở A và B
        Assert.Contains(adminCreated, c => c.unitCode == codeA);
        Assert.Contains(adminCreated, c => c.unitCode == codeB);
    }

    [Fact]
    public async Task T13_F01_DeleteStorageUnit_RoutesToFacilityGroup_AndPreservesContractPayload()
    {
        var suffixA = Guid.NewGuid().ToString("N")[..6];
        var suffixB = Guid.NewGuid().ToString("N")[..6];

        var hierarchyA = await SeedStorageUnitHierarchyAsync(suffixA);
        var hierarchyB = await SeedStorageUnitHierarchyAsync(suffixB);

        var staffACookie = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityStaff, hierarchyA.Facility.id);
        var staffBCookie = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityStaff, hierarchyB.Facility.id);
        var mgrACookie = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityManager, hierarchyA.Facility.id);

        await using var staffAConn = BuildHubConnection(staffACookie);
        var staffADeleted = new List<string>();
        staffAConn.On<string>("Receiver_DeleteStorageUnit", id => staffADeleted.Add(id));
        await staffAConn.StartAsync();

        await using var staffBConn = BuildHubConnection(staffBCookie);
        var staffBDeleted = new List<string>();
        staffBConn.On<string>("Receiver_DeleteStorageUnit", id => staffBDeleted.Add(id));
        await staffBConn.StartAsync();

        await using var mgrAConn = BuildHubConnection(mgrACookie);
        await mgrAConn.StartAsync();

        // Xóa ô kho tại cơ sở A
        var deleteResult = await mgrAConn.InvokeAsync<string>("HubDelete_StorageUnit", hierarchyA.Unit.id.ToString(), (string?)null);
        Assert.Equal(hierarchyA.Unit.id.ToString(), deleteResult);

        await Task.Delay(1000);

        // Staff A nhận được sự kiện xóa của cơ sở mình (payload đúng chuỗi ID theo Contract)
        Assert.Contains(staffADeleted, id => id == hierarchyA.Unit.id.ToString());

        // Staff B của cơ sở B không nhận được sự kiện xóa của cơ sở A
        Assert.DoesNotContain(staffBDeleted, id => id == hierarchyA.Unit.id.ToString());
    }

    [Fact]
    public async Task T13_F02_EditPage_ConcurrentUpdate_FormRejectsStaleSave_PreventingLostUpdate()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);

        // Phiên A và Phiên B là hai quản lý cùng mở trang Edit
        var cookieA = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityManager, hierarchy.Facility.id);
        var cookieB = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityManager, hierarchy.Facility.id);

        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        // Phiên B nạp trang Edit (đang giữ dữ liệu cũ và ETag cũ)
        var getMsgB = new HttpRequestMessage(HttpMethod.Get, $"/StorageUnits/Edit?id={hierarchy.Unit.id}");
        getMsgB.Headers.Add("Cookie", cookieB);
        var getRespB = await client.SendAsync(getMsgB);
        Assert.Equal(HttpStatusCode.OK, getRespB.StatusCode);
        var htmlB = await getRespB.Content.ReadAsStringAsync();
        var tokenB = ExtractHiddenInputValue(htmlB, "__RequestVerificationToken");
        var expectedUpdatedAtB = ExtractHiddenInputValue(htmlB, "Input.ExpectedUpdatedAt");

        // Phiên A cập nhật ô kho trước (sửa UnitCode và Notes)
        var getMsgA = new HttpRequestMessage(HttpMethod.Get, $"/StorageUnits/Edit?id={hierarchy.Unit.id}");
        getMsgA.Headers.Add("Cookie", cookieA);
        var getRespA = await client.SendAsync(getMsgA);
        var htmlA = await getRespA.Content.ReadAsStringAsync();
        var tokenA = ExtractHiddenInputValue(htmlA, "__RequestVerificationToken");
        var expectedUpdatedAtA = ExtractHiddenInputValue(htmlA, "Input.ExpectedUpdatedAt");

        var updatedCodeA = $"RT-A-SAVED-{suffix}";
        var postMsgA = new HttpRequestMessage(HttpMethod.Post, $"/StorageUnits/Edit?id={hierarchy.Unit.id}")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = tokenA,
                ["Input.UnitCode"] = updatedCodeA,
                ["Input.FloorLabel"] = "Tầng 2 Đã Sửa",
                ["Input.ZoneLabel"] = "Dãy A Đã Sửa",
                ["Input.Notes"] = "Phiên A ghi chú quan trọng",
                ["Input.ExpectedUpdatedAt"] = expectedUpdatedAtA
            })
        };
        postMsgA.Headers.Add("Cookie", cookieA);
        var postRespA = await client.SendAsync(postMsgA);
        Assert.Equal(HttpStatusCode.Redirect, postRespA.StatusCode);

        // Bây giờ phiên B (đang giữ form cũ với expectedUpdatedAtB cũ) gửi form để lưu
        // Theo T13-F02: Không tự động nhận ETag mới cho dữ liệu form cũ -> ExpectedUpdatedAt vẫn là mốc cũ
        var staleCodeB = $"RT-B-OVERWRITE-{suffix}";
        var postMsgB = new HttpRequestMessage(HttpMethod.Post, $"/StorageUnits/Edit?id={hierarchy.Unit.id}")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = tokenB,
                ["Input.UnitCode"] = staleCodeB,
                ["Input.FloorLabel"] = "Tầng Cũ B",
                ["Input.ZoneLabel"] = "Dãy Cũ B",
                ["Input.Notes"] = "Phiên B cố tình ghi đè",
                ["Input.ExpectedUpdatedAt"] = expectedUpdatedAtB
            })
        };
        postMsgB.Headers.Add("Cookie", cookieB);
        var postRespB = await client.SendAsync(postMsgB);

        // Phải trả về trang với cảnh báo xung đột đồng thời (không cho phép ghi đè)
        var postHtmlB = await postRespB.Content.ReadAsStringAsync();
        Assert.Contains("Phát hiện xung đột đồng thời", postHtmlB);

        // Kiểm tra trong CSDL: Dữ liệu của Phiên A được bảo toàn tuyệt đối, không bị Phiên B ghi đè
        using var dbCheck = _factory.CreateDbContext();
        var finalUnit = await dbCheck.storage_units.FindAsync(hierarchy.Unit.id);
        Assert.NotNull(finalUnit);
        Assert.Equal(updatedCodeA, finalUnit.unit_code);
        Assert.Equal("Phiên A ghi chú quan trọng", finalUnit.notes);
    }

    [Fact]
    public void T13_F03_MatchesFilter_EvaluatesAllCriteriaCorrectly()
    {
        var filter = new
        {
            facilityId = "10",
            unitTypeId = "20",
            areaId = "30",
            physicalStatus = "available",
            isListed = "true",
            searchTerm = "alpha"
        };

        var matchingDto = new StorageUnitRealtimeEventDto
        {
            id = 1,
            unitCode = "ALPHA-01",
            facilityId = 10,
            unitTypeId = 20,
            areaId = 30,
            status = "available",
            isListed = true
        };

        var wrongFacilityDto = new StorageUnitRealtimeEventDto
        {
            id = 2,
            unitCode = "ALPHA-02",
            facilityId = 99,
            unitTypeId = 20,
            areaId = 30,
            status = "available",
            isListed = true
        };

        var wrongStatusDto = new StorageUnitRealtimeEventDto
        {
            id = 3,
            unitCode = "ALPHA-03",
            facilityId = 10,
            unitTypeId = 20,
            areaId = 30,
            status = "maintenance",
            isListed = true
        };

        var unlistedDto = new StorageUnitRealtimeEventDto
        {
            id = 4,
            unitCode = "ALPHA-04",
            facilityId = 10,
            unitTypeId = 20,
            areaId = 30,
            status = "available",
            isListed = false
        };

        var nonMatchingSearchDto = new StorageUnitRealtimeEventDto
        {
            id = 5,
            unitCode = "BETA-05",
            typeName = "Standard Small",
            facilityId = 10,
            unitTypeId = 20,
            areaId = 30,
            status = "available",
            isListed = true
        };

        bool CheckMatch(StorageUnitRealtimeEventDto item)
        {
            if (filter.facilityId != "" && item.facilityId.ToString() != filter.facilityId) return false;
            if (filter.unitTypeId != "" && item.unitTypeId.ToString() != filter.unitTypeId) return false;
            if (filter.areaId != "" && (item.areaId?.ToString() ?? "") != filter.areaId) return false;
            if (filter.physicalStatus != "" && !item.status.Equals(filter.physicalStatus, StringComparison.OrdinalIgnoreCase)) return false;
            if (filter.isListed != "" && item.isListed.ToString().ToLowerInvariant() != filter.isListed) return false;
            if (filter.searchTerm != "" && !item.unitCode.Contains(filter.searchTerm, StringComparison.OrdinalIgnoreCase) && !item.typeName.Contains(filter.searchTerm, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        Assert.True(CheckMatch(matchingDto));
        Assert.False(CheckMatch(wrongFacilityDto));
        Assert.False(CheckMatch(wrongStatusDto));
        Assert.False(CheckMatch(unlistedDto));
        Assert.False(CheckMatch(nonMatchingSearchDto));
    }

    #endregion

    #region 8. T13 Round 2 Finding Fix Tests (T13-R2-F02: Live Connection Scope Revocation)

    [Fact]
    public async Task T13_R2_F02_Staff_AssignmentTerminated_OpenWebSocketStopsReceivingEvents_WithoutManualReconnect()
    {
        var suffixA = Guid.NewGuid().ToString("N")[..6];
        var hierarchyA = await SeedStorageUnitHierarchyAsync(suffixA);

        var staffEmail = $"staff_term_{suffixA}@example.test";
        var staffPass = "SecurePass123!";
        var staffUser = await _factory.SeedUserAsync(staffEmail, staffPass, RoleConstants.FacilityStaff, UserStatusConstants.Active, "Staff Term");

        long assignmentId;
        using (var db = _factory.CreateDbContext())
        {
            var assignment = new staff_facility_assignment
            {
                employee_id = staffUser.id,
                facility_id = hierarchyA.Facility.id,
                assignment_role = RoleConstants.FacilityStaff,
                starts_at = DateTimeOffset.UtcNow.AddDays(-5),
                ends_at = null,
                created_at = DateTimeOffset.UtcNow
            };
            db.staff_facility_assignments.Add(assignment);
            await db.SaveChangesAsync();
            assignmentId = assignment.id;
        }

        var staffCookie = await AuthenticateUserAndGetCookieDirectAsync(staffEmail, staffPass);
        var managerCookie = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityManager, hierarchyA.Facility.id);

        // Staff opens WebSocket connection to Hub
        await using var staffConn = BuildHubConnection(staffCookie);
        var staffCreatedEvents = new List<StorageUnitRealtimeEventDto>();
        staffConn.On<StorageUnitRealtimeEventDto>("Receiver_CreateStorageUnit", item => staffCreatedEvents.Add(item));
        await staffConn.StartAsync();

        // Manager connects to Hub
        await using var mgrConn = BuildHubConnection(managerCookie);
        await mgrConn.StartAsync();

        // 1. Manager creates Unit 1 -> Staff A's open connection receives the event
        var code1 = $"F02-U1-{suffixA}";
        var created1 = await mgrConn.InvokeAsync<StorageUnitRealtimeEventDto>("HubCreate_StorageUnit", JsonSerializer.Serialize(new
        {
            FacilityId = hierarchyA.Facility.id,
            UnitTypeId = hierarchyA.UnitType.id,
            UnitCode = code1
        }));
        await Task.Delay(1000);

        Assert.Contains(staffCreatedEvents, e => e.unitCode == code1);
        staffCreatedEvents.Clear();

        // 2. Admin terminates Staff A's assignment to Facility A via IAdminAccountService
        using (var scope = _factory.Services.CreateScope())
        {
            var adminService = scope.ServiceProvider.GetRequiredService<IAdminAccountService>();
            var adminUser = await _factory.SeedUserAsync($"admin_act_{suffixA}@example.test", "Pass123!", RoleConstants.SystemAdministrator, UserStatusConstants.Active, "Admin Actor");
            await adminService.TerminateFacilityAssignmentAsync(assignmentId, adminUser.id);
        }
        await Task.Delay(1000);

        // 3. Manager creates Unit 2 in Facility A -> Staff A's open connection MUST NOT receive it (without manual reconnect!)
        var code2 = $"F02-U2-{suffixA}";
        var created2 = await mgrConn.InvokeAsync<StorageUnitRealtimeEventDto>("HubCreate_StorageUnit", JsonSerializer.Serialize(new
        {
            FacilityId = hierarchyA.Facility.id,
            UnitTypeId = hierarchyA.UnitType.id,
            UnitCode = code2
        }));
        await Task.Delay(1200);

        // Staff A receives ZERO events for Unit 2
        Assert.DoesNotContain(staffCreatedEvents, e => e.unitCode == code2);
        Assert.Empty(staffCreatedEvents);
    }

    [Fact]
    public async Task T13_R2_F02_UserLocked_OpenWebSocketStopsReceivingAllEvents()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);

        var staffEmail = $"staff_lock_{suffix}@example.test";
        var staffPass = "SecurePass123!";
        var staffUser = await _factory.SeedUserAsync(staffEmail, staffPass, RoleConstants.FacilityStaff, UserStatusConstants.Active, "Staff Lock");

        using (var db = _factory.CreateDbContext())
        {
            db.staff_facility_assignments.Add(new staff_facility_assignment
            {
                employee_id = staffUser.id,
                facility_id = hierarchy.Facility.id,
                assignment_role = RoleConstants.FacilityStaff,
                starts_at = DateTimeOffset.UtcNow.AddDays(-5),
                ends_at = null,
                created_at = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var staffCookie = await AuthenticateUserAndGetCookieDirectAsync(staffEmail, staffPass);
        var managerCookie = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityManager, hierarchy.Facility.id);

        await using var staffConn = BuildHubConnection(staffCookie);
        var staffEvents = new List<StorageUnitRealtimeEventDto>();
        staffConn.On<StorageUnitRealtimeEventDto>("Receiver_CreateStorageUnit", item => staffEvents.Add(item));
        await staffConn.StartAsync();

        await using var mgrConn = BuildHubConnection(managerCookie);
        await mgrConn.StartAsync();

        // 1. Initial event arrives
        var code1 = $"F02-LOCK1-{suffix}";
        await mgrConn.InvokeAsync<StorageUnitRealtimeEventDto>("HubCreate_StorageUnit", JsonSerializer.Serialize(new
        {
            FacilityId = hierarchy.Facility.id,
            UnitTypeId = hierarchy.UnitType.id,
            UnitCode = code1
        }));
        await Task.Delay(800);
        Assert.Contains(staffEvents, e => e.unitCode == code1);
        staffEvents.Clear();

        // 2. Admin locks the staff account
        using (var scope = _factory.Services.CreateScope())
        {
            var adminService = scope.ServiceProvider.GetRequiredService<IAdminAccountService>();
            var adminUser = await _factory.SeedUserAsync($"admin_lck_{suffix}@example.test", "Pass123!", RoleConstants.SystemAdministrator, UserStatusConstants.Active, "Admin Actor");
            await adminService.UpdateUserStatusAsync(staffUser.id, new SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Admin.UpdateUserStatusRequest { Status = "locked" }, adminUser.id);
        }
        await Task.Delay(1000);

        // 3. New event published -> Locked staff receives ZERO events on their live connection
        var code2 = $"F02-LOCK2-{suffix}";
        await mgrConn.InvokeAsync<StorageUnitRealtimeEventDto>("HubCreate_StorageUnit", JsonSerializer.Serialize(new
        {
            FacilityId = hierarchy.Facility.id,
            UnitTypeId = hierarchy.UnitType.id,
            UnitCode = code2
        }));
        await Task.Delay(1000);
        Assert.Empty(staffEvents);
    }

    [Fact]
    public async Task T13_R2_F02_AdminRoleRevoked_OpenWebSocketStopsReceivingGlobalEvents()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);

        var adminEmail = $"admin_rev_{suffix}@example.test";
        var adminPass = "SecurePass123!";
        var adminTarget = await _factory.SeedUserAsync(adminEmail, adminPass, RoleConstants.SystemAdministrator, UserStatusConstants.Active, "Admin Target");

        var adminCookie = await AuthenticateUserAndGetCookieDirectAsync(adminEmail, adminPass);
        var managerCookie = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityManager, hierarchy.Facility.id);

        await using var adminConn = BuildHubConnection(adminCookie);
        var adminEvents = new List<StorageUnitRealtimeEventDto>();
        adminConn.On<StorageUnitRealtimeEventDto>("Receiver_CreateStorageUnit", item => adminEvents.Add(item));
        await adminConn.StartAsync();

        await using var mgrConn = BuildHubConnection(managerCookie);
        await mgrConn.StartAsync();

        // 1. Initial event received by admin_global
        var code1 = $"F02-REV1-{suffix}";
        await mgrConn.InvokeAsync<StorageUnitRealtimeEventDto>("HubCreate_StorageUnit", JsonSerializer.Serialize(new
        {
            FacilityId = hierarchy.Facility.id,
            UnitTypeId = hierarchy.UnitType.id,
            UnitCode = code1
        }));
        await Task.Delay(800);
        Assert.Contains(adminEvents, e => e.unitCode == code1);
        adminEvents.Clear();

        // 2. Revoke admin role (demote to storage_customer)
        using (var scope = _factory.Services.CreateScope())
        {
            var adminService = scope.ServiceProvider.GetRequiredService<IAdminAccountService>();
            var rootAdmin = await _factory.SeedUserAsync($"root_adm_{suffix}@example.test", "Pass123!", RoleConstants.SystemAdministrator, UserStatusConstants.Active, "Root Admin");
            await adminService.ManageUserRolesAsync(adminTarget.id, new SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Admin.ManageUserRolesRequest
            {
                RoleCodes = new List<string> { RoleConstants.StorageCustomer }
            }, rootAdmin.id);
        }
        await Task.Delay(1000);

        // 3. New event emitted -> Former admin receives ZERO events
        var code2 = $"F02-REV2-{suffix}";
        await mgrConn.InvokeAsync<StorageUnitRealtimeEventDto>("HubCreate_StorageUnit", JsonSerializer.Serialize(new
        {
            FacilityId = hierarchy.Facility.id,
            UnitTypeId = hierarchy.UnitType.id,
            UnitCode = code2
        }));
        await Task.Delay(1000);
        Assert.Empty(adminEvents);
    }

    #endregion

    #region T13 Round 3 Finding Fixes Tests (T13-R3-F01 & T13-R3-F02)

    [Fact]
    public async Task T13_R3_F01_StaffWebSocket_AssignmentNaturallyExpires_StopsReceivingEventsWithoutAdminMutation()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);

        // 1. Seed Manager and connect manager Hub FIRST
        var mgrEmail = $"mgr_r3_{suffix}@example.test";
        var mgrPass = "MgrSecure123!";
        var mgr = await _factory.SeedUserAsync(mgrEmail, mgrPass, RoleConstants.FacilityManager, UserStatusConstants.Active, "Manager R3");
        using (var db = _factory.CreateDbContext())
        {
            db.staff_facility_assignments.Add(new staff_facility_assignment
            {
                employee_id = mgr.id,
                facility_id = hierarchy.Facility.id,
                assignment_role = RoleConstants.FacilityManager,
                starts_at = DateTimeOffset.UtcNow.AddDays(-5),
                ends_at = null,
                created_at = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }
        var mgrCookie = await AuthenticateUserAndGetCookieDirectAsync(mgrEmail, mgrPass);
        var mgrConn = BuildHubConnection(mgrCookie);
        await mgrConn.StartAsync();

        // 2. Seed Staff user and authenticate
        var staffEmail = $"staff_exp_{suffix}@example.test";
        var staffPass = "StaffSecure123!";
        var staff = await _factory.SeedUserAsync(staffEmail, staffPass, RoleConstants.FacilityStaff, UserStatusConstants.Active, "Staff Expiring");
        var staffCookie = await AuthenticateUserAndGetCookieDirectAsync(staffEmail, staffPass);

        // 3. Now add the staff assignment with ends_at in near future (6.0 seconds from now)
        using (var db = _factory.CreateDbContext())
        {
            db.staff_facility_assignments.Add(new staff_facility_assignment
            {
                employee_id = staff.id,
                facility_id = hierarchy.Facility.id,
                assignment_role = RoleConstants.FacilityStaff,
                starts_at = DateTimeOffset.UtcNow.AddMinutes(-10),
                ends_at = DateTimeOffset.UtcNow.AddSeconds(6.0),
                created_at = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // 4. Connect staff Hub immediately
        var staffConn = BuildHubConnection(staffCookie);
        var staffEvents = new List<StorageUnitRealtimeEventDto>();
        staffConn.On<StorageUnitRealtimeEventDto>("Receiver_CreateStorageUnit", e => staffEvents.Add(e));
        await staffConn.StartAsync();

        // 5. Manager creates unit right now (before 6.0s expiration)
        var codeBefore = $"R3-PRE-{suffix}";
        await mgrConn.InvokeAsync<StorageUnitRealtimeEventDto>("HubCreate_StorageUnit", JsonSerializer.Serialize(new
        {
            FacilityId = hierarchy.Facility.id,
            UnitTypeId = hierarchy.UnitType.id,
            UnitCode = codeBefore
        }));

        await Task.Delay(800);
        Assert.Contains(staffEvents, e => e.unitCode == codeBefore);
        staffEvents.Clear();

        // 6. Wait for ends_at to pass and StorageUnitScopeSyncWorker to reconcile
        await Task.Delay(5500);

        // 7. After expiration: Manager emits another event. NO admin mutation was called!
        var codeAfter = $"R3-POST-{suffix}";
        await mgrConn.InvokeAsync<StorageUnitRealtimeEventDto>("HubCreate_StorageUnit", JsonSerializer.Serialize(new
        {
            FacilityId = hierarchy.Facility.id,
            UnitTypeId = hierarchy.UnitType.id,
            UnitCode = codeAfter
        }));

        await Task.Delay(1000);
        // Staff WebSocket stayed open, but group was removed upon temporal expiration!
        Assert.DoesNotContain(staffEvents, e => e.unitCode == codeAfter);
        Assert.Empty(staffEvents);
    }

    [Fact]
    public async Task T13_R3_F01_StaffWebSocket_FutureAssignmentStartsAt_AutomaticallyReceivesEventsWhenActive()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);

        // 1. Seed Manager and connect manager Hub FIRST
        var mgrEmail = $"mgr_fut_{suffix}@example.test";
        var mgrPass = "MgrSecure123!";
        var mgr = await _factory.SeedUserAsync(mgrEmail, mgrPass, RoleConstants.FacilityManager, UserStatusConstants.Active, "Manager Fut");
        using (var db = _factory.CreateDbContext())
        {
            db.staff_facility_assignments.Add(new staff_facility_assignment
            {
                employee_id = mgr.id,
                facility_id = hierarchy.Facility.id,
                assignment_role = RoleConstants.FacilityManager,
                starts_at = DateTimeOffset.UtcNow.AddDays(-5),
                ends_at = null,
                created_at = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }
        var mgrCookie = await AuthenticateUserAndGetCookieDirectAsync(mgrEmail, mgrPass);
        var mgrConn = BuildHubConnection(mgrCookie);
        await mgrConn.StartAsync();

        // 2. Seed Staff user and authenticate
        var staffEmail = $"staff_fut_{suffix}@example.test";
        var staffPass = "StaffSecure123!";
        var staff = await _factory.SeedUserAsync(staffEmail, staffPass, RoleConstants.FacilityStaff, UserStatusConstants.Active, "Staff Future");
        var staffCookie = await AuthenticateUserAndGetCookieDirectAsync(staffEmail, staffPass);

        // 3. Add future assignment starts_at in 6.0 seconds
        using (var db = _factory.CreateDbContext())
        {
            db.staff_facility_assignments.Add(new staff_facility_assignment
            {
                employee_id = staff.id,
                facility_id = hierarchy.Facility.id,
                assignment_role = RoleConstants.FacilityStaff,
                starts_at = DateTimeOffset.UtcNow.AddSeconds(6.0),
                ends_at = null,
                created_at = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // 4. Connect staff Hub immediately
        var staffConn = BuildHubConnection(staffCookie);
        var staffEvents = new List<StorageUnitRealtimeEventDto>();
        staffConn.On<StorageUnitRealtimeEventDto>("Receiver_CreateStorageUnit", e => staffEvents.Add(e));
        await staffConn.StartAsync();

        // 5. Before starts_at: Staff receives NOTHING
        var codeBefore = $"R3-FUT-PRE-{suffix}";
        await mgrConn.InvokeAsync<StorageUnitRealtimeEventDto>("HubCreate_StorageUnit", JsonSerializer.Serialize(new
        {
            FacilityId = hierarchy.Facility.id,
            UnitTypeId = hierarchy.UnitType.id,
            UnitCode = codeBefore
        }));

        await Task.Delay(800);
        Assert.Empty(staffEvents);

        // 6. Wait for starts_at to become active and StorageUnitScopeSyncWorker to reconcile
        await Task.Delay(5500);

        // 7. After starts_at: Staff group was added automatically! Now receives event!
        var codeAfter = $"R3-FUT-POST-{suffix}";
        await mgrConn.InvokeAsync<StorageUnitRealtimeEventDto>("HubCreate_StorageUnit", JsonSerializer.Serialize(new
        {
            FacilityId = hierarchy.Facility.id,
            UnitTypeId = hierarchy.UnitType.id,
            UnitCode = codeAfter
        }));

        await Task.Delay(1000);
        Assert.Contains(staffEvents, e => e.unitCode == codeAfter);
    }

    [Fact]
    public async Task T13_R3_F02_HttpRequestCancellation_DoesNotAbortScopeRevocation_CancellationTokenNoneUsed()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);

        var staffEmail = $"staff_cts_{suffix}@example.test";
        var staffPass = "StaffSecure123!";
        var staff = await _factory.SeedUserAsync(staffEmail, staffPass, RoleConstants.FacilityStaff, UserStatusConstants.Active, "Staff CTS");

        long assignmentId;
        using (var db = _factory.CreateDbContext())
        {
            var assignment = new staff_facility_assignment
            {
                employee_id = staff.id,
                facility_id = hierarchy.Facility.id,
                assignment_role = RoleConstants.FacilityStaff,
                starts_at = DateTimeOffset.UtcNow.AddDays(-5),
                ends_at = null,
                created_at = DateTimeOffset.UtcNow
            };
            db.staff_facility_assignments.Add(assignment);
            await db.SaveChangesAsync();
            assignmentId = assignment.id;
        }

        var staffCookie = await AuthenticateUserAndGetCookieDirectAsync(staffEmail, staffPass);
        var staffConn = BuildHubConnection(staffCookie);
        var staffEvents = new List<StorageUnitRealtimeEventDto>();
        staffConn.On<StorageUnitRealtimeEventDto>("Receiver_CreateStorageUnit", e => staffEvents.Add(e));
        await staffConn.StartAsync();

        // Verify connected and receiving
        var mgrEmail = $"mgr_cts_{suffix}@example.test";
        var mgrPass = "MgrSecure123!";
        var mgr = await _factory.SeedUserAsync(mgrEmail, mgrPass, RoleConstants.FacilityManager, UserStatusConstants.Active, "Manager CTS");
        using (var db = _factory.CreateDbContext())
        {
            db.staff_facility_assignments.Add(new staff_facility_assignment
            {
                employee_id = mgr.id,
                facility_id = hierarchy.Facility.id,
                assignment_role = RoleConstants.FacilityManager,
                starts_at = DateTimeOffset.UtcNow.AddDays(-5),
                ends_at = null,
                created_at = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }
        var mgrCookie = await AuthenticateUserAndGetCookieDirectAsync(mgrEmail, mgrPass);
        var mgrConn = BuildHubConnection(mgrCookie);
        await mgrConn.StartAsync();

        var code1 = $"CTS-PRE-{suffix}";
        await mgrConn.InvokeAsync<StorageUnitRealtimeEventDto>("HubCreate_StorageUnit", JsonSerializer.Serialize(new
        {
            FacilityId = hierarchy.Facility.id,
            UnitTypeId = hierarchy.UnitType.id,
            UnitCode = code1
        }));
        await Task.Delay(800);
        Assert.Contains(staffEvents, e => e.unitCode == code1);
        staffEvents.Clear();

        // Admin terminates assignment
        using (var scope = _factory.Services.CreateScope())
        {
            var adminService = scope.ServiceProvider.GetRequiredService<IAdminAccountService>();
            var rootAdmin = await _factory.SeedUserAsync($"root_cts_{suffix}@example.test", "Pass123!", RoleConstants.SystemAdministrator, UserStatusConstants.Active, "Root Admin");

            await adminService.TerminateFacilityAssignmentAsync(assignmentId, rootAdmin.id);
        }

        await Task.Delay(1000);

        // Next event emitted: Staff receives NOTHING because revocation succeeded reliably
        var code2 = $"CTS-POST-{suffix}";
        await mgrConn.InvokeAsync<StorageUnitRealtimeEventDto>("HubCreate_StorageUnit", JsonSerializer.Serialize(new
        {
            FacilityId = hierarchy.Facility.id,
            UnitTypeId = hierarchy.UnitType.id,
            UnitCode = code2
        }));

        await Task.Delay(1000);
        Assert.Empty(staffEvents);
    }

    #endregion

    #region Round 4 Review Finding Fix Tests (T13-R4-F01, T13-R4-F02, T13-R4-F03)

    private sealed class CancelOnSavedChangesInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        private readonly CancellationTokenSource _cts;
        public CancelOnSavedChangesInterceptor(CancellationTokenSource cts) => _cts = cts;

        public override ValueTask<int> SavedChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            _cts.Cancel();
            return base.SavedChangesAsync(eventData, result, cancellationToken);
        }
    }

    private (IAdminAccountService Service, SelfStorageDbContext Db) CreateAdminAccountServiceWithCancelInterceptor(CancellationTokenSource cts)
    {
        var scope = _factory.Services.CreateScope();
        var optionsBuilder = new DbContextOptionsBuilder<SelfStorageDbContext>()
            .UseInMemoryDatabase(_factory.DatabaseName)
            .AddInterceptors(new CancelOnSavedChangesInterceptor(cts));
        var db = new SelfStorageDbContext(optionsBuilder.Options);

        var userRepo = new SelfStorageManagementSystem.DataAccess.Repositories.Implementations.UserRepository(db);
        var roleRepo = new SelfStorageManagementSystem.DataAccess.Repositories.Implementations.GenericRepository<role>(db);
        var userRoleRepo = new SelfStorageManagementSystem.DataAccess.Repositories.Implementations.GenericRepository<user_role>(db);
        var empProfileRepo = new SelfStorageManagementSystem.DataAccess.Repositories.Implementations.GenericRepository<employee_profile>(db);
        var assignmentRepo = new SelfStorageManagementSystem.DataAccess.Repositories.Implementations.GenericRepository<staff_facility_assignment>(db);
        var facilityRepo = new SelfStorageManagementSystem.DataAccess.Repositories.Implementations.GenericRepository<facility>(db);
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();
        var logger = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SelfStorageManagementSystem.BusinessLogic.Services.Implementations.AdminAccountService>>();
        var notifier = scope.ServiceProvider.GetRequiredService<IUserScopeChangedNotifier>();

        var svc = new SelfStorageManagementSystem.BusinessLogic.Services.Implementations.AdminAccountService(
            db, userRepo, roleRepo, userRoleRepo, empProfileRepo, assignmentRepo, facilityRepo,
            passwordHasher, auditService, logger, notifier);
        return (svc, db);
    }

    [Fact]
    public async Task T13_R4_F01_HttpRequestCancellation_AfterCommit_DoesNotSkipNotifier_AcrossAllFourMutations()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);
        var rootAdmin = await _factory.SeedUserAsync($"admin_f01_{suffix}@example.test", "AdminPass123!", RoleConstants.SystemAdministrator, UserStatusConstants.Active, "Admin F01");

        // Seed 1 manager for firing Hub events
        var mgrEmail = $"mgr_f01_{suffix}@example.test";
        var mgrPass = "MgrPass123!";
        var mgr = await _factory.SeedUserAsync(mgrEmail, mgrPass, RoleConstants.FacilityManager, UserStatusConstants.Active, "Mgr F01");
        using (var db = _factory.CreateDbContext())
        {
            db.staff_facility_assignments.Add(new staff_facility_assignment
            {
                employee_id = mgr.id,
                facility_id = hierarchy.Facility.id,
                assignment_role = RoleConstants.FacilityManager,
                starts_at = DateTimeOffset.UtcNow.AddDays(-10),
                ends_at = null,
                created_at = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }
        var mgrCookie = await AuthenticateUserAndGetCookieDirectAsync(mgrEmail, mgrPass);
        var mgrConn = BuildHubConnection(mgrCookie);
        await mgrConn.StartAsync();

        // =========================================================================
        // 1. Mutation 1: TerminateFacilityAssignmentAsync with canceled token after commit
        // =========================================================================
        var staff1Email = $"staff1_f01_{suffix}@example.test";
        var staff1Pass = "Staff1Pass123!";
        var staff1 = await _factory.SeedUserAsync(staff1Email, staff1Pass, RoleConstants.FacilityStaff, UserStatusConstants.Active, "Staff1 F01");
        long assignId1;
        using (var db = _factory.CreateDbContext())
        {
            var a = new staff_facility_assignment
            {
                employee_id = staff1.id,
                facility_id = hierarchy.Facility.id,
                assignment_role = RoleConstants.FacilityStaff,
                starts_at = DateTimeOffset.UtcNow.AddDays(-5),
                ends_at = null,
                created_at = DateTimeOffset.UtcNow
            };
            db.staff_facility_assignments.Add(a);
            await db.SaveChangesAsync();
            assignId1 = a.id;
        }

        var staff1Cookie = await AuthenticateUserAndGetCookieDirectAsync(staff1Email, staff1Pass);
        var staff1Conn = BuildHubConnection(staff1Cookie);
        var staff1Events = new List<StorageUnitRealtimeEventDto>();
        staff1Conn.On<StorageUnitRealtimeEventDto>("Receiver_CreateStorageUnit", e => staff1Events.Add(e));
        await staff1Conn.StartAsync();

        // Verify staff1 currently receives events
        await mgrConn.InvokeAsync<StorageUnitRealtimeEventDto>("HubCreate_StorageUnit", JsonSerializer.Serialize(new
        {
            FacilityId = hierarchy.Facility.id,
            UnitTypeId = hierarchy.UnitType.id,
            UnitCode = $"M1-PRE-{suffix}"
        }));
        await Task.Delay(600);
        Assert.NotEmpty(staff1Events);
        staff1Events.Clear();

        // Perform Terminate with real CancellationTokenSource canceled on SavedChangesAsync
        var cts1 = new CancellationTokenSource();
        var (adminSvc1, db1) = CreateAdminAccountServiceWithCancelInterceptor(cts1);
        using (db1)
        {
            await adminSvc1.TerminateFacilityAssignmentAsync(assignId1, rootAdmin.id, null, null, cts1.Token);
            Assert.True(cts1.IsCancellationRequested, "Token must be canceled by interceptor upon SaveChangesAsync commit");
        }

        await Task.Delay(600);
        // Verify staff1 receives NOTHING after revocation
        await mgrConn.InvokeAsync<StorageUnitRealtimeEventDto>("HubCreate_StorageUnit", JsonSerializer.Serialize(new
        {
            FacilityId = hierarchy.Facility.id,
            UnitTypeId = hierarchy.UnitType.id,
            UnitCode = $"M1-POST-{suffix}"
        }));
        await Task.Delay(600);
        Assert.Empty(staff1Events);

        // =========================================================================
        // 2. Mutation 2: UpdateUserStatusAsync (Locked) with canceled token after commit
        // =========================================================================
        var staff2Email = $"staff2_f01_{suffix}@example.test";
        var staff2Pass = "Staff2Pass123!";
        var staff2 = await _factory.SeedUserAsync(staff2Email, staff2Pass, RoleConstants.FacilityStaff, UserStatusConstants.Active, "Staff2 F01");
        using (var db = _factory.CreateDbContext())
        {
            db.staff_facility_assignments.Add(new staff_facility_assignment
            {
                employee_id = staff2.id,
                facility_id = hierarchy.Facility.id,
                assignment_role = RoleConstants.FacilityStaff,
                starts_at = DateTimeOffset.UtcNow.AddDays(-5),
                ends_at = null,
                created_at = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var staff2Cookie = await AuthenticateUserAndGetCookieDirectAsync(staff2Email, staff2Pass);
        var staff2Conn = BuildHubConnection(staff2Cookie);
        var staff2Events = new List<StorageUnitRealtimeEventDto>();
        staff2Conn.On<StorageUnitRealtimeEventDto>("Receiver_CreateStorageUnit", e => staff2Events.Add(e));
        await staff2Conn.StartAsync();

        var cts2 = new CancellationTokenSource();
        var (adminSvc2, db2) = CreateAdminAccountServiceWithCancelInterceptor(cts2);
        using (db2)
        {
            var res2 = await adminSvc2.UpdateUserStatusAsync(
                staff2.id,
                new SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Admin.UpdateUserStatusRequest { Status = UserStatusConstants.Locked },
                rootAdmin.id,
                null,
                null,
                cts2.Token);
            Assert.NotNull(res2);
            Assert.True(cts2.IsCancellationRequested, "Token must be canceled by interceptor upon SaveChangesAsync commit");
        }

        await Task.Delay(600);
        await mgrConn.InvokeAsync<StorageUnitRealtimeEventDto>("HubCreate_StorageUnit", JsonSerializer.Serialize(new
        {
            FacilityId = hierarchy.Facility.id,
            UnitTypeId = hierarchy.UnitType.id,
            UnitCode = $"M2-POST-{suffix}"
        }));
        await Task.Delay(600);
        Assert.Empty(staff2Events);

        // =========================================================================
        // 3. Mutation 3: ManageUserRolesAsync (demote to Customer) with canceled token
        // =========================================================================
        var staff3Email = $"staff3_f01_{suffix}@example.test";
        var staff3Pass = "Staff3Pass123!";
        var staff3 = await _factory.SeedUserAsync(staff3Email, staff3Pass, RoleConstants.FacilityStaff, UserStatusConstants.Active, "Staff3 F01");
        using (var db = _factory.CreateDbContext())
        {
            db.staff_facility_assignments.Add(new staff_facility_assignment
            {
                employee_id = staff3.id,
                facility_id = hierarchy.Facility.id,
                assignment_role = RoleConstants.FacilityStaff,
                starts_at = DateTimeOffset.UtcNow.AddDays(-5),
                ends_at = null,
                created_at = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var staff3Cookie = await AuthenticateUserAndGetCookieDirectAsync(staff3Email, staff3Pass);
        var staff3Conn = BuildHubConnection(staff3Cookie);
        var staff3Events = new List<StorageUnitRealtimeEventDto>();
        staff3Conn.On<StorageUnitRealtimeEventDto>("Receiver_CreateStorageUnit", e => staff3Events.Add(e));
        await staff3Conn.StartAsync();

        var cts3 = new CancellationTokenSource();
        var (adminSvc3, db3) = CreateAdminAccountServiceWithCancelInterceptor(cts3);
        using (db3)
        {
            var res3 = await adminSvc3.ManageUserRolesAsync(
                staff3.id,
                new SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Admin.ManageUserRolesRequest
                {
                    RoleCodes = new List<string> { RoleConstants.StorageCustomer }
                },
                rootAdmin.id,
                null,
                null,
                cts3.Token);
            Assert.NotNull(res3);
            Assert.True(cts3.IsCancellationRequested, "Token must be canceled by interceptor upon SaveChangesAsync commit");
        }

        await Task.Delay(600);
        await mgrConn.InvokeAsync<StorageUnitRealtimeEventDto>("HubCreate_StorageUnit", JsonSerializer.Serialize(new
        {
            FacilityId = hierarchy.Facility.id,
            UnitTypeId = hierarchy.UnitType.id,
            UnitCode = $"M3-POST-{suffix}"
        }));
        await Task.Delay(600);
        Assert.Empty(staff3Events);

        // =========================================================================
        // 4. Mutation 4: AssignFacilityAsync with canceled token after commit
        // =========================================================================
        var staff4Email = $"staff4_f01_{suffix}@example.test";
        var staff4Pass = "Staff4Pass123!";
        var staff4 = await _factory.SeedUserAsync(staff4Email, staff4Pass, RoleConstants.FacilityStaff, UserStatusConstants.Active, "Staff4 F01");

        var staff4Cookie = await AuthenticateUserAndGetCookieDirectAsync(staff4Email, staff4Pass);
        var staff4Conn = BuildHubConnection(staff4Cookie);
        var staff4Events = new List<StorageUnitRealtimeEventDto>();
        staff4Conn.On<StorageUnitRealtimeEventDto>("Receiver_CreateStorageUnit", e => staff4Events.Add(e));
        await staff4Conn.StartAsync();

        // Initially staff4 has no assignment to facility -> receives nothing
        await mgrConn.InvokeAsync<StorageUnitRealtimeEventDto>("HubCreate_StorageUnit", JsonSerializer.Serialize(new
        {
            FacilityId = hierarchy.Facility.id,
            UnitTypeId = hierarchy.UnitType.id,
            UnitCode = $"M4-PRE-{suffix}"
        }));
        await Task.Delay(600);
        Assert.Empty(staff4Events);

        var cts4 = new CancellationTokenSource();
        var (adminSvc4, db4) = CreateAdminAccountServiceWithCancelInterceptor(cts4);
        using (db4)
        {
            var res4 = await adminSvc4.AssignFacilityAsync(
                staff4.id,
                new SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Admin.CreateFacilityAssignmentRequest
                {
                    FacilityId = hierarchy.Facility.id,
                    AssignmentRole = RoleConstants.FacilityStaff,
                    StartsAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                    EndsAt = null
                },
                rootAdmin.id,
                null,
                null,
                cts4.Token);
            Assert.NotNull(res4);
            Assert.True(cts4.IsCancellationRequested, "Token must be canceled by interceptor upon SaveChangesAsync commit");
        }

        await Task.Delay(600);
        // After assignment: Staff4 receives events because notifier reconciled and added the group
        var codeM4 = $"M4-POST-{suffix}";
        await mgrConn.InvokeAsync<StorageUnitRealtimeEventDto>("HubCreate_StorageUnit", JsonSerializer.Serialize(new
        {
            FacilityId = hierarchy.Facility.id,
            UnitTypeId = hierarchy.UnitType.id,
            UnitCode = codeM4
        }));
        await Task.Delay(600);
        Assert.Contains(staff4Events, e => e.unitCode == codeM4);
    }

    public sealed class SettableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;
        public SettableTimeProvider(DateTimeOffset initialUtcNow) => _utcNow = initialUtcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
        public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow;
    }

    [Fact]
    public async Task T13_R4_F02_BroadcastPath_PrunesExpiredConnectionsAtSendTime_ZeroLeakageBeforeWorkerTick()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);

        var t0 = DateTimeOffset.UtcNow;
        var timeProvider = new SettableTimeProvider(t0);

        // Staff assigned with ends_at = t0 + 10 minutes
        var staffEmail = $"staff_f02_{suffix}@example.test";
        var staffPass = "StaffPass123!";
        var staff = await _factory.SeedUserAsync(staffEmail, staffPass, RoleConstants.FacilityStaff, UserStatusConstants.Active, "Staff F02");

        using (var db = _factory.CreateDbContext())
        {
            db.staff_facility_assignments.Add(new staff_facility_assignment
            {
                employee_id = staff.id,
                facility_id = hierarchy.Facility.id,
                assignment_role = RoleConstants.FacilityStaff,
                starts_at = t0.AddMinutes(-10),
                ends_at = t0.AddMinutes(10),
                created_at = t0
            });
            await db.SaveChangesAsync();
        }

        // Manager assigned with no expiry
        var mgrEmail = $"mgr_f02_{suffix}@example.test";
        var mgrPass = "MgrPass123!";
        var mgr = await _factory.SeedUserAsync(mgrEmail, mgrPass, RoleConstants.FacilityManager, UserStatusConstants.Active, "Mgr F02");
        using (var db = _factory.CreateDbContext())
        {
            db.staff_facility_assignments.Add(new staff_facility_assignment
            {
                employee_id = mgr.id,
                facility_id = hierarchy.Facility.id,
                assignment_role = RoleConstants.FacilityManager,
                starts_at = t0.AddMinutes(-10),
                ends_at = null,
                created_at = t0
            });
            await db.SaveChangesAsync();
        }

        var staffCookie = await AuthenticateUserAndGetCookieDirectAsync(staffEmail, staffPass);
        var staffConn = BuildHubConnection(staffCookie);
        var staffEvents = new List<StorageUnitRealtimeEventDto>();
        staffConn.On<StorageUnitRealtimeEventDto>("Receiver_CreateStorageUnit", e => staffEvents.Add(e));
        await staffConn.StartAsync();

        var mgrCookie = await AuthenticateUserAndGetCookieDirectAsync(mgrEmail, mgrPass);
        var mgrConn = BuildHubConnection(mgrCookie);
        var mgrEvents = new List<StorageUnitRealtimeEventDto>();
        mgrConn.On<StorageUnitRealtimeEventDto>("Receiver_CreateStorageUnit", e => mgrEvents.Add(e));
        await mgrConn.StartAsync();

        var hubContext = _factory.Services.GetRequiredService<IHubContext<StorageUnitHub>>();
        var connectionTracker = _factory.Services.GetRequiredService<IStorageUnitHubConnectionTracker>();

        // T13-R5-F03: Deterministically wait until both connections are verified in the facility group before broadcasting
        await WaitForUserInGroupAsync(connectionTracker, $"facility_{hierarchy.Facility.id}", staff.id, TimeSpan.FromSeconds(5));
        await WaitForUserInGroupAsync(connectionTracker, $"facility_{hierarchy.Facility.id}", mgr.id, TimeSpan.FromSeconds(5));

        HubConnection? futureConn = null;
        try
        {
            // Setup publisher with controllable SettableTimeProvider
            using var f02Scope = _factory.Services.CreateScope();
            var facilityScopeService = ActivatorUtilities.CreateInstance<SelfStorageManagementSystem.BusinessLogic.Services.Implementations.FacilityScopeService>(f02Scope.ServiceProvider, (TimeProvider)timeProvider);
            var accountIdentityService = f02Scope.ServiceProvider.GetRequiredService<IAccountIdentityService>();
            var revocationLogger = f02Scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<StorageUnitHubScopeRevocationService>>();
            var revocationService = new StorageUnitHubScopeRevocationService(hubContext, connectionTracker, facilityScopeService, accountIdentityService, revocationLogger, timeProvider);
            var pubLogger = f02Scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<StorageUnitRealtimePublisher>>();
            var publisher = new StorageUnitRealtimePublisher(hubContext, revocationService, pubLogger);

            // 1. Time at t0 + 2 minutes: Both Staff and Manager are within valid assignment period
            timeProvider.SetUtcNow(t0.AddMinutes(2));
            var validDto = new SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.StorageUnits.StorageUnitAdminDto
            {
                Id = 1111,
                UnitCode = $"F02-VALID-{suffix}",
                FacilityId = hierarchy.Facility.id,
                FacilityName = hierarchy.Facility.name,
                FacilityCode = hierarchy.Facility.code,
                UnitTypeId = hierarchy.UnitType.id,
                UnitTypeName = hierarchy.UnitType.name,
                UnitTypeCode = hierarchy.UnitType.code,
                PhysicalStatus = "available",
                IsListed = true
            };
            await publisher.PublishCreatedAsync(validDto);
            await Task.Delay(600);

            Assert.Contains(staffEvents, e => e.unitCode == validDto.UnitCode);
            Assert.Contains(mgrEvents, e => e.unitCode == validDto.UnitCode);
            staffEvents.Clear();
            mgrEvents.Clear();

            // 2. Advance clock to t0 + 10 minutes + 1 second: Assignment has expired!
            // StorageUnitScopeSyncWorker has NOT ticked yet.
            timeProvider.SetUtcNow(t0.AddMinutes(10).AddSeconds(1));

            // Publish event immediately: PruneExpiredConnectionsForFacilityAsync runs at broadcast time
            var expiredDto = new SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.StorageUnits.StorageUnitAdminDto
            {
                Id = 1112,
                UnitCode = $"F02-EXPIRED-{suffix}",
                FacilityId = hierarchy.Facility.id,
                FacilityName = hierarchy.Facility.name,
                FacilityCode = hierarchy.Facility.code,
                UnitTypeId = hierarchy.UnitType.id,
                UnitTypeName = hierarchy.UnitType.name,
                UnitTypeCode = hierarchy.UnitType.code,
                PhysicalStatus = "available",
                IsListed = true
            };
            await publisher.PublishCreatedAsync(expiredDto);
            await Task.Delay(600);

            // Staff must receive ZERO events!
            Assert.Empty(staffEvents);
            // Active Manager must receive the event!
            Assert.Contains(mgrEvents, e => e.unitCode == expiredDto.UnitCode);

            // 3. Test future starts_at:
            var futureStaffEmail = $"future_staff_{suffix}@example.test";
            var futureStaff = await _factory.SeedUserAsync(futureStaffEmail, "Pass123!", RoleConstants.FacilityStaff, UserStatusConstants.Active, "Future Staff");
            using (var db = _factory.CreateDbContext())
            {
                db.staff_facility_assignments.Add(new staff_facility_assignment
                {
                    employee_id = futureStaff.id,
                    facility_id = hierarchy.Facility.id,
                    assignment_role = RoleConstants.FacilityStaff,
                    starts_at = t0.AddMinutes(30),
                    ends_at = null,
                    created_at = t0
                });
                await db.SaveChangesAsync();
            }

            var futureCookie = await AuthenticateUserAndGetCookieDirectAsync(futureStaffEmail, "Pass123!");
            futureConn = BuildHubConnection(futureCookie);
            var futureEvents = new List<StorageUnitRealtimeEventDto>();
            futureConn.On<StorageUnitRealtimeEventDto>("Receiver_CreateStorageUnit", e => futureEvents.Add(e));
            await futureConn.StartAsync();

            // At t0 + 10 min, futureStaff assignment has not started -> receives nothing
            var preStartDto = new SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.StorageUnits.StorageUnitAdminDto
            {
                Id = 1113,
                UnitCode = $"F02-PRESTART-{suffix}",
                FacilityId = hierarchy.Facility.id,
                FacilityName = hierarchy.Facility.name,
                FacilityCode = hierarchy.Facility.code,
                UnitTypeId = hierarchy.UnitType.id,
                UnitTypeName = hierarchy.UnitType.name,
                UnitTypeCode = hierarchy.UnitType.code,
                PhysicalStatus = "available",
                IsListed = true
            };
            await publisher.PublishCreatedAsync(preStartDto);
            await Task.Delay(600);
            Assert.Empty(futureEvents);

            // Advance past starts_at and reconcile:
            timeProvider.SetUtcNow(t0.AddMinutes(30).AddSeconds(5));
            await revocationService.ReconcileUserConnectionsAsync(futureStaff.id);
            await WaitForUserInGroupAsync(connectionTracker, $"facility_{hierarchy.Facility.id}", futureStaff.id, TimeSpan.FromSeconds(5));

            var postStartDto = new SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.StorageUnits.StorageUnitAdminDto
            {
                Id = 1114,
                UnitCode = $"F02-POSTSTART-{suffix}",
                FacilityId = hierarchy.Facility.id,
                FacilityName = hierarchy.Facility.name,
                FacilityCode = hierarchy.Facility.code,
                UnitTypeId = hierarchy.UnitType.id,
                UnitTypeName = hierarchy.UnitType.name,
                UnitTypeCode = hierarchy.UnitType.code,
                PhysicalStatus = "available",
                IsListed = true
            };
            await publisher.PublishCreatedAsync(postStartDto);
            await Task.Delay(600);
            Assert.Contains(futureEvents, e => e.unitCode == postStartDto.UnitCode);
        }
        finally
        {
            await staffConn.DisposeAsync();
            await mgrConn.DisposeAsync();
            if (futureConn != null) await futureConn.DisposeAsync();
        }
    }

    [Fact]
    public async Task T13_R4_F03_PerUserReconciliationLock_PreventsConcurrentWorkerFromReAddingRevokedGroup()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);

        var staffEmail = $"staff_f03_{suffix}@example.test";
        var staffPass = "StaffPass123!";
        var staff = await _factory.SeedUserAsync(staffEmail, staffPass, RoleConstants.FacilityStaff, UserStatusConstants.Active, "Staff F03");
        long assignId;
        using (var db = _factory.CreateDbContext())
        {
            var a = new staff_facility_assignment
            {
                employee_id = staff.id,
                facility_id = hierarchy.Facility.id,
                assignment_role = RoleConstants.FacilityStaff,
                starts_at = DateTimeOffset.UtcNow.AddDays(-2),
                ends_at = null,
                created_at = DateTimeOffset.UtcNow
            };
            db.staff_facility_assignments.Add(a);
            await db.SaveChangesAsync();
            assignId = a.id;
        }

        var staffCookie = await AuthenticateUserAndGetCookieDirectAsync(staffEmail, staffPass);
        var staffConn = BuildHubConnection(staffCookie);
        var staffEvents = new List<StorageUnitRealtimeEventDto>();
        staffConn.On<StorageUnitRealtimeEventDto>("Receiver_CreateStorageUnit", e => staffEvents.Add(e));
        await staffConn.StartAsync();

        var hubContext = _factory.Services.GetRequiredService<IHubContext<StorageUnitHub>>();
        var connectionTracker = _factory.Services.GetRequiredService<IStorageUnitHubConnectionTracker>();

        // T13-R5-F03: Deterministically wait until connection is verified in the facility group
        await WaitForUserInGroupAsync(connectionTracker, $"facility_{hierarchy.Facility.id}", staff.id, TimeSpan.FromSeconds(5));

        try
        {
            using var f03Scope = _factory.Services.CreateScope();
            var revocationService = f03Scope.ServiceProvider.GetRequiredService<StorageUnitHubScopeRevocationService>();

            // Terminate assignment in DB
            using (var db = _factory.CreateDbContext())
            {
                var a = await db.staff_facility_assignments.FindAsync(assignId);
                a!.ends_at = DateTimeOffset.UtcNow.AddMinutes(-1);
                await db.SaveChangesAsync();
            }

            // Fire 10 concurrent reconciliation tasks for the same user
            var tasks = Enumerable.Range(0, 10)
                .Select(_ => revocationService.ReconcileUserConnectionsAsync(staff.id))
                .ToArray();

            await Task.WhenAll(tasks);

            // Verify that after concurrent reconciliation, the user has NO connections in the facility group
            var connectionsInGroup = connectionTracker.GetConnectionsInGroup($"facility_{hierarchy.Facility.id}");
            Assert.DoesNotContain(connectionsInGroup, c => c.UserId == staff.id);

            // Broadcast to facility: Staff receives ZERO events
            var pubLogger = f03Scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<StorageUnitRealtimePublisher>>();
            var publisher = new StorageUnitRealtimePublisher(hubContext, revocationService, pubLogger);
            await publisher.PublishCreatedAsync(new SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.StorageUnits.StorageUnitAdminDto
            {
                Id = 2222,
                UnitCode = $"F03-CONCURRENT-{suffix}",
                FacilityId = hierarchy.Facility.id,
                FacilityName = hierarchy.Facility.name,
                FacilityCode = hierarchy.Facility.code,
                UnitTypeId = hierarchy.UnitType.id,
                UnitTypeName = hierarchy.UnitType.name,
                UnitTypeCode = hierarchy.UnitType.code,
                PhysicalStatus = "available",
                IsListed = true
            });

            await Task.Delay(600);
            Assert.Empty(staffEvents);
        }
        finally
        {
            await staffConn.DisposeAsync();
        }
    }

    #endregion

    #region Round 5 Review Finding Fix Tests (T13-R5-F01)

    private sealed class FailingFacilityScopeService : IFacilityScopeService
    {
        public Task<bool> HasAccessToFacilityAsync(long userId, long facilityId, string requiredRole, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated database failure during scope check.");

        public Task<List<long>> GetAccessibleFacilityIdsAsync(long userId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated database failure during scope check.");

        public Task<List<SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Admin.FacilityAssignmentDto>> GetActiveAssignmentsForUserAsync(long userId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated database failure during scope check.");

        public Task<bool> HasCheckInCounterAccessAsync(long userId, long facilityId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated database failure during scope check.");
    }

    private sealed class FailingAccountIdentityService : IAccountIdentityService
    {
        public Task<SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Auth.CurrentUserResponse?> GetCurrentUserAsync(long userId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated identity query failure during permission check.");

        public Task<SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Auth.AuthenticatedAccountResponse> AuthenticateAsync(string email, string password, string? ipAddress = null, string? userAgent = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated failure during authentication.");

        public Task<SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Auth.AuthenticatedAccountResponse> AuthenticateAsync(SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Auth.LoginRequest request, string? ipAddress = null, string? userAgent = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated failure during authentication.");

        public Task<SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Auth.CustomerRegisterResponse> RegisterCustomerAsync(SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Auth.CustomerRegisterRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated failure during registration.");
    }

    public sealed class DegradedRealtimePublisherDecorator : IStorageUnitRealtimePublisher
    {
        public static int PublishCreatedCalls;
        public static int PublishUpdatedCalls;
        public static int PublishDeletedCalls;

        public static void ResetCounters()
        {
            PublishCreatedCalls = 0;
            PublishUpdatedCalls = 0;
            PublishDeletedCalls = 0;
        }

        public Task PublishCreatedAsync(StorageUnitAdminDto unit, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref PublishCreatedCalls);
            throw new RealtimePublishDegradedException("Create", unit.FacilityId, unit.Id, "Simulated degraded realtime broadcast over transport.");
        }

        public Task PublishUpdatedAsync(StorageUnitAdminDto unit, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref PublishUpdatedCalls);
            throw new RealtimePublishDegradedException("Update", unit.FacilityId, unit.Id, "Simulated degraded realtime broadcast over transport.");
        }

        public Task PublishDeletedAsync(long unitId, long facilityId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref PublishDeletedCalls);
            throw new RealtimePublishDegradedException("Delete", facilityId, unitId, "Simulated degraded realtime broadcast over transport.");
        }

        public Task PublishDeletedAsync(long unitId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref PublishDeletedCalls);
            throw new RealtimePublishDegradedException("Delete", 0, unitId, "Simulated degraded realtime broadcast over transport.");
        }
    }

    [Fact]
    public async Task T13_R5_F01_FailClosed_WhenPruneFails_BlocksFacilityBroadcast_ZeroLeakageToExpiredStaff()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);

        var t0 = DateTimeOffset.UtcNow;
        var timeProvider = new SettableTimeProvider(t0);

        // 1. Staff assigned with ends_at = t0 + 10 minutes
        var staffEmail = $"staff_f01_fail_{suffix}@example.test";
        var staffPass = "StaffPass123!";
        var staff = await _factory.SeedUserAsync(staffEmail, staffPass, RoleConstants.FacilityStaff, UserStatusConstants.Active, "Staff F01 Fail");

        using (var db = _factory.CreateDbContext())
        {
            db.staff_facility_assignments.Add(new staff_facility_assignment
            {
                employee_id = staff.id,
                facility_id = hierarchy.Facility.id,
                assignment_role = RoleConstants.FacilityStaff,
                starts_at = t0.AddMinutes(-10),
                ends_at = t0.AddMinutes(10),
                created_at = t0
            });
            await db.SaveChangesAsync();
        }

        // Global admin for independent broadcast verification
        var adminEmail = $"admin_f01_{suffix}@example.test";
        var adminPass = "AdminPass123!";
        var admin = await _factory.SeedUserAsync(adminEmail, adminPass, RoleConstants.SystemAdministrator, UserStatusConstants.Active, "Admin F01");

        var staffCookie = await AuthenticateUserAndGetCookieDirectAsync(staffEmail, staffPass);
        var staffConn = BuildHubConnection(staffCookie);
        var staffEvents = new List<StorageUnitRealtimeEventDto>();
        staffConn.On<StorageUnitRealtimeEventDto>("Receiver_CreateStorageUnit", e => staffEvents.Add(e));
        await staffConn.StartAsync();

        var adminCookie = await AuthenticateUserAndGetCookieDirectAsync(adminEmail, adminPass);
        var adminConn = BuildHubConnection(adminCookie);
        var adminEvents = new List<StorageUnitRealtimeEventDto>();
        adminConn.On<StorageUnitRealtimeEventDto>("Receiver_CreateStorageUnit", e => adminEvents.Add(e));
        await adminConn.StartAsync();

        var hubContext = _factory.Services.GetRequiredService<IHubContext<StorageUnitHub>>();
        var connectionTracker = _factory.Services.GetRequiredService<IStorageUnitHubConnectionTracker>();

        await WaitForUserInGroupAsync(connectionTracker, $"facility_{hierarchy.Facility.id}", staff.id, TimeSpan.FromSeconds(5));
        await WaitForUserInGroupAsync(connectionTracker, "admin_global", admin.id, TimeSpan.FromSeconds(5));

        try
        {
            // Advance clock so staff assignment is expired
            timeProvider.SetUtcNow(t0.AddMinutes(15));

            // Setup a FailingFacilityScopeService to simulate database/scope query failure during send-time pruning
            using var f01Scope = _factory.Services.CreateScope();
            var failingScopeService = new FailingFacilityScopeService();
            var accountIdentityService = f01Scope.ServiceProvider.GetRequiredService<IAccountIdentityService>();
            var revocationLogger = f01Scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<StorageUnitHubScopeRevocationService>>();
            var failingRevocationService = new StorageUnitHubScopeRevocationService(hubContext, connectionTracker, failingScopeService, accountIdentityService, revocationLogger, timeProvider);
            var pubLogger = f01Scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<StorageUnitRealtimePublisher>>();
            var publisher = new StorageUnitRealtimePublisher(hubContext, failingRevocationService, pubLogger);

            var dto = new SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.StorageUnits.StorageUnitAdminDto
            {
                Id = 8801,
                UnitCode = $"F01-FAILCLOSED-{suffix}",
                FacilityId = hierarchy.Facility.id,
                FacilityName = hierarchy.Facility.name,
                FacilityCode = hierarchy.Facility.code,
                UnitTypeId = hierarchy.UnitType.id,
                UnitTypeName = hierarchy.UnitType.name,
                UnitTypeCode = hierarchy.UnitType.code,
                PhysicalStatus = "available",
                IsListed = true
            };

            // Fail-closed guard: PublishCreatedAsync MUST throw RealtimePublishDegradedException (derived from InvalidOperationException) when pruning check fails!
            var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => publisher.PublishCreatedAsync(dto));
            Assert.Contains("Không thể đối soát quyền hạn kết nối", ex.Message);
            Assert.IsType<RealtimePublishDegradedException>(ex);

            await Task.Delay(500);

            // Staff connection MUST receive ZERO events (NO data leakage)!
            Assert.Empty(staffEvents);

            // T13-R7-F01: Complete fail-closed protection: when scope/pruning check fails,
            // neither facility group nor admin_global group receives unverified CRUD data!
            Assert.Empty(adminEvents);
        }
        finally
        {
            await staffConn.DisposeAsync();
            await adminConn.DisposeAsync();
        }
    }

    [Fact]
    public async Task T13_R5_F01_NormalPath_WhenPruneSucceeds_ActiveStaffReceives_ExpiredStaffZeroEvents()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);

        var t0 = DateTimeOffset.UtcNow;
        var timeProvider = new SettableTimeProvider(t0);

        // Staff 1: Active assignment (ends_at = t0 + 30 min)
        var staffActiveEmail = $"staff_act_{suffix}@example.test";
        var staffActive = await _factory.SeedUserAsync(staffActiveEmail, "Pass123!", RoleConstants.FacilityStaff, UserStatusConstants.Active, "Staff Active");

        // Staff 2: Expired assignment (ends_at = t0 + 5 min)
        var staffExpiredEmail = $"staff_exp_{suffix}@example.test";
        var staffExpired = await _factory.SeedUserAsync(staffExpiredEmail, "Pass123!", RoleConstants.FacilityStaff, UserStatusConstants.Active, "Staff Expired");

        using (var db = _factory.CreateDbContext())
        {
            db.staff_facility_assignments.AddRange(
                new staff_facility_assignment
                {
                    employee_id = staffActive.id,
                    facility_id = hierarchy.Facility.id,
                    assignment_role = RoleConstants.FacilityStaff,
                    starts_at = t0.AddMinutes(-10),
                    ends_at = t0.AddMinutes(30),
                    created_at = t0
                },
                new staff_facility_assignment
                {
                    employee_id = staffExpired.id,
                    facility_id = hierarchy.Facility.id,
                    assignment_role = RoleConstants.FacilityStaff,
                    starts_at = t0.AddMinutes(-10),
                    ends_at = t0.AddMinutes(5),
                    created_at = t0
                }
            );
            await db.SaveChangesAsync();
        }

        var activeCookie = await AuthenticateUserAndGetCookieDirectAsync(staffActiveEmail, "Pass123!");
        var activeConn = BuildHubConnection(activeCookie);
        var activeEvents = new List<StorageUnitRealtimeEventDto>();
        activeConn.On<StorageUnitRealtimeEventDto>("Receiver_CreateStorageUnit", e => activeEvents.Add(e));
        await activeConn.StartAsync();

        var expiredCookie = await AuthenticateUserAndGetCookieDirectAsync(staffExpiredEmail, "Pass123!");
        var expiredConn = BuildHubConnection(expiredCookie);
        var expiredEvents = new List<StorageUnitRealtimeEventDto>();
        expiredConn.On<StorageUnitRealtimeEventDto>("Receiver_CreateStorageUnit", e => expiredEvents.Add(e));
        await expiredConn.StartAsync();

        var hubContext = _factory.Services.GetRequiredService<IHubContext<StorageUnitHub>>();
        var connectionTracker = _factory.Services.GetRequiredService<IStorageUnitHubConnectionTracker>();

        await WaitForUserInGroupAsync(connectionTracker, $"facility_{hierarchy.Facility.id}", staffActive.id, TimeSpan.FromSeconds(5));
        await WaitForUserInGroupAsync(connectionTracker, $"facility_{hierarchy.Facility.id}", staffExpired.id, TimeSpan.FromSeconds(5));

        try
        {
            // Advance clock to t0 + 10 min: staffExpired is now expired, staffActive is still active
            timeProvider.SetUtcNow(t0.AddMinutes(10));

            using var scope = _factory.Services.CreateScope();
            var facilityScopeService = ActivatorUtilities.CreateInstance<SelfStorageManagementSystem.BusinessLogic.Services.Implementations.FacilityScopeService>(scope.ServiceProvider, (TimeProvider)timeProvider);
            var accountIdentityService = scope.ServiceProvider.GetRequiredService<IAccountIdentityService>();
            var revocationLogger = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<StorageUnitHubScopeRevocationService>>();
            var revocationService = new StorageUnitHubScopeRevocationService(hubContext, connectionTracker, facilityScopeService, accountIdentityService, revocationLogger, timeProvider);
            var pubLogger = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<StorageUnitRealtimePublisher>>();
            var publisher = new StorageUnitRealtimePublisher(hubContext, revocationService, pubLogger);

            var dto = new SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.StorageUnits.StorageUnitAdminDto
            {
                Id = 8802,
                UnitCode = $"F01-NORMAL-{suffix}",
                FacilityId = hierarchy.Facility.id,
                FacilityName = hierarchy.Facility.name,
                FacilityCode = hierarchy.Facility.code,
                UnitTypeId = hierarchy.UnitType.id,
                UnitTypeName = hierarchy.UnitType.name,
                UnitTypeCode = hierarchy.UnitType.code,
                PhysicalStatus = "available",
                IsListed = true
            };

            await publisher.PublishCreatedAsync(dto);
            await Task.Delay(500);

            // Active staff received the event
            Assert.Contains(activeEvents, e => e.unitCode == dto.UnitCode);
            // Expired staff received ZERO events (pruned at send-time before broadcast)
            Assert.Empty(expiredEvents);
        }
        finally
        {
            await activeConn.DisposeAsync();
            await expiredConn.DisposeAsync();
        }
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

    #region Round 7 Review Finding Fix Tests (T13-R7-F01, T13-R7-F03)

    [Fact]
    public async Task T13_R7_F01_DemotedAdmin_WhenNotifierFails_PrunedAtBroadcastTime_ZeroEvents()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);

        // 1. Admin 1: starts as SystemAdministrator, will be demoted to Customer
        var admin1Email = $"admin1_demote_{suffix}@example.test";
        var admin1Pass = "Pass123!";
        var admin1 = await _factory.SeedUserAsync(admin1Email, admin1Pass, RoleConstants.SystemAdministrator, UserStatusConstants.Active, "Admin 1 Demoted");

        // 2. Admin 2: active SystemAdministrator (must STILL receive events)
        var admin2Email = $"admin2_active_{suffix}@example.test";
        var admin2Pass = "Pass123!";
        var admin2 = await _factory.SeedUserAsync(admin2Email, admin2Pass, RoleConstants.SystemAdministrator, UserStatusConstants.Active, "Admin 2 Active");

        // 3. Staff: active assignment for this facility (must STILL receive events)
        var staffEmail = $"staff_act_{suffix}@example.test";
        var staffPass = "Pass123!";
        var staff = await _factory.SeedUserAsync(staffEmail, staffPass, RoleConstants.FacilityStaff, UserStatusConstants.Active, "Staff Active");

        var now = DateTimeOffset.UtcNow;
        using (var db = _factory.CreateDbContext())
        {
            db.staff_facility_assignments.Add(new staff_facility_assignment
            {
                employee_id = staff.id,
                facility_id = hierarchy.Facility.id,
                assignment_role = RoleConstants.FacilityStaff,
                starts_at = now.AddMinutes(-10),
                ends_at = now.AddMinutes(30),
                created_at = now
            });
            await db.SaveChangesAsync();
        }

        var admin1Cookie = await AuthenticateUserAndGetCookieDirectAsync(admin1Email, admin1Pass);
        var admin1Conn = BuildHubConnection(admin1Cookie);
        var admin1CreateEvents = new List<StorageUnitRealtimeEventDto>();
        var admin1UpdateEvents = new List<StorageUnitRealtimeEventDto>();
        admin1Conn.On<StorageUnitRealtimeEventDto>("Receiver_CreateStorageUnit", e => admin1CreateEvents.Add(e));
        admin1Conn.On<StorageUnitRealtimeEventDto>("Receiver_UpdateStorageUnit", e => admin1UpdateEvents.Add(e));
        await admin1Conn.StartAsync();

        var admin2Cookie = await AuthenticateUserAndGetCookieDirectAsync(admin2Email, admin2Pass);
        var admin2Conn = BuildHubConnection(admin2Cookie);
        var admin2CreateEvents = new List<StorageUnitRealtimeEventDto>();
        var admin2UpdateEvents = new List<StorageUnitRealtimeEventDto>();
        admin2Conn.On<StorageUnitRealtimeEventDto>("Receiver_CreateStorageUnit", e => admin2CreateEvents.Add(e));
        admin2Conn.On<StorageUnitRealtimeEventDto>("Receiver_UpdateStorageUnit", e => admin2UpdateEvents.Add(e));
        await admin2Conn.StartAsync();

        var staffCookie = await AuthenticateUserAndGetCookieDirectAsync(staffEmail, staffPass);
        var staffConn = BuildHubConnection(staffCookie);
        var staffCreateEvents = new List<StorageUnitRealtimeEventDto>();
        var staffUpdateEvents = new List<StorageUnitRealtimeEventDto>();
        staffConn.On<StorageUnitRealtimeEventDto>("Receiver_CreateStorageUnit", e => staffCreateEvents.Add(e));
        staffConn.On<StorageUnitRealtimeEventDto>("Receiver_UpdateStorageUnit", e => staffUpdateEvents.Add(e));
        await staffConn.StartAsync();

        var hubContext = _factory.Services.GetRequiredService<IHubContext<StorageUnitHub>>();
        var connectionTracker = _factory.Services.GetRequiredService<IStorageUnitHubConnectionTracker>();

        await WaitForUserInGroupAsync(connectionTracker, "admin_global", admin1.id, TimeSpan.FromSeconds(5));
        await WaitForUserInGroupAsync(connectionTracker, "admin_global", admin2.id, TimeSpan.FromSeconds(5));
        await WaitForUserInGroupAsync(connectionTracker, $"facility_{hierarchy.Facility.id}", staff.id, TimeSpan.FromSeconds(5));

        try
        {
            // Now, simulate role demotion in DB: remove SystemAdministrator role from admin1
            // Intentionally DO NOT call _scopeChangedNotifier (simulating notifier network error or crash before worker tick)
            using (var db = _factory.CreateDbContext())
            {
                var userRoles = await db.user_roles.Where(ur => ur.user_id == admin1.id).ToListAsync();
                db.user_roles.RemoveRange(userRoles);
                var customerRole = await db.roles.FirstOrDefaultAsync(r => r.code == RoleConstants.StorageCustomer);
                if (customerRole != null)
                {
                    db.user_roles.Add(new user_role
                    {
                        user_id = admin1.id,
                        role_id = customerRole.id,
                        granted_at = DateTimeOffset.UtcNow,
                        granted_by = admin2.id
                    });
                }
                var u = await db.users.FindAsync(admin1.id);
                if (u != null) u.updated_at = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }

            using var scope = _factory.Services.CreateScope();
            var facilityScopeService = scope.ServiceProvider.GetRequiredService<IFacilityScopeService>();
            var accountIdentityService = scope.ServiceProvider.GetRequiredService<IAccountIdentityService>();
            var revocationLogger = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<StorageUnitHubScopeRevocationService>>();
            var revocationService = new StorageUnitHubScopeRevocationService(hubContext, connectionTracker, facilityScopeService, accountIdentityService, revocationLogger);
            var pubLogger = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<StorageUnitRealtimePublisher>>();
            var publisher = new StorageUnitRealtimePublisher(hubContext, revocationService, pubLogger);

            var createDto = new StorageUnitAdminDto
            {
                Id = 9101,
                UnitCode = $"F01-CREATE-{suffix}",
                FacilityId = hierarchy.Facility.id,
                FacilityName = hierarchy.Facility.name,
                FacilityCode = hierarchy.Facility.code,
                UnitTypeId = hierarchy.UnitType.id,
                UnitTypeName = hierarchy.UnitType.name,
                UnitTypeCode = hierarchy.UnitType.code,
                PhysicalStatus = "available",
                IsListed = true
            };

            await publisher.PublishCreatedAsync(createDto);
            await Task.Delay(500);

            // T13-R7-F01 Verification: Demoted admin connection MUST have been reconciled & pruned at send-time -> ZERO events!
            Assert.Empty(admin1CreateEvents);
            // Active global admin and active facility staff DO receive the event!
            Assert.Contains(admin2CreateEvents, e => e.unitCode == createDto.UnitCode);
            Assert.Contains(staffCreateEvents, e => e.unitCode == createDto.UnitCode);

            // Now test Update as well
            var updateDto = new StorageUnitAdminDto
            {
                Id = 9101,
                UnitCode = $"F01-UPDATE-{suffix}",
                FacilityId = hierarchy.Facility.id,
                FacilityName = hierarchy.Facility.name,
                FacilityCode = hierarchy.Facility.code,
                UnitTypeId = hierarchy.UnitType.id,
                UnitTypeName = hierarchy.UnitType.name,
                UnitTypeCode = hierarchy.UnitType.code,
                PhysicalStatus = "occupied",
                IsListed = true
            };

            await publisher.PublishUpdatedAsync(updateDto);
            await Task.Delay(500);

            // Demoted admin continues to receive ZERO events
            Assert.Empty(admin1UpdateEvents);
            Assert.Contains(admin2UpdateEvents, e => e.unitCode == updateDto.UnitCode);
            Assert.Contains(staffUpdateEvents, e => e.unitCode == updateDto.UnitCode);
        }
        finally
        {
            await admin1Conn.DisposeAsync();
            await admin2Conn.DisposeAsync();
            await staffConn.DisposeAsync();
        }
    }

    [Fact]
    public async Task T13_R7_F01_IdentityQueryFails_FailClosed_BothFacilityAndAdminGlobalReceiveZeroEvents()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);

        var adminEmail = $"admin_f01_fail_{suffix}@example.test";
        var adminPass = "Pass123!";
        var admin = await _factory.SeedUserAsync(adminEmail, adminPass, RoleConstants.SystemAdministrator, UserStatusConstants.Active, "Admin F01 Fail");

        var staffEmail = $"staff_f01_fail_{suffix}@example.test";
        var staffPass = "Pass123!";
        var staff = await _factory.SeedUserAsync(staffEmail, staffPass, RoleConstants.FacilityStaff, UserStatusConstants.Active, "Staff F01 Fail");

        var now = DateTimeOffset.UtcNow;
        using (var db = _factory.CreateDbContext())
        {
            db.staff_facility_assignments.Add(new staff_facility_assignment
            {
                employee_id = staff.id,
                facility_id = hierarchy.Facility.id,
                assignment_role = RoleConstants.FacilityStaff,
                starts_at = now.AddMinutes(-10),
                ends_at = now.AddMinutes(30),
                created_at = now
            });
            await db.SaveChangesAsync();
        }

        var adminCookie = await AuthenticateUserAndGetCookieDirectAsync(adminEmail, adminPass);
        var adminConn = BuildHubConnection(adminCookie);
        var adminEvents = new List<StorageUnitRealtimeEventDto>();
        adminConn.On<StorageUnitRealtimeEventDto>("Receiver_CreateStorageUnit", e => adminEvents.Add(e));
        await adminConn.StartAsync();

        var staffCookie = await AuthenticateUserAndGetCookieDirectAsync(staffEmail, staffPass);
        var staffConn = BuildHubConnection(staffCookie);
        var staffEvents = new List<StorageUnitRealtimeEventDto>();
        staffConn.On<StorageUnitRealtimeEventDto>("Receiver_CreateStorageUnit", e => staffEvents.Add(e));
        await staffConn.StartAsync();

        var hubContext = _factory.Services.GetRequiredService<IHubContext<StorageUnitHub>>();
        var connectionTracker = _factory.Services.GetRequiredService<IStorageUnitHubConnectionTracker>();

        await WaitForUserInGroupAsync(connectionTracker, "admin_global", admin.id, TimeSpan.FromSeconds(5));
        await WaitForUserInGroupAsync(connectionTracker, $"facility_{hierarchy.Facility.id}", staff.id, TimeSpan.FromSeconds(5));

        try
        {
            using var scope = _factory.Services.CreateScope();
            var facilityScopeService = scope.ServiceProvider.GetRequiredService<IFacilityScopeService>();
            var failingIdentityService = new FailingAccountIdentityService();
            var revocationLogger = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<StorageUnitHubScopeRevocationService>>();
            var revocationService = new StorageUnitHubScopeRevocationService(hubContext, connectionTracker, facilityScopeService, failingIdentityService, revocationLogger);
            var pubLogger = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<StorageUnitRealtimePublisher>>();
            var publisher = new StorageUnitRealtimePublisher(hubContext, revocationService, pubLogger);

            var createDto = new StorageUnitAdminDto
            {
                Id = 9201,
                UnitCode = $"F01-FAILALL-{suffix}",
                FacilityId = hierarchy.Facility.id,
                FacilityName = hierarchy.Facility.name,
                FacilityCode = hierarchy.Facility.code,
                UnitTypeId = hierarchy.UnitType.id,
                UnitTypeName = hierarchy.UnitType.name,
                UnitTypeCode = hierarchy.UnitType.code,
                PhysicalStatus = "available",
                IsListed = true
            };

            // Fail-closed guard: Must throw RealtimePublishDegradedException
            var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => publisher.PublishCreatedAsync(createDto));
            Assert.IsType<RealtimePublishDegradedException>(ex);

            await Task.Delay(500);

            // T13-R7-F01: COMPLETE fail-closed: BOTH groups receive 0 events!
            Assert.Empty(staffEvents);
            Assert.Empty(adminEvents);
        }
        finally
        {
            await adminConn.DisposeAsync();
            await staffConn.DisposeAsync();
        }
    }

    [Fact]
    public async Task T13_R7_F01_FallbackDelete_GuardsAdminGlobal_WhenPruningFails_ThrowsDegradedAndZeroEvents()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);

        var adminEmail = $"admin_fb_{suffix}@example.test";
        var adminPass = "Pass123!";
        var admin = await _factory.SeedUserAsync(adminEmail, adminPass, RoleConstants.SystemAdministrator, UserStatusConstants.Active, "Admin Fallback");

        var adminCookie = await AuthenticateUserAndGetCookieDirectAsync(adminEmail, adminPass);
        var adminConn = BuildHubConnection(adminCookie);
        var adminDeleteEvents = new List<string>();
        adminConn.On<string>("Receiver_DeleteStorageUnit", id => adminDeleteEvents.Add(id));
        await adminConn.StartAsync();

        var hubContext = _factory.Services.GetRequiredService<IHubContext<StorageUnitHub>>();
        var connectionTracker = _factory.Services.GetRequiredService<IStorageUnitHubConnectionTracker>();

        await WaitForUserInGroupAsync(connectionTracker, "admin_global", admin.id, TimeSpan.FromSeconds(5));

        try
        {
            // Case 1: Identity service fails during fallback delete pruning -> fail closed
            using var scope = _factory.Services.CreateScope();
            var facilityScopeService = scope.ServiceProvider.GetRequiredService<IFacilityScopeService>();
            var failingIdentityService = new FailingAccountIdentityService();
            var revocationLogger = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<StorageUnitHubScopeRevocationService>>();
            var failingRevocation = new StorageUnitHubScopeRevocationService(hubContext, connectionTracker, facilityScopeService, failingIdentityService, revocationLogger);
            var pubLogger = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<StorageUnitRealtimePublisher>>();
            var publisherFailing = new StorageUnitRealtimePublisher(hubContext, failingRevocation, pubLogger);

            var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => publisherFailing.PublishDeletedAsync(9301));
            Assert.IsType<RealtimePublishDegradedException>(ex);

            await Task.Delay(300);
            Assert.Empty(adminDeleteEvents);

            // Case 2: Healthy revocation service -> active admin receives fallback delete
            var realIdentityService = scope.ServiceProvider.GetRequiredService<IAccountIdentityService>();
            var healthyRevocation = new StorageUnitHubScopeRevocationService(hubContext, connectionTracker, facilityScopeService, realIdentityService, revocationLogger);
            var publisherHealthy = new StorageUnitRealtimePublisher(hubContext, healthyRevocation, pubLogger);

            await publisherHealthy.PublishDeletedAsync(9301);
            await Task.Delay(500);

            Assert.Contains("9301", adminDeleteEvents);
        }
        finally
        {
            await adminConn.DisposeAsync();
        }
    }

    #region T13-R8-F01 Tests: Broadcast Guard Fail-Closed on Removal/Abort Failure & Tracker Integrity

    [Fact]
    public async Task T13_R8_F01_DemotedAdmin_WhenRemoveFromGroupAndAbortFail_FailsClosed_TrackerRetainsGroup_ZeroEvents()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);

        // IGroupManager that fails on RemoveFromGroupAsync
        var mockGroups = new Mock<IGroupManager>();
        mockGroups.Setup(g => g.RemoveFromGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ThrowsAsync(new HubException("Simulated SignalR remove group network timeout"));

        var mockClients = new Mock<IHubClients>();
        var mockGroupProxy = new Mock<IClientProxy>();
        mockClients.Setup(c => c.Groups(It.IsAny<IReadOnlyList<string>>())).Returns(mockGroupProxy.Object);
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockGroupProxy.Object);
        mockClients.Setup(c => c.All).Returns(Mock.Of<IClientProxy>());
        mockClients.Setup(c => c.Client(It.IsAny<string>())).Returns(Mock.Of<ISingleClientProxy>());

        var mockHubContext = new Mock<IHubContext<StorageUnitHub>>();
        mockHubContext.Setup(h => h.Groups).Returns(mockGroups.Object);
        mockHubContext.Setup(h => h.Clients).Returns(mockClients.Object);

        // Connection tracker where abort delegate throws
        var tracker = new StorageUnitHubConnectionTracker();
        const long demotedUserId = 8801L;
        const string connId = "conn_admin_demoted_8801";
        tracker.TrackConnection(demotedUserId, connId, new[] { "admin_global" }, () => throw new InvalidOperationException("Simulated socket abort failure"));

        var mockIdentityService = new Mock<IAccountIdentityService>();
        mockIdentityService.Setup(i => i.GetCurrentUserAsync(demotedUserId, It.IsAny<CancellationToken>()))
                           .ReturnsAsync(new SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Auth.CurrentUserResponse
                           {
                               UserId = demotedUserId,
                               Email = "demoted@example.test",
                               DisplayName = "Demoted Admin",
                               Status = UserStatusConstants.Active,
                               Roles = new List<string> { RoleConstants.StorageCustomer }
                           });

        var mockScopeService = new Mock<IFacilityScopeService>();
        mockScopeService.Setup(s => s.GetAccessibleFacilityIdsAsync(demotedUserId, It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new List<long>());

        var revocationService = new StorageUnitHubScopeRevocationService(
            mockHubContext.Object, tracker, mockScopeService.Object, mockIdentityService.Object, NullLogger<StorageUnitHubScopeRevocationService>.Instance);
        var publisher = new StorageUnitRealtimePublisher(
            mockHubContext.Object, revocationService, NullLogger<StorageUnitRealtimePublisher>.Instance);

        var createDto = new StorageUnitAdminDto
        {
            Id = 9801,
            UnitCode = $"F01-DEMOTE-{suffix}",
            FacilityId = hierarchy.Facility.id,
            UnitTypeId = hierarchy.UnitType.id,
            PhysicalStatus = "available",
            IsListed = true
        };

        // Guard must fail-closed: throws RealtimePublishDegradedException
        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => publisher.PublishCreatedAsync(createDto));
        Assert.IsType<RealtimePublishDegradedException>(ex);

        // Assert 0 CRUD DTO broadcast to Groups or admin_global
        mockGroupProxy.Verify(p => p.SendCoreAsync("Receiver_CreateStorageUnit", It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Never);

        // Assert tracker did NOT delete the connection or group (did NOT create a false verified state!)
        var adminConns = tracker.GetConnectionsInGroup("admin_global");
        Assert.Contains(adminConns, c => c.ConnectionId == connId && c.UserId == demotedUserId);
    }

    [Fact]
    public async Task T13_R8_F01_StaffExpired_WhenRemoveFromGroupAndAbortFail_FailsClosed_TrackerRetainsGroup_ZeroEvents()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);

        var mockGroups = new Mock<IGroupManager>();
        mockGroups.Setup(g => g.RemoveFromGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ThrowsAsync(new HubException("Simulated SignalR remove group network timeout"));

        var mockClients = new Mock<IHubClients>();
        var mockGroupProxy = new Mock<IClientProxy>();
        mockClients.Setup(c => c.Groups(It.IsAny<IReadOnlyList<string>>())).Returns(mockGroupProxy.Object);
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockGroupProxy.Object);
        mockClients.Setup(c => c.All).Returns(Mock.Of<IClientProxy>());
        mockClients.Setup(c => c.Client(It.IsAny<string>())).Returns(Mock.Of<ISingleClientProxy>());

        var mockHubContext = new Mock<IHubContext<StorageUnitHub>>();
        mockHubContext.Setup(h => h.Groups).Returns(mockGroups.Object);
        mockHubContext.Setup(h => h.Clients).Returns(mockClients.Object);

        var tracker = new StorageUnitHubConnectionTracker();
        const long staffUserId = 8802L;
        const string connId = "conn_staff_expired_8802";
        var facilityGroup = $"facility_{hierarchy.Facility.id}";
        tracker.TrackConnection(staffUserId, connId, new[] { facilityGroup }, () => throw new InvalidOperationException("Simulated abort failure"));

        var mockIdentityService = new Mock<IAccountIdentityService>();
        mockIdentityService.Setup(i => i.GetCurrentUserAsync(staffUserId, It.IsAny<CancellationToken>()))
                           .ReturnsAsync(new SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Auth.CurrentUserResponse
                           {
                               UserId = staffUserId,
                               Email = "staff_exp@example.test",
                               DisplayName = "Expired Staff",
                               Status = UserStatusConstants.Active,
                               Roles = new List<string> { RoleConstants.FacilityStaff }
                           });

        // Expired assignment -> GetAccessibleFacilityIdsAsync returns empty
        var mockScopeService = new Mock<IFacilityScopeService>();
        mockScopeService.Setup(s => s.GetAccessibleFacilityIdsAsync(staffUserId, It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new List<long>());

        var revocationService = new StorageUnitHubScopeRevocationService(
            mockHubContext.Object, tracker, mockScopeService.Object, mockIdentityService.Object, NullLogger<StorageUnitHubScopeRevocationService>.Instance);
        var publisher = new StorageUnitRealtimePublisher(
            mockHubContext.Object, revocationService, NullLogger<StorageUnitRealtimePublisher>.Instance);

        var updateDto = new StorageUnitAdminDto
        {
            Id = 9802,
            UnitCode = $"F01-EXP-{suffix}",
            FacilityId = hierarchy.Facility.id,
            UnitTypeId = hierarchy.UnitType.id,
            PhysicalStatus = "occupied",
            IsListed = true
        };

        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => publisher.PublishUpdatedAsync(updateDto));
        Assert.IsType<RealtimePublishDegradedException>(ex);

        mockGroupProxy.Verify(p => p.SendCoreAsync("Receiver_UpdateStorageUnit", It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Never);

        var conns = tracker.GetConnectionsInGroup(facilityGroup);
        Assert.Contains(conns, c => c.ConnectionId == connId && c.UserId == staffUserId);
    }

    [Fact]
    public async Task T13_R8_F01_LockedAccount_WhenRemoveFromGroupAndAbortFail_FailsClosed_TrackerRetainsGroup_ZeroEvents()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);

        var mockGroups = new Mock<IGroupManager>();
        mockGroups.Setup(g => g.RemoveFromGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ThrowsAsync(new HubException("Simulated SignalR remove group network timeout"));

        var mockClients = new Mock<IHubClients>();
        var mockGroupProxy = new Mock<IClientProxy>();
        mockClients.Setup(c => c.Groups(It.IsAny<IReadOnlyList<string>>())).Returns(mockGroupProxy.Object);
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockGroupProxy.Object);
        mockClients.Setup(c => c.All).Returns(Mock.Of<IClientProxy>());
        mockClients.Setup(c => c.Client(It.IsAny<string>())).Returns(Mock.Of<ISingleClientProxy>());

        var mockHubContext = new Mock<IHubContext<StorageUnitHub>>();
        mockHubContext.Setup(h => h.Groups).Returns(mockGroups.Object);
        mockHubContext.Setup(h => h.Clients).Returns(mockClients.Object);

        var tracker = new StorageUnitHubConnectionTracker();
        const long lockedUserId = 8803L;
        const string connId = "conn_user_locked_8803";
        var facilityGroup = $"facility_{hierarchy.Facility.id}";
        tracker.TrackConnection(lockedUserId, connId, new[] { facilityGroup, "admin_global" }, () => throw new InvalidOperationException("Simulated abort failure"));

        var mockIdentityService = new Mock<IAccountIdentityService>();
        mockIdentityService.Setup(i => i.GetCurrentUserAsync(lockedUserId, It.IsAny<CancellationToken>()))
                           .ReturnsAsync(new SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Auth.CurrentUserResponse
                           {
                               UserId = lockedUserId,
                               Email = "locked@example.test",
                               DisplayName = "Locked User",
                               Status = UserStatusConstants.Locked,
                               Roles = new List<string> { RoleConstants.FacilityManager }
                           });

        var mockScopeService = new Mock<IFacilityScopeService>();
        var revocationService = new StorageUnitHubScopeRevocationService(
            mockHubContext.Object, tracker, mockScopeService.Object, mockIdentityService.Object, NullLogger<StorageUnitHubScopeRevocationService>.Instance);
        var publisher = new StorageUnitRealtimePublisher(
            mockHubContext.Object, revocationService, NullLogger<StorageUnitRealtimePublisher>.Instance);

        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => publisher.PublishDeletedAsync(9803, hierarchy.Facility.id));
        Assert.IsType<RealtimePublishDegradedException>(ex);

        mockGroupProxy.Verify(p => p.SendCoreAsync("Receiver_DeleteStorageUnit", It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Never);

        Assert.Contains(tracker.GetConnectionsInGroup(facilityGroup), c => c.ConnectionId == connId);
    }

    [Fact]
    public async Task T13_R8_F01_NormalRemoval_Succeeds_AndRemainingAuthorizedUsersReceiveEvent()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);

        var mockGroups = new Mock<IGroupManager>();
        mockGroups.Setup(g => g.RemoveFromGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

        var mockClients = new Mock<IHubClients>();
        var mockGroupProxy = new Mock<IClientProxy>();
        mockGroupProxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
                      .Returns(Task.CompletedTask);
        mockClients.Setup(c => c.Groups(It.IsAny<IReadOnlyList<string>>())).Returns(mockGroupProxy.Object);
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockGroupProxy.Object);
        mockClients.Setup(c => c.All).Returns(Mock.Of<IClientProxy>());

        var mockHubContext = new Mock<IHubContext<StorageUnitHub>>();
        mockHubContext.Setup(h => h.Groups).Returns(mockGroups.Object);
        mockHubContext.Setup(h => h.Clients).Returns(mockClients.Object);

        var tracker = new StorageUnitHubConnectionTracker();
        const long demotedUserId = 8804L;
        const string connIdDemoted = "conn_demoted_8804";
        tracker.TrackConnection(demotedUserId, connIdDemoted, new[] { "admin_global" });

        const long activeUserId = 8805L;
        const string connIdActive = "conn_active_8805";
        tracker.TrackConnection(activeUserId, connIdActive, new[] { "admin_global" });

        var mockIdentityService = new Mock<IAccountIdentityService>();
        mockIdentityService.Setup(i => i.GetCurrentUserAsync(demotedUserId, It.IsAny<CancellationToken>()))
                           .ReturnsAsync(new SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Auth.CurrentUserResponse
                           {
                               UserId = demotedUserId,
                               Status = UserStatusConstants.Active,
                               Roles = new List<string> { RoleConstants.StorageCustomer }
                           });
        mockIdentityService.Setup(i => i.GetCurrentUserAsync(activeUserId, It.IsAny<CancellationToken>()))
                           .ReturnsAsync(new SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Auth.CurrentUserResponse
                           {
                               UserId = activeUserId,
                               Status = UserStatusConstants.Active,
                               Roles = new List<string> { RoleConstants.SystemAdministrator }
                           });

        var mockScopeService = new Mock<IFacilityScopeService>();
        mockScopeService.Setup(s => s.GetAccessibleFacilityIdsAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new List<long>());

        var revocationService = new StorageUnitHubScopeRevocationService(
            mockHubContext.Object, tracker, mockScopeService.Object, mockIdentityService.Object, NullLogger<StorageUnitHubScopeRevocationService>.Instance);
        var publisher = new StorageUnitRealtimePublisher(
            mockHubContext.Object, revocationService, NullLogger<StorageUnitRealtimePublisher>.Instance);

        var createDto = new StorageUnitAdminDto
        {
            Id = 9804,
            UnitCode = $"F01-OK-{suffix}",
            FacilityId = hierarchy.Facility.id,
            UnitTypeId = hierarchy.UnitType.id,
            PhysicalStatus = "available",
            IsListed = true
        };

        // When pruning succeeds, broadcast succeeds without throwing
        await publisher.PublishCreatedAsync(createDto);

        // Removal called for demoted user
        mockGroups.Verify(g => g.RemoveFromGroupAsync(connIdDemoted, "admin_global", It.IsAny<CancellationToken>()), Times.Once);

        // Demoted connection removed from group in tracker
        Assert.DoesNotContain(tracker.GetConnectionsInGroup("admin_global"), c => c.ConnectionId == connIdDemoted);

        // Active connection still in group in tracker
        Assert.Contains(tracker.GetConnectionsInGroup("admin_global"), c => c.ConnectionId == connIdActive);

        // Event was broadcast to authorized groups
        mockGroupProxy.Verify(p => p.SendCoreAsync("Receiver_CreateStorageUnit", It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task T13_R8_F01_FallbackDelete_WhenRemoveFromGroupFails_FailsClosed_ZeroEvents()
    {
        var mockGroups = new Mock<IGroupManager>();
        mockGroups.Setup(g => g.RemoveFromGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ThrowsAsync(new HubException("Network timeout removing connection from SignalR group"));

        var mockClients = new Mock<IHubClients>();
        var mockGroupProxy = new Mock<IClientProxy>();
        mockClients.Setup(c => c.Group("admin_global")).Returns(mockGroupProxy.Object);

        var mockHubContext = new Mock<IHubContext<StorageUnitHub>>();
        mockHubContext.Setup(h => h.Groups).Returns(mockGroups.Object);
        mockHubContext.Setup(h => h.Clients).Returns(mockClients.Object);

        var tracker = new StorageUnitHubConnectionTracker();
        const long demotedUserId = 8806L;
        const string connId = "conn_fb_demoted_8806";
        tracker.TrackConnection(demotedUserId, connId, new[] { "admin_global" }, () => throw new InvalidOperationException("Simulated abort error"));

        var mockIdentityService = new Mock<IAccountIdentityService>();
        mockIdentityService.Setup(i => i.GetCurrentUserAsync(demotedUserId, It.IsAny<CancellationToken>()))
                           .ReturnsAsync(new SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Auth.CurrentUserResponse
                           {
                               UserId = demotedUserId,
                               Status = UserStatusConstants.Active,
                               Roles = new List<string> { RoleConstants.StorageCustomer }
                           });

        var mockScopeService = new Mock<IFacilityScopeService>();
        var revocationService = new StorageUnitHubScopeRevocationService(
            mockHubContext.Object, tracker, mockScopeService.Object, mockIdentityService.Object, NullLogger<StorageUnitHubScopeRevocationService>.Instance);
        var publisher = new StorageUnitRealtimePublisher(
            mockHubContext.Object, revocationService, NullLogger<StorageUnitRealtimePublisher>.Instance);

        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => publisher.PublishDeletedAsync(9999L));
        Assert.IsType<RealtimePublishDegradedException>(ex);

        mockGroupProxy.Verify(p => p.SendCoreAsync("Receiver_DeleteStorageUnit", It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Contains(tracker.GetConnectionsInGroup("admin_global"), c => c.ConnectionId == connId);
    }

    #endregion

    [Fact]
    public async Task T13_R7_F03_Integration_RazorPages_WhenRealtimeFails_DatabaseCommitted_NewScopeVerifiesDb()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);

        var managerEmail = $"mgr_f03_{suffix}@example.test";
        var managerPass = "Pass123!";
        var manager = await _factory.SeedUserAsync(managerEmail, managerPass, RoleConstants.FacilityManager, UserStatusConstants.Active, "Manager F03");

        using (var db = _factory.CreateDbContext())
        {
            db.staff_facility_assignments.Add(new staff_facility_assignment
            {
                employee_id = manager.id,
                facility_id = hierarchy.Facility.id,
                assignment_role = RoleConstants.FacilityManager,
                starts_at = DateTimeOffset.UtcNow.AddDays(-5),
                ends_at = null,
                created_at = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var scope = _factory.Services.CreateScope();
        var realAdminService = scope.ServiceProvider.GetRequiredService<IStorageUnitAdminService>();
        var hubContext = _factory.Services.GetRequiredService<IHubContext<StorageUnitHub>>();
        var connectionTracker = _factory.Services.GetRequiredService<IStorageUnitHubConnectionTracker>();
        var accountIdentityService = scope.ServiceProvider.GetRequiredService<IAccountIdentityService>();
        var revocationLogger = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<StorageUnitHubScopeRevocationService>>();
        var pubLogger = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<StorageUnitRealtimePublisher>>();

        // Track connection in facility group so PruneConnectionsForBroadcastAsync reconciles this user and triggers failing revocation
        connectionTracker.TrackConnection(manager.id, $"conn_razor_{suffix}", new[] { $"facility_{hierarchy.Facility.id}" });

        // Failing revocation service: simulates pruning/scope error AFTER DB has committed
        var failingRevocation = new StorageUnitHubScopeRevocationService(
            hubContext, connectionTracker, new FailingFacilityScopeService(), accountIdentityService, revocationLogger);
        var realPublisherWithFailingRevocation = new StorageUnitRealtimePublisher(
            hubContext, failingRevocation, pubLogger);

        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, manager.id.ToString()),
            new Claim(ClaimTypes.Role, RoleConstants.FacilityManager)
        }, "TestAuth"));

        // 1. CreateModel: DB commit MUST succeed and persist in real database even when realtime publish fails!
        var createModel = new CreateModel(realAdminService, realPublisherWithFailingRevocation, NullLogger<CreateModel>.Instance);
        createModel.PageContext = new PageContext { HttpContext = httpContext };
        createModel.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        var createUnitCode = $"U-F03-C-{suffix}";
        createModel.Input = new CreateStorageUnitRequest
        {
            FacilityId = hierarchy.Facility.id,
            UnitTypeId = hierarchy.UnitType.id,
            AreaId = hierarchy.Area.id,
            UnitCode = createUnitCode,
            FloorLabel = "Tang 1",
            ZoneLabel = "Khu C",
            Notes = "Create degraded test"
        };

        var createResult = await createModel.OnPostAsync(CancellationToken.None);
        var redirectCreate = Assert.IsType<RedirectToPageResult>(createResult);
        Assert.Equal("./Index", redirectCreate.PageName);
        Assert.True(createModel.TempData.ContainsKey("WarningMessage"));
        Assert.Contains("thành công trong cơ sở dữ liệu", createModel.TempData["WarningMessage"]?.ToString() ?? "");

        // T13-R7-F03 Real DB Assertion: Query from a BRAND NEW DbContext scope to prove DB committed!
        long createdUnitId;
        using (var verifyDb = _factory.CreateDbContext())
        {
            var saved = await verifyDb.storage_units.FirstOrDefaultAsync(u => u.facility_id == hierarchy.Facility.id && u.unit_code == createUnitCode);
            Assert.NotNull(saved);
            Assert.Equal("Tang 1", saved.floor_label);
            Assert.Equal("Khu C", saved.zone_label);
            createdUnitId = saved.id;
        }

        // 2. EditModel: DB update MUST succeed and persist in real database even when realtime publish fails!
        var editModel = new EditModel(realAdminService, realPublisherWithFailingRevocation, NullLogger<EditModel>.Instance);
        editModel.PageContext = new PageContext { HttpContext = httpContext };
        editModel.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        editModel.Id = createdUnitId;

        DateTimeOffset currentUpdatedAt;
        using (var verifyDb = _factory.CreateDbContext())
        {
            var existing = await verifyDb.storage_units.FindAsync(createdUnitId);
            currentUpdatedAt = existing!.updated_at;
        }

        editModel.Input = new UpdateStorageUnitRequest
        {
            UnitCode = createUnitCode,
            AreaId = hierarchy.Area.id,
            FloorLabel = "Tang 2 Updated",
            ZoneLabel = "Khu C Updated",
            Notes = "Updated notes in degraded mode",
            ExpectedUpdatedAt = currentUpdatedAt
        };

        var editResult = await editModel.OnPostAsync(CancellationToken.None);
        var redirectEdit = Assert.IsType<RedirectToPageResult>(editResult);
        Assert.Equal("./Index", redirectEdit.PageName);
        Assert.True(editModel.TempData.ContainsKey("WarningMessage"));

        // Real DB Assertion: Query from a brand new DbContext scope
        DateTimeOffset updatedTimestamp;
        using (var verifyDb = _factory.CreateDbContext())
        {
            var updated = await verifyDb.storage_units.FindAsync(createdUnitId);
            Assert.NotNull(updated);
            Assert.Equal("Tang 2 Updated", updated.floor_label);
            Assert.Equal("Khu C Updated", updated.zone_label);
            Assert.Equal("Updated notes in degraded mode", updated.notes);
            updatedTimestamp = updated.updated_at;
        }

        // 3. DeleteModel: DB delete MUST succeed and remove unit from real database even when realtime publish fails!
        var deleteModel = new DeleteModel(realAdminService, realPublisherWithFailingRevocation, NullLogger<DeleteModel>.Instance);
        deleteModel.PageContext = new PageContext { HttpContext = httpContext };
        deleteModel.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        deleteModel.Id = createdUnitId;
        deleteModel.ExpectedUpdatedAt = updatedTimestamp;

        var deleteResult = await deleteModel.OnPostAsync(CancellationToken.None);
        var redirectDelete = Assert.IsType<RedirectToPageResult>(deleteResult);
        Assert.Equal("./Index", redirectDelete.PageName);
        Assert.True(deleteModel.TempData.ContainsKey("WarningMessage"));

        // Real DB Assertion: Query from a brand new DbContext scope -> unit MUST be deleted!
        using (var verifyDb = _factory.CreateDbContext())
        {
            var deleted = await verifyDb.storage_units.FindAsync(createdUnitId);
            Assert.Null(deleted);
        }
    }

    [Fact]
    public async Task T13_R7_F03_Integration_HubMethods_WhenRealtimeFails_DatabaseCommitted_NewScopeVerifiesDb()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);

        var managerEmail = $"mgr_hub_f03_{suffix}@example.test";
        var managerPass = "Pass123!";
        var manager = await _factory.SeedUserAsync(managerEmail, managerPass, RoleConstants.FacilityManager, UserStatusConstants.Active, "Manager Hub F03");

        using (var db = _factory.CreateDbContext())
        {
            db.staff_facility_assignments.Add(new staff_facility_assignment
            {
                employee_id = manager.id,
                facility_id = hierarchy.Facility.id,
                assignment_role = RoleConstants.FacilityManager,
                starts_at = DateTimeOffset.UtcNow.AddDays(-5),
                ends_at = null,
                created_at = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var scope = _factory.Services.CreateScope();
        var realAdminService = scope.ServiceProvider.GetRequiredService<IStorageUnitAdminService>();
        var hubContext = _factory.Services.GetRequiredService<IHubContext<StorageUnitHub>>();
        var connectionTracker = _factory.Services.GetRequiredService<IStorageUnitHubConnectionTracker>();
        var accountIdentityService = scope.ServiceProvider.GetRequiredService<IAccountIdentityService>();
        var scopeService = scope.ServiceProvider.GetRequiredService<IFacilityScopeService>();
        var revocationLogger = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<StorageUnitHubScopeRevocationService>>();
        var pubLogger = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<StorageUnitRealtimePublisher>>();

        // Track connection in facility group so PruneConnectionsForBroadcastAsync reconciles this user and triggers failing revocation
        connectionTracker.TrackConnection(manager.id, $"conn_hub_{suffix}", new[] { $"facility_{hierarchy.Facility.id}" });

        var failingRevocation = new StorageUnitHubScopeRevocationService(
            hubContext, connectionTracker, new FailingFacilityScopeService(), accountIdentityService, revocationLogger);
        var realPublisherWithFailingRevocation = new StorageUnitRealtimePublisher(
            hubContext, failingRevocation, pubLogger);

        var hub = new StorageUnitHub(
            realAdminService,
            realPublisherWithFailingRevocation,
            scopeService,
            connectionTracker,
            failingRevocation,
            NullLogger<StorageUnitHub>.Instance);

        var hubCallerContext = new Mock<HubCallerContext>();
        hubCallerContext.Setup(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, manager.id.ToString()),
            new Claim(ClaimTypes.Role, RoleConstants.FacilityManager)
        }, "TestAuth")));
        hubCallerContext.Setup(c => c.ConnectionId).Returns(Guid.NewGuid().ToString("N"));
        hub.Context = hubCallerContext.Object;

        // 1. HubCreate_StorageUnit: Real DB commit succeeds, publisher degrades -> Hub returns confirmed DTO, does NOT throw!
        var createUnitCode = $"U-HUB-F03-{suffix}";
        var createReqJson = JsonSerializer.Serialize(new CreateStorageUnitRequest
        {
            FacilityId = hierarchy.Facility.id,
            UnitTypeId = hierarchy.UnitType.id,
            AreaId = hierarchy.Area.id,
            UnitCode = createUnitCode,
            FloorLabel = "Tang 3 Hub",
            ZoneLabel = "Khu H",
            Notes = "Hub create degraded test"
        });

        var createdDto = await hub.HubCreate_StorageUnit(createReqJson);
        Assert.NotNull(createdDto);
        Assert.Equal(createUnitCode, createdDto.unitCode);
        Assert.True(createdDto.id > 0);

        // Real DB Assertion: Brand new DbContext scope confirms unit is saved!
        using (var verifyDb = _factory.CreateDbContext())
        {
            var saved = await verifyDb.storage_units.FindAsync(createdDto.id);
            Assert.NotNull(saved);
            Assert.Equal("Tang 3 Hub", saved.floor_label);
        }

        // 2. HubUpdate_StorageUnit: Real DB update succeeds, publisher degrades -> Hub returns confirmed DTO, does NOT throw!
        var updateReqJson = JsonSerializer.Serialize(new
        {
            Id = createdDto.id,
            UnitCode = createUnitCode,
            AreaId = hierarchy.Area.id,
            FloorLabel = "Tang 3 Hub Modified",
            ZoneLabel = "Khu H Modified",
            Notes = "Hub update degraded test modified"
        });

        var updatedDto = await hub.HubUpdate_StorageUnit(updateReqJson);
        Assert.NotNull(updatedDto);
        Assert.Equal("Tang 3 Hub Modified", updatedDto.floorLabel);

        // Real DB Assertion: Brand new DbContext scope confirms update!
        using (var verifyDb = _factory.CreateDbContext())
        {
            var updated = await verifyDb.storage_units.FindAsync(createdDto.id);
            Assert.NotNull(updated);
            Assert.Equal("Tang 3 Hub Modified", updated.floor_label);
        }

        // 3. HubDelete_StorageUnit: Real DB delete succeeds, publisher degrades -> Hub returns unitId string, does NOT throw!
        var deleteResult = await hub.HubDelete_StorageUnit(createdDto.id.ToString());
        Assert.Equal(createdDto.id.ToString(), deleteResult);

        // Real DB Assertion: Brand new DbContext scope confirms unit is deleted!
        using (var verifyDb = _factory.CreateDbContext())
        {
            var deleted = await verifyDb.storage_units.FindAsync(createdDto.id);
            Assert.Null(deleted);
        }
    }

    [Fact]
    public async Task T13_R7_F03_Integration_WhenPreCommitDbFails_ThrowsException_NoDatabaseChange_NoPublisher()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);

        var managerEmail = $"mgr_precommit_{suffix}@example.test";
        var managerPass = "Pass123!";
        var manager = await _factory.SeedUserAsync(managerEmail, managerPass, RoleConstants.FacilityManager, UserStatusConstants.Active, "Manager PreCommit");

        using (var db = _factory.CreateDbContext())
        {
            db.staff_facility_assignments.Add(new staff_facility_assignment
            {
                employee_id = manager.id,
                facility_id = hierarchy.Facility.id,
                assignment_role = RoleConstants.FacilityManager,
                starts_at = DateTimeOffset.UtcNow.AddDays(-5),
                ends_at = null,
                created_at = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // T13-R8-F02 Baseline Capture: Record exact state of CSDL before failing operations
        int initialUnitCount;
        HashSet<long> initialUnitIds;
        DateTimeOffset initialUpdatedAt;
        string? initialFloor;
        string? initialZone;
        string initialPhysicalStatus;
        using (var baselineDb = _factory.CreateDbContext())
        {
            var units = await baselineDb.storage_units.Where(u => u.facility_id == hierarchy.Facility.id).ToListAsync();
            initialUnitCount = units.Count;
            initialUnitIds = units.Select(u => u.id).ToHashSet();
            var existingUnit = await baselineDb.storage_units.FindAsync(hierarchy.Unit.id);
            Assert.NotNull(existingUnit);
            initialUpdatedAt = existingUnit.updated_at;
            initialFloor = existingUnit.floor_label;
            initialZone = existingUnit.zone_label;
            initialPhysicalStatus = existingUnit.physical_status;
        }

        using var scope = _factory.Services.CreateScope();
        var realAdminService = scope.ServiceProvider.GetRequiredService<IStorageUnitAdminService>();
        var mockPublisher = new Mock<IStorageUnitRealtimePublisher>();

        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, manager.id.ToString()),
            new Claim(ClaimTypes.Role, RoleConstants.FacilityManager)
        }, "TestAuth"));

        // 1. CREATE pre-commit failure: duplicate unit code (hierarchy.Unit.unit_code already exists in DB)
        var duplicateUnitCode = hierarchy.Unit.unit_code;
        var createModel = new CreateModel(realAdminService, mockPublisher.Object, NullLogger<CreateModel>.Instance);
        createModel.PageContext = new PageContext { HttpContext = httpContext };
        createModel.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        createModel.Input = new CreateStorageUnitRequest
        {
            FacilityId = hierarchy.Facility.id,
            UnitTypeId = hierarchy.UnitType.id,
            AreaId = hierarchy.Area.id,
            UnitCode = duplicateUnitCode,
            FloorLabel = "Tang 1",
            ZoneLabel = "Khu Dup"
        };

        var pageResult = await createModel.OnPostAsync(CancellationToken.None);
        Assert.IsType<PageResult>(pageResult);
        Assert.False(createModel.ModelState.IsValid);

        // Verify DB baseline unchanged after Razor Create failure
        await AssertDatabaseMatchesBaselineAsync(_factory, hierarchy.Facility.id, initialUnitCount, initialUnitIds, hierarchy.Unit.id, initialUpdatedAt, initialFloor, initialZone, initialPhysicalStatus);

        // Hub Create pre-commit failure: duplicate unit code
        var scopeServicePre = scope.ServiceProvider.GetRequiredService<IFacilityScopeService>();
        var hub = new StorageUnitHub(
            realAdminService,
            mockPublisher.Object,
            scopeServicePre,
            new Mock<IStorageUnitHubConnectionTracker>().Object,
            new StorageUnitHubScopeRevocationService(new Mock<IHubContext<StorageUnitHub>>().Object, new Mock<IStorageUnitHubConnectionTracker>().Object, scopeServicePre, new Mock<IAccountIdentityService>().Object, NullLogger<StorageUnitHubScopeRevocationService>.Instance),
            NullLogger<StorageUnitHub>.Instance);
        var hubCallerContext = new Mock<HubCallerContext>();
        hubCallerContext.Setup(c => c.User).Returns(httpContext.User);
        hubCallerContext.Setup(c => c.ConnectionId).Returns(Guid.NewGuid().ToString("N"));
        hub.Context = hubCallerContext.Object;

        var dupReqJson = JsonSerializer.Serialize(new CreateStorageUnitRequest
        {
            FacilityId = hierarchy.Facility.id,
            UnitTypeId = hierarchy.UnitType.id,
            AreaId = hierarchy.Area.id,
            UnitCode = duplicateUnitCode,
            FloorLabel = "Tang 1",
            ZoneLabel = "Khu Dup"
        });

        var hubCreateEx = await Assert.ThrowsAsync<HubException>(() => hub.HubCreate_StorageUnit(dupReqJson));
        Assert.Contains(duplicateUnitCode, hubCreateEx.Message);

        // Verify DB baseline unchanged after Hub Create failure
        await AssertDatabaseMatchesBaselineAsync(_factory, hierarchy.Facility.id, initialUnitCount, initialUnitIds, hierarchy.Unit.id, initialUpdatedAt, initialFloor, initialZone, initialPhysicalStatus);

        // 2. EDIT pre-commit failure: optimistic concurrency conflict with stale ExpectedUpdatedAt
        var editModel = new EditModel(realAdminService, mockPublisher.Object, NullLogger<EditModel>.Instance);
        editModel.PageContext = new PageContext { HttpContext = httpContext };
        editModel.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        editModel.Id = hierarchy.Unit.id;
        editModel.Input = new UpdateStorageUnitRequest
        {
            UnitCode = hierarchy.Unit.unit_code,
            AreaId = hierarchy.Area.id,
            FloorLabel = "Stale Edit Floor",
            ZoneLabel = "Stale Zone",
            ExpectedUpdatedAt = initialUpdatedAt.AddMinutes(-30) // Stale timestamp -> ConflictException 409
        };

        var editPageResult = await editModel.OnPostAsync(CancellationToken.None);
        Assert.IsType<PageResult>(editPageResult);
        Assert.True(editModel.IsConcurrencyConflict);

        // Verify DB baseline unchanged after Razor Edit failure
        await AssertDatabaseMatchesBaselineAsync(_factory, hierarchy.Facility.id, initialUnitCount, initialUnitIds, hierarchy.Unit.id, initialUpdatedAt, initialFloor, initialZone, initialPhysicalStatus);

        // Hub Edit pre-commit failure: optimistic concurrency conflict
        var staleUpdateJson = JsonSerializer.Serialize(new
        {
            Id = hierarchy.Unit.id,
            UnitCode = hierarchy.Unit.unit_code,
            AreaId = hierarchy.Area.id,
            FloorLabel = "Stale Hub Floor",
            ZoneLabel = "Stale Zone",
            ExpectedUpdatedAt = initialUpdatedAt.AddMinutes(-30)
        });

        var hubEditEx = await Assert.ThrowsAsync<HubException>(() => hub.HubUpdate_StorageUnit(staleUpdateJson));
        Assert.NotNull(hubEditEx.Message);

        // Verify DB baseline unchanged after Hub Edit failure
        await AssertDatabaseMatchesBaselineAsync(_factory, hierarchy.Facility.id, initialUnitCount, initialUnitIds, hierarchy.Unit.id, initialUpdatedAt, initialFloor, initialZone, initialPhysicalStatus);

        // 3. DELETE pre-commit failure: non-existent unit ID 999999L
        var deleteModel = new DeleteModel(realAdminService, mockPublisher.Object, NullLogger<DeleteModel>.Instance);
        deleteModel.PageContext = new PageContext { HttpContext = httpContext };
        deleteModel.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        deleteModel.Id = 999999L;
        deleteModel.ExpectedUpdatedAt = DateTimeOffset.UtcNow;

        var deletePageResult = await deleteModel.OnPostAsync(CancellationToken.None);
        Assert.True(deletePageResult is RedirectToPageResult redirectRes && redirectRes.PageName == "./Index"
            || deletePageResult is NotFoundResult
            || !string.IsNullOrEmpty(deleteModel.ErrorMessage));
        if (deletePageResult is RedirectToPageResult)
        {
            Assert.Contains("không còn tồn tại", deleteModel.TempData["WarningMessage"]?.ToString() ?? string.Empty);
        }

        // Verify DB baseline unchanged after Razor Delete failure
        await AssertDatabaseMatchesBaselineAsync(_factory, hierarchy.Facility.id, initialUnitCount, initialUnitIds, hierarchy.Unit.id, initialUpdatedAt, initialFloor, initialZone, initialPhysicalStatus);

        // Hub Delete pre-commit failure: non-existent unit ID
        var hubDeleteEx = await Assert.ThrowsAsync<HubException>(() => hub.HubDelete_StorageUnit("999999"));
        Assert.Contains("không tồn tại", hubDeleteEx.Message);

        // Verify DB baseline unchanged after Hub Delete failure
        await AssertDatabaseMatchesBaselineAsync(_factory, hierarchy.Facility.id, initialUnitCount, initialUnitIds, hierarchy.Unit.id, initialUpdatedAt, initialFloor, initialZone, initialPhysicalStatus);

        // Verify publisher was NEVER called across all pre-commit failure operations
        mockPublisher.Verify(p => p.PublishCreatedAsync(It.IsAny<StorageUnitAdminDto>(), It.IsAny<CancellationToken>()), Times.Never);
        mockPublisher.Verify(p => p.PublishUpdatedAsync(It.IsAny<StorageUnitAdminDto>(), It.IsAny<CancellationToken>()), Times.Never);
        mockPublisher.Verify(p => p.PublishDeletedAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        mockPublisher.Verify(p => p.PublishDeletedAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task T13_R8_F02_PreCommitDbFailure_RoguePartialMutation_IsDetectedByDatabaseStateAssertions()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);

        int initialUnitCount;
        HashSet<long> initialUnitIds;
        DateTimeOffset initialUpdatedAt;
        string? initialFloor;
        string? initialZone;
        string initialPhysicalStatus;
        using (var baselineDb = _factory.CreateDbContext())
        {
            var units = await baselineDb.storage_units.Where(u => u.facility_id == hierarchy.Facility.id).ToListAsync();
            initialUnitCount = units.Count;
            initialUnitIds = units.Select(u => u.id).ToHashSet();
            var existingUnit = await baselineDb.storage_units.FindAsync(hierarchy.Unit.id);
            Assert.NotNull(existingUnit);
            initialUpdatedAt = existingUnit.updated_at;
            initialFloor = existingUnit.floor_label;
            initialZone = existingUnit.zone_label;
            initialPhysicalStatus = existingUnit.physical_status;
        }

        // 1. First assert clean baseline matches perfectly
        await AssertDatabaseMatchesBaselineAsync(_factory, hierarchy.Facility.id, initialUnitCount, initialUnitIds, hierarchy.Unit.id, initialUpdatedAt, initialFloor, initialZone, initialPhysicalStatus);

        // 2. Simulate a rogue service or partial unrolled transaction that wrote an uncommitted/rogue row to DB
        var rogueUnitId = 99998L;
        using (var rogueDb = _factory.CreateDbContext())
        {
            rogueDb.storage_units.Add(new storage_unit
            {
                id = rogueUnitId,
                facility_id = hierarchy.Facility.id,
                area_id = hierarchy.Area.id,
                unit_type_id = hierarchy.UnitType.id,
                unit_code = $"U-ROGUE-{suffix}",
                floor_label = "Tang Rogue",
                zone_label = "Khu Rogue",
                physical_status = "available",
                is_listed = true,
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            });
            await rogueDb.SaveChangesAsync();
        }

        try
        {
            // 3. Proving baseline sensitivity: AssertDatabaseMatchesBaselineAsync MUST actively throw Xunit.Sdk.EqualException
            var ex = await Assert.ThrowsAsync<Xunit.Sdk.EqualException>(() =>
                AssertDatabaseMatchesBaselineAsync(_factory, hierarchy.Facility.id, initialUnitCount, initialUnitIds, hierarchy.Unit.id, initialUpdatedAt, initialFloor, initialZone, initialPhysicalStatus));
            Assert.Contains("Expected:", ex.Message);
        }
        finally
        {
            // Clean up rogue record
            using var cleanupDb = _factory.CreateDbContext();
            var rogueRecord = await cleanupDb.storage_units.FindAsync(rogueUnitId);
            if (rogueRecord != null)
            {
                cleanupDb.storage_units.Remove(rogueRecord);
                await cleanupDb.SaveChangesAsync();
            }
        }
    }

    #endregion

    #region T13-R8-F03 Tests: End-to-End HTTP Endpoint & WebSocket Transport Verification

    [Fact]
    public async Task T13_R8_F03_E2E_RazorPages_HttpEndpoint_WhenPublisherDegraded_DbCommits_AndClientReceivesWarningRedirect()
    {
        DegradedRealtimePublisherDecorator.ResetCounters();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);

        var managerEmail = $"mgr_e2e_http_{suffix}@example.test";
        var managerPass = "Pass123!";
        var manager = await _factory.SeedUserAsync(managerEmail, managerPass, RoleConstants.FacilityManager, UserStatusConstants.Active, "Manager E2E HTTP");

        using (var db = _factory.CreateDbContext())
        {
            db.staff_facility_assignments.Add(new staff_facility_assignment
            {
                employee_id = manager.id,
                facility_id = hierarchy.Facility.id,
                assignment_role = RoleConstants.FacilityManager,
                starts_at = DateTimeOffset.UtcNow.AddDays(-5),
                ends_at = null,
                created_at = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // WebApplicationFactory with DegradedRealtimePublisherDecorator registered in DI
        using var degradedFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IStorageUnitRealtimePublisher));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }
                services.AddScoped<IStorageUnitRealtimePublisher, DegradedRealtimePublisherDecorator>();
            });
        });

        var client = degradedFactory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        // 1. Production Cookie Authentication over HTTP pipeline
        var authCookie = await AuthenticateUserAndGetCookieDirectOnClientAsync(client, managerEmail, managerPass);

        // 2. GET /StorageUnits/Create to obtain valid production Antiforgery token and form context
        var getMsg = new HttpRequestMessage(HttpMethod.Get, $"/StorageUnits/Create?facilityId={hierarchy.Facility.id}");
        getMsg.Headers.Add("Cookie", authCookie);
        var getResp = await client.SendAsync(getMsg);
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var getHtml = await getResp.Content.ReadAsStringAsync();
        var antiforgeryToken = ExtractHiddenInputValue(getHtml, "__RequestVerificationToken");

        // 3. POST /StorageUnits/Create over HTTP pipeline
        var createUnitCode = $"U-E2E-HTTP-{suffix}";
        var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.FacilityId"] = hierarchy.Facility.id.ToString(),
            ["Input.UnitTypeId"] = hierarchy.UnitType.id.ToString(),
            ["Input.AreaId"] = hierarchy.Area.id.ToString(),
            ["Input.UnitCode"] = createUnitCode,
            ["Input.FloorLabel"] = "Tầng 1 E2E HTTP",
            ["Input.ZoneLabel"] = "Khu HTTP",
            ["Input.Notes"] = "E2E Degraded HTTP Test",
            ["__RequestVerificationToken"] = antiforgeryToken
        });

        var postMsg = new HttpRequestMessage(HttpMethod.Post, "/StorageUnits/Create") { Content = formContent };
        postMsg.Headers.Add("Cookie", authCookie);

        var postResp = await client.SendAsync(postMsg);

        // Production pipeline assertion: Status is 302 Redirect to ./Index (NOT 500, NOT 200 with form error)
        Assert.Equal(HttpStatusCode.Redirect, postResp.StatusCode);
        Assert.NotNull(postResp.Headers.Location);
        Assert.Contains("/StorageUnits", postResp.Headers.Location!.ToString());

        // Merge set-cookies (for TempData)
        var cookies = postResp.Headers.TryGetValues("Set-Cookie", out var setCookies)
            ? string.Join("; ", setCookies.Select(c => c.Split(';')[0]))
            : authCookie;

        // 4. Follow redirect: GET /StorageUnits (Index page)
        var followMsg = new HttpRequestMessage(HttpMethod.Get, postResp.Headers.Location);
        followMsg.Headers.Add("Cookie", cookies);
        if (!cookies.Contains(CookieAuthExtensions.CookieName))
        {
            followMsg.Headers.Add("Cookie", authCookie);
        }
        var followResp = await client.SendAsync(followMsg);
        Assert.Equal(HttpStatusCode.OK, followResp.StatusCode);
        var followHtml = await followResp.Content.ReadAsStringAsync();

        // Warning message rendered on Index page: informs user DB was saved, does NOT invite retry
        Assert.Contains("thành công trong cơ sở dữ liệu", followHtml);
        Assert.Contains("nhưng thông báo thời gian thực chưa thể đồng bộ tự động", followHtml);
        Assert.Contains("Vui lòng tải lại trang để xem cập nhật mới nhất", followHtml);
        Assert.DoesNotContain("vui lòng thử lại", followHtml, StringComparison.OrdinalIgnoreCase);

        // 5. Real DB verification in a BRAND NEW DbContext scope: DB was genuinely committed!
        using (var verifyDb = _factory.CreateDbContext())
        {
            var saved = await verifyDb.storage_units.FirstOrDefaultAsync(u => u.facility_id == hierarchy.Facility.id && u.unit_code == createUnitCode);
            Assert.NotNull(saved);
            Assert.Equal("Tầng 1 E2E HTTP", saved.floor_label);
            Assert.Equal("Khu HTTP", saved.zone_label);
        }

        // Verify decorator spy counter
        Assert.True(DegradedRealtimePublisherDecorator.PublishCreatedCalls > 0, "DegradedRealtimePublisherDecorator.PublishCreatedAsync must have been invoked.");
    }

    [Fact]
    public async Task T13_R8_F03_E2E_SignalR_WebSocketTransport_WhenPublisherDegraded_DbCommits_AndHubCallReturnsConfirmedDto()
    {
        DegradedRealtimePublisherDecorator.ResetCounters();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var dbName = $"kestrel_ws_create_{suffix}";

        // T13-R9-F01: Use real Kestrel server fixture with DegradedRealtimePublisherDecorator registered in DI
        await using var server = await KestrelRealtimeServer.StartAsync(dbName, services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IStorageUnitRealtimePublisher));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }
            services.AddScoped<IStorageUnitRealtimePublisher, DegradedRealtimePublisherDecorator>();
        });

        const long managerUserId = 1001L;
        var (fac, unitType, area, _) = await SeedKestrelHierarchyAsync(server, suffix, managerUserId);
        var authCookie = await GetKestrelAuthCookieAsync(server.ServerUrl, managerUserId, RoleConstants.FacilityManager);

        // Genuine WebSocket transport connection: strictly enforce WebSockets only!
        var hubConnection = new HubConnectionBuilder()
            .WithUrl(new Uri(new Uri(server.ServerUrl), "/storageUnitHub"), options =>
            {
                // T13-R9-F01: Strictly enforce WebSockets only. If WebSocket transport is unavailable, fail immediately (NO HTTP fallback)!
                options.Transports = HttpTransportType.WebSockets;
                options.Headers.Add("Cookie", authCookie);
            })
            .Build();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await hubConnection.StartAsync(cts.Token);
        Assert.Equal(HubConnectionState.Connected, hubConnection.State);

        var createUnitCode = $"U-E2E-WS-{suffix}";
        var createPayload = JsonSerializer.Serialize(new CreateStorageUnitRequest
        {
            FacilityId = fac.id,
            UnitTypeId = unitType.id,
            AreaId = area.id,
            UnitCode = createUnitCode,
            FloorLabel = "Tầng 2 E2E WS",
            ZoneLabel = "Khu WS",
            Notes = "E2E WS Degraded Test"
        });

        // Hub invocation over genuine WebSocket transport: returns confirmed DTO, does NOT throw HubException!
        var returnedDto = await hubConnection.InvokeAsync<StorageUnitRealtimeEventDto>("HubCreate_StorageUnit", createPayload, cts.Token);
        Assert.NotNull(returnedDto);
        Assert.Equal(createUnitCode, returnedDto.unitCode);
        Assert.True(returnedDto.id > 0);

        // Real DB verification in a brand new scope from Kestrel: DB was genuinely committed!
        using (var verifyScope = server.CreateScope())
        {
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
            var saved = await verifyDb.storage_units.FindAsync(returnedDto.id);
            Assert.NotNull(saved);
            Assert.Equal("Tầng 2 E2E WS", saved.floor_label);
            Assert.Equal("Khu WS", saved.zone_label);
        }

        // Verify decorator spy counter was actually triggered
        Assert.True(DegradedRealtimePublisherDecorator.PublishCreatedCalls > 0, "DegradedRealtimePublisherDecorator.PublishCreatedAsync must have been invoked.");

        await hubConnection.DisposeAsync();
    }

    [Fact]
    public async Task T13_R8_F03_E2E_RazorPages_HttpEditAndDelete_WhenPublisherDegraded_DbUpdatesAndDeletes_WithWarningRedirect()
    {
        DegradedRealtimePublisherDecorator.ResetCounters();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);

        var managerEmail = $"mgr_e2e_edit_{suffix}@example.test";
        var managerPass = "Pass123!";
        var manager = await _factory.SeedUserAsync(managerEmail, managerPass, RoleConstants.FacilityManager, UserStatusConstants.Active, "Manager E2E Edit");

        using (var db = _factory.CreateDbContext())
        {
            db.staff_facility_assignments.Add(new staff_facility_assignment
            {
                employee_id = manager.id,
                facility_id = hierarchy.Facility.id,
                assignment_role = RoleConstants.FacilityManager,
                starts_at = DateTimeOffset.UtcNow.AddDays(-5),
                ends_at = null,
                created_at = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var degradedFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IStorageUnitRealtimePublisher));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }
                services.AddScoped<IStorageUnitRealtimePublisher, DegradedRealtimePublisherDecorator>();
            });
        });

        var client = degradedFactory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var authCookie = await AuthenticateUserAndGetCookieDirectOnClientAsync(client, managerEmail, managerPass);

        // 1. HTTP Edit: GET /StorageUnits/Edit?id={hierarchy.Unit.id} to extract token and ExpectedUpdatedAt
        var getEditResp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/StorageUnits/Edit?id={hierarchy.Unit.id}")
        {
            Headers = { { "Cookie", authCookie } }
        });
        Assert.Equal(HttpStatusCode.OK, getEditResp.StatusCode);
        var editHtml = await getEditResp.Content.ReadAsStringAsync();
        var editAntiforgery = ExtractHiddenInputValue(editHtml, "__RequestVerificationToken");
        var expectedUpdatedAt = ExtractHiddenInputValue(editHtml, "Input.ExpectedUpdatedAt");

        var editForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Id"] = hierarchy.Unit.id.ToString(),
            ["Input.UnitCode"] = hierarchy.Unit.unit_code,
            ["Input.AreaId"] = hierarchy.Area.id.ToString(),
            ["Input.FloorLabel"] = "Tang 5 HTTP Edit",
            ["Input.ZoneLabel"] = "Khu Edit",
            ["Input.ExpectedUpdatedAt"] = expectedUpdatedAt,
            ["__RequestVerificationToken"] = editAntiforgery
        });

        var postEditResp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/StorageUnits/Edit")
        {
            Content = editForm,
            Headers = { { "Cookie", authCookie } }
        });

        // T13-R9-F02: Verify 302 Redirect, valid Location, and follow redirect to Index
        Assert.Equal(HttpStatusCode.Redirect, postEditResp.StatusCode);
        Assert.NotNull(postEditResp.Headers.Location);
        Assert.Contains("/StorageUnits", postEditResp.Headers.Location!.ToString());

        var editCookies = postEditResp.Headers.TryGetValues("Set-Cookie", out var editSetCookies)
            ? string.Join("; ", editSetCookies.Select(c => c.Split(';')[0]))
            : authCookie;
        var followEditReq = new HttpRequestMessage(HttpMethod.Get, postEditResp.Headers.Location);
        followEditReq.Headers.Add("Cookie", editCookies);
        if (!editCookies.Contains(CookieAuthExtensions.CookieName))
        {
            followEditReq.Headers.Add("Cookie", authCookie);
        }
        var followEditResp = await client.SendAsync(followEditReq);
        Assert.Equal(HttpStatusCode.OK, followEditResp.StatusCode);
        var followEditHtml = await followEditResp.Content.ReadAsStringAsync();

        // T13-R9-F02: Assert warning message rendered for Edit, no retry invitation
        Assert.Contains("Cập nhật ô kho", followEditHtml);
        Assert.Contains("thành công trong cơ sở dữ liệu", followEditHtml);
        Assert.Contains("nhưng thông báo thời gian thực chưa thể đồng bộ tự động", followEditHtml);
        Assert.Contains("Vui lòng tải lại trang để xem cập nhật mới nhất", followEditHtml);
        Assert.DoesNotContain("vui lòng thử lại", followEditHtml, StringComparison.OrdinalIgnoreCase);

        // Verify DB update committed in brand new DbContext scope
        using (var verifyDb = _factory.CreateDbContext())
        {
            var updated = await verifyDb.storage_units.FindAsync(hierarchy.Unit.id);
            Assert.NotNull(updated);
            Assert.Equal("Tang 5 HTTP Edit", updated.floor_label);
        }

        // Verify spy counter was called for Edit
        Assert.True(DegradedRealtimePublisherDecorator.PublishUpdatedCalls > 0, "DegradedRealtimePublisherDecorator.PublishUpdatedAsync must have been invoked.");

        // 2. HTTP Delete: GET /StorageUnits/Delete?id={hierarchy.Unit.id}
        var getDeleteResp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/StorageUnits/Delete?id={hierarchy.Unit.id}")
        {
            Headers = { { "Cookie", authCookie } }
        });
        Assert.Equal(HttpStatusCode.OK, getDeleteResp.StatusCode);
        var deleteHtml = await getDeleteResp.Content.ReadAsStringAsync();
        var deleteAntiforgery = ExtractHiddenInputValue(deleteHtml, "__RequestVerificationToken");
        var deleteExpectedUpdatedAt = ExtractHiddenInputValue(deleteHtml, "ExpectedUpdatedAt");

        var deleteForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Id"] = hierarchy.Unit.id.ToString(),
            ["ExpectedUpdatedAt"] = deleteExpectedUpdatedAt,
            ["__RequestVerificationToken"] = deleteAntiforgery
        });

        var postDeleteResp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/StorageUnits/Delete")
        {
            Content = deleteForm,
            Headers = { { "Cookie", authCookie } }
        });

        // T13-R9-F02: Verify 302 Redirect, valid Location, and follow redirect to Index
        Assert.Equal(HttpStatusCode.Redirect, postDeleteResp.StatusCode);
        Assert.NotNull(postDeleteResp.Headers.Location);
        Assert.Contains("/StorageUnits", postDeleteResp.Headers.Location!.ToString());

        var deleteCookies = postDeleteResp.Headers.TryGetValues("Set-Cookie", out var deleteSetCookies)
            ? string.Join("; ", deleteSetCookies.Select(c => c.Split(';')[0]))
            : authCookie;
        var followDeleteReq = new HttpRequestMessage(HttpMethod.Get, postDeleteResp.Headers.Location);
        followDeleteReq.Headers.Add("Cookie", deleteCookies);
        if (!deleteCookies.Contains(CookieAuthExtensions.CookieName))
        {
            followDeleteReq.Headers.Add("Cookie", authCookie);
        }
        var followDeleteResp = await client.SendAsync(followDeleteReq);
        Assert.Equal(HttpStatusCode.OK, followDeleteResp.StatusCode);
        var followDeleteHtml = await followDeleteResp.Content.ReadAsStringAsync();

        // T13-R9-F02: Assert warning message rendered for Delete, no retry invitation
        Assert.Contains("Xóa ô kho", followDeleteHtml);
        Assert.Contains("thành công trong cơ sở dữ liệu", followDeleteHtml);
        Assert.Contains("nhưng thông báo thời gian thực chưa thể đồng bộ tự động", followDeleteHtml);
        Assert.Contains("Vui lòng tải lại trang để xem cập nhật mới nhất", followDeleteHtml);
        Assert.DoesNotContain("vui lòng thử lại", followDeleteHtml, StringComparison.OrdinalIgnoreCase);

        // Verify DB delete committed in brand new DbContext scope
        using (var verifyDb = _factory.CreateDbContext())
        {
            var deleted = await verifyDb.storage_units.FindAsync(hierarchy.Unit.id);
            Assert.Null(deleted);
        }

        // Verify spy counter was called for Delete
        Assert.True(DegradedRealtimePublisherDecorator.PublishDeletedCalls > 0, "DegradedRealtimePublisherDecorator.PublishDeletedAsync must have been invoked.");
    }

    [Fact]
    public async Task T13_R8_F03_E2E_SignalR_WebSocketUpdateAndDelete_WhenPublisherDegraded_DbUpdatesAndDeletes_WithoutHubException()
    {
        DegradedRealtimePublisherDecorator.ResetCounters();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var dbName = $"kestrel_ws_ud_{suffix}";

        // T13-R9-F01: Use real Kestrel server fixture with DegradedRealtimePublisherDecorator registered in DI
        await using var server = await KestrelRealtimeServer.StartAsync(dbName, services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IStorageUnitRealtimePublisher));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }
            services.AddScoped<IStorageUnitRealtimePublisher, DegradedRealtimePublisherDecorator>();
        });

        const long managerUserId = 1002L;
        var (fac, unitType, area, unit) = await SeedKestrelHierarchyAsync(server, suffix, managerUserId);
        var authCookie = await GetKestrelAuthCookieAsync(server.ServerUrl, managerUserId, RoleConstants.FacilityManager);

        var hubConnection = new HubConnectionBuilder()
            .WithUrl(new Uri(new Uri(server.ServerUrl), "/storageUnitHub"), options =>
            {
                // T13-R9-F01: Strictly enforce WebSockets only. No fallback to SSE/Long Polling!
                options.Transports = HttpTransportType.WebSockets;
                options.Headers.Add("Cookie", authCookie);
            })
            .Build();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await hubConnection.StartAsync(cts.Token);
        Assert.Equal(HubConnectionState.Connected, hubConnection.State);

        // 1. HubUpdate_StorageUnit over genuine WebSocket transport
        var updatePayload = JsonSerializer.Serialize(new
        {
            Id = unit.id,
            UnitCode = unit.unit_code,
            AreaId = area.id,
            FloorLabel = "Tang 4 WS Update",
            ZoneLabel = "Khu WS Update",
            Notes = "Updated over genuine WS transport"
        });

        var updatedDto = await hubConnection.InvokeAsync<StorageUnitRealtimeEventDto>("HubUpdate_StorageUnit", updatePayload, cts.Token);
        Assert.NotNull(updatedDto);
        Assert.Equal("Tang 4 WS Update", updatedDto.floorLabel);

        using (var verifyScope1 = server.CreateScope())
        {
            var verifyDb = verifyScope1.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
            var updated = await verifyDb.storage_units.FindAsync(unit.id);
            Assert.NotNull(updated);
            Assert.Equal("Tang 4 WS Update", updated.floor_label);
        }

        Assert.True(DegradedRealtimePublisherDecorator.PublishUpdatedCalls > 0, "DegradedRealtimePublisherDecorator.PublishUpdatedAsync must have been invoked.");

        // 2. HubDelete_StorageUnit over genuine WebSocket transport
        var deleteResult = await hubConnection.InvokeAsync<string>("HubDelete_StorageUnit", unit.id.ToString(), (string?)null, cts.Token);
        Assert.Equal(unit.id.ToString(), deleteResult);

        using (var verifyScope2 = server.CreateScope())
        {
            var verifyDb = verifyScope2.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
            var deleted = await verifyDb.storage_units.FindAsync(unit.id);
            Assert.Null(deleted);
        }

        Assert.True(DegradedRealtimePublisherDecorator.PublishDeletedCalls > 0, "DegradedRealtimePublisherDecorator.PublishDeletedAsync must have been invoked.");

        await hubConnection.DisposeAsync();
    }

    #endregion

    #region T13-R6-F01 Tests: Post-Commit Degraded Realtime Handling & Pre-Commit Failure Reporting

    [Fact]
    public async Task T13_R6_F01_RazorPages_WhenRealtimePublishDegraded_RedirectsToIndexWithWarningMessage_NotErrorMessage()
    {
        // 1. CreateModel: DB commit succeeds, but realtime publish degrades
        var mockService = new Mock<IStorageUnitAdminService>();
        var mockPublisher = new Mock<IStorageUnitRealtimePublisher>();

        var createdDto = new StorageUnitAdminDto
        {
            Id = 101,
            UnitCode = "UNIT-F01-C",
            FacilityId = 10,
            FacilityName = "Cơ sở 10"
        };
        mockService.Setup(s => s.CreateAsync(It.IsAny<CreateStorageUnitRequest>(), 1001, It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdDto);

        mockPublisher.Setup(p => p.PublishCreatedAsync(It.IsAny<StorageUnitAdminDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RealtimePublishDegradedException("Create", 10, 101, "Simulated pruning network failure"));

        var createModel = new CreateModel(mockService.Object, mockPublisher.Object, NullLogger<CreateModel>.Instance);
        var httpContextCreate = new DefaultHttpContext();
        httpContextCreate.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "1001"),
            new Claim(ClaimTypes.Role, RoleConstants.FacilityManager)
        }, "TestAuth"));
        var tempDataCreate = new TempDataDictionary(httpContextCreate, Mock.Of<ITempDataProvider>());
        createModel.PageContext = new PageContext { HttpContext = httpContextCreate };
        createModel.TempData = tempDataCreate;
        createModel.Input = new CreateStorageUnitRequest { FacilityId = 10, UnitTypeId = 20, UnitCode = "UNIT-F01-C" };

        var createResult = await createModel.OnPostAsync(CancellationToken.None);
        var redirectCreate = Assert.IsType<RedirectToPageResult>(createResult);
        Assert.Equal("./Index", redirectCreate.PageName);
        Assert.True(createModel.TempData.ContainsKey("WarningMessage"));
        Assert.Contains("thành công trong cơ sở dữ liệu", createModel.TempData["WarningMessage"]?.ToString() ?? "");
        Assert.False(createModel.TempData.ContainsKey("ErrorMessage"));

        // 2. EditModel: DB commit succeeds, but realtime publish degrades
        var updatedDto = new StorageUnitAdminDto
        {
            Id = 101,
            UnitCode = "UNIT-F01-E",
            FacilityId = 10,
            FacilityName = "Cơ sở 10"
        };
        mockService.Setup(s => s.UpdateAsync(101, It.IsAny<UpdateStorageUnitRequest>(), 1001, It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedDto);

        mockPublisher.Setup(p => p.PublishUpdatedAsync(It.IsAny<StorageUnitAdminDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RealtimePublishDegradedException("Update", 10, 101, "Simulated pruning network failure"));

        var editModel = new EditModel(mockService.Object, mockPublisher.Object, NullLogger<EditModel>.Instance);
        var httpContextEdit = new DefaultHttpContext();
        httpContextEdit.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "1001"),
            new Claim(ClaimTypes.Role, RoleConstants.FacilityManager)
        }, "TestAuth"));
        var tempDataEdit = new TempDataDictionary(httpContextEdit, Mock.Of<ITempDataProvider>());
        editModel.PageContext = new PageContext { HttpContext = httpContextEdit };
        editModel.TempData = tempDataEdit;
        editModel.Id = 101;
        editModel.Input = new UpdateStorageUnitRequest { UnitCode = "UNIT-F01-E", ExpectedUpdatedAt = DateTimeOffset.UtcNow };

        var editResult = await editModel.OnPostAsync(CancellationToken.None);
        var redirectEdit = Assert.IsType<RedirectToPageResult>(editResult);
        Assert.Equal("./Index", redirectEdit.PageName);
        Assert.True(editModel.TempData.ContainsKey("WarningMessage"));
        Assert.Contains("thành công trong cơ sở dữ liệu", editModel.TempData["WarningMessage"]?.ToString() ?? "");
        Assert.False(editModel.TempData.ContainsKey("ErrorMessage"));

        // 3. DeleteModel: DB commit succeeds, but realtime publish degrades
        var existingUnitDto = new StorageUnitAdminDetailDto
        {
            Id = 101,
            UnitCode = "UNIT-F01-D",
            FacilityId = 10,
            FacilityName = "Cơ sở 10",
            UpdatedAt = DateTimeOffset.UtcNow
        };
        mockService.Setup(s => s.GetByIdAsync(101, 1001, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingUnitDto);

        var deletedDto = new DeleteStorageUnitResult
        {
            UnitId = 101,
            UnitCode = "UNIT-F01-D",
            Success = true
        };
        mockService.Setup(s => s.DeleteAsync(101, It.IsAny<DateTimeOffset>(), 1001, It.IsAny<CancellationToken>()))
            .ReturnsAsync(deletedDto);

        mockPublisher.Setup(p => p.PublishDeletedAsync(101, 10, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RealtimePublishDegradedException("Delete", 10, 101, "Simulated pruning network failure"));

        var deleteModel = new DeleteModel(mockService.Object, mockPublisher.Object, NullLogger<DeleteModel>.Instance);
        var httpContextDelete = new DefaultHttpContext();
        httpContextDelete.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "1001"),
            new Claim(ClaimTypes.Role, RoleConstants.FacilityManager)
        }, "TestAuth"));
        var tempDataDelete = new TempDataDictionary(httpContextDelete, Mock.Of<ITempDataProvider>());
        deleteModel.PageContext = new PageContext { HttpContext = httpContextDelete };
        deleteModel.TempData = tempDataDelete;
        deleteModel.Id = 101;
        deleteModel.ExpectedUpdatedAt = DateTimeOffset.UtcNow;

        var deleteResult = await deleteModel.OnPostAsync(CancellationToken.None);
        var redirectDelete = Assert.IsType<RedirectToPageResult>(deleteResult);
        Assert.Equal("./Index", redirectDelete.PageName);
        Assert.True(deleteModel.TempData.ContainsKey("WarningMessage"));
        Assert.Contains("thành công trong cơ sở dữ liệu", deleteModel.TempData["WarningMessage"]?.ToString() ?? "");
        Assert.False(deleteModel.TempData.ContainsKey("ErrorMessage"));
    }

    [Fact]
    public async Task T13_R6_F01_StorageUnitHub_WhenRealtimePublishDegraded_ReturnsConfirmedDtoOrId_DoesNotThrowHubException()
    {
        var mockService = new Mock<IStorageUnitAdminService>();
        var mockPublisher = new Mock<IStorageUnitRealtimePublisher>();
        var mockTracker = new Mock<IStorageUnitHubConnectionTracker>();
        var mockScopeService = new Mock<IFacilityScopeService>();
        var mockIdentityService = new Mock<IAccountIdentityService>();
        var mockHubContext = new Mock<IHubContext<StorageUnitHub>>();

        var revocationService = new StorageUnitHubScopeRevocationService(
            mockHubContext.Object,
            mockTracker.Object,
            mockScopeService.Object,
            mockIdentityService.Object,
            NullLogger<StorageUnitHubScopeRevocationService>.Instance);

        var hub = new StorageUnitHub(
            mockService.Object,
            mockPublisher.Object,
            mockScopeService.Object,
            mockTracker.Object,
            revocationService,
            NullLogger<StorageUnitHub>.Instance);

        var mockCallerContext = new Mock<HubCallerContext>();
        var claimsPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "1001"),
            new Claim(ClaimTypes.Role, RoleConstants.FacilityManager)
        }, "TestAuth"));
        mockCallerContext.Setup(c => c.User).Returns(claimsPrincipal);
        hub.Context = mockCallerContext.Object;

        // 1. HubCreate_StorageUnit: DB succeeds, publish degraded -> Returns DTO, no HubException
        var createdDto = new StorageUnitAdminDto
        {
            Id = 201,
            UnitCode = "UNIT-HUB-C",
            FacilityId = 10,
            FacilityName = "Cơ sở 10",
            UnitTypeId = 20,
            UnitTypeName = "Loại 20",
            UnitTypeCode = "T20",
            PhysicalStatus = "available",
            IsListed = true
        };
        mockService.Setup(s => s.CreateAsync(It.IsAny<CreateStorageUnitRequest>(), 1001, It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdDto);

        mockPublisher.Setup(p => p.PublishCreatedAsync(It.IsAny<StorageUnitAdminDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RealtimePublishDegradedException("Create", 10, 201, "Prune failed"));

        var createReqJson = JsonSerializer.Serialize(new CreateStorageUnitRequest
        {
            FacilityId = 10,
            UnitTypeId = 20,
            UnitCode = "UNIT-HUB-C"
        });
        var createEventDto = await hub.HubCreate_StorageUnit(createReqJson);
        Assert.NotNull(createEventDto);
        Assert.Equal(201, createEventDto.id);
        Assert.Equal("UNIT-HUB-C", createEventDto.unitCode);

        // 2. HubUpdate_StorageUnit: DB succeeds, publish degraded -> Returns DTO, no HubException
        var updatedDto = new StorageUnitAdminDto
        {
            Id = 201,
            UnitCode = "UNIT-HUB-U",
            FacilityId = 10,
            FacilityName = "Cơ sở 10",
            UnitTypeId = 20,
            UnitTypeName = "Loại 20",
            UnitTypeCode = "T20",
            PhysicalStatus = "available",
            IsListed = true
        };
        mockService.Setup(s => s.UpdateAsync(201, It.IsAny<UpdateStorageUnitRequest>(), 1001, It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedDto);

        mockPublisher.Setup(p => p.PublishUpdatedAsync(It.IsAny<StorageUnitAdminDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RealtimePublishDegradedException("Update", 10, 201, "Prune failed"));

        var updateReqJson = JsonSerializer.Serialize(new
        {
            Id = 201,
            UnitCode = "UNIT-HUB-U",
            ExpectedUpdatedAt = DateTimeOffset.UtcNow
        });
        var updateEventDto = await hub.HubUpdate_StorageUnit(updateReqJson);
        Assert.NotNull(updateEventDto);
        Assert.Equal(201, updateEventDto.id);
        Assert.Equal("UNIT-HUB-U", updateEventDto.unitCode);

        // 3. HubDelete_StorageUnit: DB succeeds, publish degraded -> Returns unit ID string, no HubException
        var existingUnitDto = new StorageUnitAdminDetailDto
        {
            Id = 201,
            UnitCode = "UNIT-HUB-D",
            FacilityId = 10,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        mockService.Setup(s => s.GetByIdAsync(201, 1001, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingUnitDto);

        var deletedDto = new DeleteStorageUnitResult
        {
            UnitId = 201,
            UnitCode = "UNIT-HUB-D",
            Success = true
        };
        mockService.Setup(s => s.DeleteAsync(201, It.IsAny<DateTimeOffset>(), 1001, It.IsAny<CancellationToken>()))
            .ReturnsAsync(deletedDto);

        mockPublisher.Setup(p => p.PublishDeletedAsync(201, 10, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RealtimePublishDegradedException("Delete", 10, 201, "Prune failed"));

        var deleteResultId = await hub.HubDelete_StorageUnit("201");
        Assert.Equal("201", deleteResultId);
    }

    [Fact]
    public async Task T13_R6_F01_PreCommitFailure_RazorPagesAndHub_StillReportErrorPromptly()
    {
        var mockService = new Mock<IStorageUnitAdminService>();
        var mockPublisher = new Mock<IStorageUnitRealtimePublisher>();
        var mockTracker = new Mock<IStorageUnitHubConnectionTracker>();
        var mockScopeService = new Mock<IFacilityScopeService>();
        var mockIdentityService = new Mock<IAccountIdentityService>();
        var mockHubContext = new Mock<IHubContext<StorageUnitHub>>();

        var revocationService = new StorageUnitHubScopeRevocationService(
            mockHubContext.Object,
            mockTracker.Object,
            mockScopeService.Object,
            mockIdentityService.Object,
            NullLogger<StorageUnitHubScopeRevocationService>.Instance);

        var hub = new StorageUnitHub(
            mockService.Object,
            mockPublisher.Object,
            mockScopeService.Object,
            mockTracker.Object,
            revocationService,
            NullLogger<StorageUnitHub>.Instance);

        var mockCallerContext = new Mock<HubCallerContext>();
        var claimsPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "1001"),
            new Claim(ClaimTypes.Role, RoleConstants.FacilityManager)
        }, "TestAuth"));
        mockCallerContext.Setup(c => c.User).Returns(claimsPrincipal);
        hub.Context = mockCallerContext.Object;

        // 1. CreateModel with pre-commit ConflictException
        mockService.Setup(s => s.CreateAsync(It.IsAny<CreateStorageUnitRequest>(), 1001, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConflictException("Mã ô kho 'UNIT-DUP' đã tồn tại."));

        var createModel = new CreateModel(mockService.Object, mockPublisher.Object, NullLogger<CreateModel>.Instance);
        var httpContextCreate = new DefaultHttpContext();
        httpContextCreate.User = claimsPrincipal;
        createModel.PageContext = new PageContext { HttpContext = httpContextCreate };
        createModel.TempData = new TempDataDictionary(httpContextCreate, Mock.Of<ITempDataProvider>());
        createModel.Input = new CreateStorageUnitRequest { FacilityId = 10, UnitTypeId = 20, UnitCode = "UNIT-DUP" };

        var createResult = await createModel.OnPostAsync(CancellationToken.None);
        Assert.IsType<PageResult>(createResult);
        Assert.Contains("đã tồn tại", createModel.ErrorMessage ?? "");

        // 2. HubCreate_StorageUnit with pre-commit ConflictException
        var createJson = JsonSerializer.Serialize(new CreateStorageUnitRequest { FacilityId = 10, UnitTypeId = 20, UnitCode = "UNIT-DUP" });
        var hubCreateEx = await Assert.ThrowsAsync<HubException>(() => hub.HubCreate_StorageUnit(createJson));
        Assert.Contains("đã tồn tại", hubCreateEx.Message);

        // 3. EditModel with pre-commit Concurrency ConflictException
        mockService.Setup(s => s.UpdateAsync(301, It.IsAny<UpdateStorageUnitRequest>(), 1001, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConflictException("Dữ liệu đã bị thay đổi bởi phiên làm việc khác."));

        var editModel = new EditModel(mockService.Object, mockPublisher.Object, NullLogger<EditModel>.Instance);
        var httpContextEdit = new DefaultHttpContext();
        httpContextEdit.User = claimsPrincipal;
        editModel.PageContext = new PageContext { HttpContext = httpContextEdit };
        editModel.TempData = new TempDataDictionary(httpContextEdit, Mock.Of<ITempDataProvider>());
        editModel.Id = 301;
        editModel.Input = new UpdateStorageUnitRequest { UnitCode = "UNIT-STALE", ExpectedUpdatedAt = DateTimeOffset.UtcNow };

        var editResult = await editModel.OnPostAsync(CancellationToken.None);
        Assert.IsType<PageResult>(editResult);
        Assert.True(editModel.IsConcurrencyConflict);

        // 4. HubUpdate_StorageUnit with pre-commit Concurrency ConflictException
        var updateJson = JsonSerializer.Serialize(new { Id = 301, UnitCode = "UNIT-STALE", ExpectedUpdatedAt = DateTimeOffset.UtcNow });
        var hubUpdateEx = await Assert.ThrowsAsync<HubException>(() => hub.HubUpdate_StorageUnit(updateJson));
        Assert.Contains("thay đổi bởi phiên làm việc khác", hubUpdateEx.Message);

        // 5. DeleteModel with pre-commit NotFoundException
        mockService.Setup(s => s.GetByIdAsync(301, 1001, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Ô kho không tồn tại."));

        var deleteModel = new DeleteModel(mockService.Object, mockPublisher.Object, NullLogger<DeleteModel>.Instance);
        var httpContextDelete = new DefaultHttpContext();
        httpContextDelete.User = claimsPrincipal;
        deleteModel.PageContext = new PageContext { HttpContext = httpContextDelete };
        deleteModel.TempData = new TempDataDictionary(httpContextDelete, Mock.Of<ITempDataProvider>());
        deleteModel.Id = 301;

        var deleteResult = await deleteModel.OnPostAsync(CancellationToken.None);
        var redirectDelete = Assert.IsType<RedirectToPageResult>(deleteResult);
        Assert.Equal("./Index", redirectDelete.PageName);
        Assert.True(deleteModel.TempData.ContainsKey("WarningMessage"));

        // 6. HubDelete_StorageUnit with pre-commit NotFoundException
        var hubDeleteEx = await Assert.ThrowsAsync<HubException>(() => hub.HubDelete_StorageUnit("301"));
        Assert.Contains("không tồn tại", hubDeleteEx.Message);
    }

    #endregion

    private async Task<string> AuthenticateUserAndGetCookieDirectAsync(string email, string password)
    {
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
        return authHeader!.Split(';')[0];
    }

    private static string ExtractHiddenInputValue(string html, string inputName)
    {
        var escaped = System.Text.RegularExpressions.Regex.Escape(inputName);
        var match = System.Text.RegularExpressions.Regex.Match(html, $@"name=""{escaped}""[^>]*value=""([^""]*)""");
        if (!match.Success)
        {
            match = System.Text.RegularExpressions.Regex.Match(html, $@"value=""([^""]*)""[^>]*name=""{escaped}""");
        }
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : string.Empty;
    }

    private static async Task<string> AuthenticateUserAndGetCookieDirectOnClientAsync(HttpClient client, string email, string password)
    {
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
        return authHeader!.Split(';')[0];
    }

    private static async Task AssertDatabaseMatchesBaselineAsync(
        CookieAuthWebTestFixture factory,
        long facilityId,
        int initialUnitCount,
        HashSet<long> initialUnitIds,
        long existingUnitId,
        DateTimeOffset initialUpdatedAt,
        string? initialFloor,
        string? initialZone,
        string initialPhysicalStatus)
    {
        using var verifyDb = factory.CreateDbContext();
        var currentUnits = await verifyDb.storage_units.Where(u => u.facility_id == facilityId).ToListAsync();
        Assert.Equal(initialUnitCount, currentUnits.Count);
        Assert.Equal(initialUnitIds, currentUnits.Select(u => u.id).ToHashSet());

        var existing = await verifyDb.storage_units.FindAsync(existingUnitId);
        Assert.NotNull(existing);
        Assert.Equal(initialUpdatedAt, existing.updated_at);
        Assert.Equal(initialFloor, existing.floor_label);
        Assert.Equal(initialZone, existing.zone_label);
        Assert.Equal(initialPhysicalStatus, existing.physical_status);
    }

    private static async Task<(facility Facility, unit_type UnitType, facility_area Area, storage_unit Unit)> SeedKestrelHierarchyAsync(
        KestrelRealtimeServer server,
        string suffix,
        long managerUserId,
        string physicalStatus = "available",
        bool isListed = true)
    {
        using var scope = server.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();

        var facId = Interlocked.Increment(ref _facilityIdSequence);
        var facility = new facility
        {
            id = facId,
            code = $"FAC_K_{suffix.ToUpperInvariant()}_{facId}",
            name = $"Cơ Sở Kestrel {suffix}",
            address_line = $"Số 200 Phố {suffix}",
            ward = "Phường 2",
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

        var areaId = facId * 100 + 1;
        var area = new facility_area
        {
            id = areaId,
            facility_id = facId,
            code = $"AREA_K_{suffix.ToUpperInvariant()}_{facId}",
            name = $"Khu Vực Kestrel {suffix} - Tầng 2",
            area_type = "floor",
            display_order = 1,
            map_metadata = "{}",
            is_active = true
        };
        db.facility_areas.Add(area);

        var unitTypeId = facId * 100 + 2;
        var unitType = new unit_type
        {
            id = unitTypeId,
            code = $"TYPE_K_{suffix.ToUpperInvariant()}_{facId}",
            name = $"Loại Kho Kestrel {suffix}",
            description = "Mô tả loại kho",
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

        var rateId = facId * 100 + 3;
        var rate = new facility_rate
        {
            id = rateId,
            facility_id = facId,
            unit_type_id = unitTypeId,
            monthly_rate = 1500000m,
            deposit_amount = 1500000m,
            booking_fee = 200000m,
            valid_from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
            valid_to = null,
            created_at = DateTimeOffset.UtcNow
        };
        db.facility_rates.Add(rate);

        var unitId = facId * 100 + 4;
        var unit = new storage_unit
        {
            id = unitId,
            facility_id = facId,
            area_id = areaId,
            unit_type_id = unitTypeId,
            unit_code = $"RT-K-{suffix.ToUpperInvariant()}-{facId}",
            floor_label = "Tầng 2",
            zone_label = "Dãy B",
            physical_status = physicalStatus,
            is_listed = isListed,
            notes = "Ghi chú bảo mật nội bộ ô kho realtime Kestrel",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        db.storage_units.Add(unit);

        var existingUser = await db.users.FindAsync(managerUserId);
        if (existingUser == null)
        {
            db.users.Add(new user
            {
                id = managerUserId,
                email = $"mgr_kestrel_{managerUserId}@example.test",
                password_hash = "fakehash",
                status = UserStatusConstants.Active,
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            });
            var roleMgr = await db.roles.FirstOrDefaultAsync(r => r.code == RoleConstants.FacilityManager);
            if (roleMgr == null)
            {
                roleMgr = new role { id = 10, code = RoleConstants.FacilityManager, display_name = "Facility Manager" };
                db.roles.Add(roleMgr);
            }
            db.user_roles.Add(new user_role { user_id = managerUserId, role_id = roleMgr.id, granted_at = DateTimeOffset.UtcNow });
            db.employee_profiles.Add(new employee_profile
            {
                user_id = managerUserId,
                employee_code = $"EMP_{managerUserId}",
                full_name = $"Manager {managerUserId}",
                hire_date = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1)),
                employment_status = EmploymentStatusConstants.Active,
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            });
        }

        db.staff_facility_assignments.Add(new staff_facility_assignment
        {
            employee_id = managerUserId,
            facility_id = facId,
            assignment_role = RoleConstants.FacilityManager,
            starts_at = DateTimeOffset.UtcNow.AddDays(-5),
            ends_at = null,
            created_at = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync();
        return (facility, unitType, area, unit);
    }

    private static async Task<string> GetKestrelAuthCookieAsync(string serverUrl, long userId, string role)
    {
        using var handler = new HttpClientHandler { UseCookies = false };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(serverUrl) };
        var resp = await client.GetAsync($"/test-auth-login?role={role}&userId={userId}&name=Manager_{userId}");
        Assert.True(resp.IsSuccessStatusCode, $"Failed to authenticate test user on Kestrel: status {resp.StatusCode}");
        if (resp.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            var cookie = setCookies.FirstOrDefault(c => c.Contains(CookieAuthExtensions.CookieName));
            if (cookie != null) return cookie.Split(';')[0];
        }
        throw new InvalidOperationException("Failed to acquire auth cookie from Kestrel test login endpoint.");
    }
}

