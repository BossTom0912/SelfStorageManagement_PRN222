using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.WebTests.Fixtures;
using Xunit;

namespace SelfStorageManagementSystem.WebTests;

public class StaffCheckInPageTests : IClassFixture<CheckInWebTestFixture>
{
    private readonly CheckInWebTestFixture _factory;

    public StaffCheckInPageTests(CheckInWebTestFixture factory)
    {
        _factory = factory;
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = Regex.Match(html, @"name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)""");
        if (!match.Success)
        {
            match = Regex.Match(html, @"value=""([^""]+)"" name=""__RequestVerificationToken""");
        }
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    #region 1. Kiểm tra Quyền truy cập Quầy & Không có đường Bypass

    [Fact]
    public async Task Anonymous_Access_RedirectsToLogin()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/Staff/CheckIn");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task CustomerRole_Access_ReturnsForbidden()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.CustomerUser.email, CheckInWebTestFixture.DefaultPassword);

        var response = await client.GetAsync("/Staff/CheckIn");

        Assert.True(
            response.StatusCode == HttpStatusCode.Forbidden ||
            (response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location?.OriginalString.Contains("Forbidden") == true),
            "Khách hàng thuê kho không được phép truy cập quầy tiếp nhận");
    }

    [Fact]
    public async Task AdminOrOps_WithoutStaffCounterRole_Access_ShowsForbiddenWarning()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var adminClient = await _factory.CreateAuthenticatedClientAsync(seed.AdminUser.email, CheckInWebTestFixture.DefaultPassword);
        var adminResponse = await adminClient.GetAsync("/Staff/CheckIn");
        var adminHtml = await adminResponse.Content.ReadAsStringAsync();

        Assert.True(
            adminResponse.StatusCode == HttpStatusCode.Forbidden ||
            adminHtml.Contains("Truy Cập Quầy Bị Từ Chối") ||
            adminHtml.Contains("Không hỗ trợ quyền bypass"),
            "Quản trị viên không có phân công quầy không được phép thao tác");

        var opsClient = await _factory.CreateAuthenticatedClientAsync(seed.OpsUser.email, CheckInWebTestFixture.DefaultPassword);
        var opsResponse = await opsClient.GetAsync("/Staff/CheckIn");
        var opsHtml = await opsResponse.Content.ReadAsStringAsync();

        Assert.True(
            opsResponse.StatusCode == HttpStatusCode.Forbidden ||
            opsHtml.Contains("Truy Cập Quầy Bị Từ Chối") ||
            opsHtml.Contains("Không hỗ trợ quyền bypass"),
            "Quản lý vận hành không có phân công quầy không được phép thao tác");
    }

    [Fact]
    public async Task Staff_WithActiveAssignment_Access_Succeeds()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.StaffAssigned.email, CheckInWebTestFixture.DefaultPassword);
        var response = await client.GetAsync("/Staff/CheckIn");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Quầy Tiếp Nhận", html);
        Assert.Contains("Bàn Giao Kho Số Hóa", html);
    }

    [Fact]
    public async Task Manager_WithActiveAssignment_Access_Succeeds()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.ManagerAssigned.email, CheckInWebTestFixture.DefaultPassword);
        var response = await client.GetAsync("/Staff/CheckIn");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Quầy Tiếp Nhận", html);
        Assert.Contains("Bàn Giao Kho Số Hóa", html);
    }

    #endregion

    #region 2. Tra cứu Đơn đặt chỗ (Search CheckIn)

    [Fact]
    public async Task Search_NonExistentCode_ShowsErrorMessage()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.StaffAssigned.email, CheckInWebTestFixture.DefaultPassword);
        var response = await client.GetAsync("/Staff/CheckIn?code=RSV-NONEXISTENT");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Không tìm thấy thông tin", html);
    }

    [Fact]
    public async Task Search_UnassignedStaff_ShowsForbiddenError()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.StaffUnassigned.email, CheckInWebTestFixture.DefaultPassword);
        var response = await client.GetAsync($"/Staff/CheckIn?code={seed.ReservationCode}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.True(
            html.Contains("Truy Cập Quầy Bị Từ Chối") || html.Contains("không có quyền thao tác trên cơ sở"),
            "Nhân viên không có phân công tại cơ sở phải nhận thông báo từ chối truy cập");
    }

    [Fact]
    public async Task Search_ValidCode_RendersCustomerAndUnitInfo()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.StaffAssigned.email, CheckInWebTestFixture.DefaultPassword);
        var response = await client.GetAsync($"/Staff/CheckIn?code={seed.ReservationCode}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains(seed.ReservationCode, html);
        Assert.Contains(seed.Facility.name, html);
        Assert.Contains(seed.UnitType.name, html);
        Assert.Contains("********1234", html); // Masked identity number
        Assert.Contains("Đã thanh toán đủ", html);
    }

    #endregion

    #region 3. Xác minh giấy tờ tùy thân (Verify ID)

    [Fact]
    public async Task VerifyId_DocumentMismatch_ShowsErrorMessage()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.StaffAssigned.email, CheckInWebTestFixture.DefaultPassword);
        var getResp = await client.GetAsync($"/Staff/CheckIn?code={seed.ReservationCode}");
        var html = await getResp.Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(html);

        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ReservationId"] = seed.Reservation.id.ToString(),
            ["Code"] = seed.ReservationCode,
            ["VerificationMethod"] = "government_id",
            ["DocumentNumber"] = "999999999999", // Mismatched number
            ["IsConfirmedMatch"] = "true",
            ["Notes"] = "Test mismatch"
        };

        var postResp = await client.PostAsync("/Staff/CheckIn?handler=VerifyId", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.OK, postResp.StatusCode);
        var resultHtml = await postResp.Content.ReadAsStringAsync();

        Assert.Contains("không khớp với số giấy tờ đã lưu", resultHtml);
    }

    [Fact]
    public async Task VerifyId_Successful_ShowsVerifiedBadge()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.StaffAssigned.email, CheckInWebTestFixture.DefaultPassword);
        var getResp = await client.GetAsync($"/Staff/CheckIn?code={seed.ReservationCode}");
        var html = await getResp.Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(html);

        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ReservationId"] = seed.Reservation.id.ToString(),
            ["Code"] = seed.ReservationCode,
            ["VerificationMethod"] = "government_id",
            ["DocumentNumber"] = seed.CustomerIdentityNumber, // Exact match
            ["IsConfirmedMatch"] = "true",
            ["Notes"] = "Khách hàng xuất trình CCCD gắn chip gốc"
        };

        var postResp = await client.PostAsync("/Staff/CheckIn?handler=VerifyId", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.OK, postResp.StatusCode);
        var resultHtml = await postResp.Content.ReadAsStringAsync();

        Assert.Contains("Xác minh giấy tờ tùy thân thành công", resultHtml);
        Assert.Contains("ĐÃ XÁC MINH", resultHtml);
    }

    #endregion

    #region 4. Gán và Giải phóng Ô kho (Assign & Release Storage Unit)

    [Fact]
    public async Task AssignUnit_And_ReleaseUnit_LifecycleSucceeds()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.StaffAssigned.email, CheckInWebTestFixture.DefaultPassword);

        // 1. Verify ID first
        var getResp = await client.GetAsync($"/Staff/CheckIn?code={seed.ReservationCode}");
        var token = ExtractAntiforgeryToken(await getResp.Content.ReadAsStringAsync());

        await client.PostAsync("/Staff/CheckIn?handler=VerifyId", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ReservationId"] = seed.Reservation.id.ToString(),
            ["Code"] = seed.ReservationCode,
            ["VerificationMethod"] = "government_id",
            ["DocumentNumber"] = seed.CustomerIdentityNumber,
            ["IsConfirmedMatch"] = "true"
        }));

        // 2. Assign Unit 1
        var afterVerif = await client.GetAsync($"/Staff/CheckIn?code={seed.ReservationCode}");
        var tokenAssign = ExtractAntiforgeryToken(await afterVerif.Content.ReadAsStringAsync());

        var assignResp = await client.PostAsync("/Staff/CheckIn?handler=AssignUnit", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = tokenAssign,
            ["ReservationId"] = seed.Reservation.id.ToString(),
            ["Code"] = seed.ReservationCode,
            ["StorageUnitId"] = seed.Unit1.id.ToString()
        }));

        Assert.Equal(HttpStatusCode.OK, assignResp.StatusCode);
        var assignHtml = await assignResp.Content.ReadAsStringAsync();
        Assert.Contains($"Gán ô kho {seed.Unit1.unit_code} thành công", assignHtml);
        Assert.Contains("ĐÃ GÁN Ô:", assignHtml);

        // Check DB unit physical status is now 'reserved'
        using (var db = _factory.CreateDbContext())
        {
            var u1 = await db.storage_units.FindAsync(seed.Unit1.id);
            Assert.Equal("reserved", u1?.physical_status);
        }

        // 3. Release Unit 1 (to re-assign Unit 2)
        var afterAssign = await client.GetAsync($"/Staff/CheckIn?code={seed.ReservationCode}");
        var assignHtml2 = await afterAssign.Content.ReadAsStringAsync();
        var tokenRelease = ExtractAntiforgeryToken(assignHtml2);

        // Extract CurrentAllocationId
        var allocIdMatch = Regex.Match(assignHtml2, @"name=""CurrentAllocationId"" value=""(\d+)""");
        Assert.True(allocIdMatch.Success, "Phải tìm thấy CurrentAllocationId trên giao diện");
        var allocId = allocIdMatch.Groups[1].Value;

        var releaseResp = await client.PostAsync("/Staff/CheckIn?handler=ReleaseUnit", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = tokenRelease,
            ["ReservationId"] = seed.Reservation.id.ToString(),
            ["Code"] = seed.ReservationCode,
            ["CurrentAllocationId"] = allocId,
            ["Reason"] = "Khách hàng muốn đổi sang ô kho ở vị trí khác"
        }));

        Assert.Equal(HttpStatusCode.OK, releaseResp.StatusCode);
        var releaseHtml = await releaseResp.Content.ReadAsStringAsync();
        Assert.Contains("Đã giải phóng ô kho", releaseHtml);

        // Check DB unit physical status returned to 'available'
        using (var db = _factory.CreateDbContext())
        {
            var u1 = await db.storage_units.FindAsync(seed.Unit1.id);
            Assert.Equal("available", u1?.physical_status);
        }
    }

    [Fact]
    public async Task ReleaseUnit_StaleAllocationId_ShowsStaleConflictAlert()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.StaffAssigned.email, CheckInWebTestFixture.DefaultPassword);

        // Verify ID & Assign Unit
        var getResp = await client.GetAsync($"/Staff/CheckIn?code={seed.ReservationCode}");
        var token = ExtractAntiforgeryToken(await getResp.Content.ReadAsStringAsync());

        await client.PostAsync("/Staff/CheckIn?handler=VerifyId", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ReservationId"] = seed.Reservation.id.ToString(),
            ["Code"] = seed.ReservationCode,
            ["VerificationMethod"] = "government_id",
            ["DocumentNumber"] = seed.CustomerIdentityNumber,
            ["IsConfirmedMatch"] = "true"
        }));

        var getResp2 = await client.GetAsync($"/Staff/CheckIn?code={seed.ReservationCode}");
        var tokenAssign = ExtractAntiforgeryToken(await getResp2.Content.ReadAsStringAsync());

        await client.PostAsync("/Staff/CheckIn?handler=AssignUnit", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = tokenAssign,
            ["ReservationId"] = seed.Reservation.id.ToString(),
            ["Code"] = seed.ReservationCode,
            ["StorageUnitId"] = seed.Unit1.id.ToString()
        }));

        // Send release with a stale/non-matching CurrentAllocationId
        var getResp3 = await client.GetAsync($"/Staff/CheckIn?code={seed.ReservationCode}");
        var tokenRelease = ExtractAntiforgeryToken(await getResp3.Content.ReadAsStringAsync());

        var staleResp = await client.PostAsync("/Staff/CheckIn?handler=ReleaseUnit", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = tokenRelease,
            ["ReservationId"] = seed.Reservation.id.ToString(),
            ["Code"] = seed.ReservationCode,
            ["CurrentAllocationId"] = "999999", // Stale ID
            ["Reason"] = "Stale test"
        }));

        Assert.Equal(HttpStatusCode.OK, staleResp.StatusCode);
        var staleHtml = await staleResp.Content.ReadAsStringAsync();
        Assert.Contains("Xung Đột Dữ Liệu (Stale Data Conflict)", staleHtml);
        Assert.Contains("Tải lại dữ liệu mới nhất", staleHtml);
    }

    #endregion

    #region 5. Kiểm tra Tình trạng Ô kho (Inspection)

    [Fact]
    public async Task Inspection_Pass_RecordsCompleted_EnablesHandover()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.StaffAssigned.email, CheckInWebTestFixture.DefaultPassword);

        // 1. Verify ID & Assign Unit
        var getResp = await client.GetAsync($"/Staff/CheckIn?code={seed.ReservationCode}");
        var token = ExtractAntiforgeryToken(await getResp.Content.ReadAsStringAsync());

        await client.PostAsync("/Staff/CheckIn?handler=VerifyId", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ReservationId"] = seed.Reservation.id.ToString(),
            ["Code"] = seed.ReservationCode,
            ["VerificationMethod"] = "government_id",
            ["DocumentNumber"] = seed.CustomerIdentityNumber,
            ["IsConfirmedMatch"] = "true"
        }));

        var getResp2 = await client.GetAsync($"/Staff/CheckIn?code={seed.ReservationCode}");
        var tokenAssign = ExtractAntiforgeryToken(await getResp2.Content.ReadAsStringAsync());

        await client.PostAsync("/Staff/CheckIn?handler=AssignUnit", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = tokenAssign,
            ["ReservationId"] = seed.Reservation.id.ToString(),
            ["Code"] = seed.ReservationCode,
            ["StorageUnitId"] = seed.Unit1.id.ToString()
        }));

        // 2. Perform Inspection (Pass)
        var getResp3 = await client.GetAsync($"/Staff/CheckIn?code={seed.ReservationCode}");
        var html3 = await getResp3.Content.ReadAsStringAsync();
        var tokenInsp = ExtractAntiforgeryToken(html3);
        var allocId = Regex.Match(html3, @"name=""CurrentAllocationId"" value=""(\d+)""").Groups[1].Value;

        var inspForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = tokenInsp,
            ["ReservationId"] = seed.Reservation.id.ToString(),
            ["Code"] = seed.ReservationCode,
            ["CurrentAllocationId"] = allocId,
            ["IsUnsafe"] = "false",
            ["OverallSummary"] = "Kho sạch sẽ thoáng mát",
            ["Items[0].ItemName"] = "Cửa ô kho",
            ["Items[0].Condition"] = "good",
            ["Items[1].ItemName"] = "Khóa & then cài",
            ["Items[1].Condition"] = "good",
            ["Items[2].ItemName"] = "Đèn chiếu sáng",
            ["Items[2].Condition"] = "acceptable",
            ["Items[3].ItemName"] = "Vệ sinh sạch sẽ",
            ["Items[3].Condition"] = "good"
        };

        var inspResp = await client.PostAsync("/Staff/CheckIn?handler=PerformInspection", new FormUrlEncodedContent(inspForm));
        Assert.Equal(HttpStatusCode.OK, inspResp.StatusCode);
        var inspHtml = await inspResp.Content.ReadAsStringAsync();

        Assert.Contains("ĐẠT YÊU CẦU", inspHtml);
        Assert.Contains("Hoàn Tất Bàn Giao", inspHtml);
        Assert.Contains("Cấp Mã PIN", inspHtml);
    }

    [Fact]
    public async Task Inspection_Fail_DamagedItem_ReleasesUnit_SetsMaintenance()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.StaffAssigned.email, CheckInWebTestFixture.DefaultPassword);

        // Verify ID & Assign Unit
        var getResp = await client.GetAsync($"/Staff/CheckIn?code={seed.ReservationCode}");
        var token = ExtractAntiforgeryToken(await getResp.Content.ReadAsStringAsync());

        await client.PostAsync("/Staff/CheckIn?handler=VerifyId", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ReservationId"] = seed.Reservation.id.ToString(),
            ["Code"] = seed.ReservationCode,
            ["VerificationMethod"] = "government_id",
            ["DocumentNumber"] = seed.CustomerIdentityNumber,
            ["IsConfirmedMatch"] = "true"
        }));

        var getResp2 = await client.GetAsync($"/Staff/CheckIn?code={seed.ReservationCode}");
        var tokenAssign = ExtractAntiforgeryToken(await getResp2.Content.ReadAsStringAsync());

        await client.PostAsync("/Staff/CheckIn?handler=AssignUnit", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = tokenAssign,
            ["ReservationId"] = seed.Reservation.id.ToString(),
            ["Code"] = seed.ReservationCode,
            ["StorageUnitId"] = seed.Unit1.id.ToString()
        }));

        // Inspection with damaged lock
        var getResp3 = await client.GetAsync($"/Staff/CheckIn?code={seed.ReservationCode}");
        var html3 = await getResp3.Content.ReadAsStringAsync();
        var tokenInsp = ExtractAntiforgeryToken(html3);
        var allocId = Regex.Match(html3, @"name=""CurrentAllocationId"" value=""(\d+)""").Groups[1].Value;

        var inspForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = tokenInsp,
            ["ReservationId"] = seed.Reservation.id.ToString(),
            ["Code"] = seed.ReservationCode,
            ["CurrentAllocationId"] = allocId,
            ["IsUnsafe"] = "false",
            ["OverallSummary"] = "Khóa bị gãy then cài",
            ["Items[0].ItemName"] = "Cửa ô kho",
            ["Items[0].Condition"] = "good",
            ["Items[1].ItemName"] = "Khóa & then cài",
            ["Items[1].Condition"] = "damaged", // FAILURE!
            ["Items[2].ItemName"] = "Đèn chiếu sáng",
            ["Items[2].Condition"] = "good",
            ["Items[3].ItemName"] = "Vệ sinh sạch sẽ",
            ["Items[3].Condition"] = "good"
        };

        var inspResp = await client.PostAsync("/Staff/CheckIn?handler=PerformInspection", new FormUrlEncodedContent(inspForm));
        Assert.Equal(HttpStatusCode.OK, inspResp.StatusCode);
        var inspHtml = await inspResp.Content.ReadAsStringAsync();

        Assert.Contains("KHÔNG ĐẠT", inspHtml);
        Assert.Contains("Bảo trì (Maintenance)", inspHtml);

        // Verify DB unit status is now 'maintenance'
        using (var db = _factory.CreateDbContext())
        {
            var u1 = await db.storage_units.FindAsync(seed.Unit1.id);
            Assert.Equal("maintenance", u1?.physical_status);
        }
    }

    [Fact]
    public async Task Inspection_Fail_IsUnsafeFlag_ReleasesUnit_SetsMaintenance()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.StaffAssigned.email, CheckInWebTestFixture.DefaultPassword);

        // Verify ID & Assign Unit
        var getResp = await client.GetAsync($"/Staff/CheckIn?code={seed.ReservationCode}");
        var token = ExtractAntiforgeryToken(await getResp.Content.ReadAsStringAsync());

        await client.PostAsync("/Staff/CheckIn?handler=VerifyId", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ReservationId"] = seed.Reservation.id.ToString(),
            ["Code"] = seed.ReservationCode,
            ["VerificationMethod"] = "government_id",
            ["DocumentNumber"] = seed.CustomerIdentityNumber,
            ["IsConfirmedMatch"] = "true"
        }));

        var getResp2 = await client.GetAsync($"/Staff/CheckIn?code={seed.ReservationCode}");
        var tokenAssign = ExtractAntiforgeryToken(await getResp2.Content.ReadAsStringAsync());

        await client.PostAsync("/Staff/CheckIn?handler=AssignUnit", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = tokenAssign,
            ["ReservationId"] = seed.Reservation.id.ToString(),
            ["Code"] = seed.ReservationCode,
            ["StorageUnitId"] = seed.Unit1.id.ToString()
        }));

        // Inspection with isUnsafe = true (all items good, but unsafe flag set)
        var getResp3 = await client.GetAsync($"/Staff/CheckIn?code={seed.ReservationCode}");
        var html3 = await getResp3.Content.ReadAsStringAsync();
        var tokenInsp = ExtractAntiforgeryToken(html3);
        var allocId = Regex.Match(html3, @"name=""CurrentAllocationId"" value=""(\d+)""").Groups[1].Value;

        var inspForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = tokenInsp,
            ["ReservationId"] = seed.Reservation.id.ToString(),
            ["Code"] = seed.ReservationCode,
            ["CurrentAllocationId"] = allocId,
            ["IsUnsafe"] = "true", // FLAG IS SET!
            ["OverallSummary"] = "Phát hiện vết rạn nứt trần có nguy cơ sập",
            ["Items[0].ItemName"] = "Cửa ô kho",
            ["Items[0].Condition"] = "good",
            ["Items[1].ItemName"] = "Khóa & then cài",
            ["Items[1].Condition"] = "good",
            ["Items[2].ItemName"] = "Đèn chiếu sáng",
            ["Items[2].Condition"] = "good",
            ["Items[3].ItemName"] = "Vệ sinh sạch sẽ",
            ["Items[3].Condition"] = "good"
        };

        var inspResp = await client.PostAsync("/Staff/CheckIn?handler=PerformInspection", new FormUrlEncodedContent(inspForm));
        Assert.Equal(HttpStatusCode.OK, inspResp.StatusCode);
        var inspHtml = await inspResp.Content.ReadAsStringAsync();

        Assert.Contains("KHÔNG ĐẠT", inspHtml);
        Assert.Contains("unsafe", inspHtml);
        Assert.Contains("Bảo trì (Maintenance)", inspHtml);

        using (var db = _factory.CreateDbContext())
        {
            var u1 = await db.storage_units.FindAsync(seed.Unit1.id);
            Assert.Equal("maintenance", u1?.physical_status);
        }
    }

    #endregion

    #region 6. Bàn giao kho & Cấp PIN một lần (Complete Handover & Issue PIN)

    [Fact]
    public async Task CompleteCheckIn_Successful_IssuesPin_SetsNoStoreHeaders_AndZeroPersistence()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.StaffAssigned.email, CheckInWebTestFixture.DefaultPassword);

        // 1. Verify ID
        var getResp = await client.GetAsync($"/Staff/CheckIn?code={seed.ReservationCode}");
        var token = ExtractAntiforgeryToken(await getResp.Content.ReadAsStringAsync());

        await client.PostAsync("/Staff/CheckIn?handler=VerifyId", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ReservationId"] = seed.Reservation.id.ToString(),
            ["Code"] = seed.ReservationCode,
            ["VerificationMethod"] = "government_id",
            ["DocumentNumber"] = seed.CustomerIdentityNumber,
            ["IsConfirmedMatch"] = "true"
        }));

        // 2. Assign Unit
        var getResp2 = await client.GetAsync($"/Staff/CheckIn?code={seed.ReservationCode}");
        var tokenAssign = ExtractAntiforgeryToken(await getResp2.Content.ReadAsStringAsync());

        await client.PostAsync("/Staff/CheckIn?handler=AssignUnit", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = tokenAssign,
            ["ReservationId"] = seed.Reservation.id.ToString(),
            ["Code"] = seed.ReservationCode,
            ["StorageUnitId"] = seed.Unit1.id.ToString()
        }));

        // 3. Inspection PASS
        var getResp3 = await client.GetAsync($"/Staff/CheckIn?code={seed.ReservationCode}");
        var html3 = await getResp3.Content.ReadAsStringAsync();
        var tokenInsp = ExtractAntiforgeryToken(html3);
        var allocId = Regex.Match(html3, @"name=""CurrentAllocationId"" value=""(\d+)""").Groups[1].Value;

        await client.PostAsync("/Staff/CheckIn?handler=PerformInspection", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = tokenInsp,
            ["ReservationId"] = seed.Reservation.id.ToString(),
            ["Code"] = seed.ReservationCode,
            ["CurrentAllocationId"] = allocId,
            ["IsUnsafe"] = "false",
            ["Items[0].ItemName"] = "Cửa ô kho",
            ["Items[0].Condition"] = "good",
            ["Items[1].ItemName"] = "Khóa & then cài",
            ["Items[1].Condition"] = "good",
            ["Items[2].ItemName"] = "Đèn chiếu sáng",
            ["Items[2].Condition"] = "good",
            ["Items[3].ItemName"] = "Vệ sinh sạch sẽ",
            ["Items[3].Condition"] = "good"
        }));

        // 4. Complete Check-In
        var getResp4 = await client.GetAsync($"/Staff/CheckIn?code={seed.ReservationCode}");
        var html4 = await getResp4.Content.ReadAsStringAsync();
        var tokenComplete = ExtractAntiforgeryToken(html4);
        var inspId = Regex.Match(html4, @"name=""CurrentInspectionId"" value=""(\d+)""").Groups[1].Value;

        var completeForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = tokenComplete,
            ["ReservationId"] = seed.Reservation.id.ToString(),
            ["Code"] = seed.ReservationCode,
            ["CurrentAllocationId"] = allocId,
            ["CurrentInspectionId"] = inspId,
            ["CustomerAcknowledged"] = "true",
            ["Notes"] = "Khách hàng nhận kho trực tiếp tại quầy"
        };

        var completeResp = await client.PostAsync("/Staff/CheckIn?handler=CompleteCheckIn", new FormUrlEncodedContent(completeForm));
        Assert.Equal(HttpStatusCode.OK, completeResp.StatusCode);

        // Security check: no-store, no-cache headers present
        var cacheControl = completeResp.Headers.CacheControl?.ToString();
        Assert.NotNull(cacheControl);
        Assert.Contains("no-store", cacheControl, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no-cache", cacheControl, StringComparison.OrdinalIgnoreCase);

        var completeHtml = await completeResp.Content.ReadAsStringAsync();
        Assert.Contains("MÃ PIN TRUY CẬP SỐ HÓA", completeHtml);
        Assert.Contains("CHỈ HIỂN THỊ 1 LẦN", completeHtml);

        // Extract PIN
        var pinMatch = Regex.Match(completeHtml, @"id=""displayPinValue""[^>]*>(\d{6})</span>");
        Assert.True(pinMatch.Success, "Phải tìm thấy mã PIN 6 số trên màn hình kết quả");
        var issuedPin = pinMatch.Groups[1].Value;

        // Security check: PIN must NOT be in Set-Cookie headers
        if (completeResp.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var cookie in setCookies)
            {
                Assert.DoesNotContain(issuedPin, cookie);
            }
        }

        // 5. Zero-Persistence Check: Refresh GET must NOT contain the PIN!
        var refreshResp = await client.GetAsync($"/Staff/CheckIn?code={seed.ReservationCode}");
        Assert.Equal(HttpStatusCode.OK, refreshResp.StatusCode);
        var refreshHtml = await refreshResp.Content.ReadAsStringAsync();

        Assert.DoesNotContain(issuedPin, refreshHtml);
        Assert.Contains("******", refreshHtml); // Masked representation
        Assert.Contains("Cấp Lại Mã PIN Tại Quầy", refreshHtml);
    }

    #endregion

    #region 7. Cấp lại mã PIN tại quầy (Reissue PIN)

    [Fact]
    public async Task ReissuePin_DocumentMismatch_Fails()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.StaffAssigned.email, CheckInWebTestFixture.DefaultPassword);

        // Perform full check-in to get active agreement & credential
        await CompleteHandoverAsync(client, seed);

        // Attempt reissue with wrong document number
        var getResp = await client.GetAsync($"/Staff/CheckIn?code={seed.ReservationCode}");
        var html = await getResp.Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(html);
        var credId = Regex.Match(html, @"name=""CurrentCredentialId"" value=""(\d+)""").Groups[1].Value;

        var reissueForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ReservationId"] = seed.Reservation.id.ToString(),
            ["Code"] = seed.ReservationCode,
            ["CurrentCredentialId"] = credId,
            ["DocumentNumber"] = "999999999999", // Wrong document
            ["IsConfirmedMatch"] = "true",
            ["Reason"] = "Khách quên PIN"
        };

        var postResp = await client.PostAsync("/Staff/CheckIn?handler=ReissuePin", new FormUrlEncodedContent(reissueForm));
        Assert.Equal(HttpStatusCode.OK, postResp.StatusCode);
        var resultHtml = await postResp.Content.ReadAsStringAsync();

        Assert.Contains("không khớp", resultHtml);
    }

    [Fact]
    public async Task ReissuePin_Successful_IssuesNewPin_SetsNoStoreHeaders_AndZeroPersistence()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.StaffAssigned.email, CheckInWebTestFixture.DefaultPassword);

        // Perform full check-in first
        await CompleteHandoverAsync(client, seed);

        // Reissue PIN with correct document
        var getResp = await client.GetAsync($"/Staff/CheckIn?code={seed.ReservationCode}");
        var html = await getResp.Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(html);
        var credId = Regex.Match(html, @"name=""CurrentCredentialId"" value=""(\d+)""").Groups[1].Value;

        var reissueForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ReservationId"] = seed.Reservation.id.ToString(),
            ["Code"] = seed.ReservationCode,
            ["CurrentCredentialId"] = credId,
            ["DocumentNumber"] = seed.CustomerIdentityNumber, // Match
            ["IsConfirmedMatch"] = "true",
            ["Reason"] = "Khách quên mã PIN cũ"
        };

        var reissueResp = await client.PostAsync("/Staff/CheckIn?handler=ReissuePin", new FormUrlEncodedContent(reissueForm));
        Assert.Equal(HttpStatusCode.OK, reissueResp.StatusCode);

        // Cache-control headers
        var cacheControl = reissueResp.Headers.CacheControl?.ToString();
        Assert.NotNull(cacheControl);
        Assert.Contains("no-store", cacheControl, StringComparison.OrdinalIgnoreCase);

        var reissueHtml = await reissueResp.Content.ReadAsStringAsync();
        Assert.Contains("MÃ PIN TRUY CẬP SỐ HÓA (CẤP LẠI)", reissueHtml);

        var pinMatch = Regex.Match(reissueHtml, @"id=""displayPinValue""[^>]*>(\d{6})</span>");
        Assert.True(pinMatch.Success, "Phải sinh mã PIN mới 6 số");
        var newPin = pinMatch.Groups[1].Value;

        // Refresh GET: new PIN disappears
        var refreshResp = await client.GetAsync($"/Staff/CheckIn?code={seed.ReservationCode}");
        var refreshHtml = await refreshResp.Content.ReadAsStringAsync();
        Assert.DoesNotContain(newPin, refreshHtml);
    }

    #endregion

    #region Helper Methods

    private async Task CompleteHandoverAsync(HttpClient client, CheckInSeedResult seed)
    {
        // 1. Verify ID
        var r1 = await client.GetAsync($"/Staff/CheckIn?code={seed.ReservationCode}");
        var t1 = ExtractAntiforgeryToken(await r1.Content.ReadAsStringAsync());

        await client.PostAsync("/Staff/CheckIn?handler=VerifyId", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = t1,
            ["ReservationId"] = seed.Reservation.id.ToString(),
            ["Code"] = seed.ReservationCode,
            ["VerificationMethod"] = "government_id",
            ["DocumentNumber"] = seed.CustomerIdentityNumber,
            ["IsConfirmedMatch"] = "true"
        }));

        // 2. Assign Unit
        var r2 = await client.GetAsync($"/Staff/CheckIn?code={seed.ReservationCode}");
        var t2 = ExtractAntiforgeryToken(await r2.Content.ReadAsStringAsync());

        await client.PostAsync("/Staff/CheckIn?handler=AssignUnit", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = t2,
            ["ReservationId"] = seed.Reservation.id.ToString(),
            ["Code"] = seed.ReservationCode,
            ["StorageUnitId"] = seed.Unit1.id.ToString()
        }));

        // 3. Inspection
        var r3 = await client.GetAsync($"/Staff/CheckIn?code={seed.ReservationCode}");
        var h3 = await r3.Content.ReadAsStringAsync();
        var t3 = ExtractAntiforgeryToken(h3);
        var allocId = Regex.Match(h3, @"name=""CurrentAllocationId"" value=""(\d+)""").Groups[1].Value;

        await client.PostAsync("/Staff/CheckIn?handler=PerformInspection", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = t3,
            ["ReservationId"] = seed.Reservation.id.ToString(),
            ["Code"] = seed.ReservationCode,
            ["CurrentAllocationId"] = allocId,
            ["IsUnsafe"] = "false",
            ["Items[0].ItemName"] = "Cửa ô kho",
            ["Items[0].Condition"] = "good",
            ["Items[1].ItemName"] = "Khóa & then cài",
            ["Items[1].Condition"] = "good",
            ["Items[2].ItemName"] = "Đèn chiếu sáng",
            ["Items[2].Condition"] = "good",
            ["Items[3].ItemName"] = "Vệ sinh sạch sẽ",
            ["Items[3].Condition"] = "good"
        }));

        // 4. Complete
        var r4 = await client.GetAsync($"/Staff/CheckIn?code={seed.ReservationCode}");
        var h4 = await r4.Content.ReadAsStringAsync();
        var t4 = ExtractAntiforgeryToken(h4);
        var inspId = Regex.Match(h4, @"name=""CurrentInspectionId"" value=""(\d+)""").Groups[1].Value;

        await client.PostAsync("/Staff/CheckIn?handler=CompleteCheckIn", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = t4,
            ["ReservationId"] = seed.Reservation.id.ToString(),
            ["Code"] = seed.ReservationCode,
            ["CurrentAllocationId"] = allocId,
            ["CurrentInspectionId"] = inspId,
            ["CustomerAcknowledged"] = "true"
        }));
    }

    #endregion
}
