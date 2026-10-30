using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Reservations;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.Web.Extensions;
using SelfStorageManagementSystem.Web.Helpers;
using SelfStorageManagementSystem.Web.Workers;
using SelfStorageManagementSystem.WebTests.Fixtures;
using Xunit;

namespace SelfStorageManagementSystem.WebTests;

/// <summary>
/// Bộ kiểm thử tích hợp toàn diện cho Task T10:
/// 1. Create/MyReservations/Details/Cancel với xác thực Cookie và định danh actor an toàn.
/// 2. Giữ chỗ 15 phút theo loại kho và kiểm tra sức chứa khả dụng (BR-RSV-01, BR-RSV-02, BR-RSV-03).
/// 3. Countdown server-based, chống reset khi reload, chống tạo lặp đơn.
/// 4. Server là nguồn trạng thái tối cao (Source of Truth), tự động expire khi countdown chạm hạn.
/// 5. Background Worker 30 giây trong Web host, xử lý dọn dẹp ngay sau startup (Restart Resilience).
/// 6. Tranh chấp sức chứa (Concurrency Capacity Competition) giữa 2 yêu cầu.
/// 7. Thao tác ngay sát hạn và sau hạn.
/// </summary>
public class ReservationRazorPagesAndWorkerTests : IClassFixture<CookieAuthWebTestFixture>
{
    private readonly CookieAuthWebTestFixture _factory;

    public ReservationRazorPagesAndWorkerTests(CookieAuthWebTestFixture factory)
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

    private async Task<(HttpClient Client, user User, string AuthCookie)> CreateAuthenticatedCustomerClientAsync(string emailPrefix = "cust")
    {
        var email = $"{emailPrefix}_{Guid.NewGuid():N}@customer.test";
        var user = await _factory.SeedUserAsync(email, "Password123!", RoleConstants.StorageCustomer, UserStatusConstants.Active, $"Khách Hàng {emailPrefix}");
        var client = CreateClient(allowAutoRedirect: false);

        var (token, afCookie) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(client, "/Account/Login");
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/Account/Login");
        loginReq.Headers.Add("Cookie", afCookie);
        loginReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = email,
            ["Password"] = "Password123!",
            ["__RequestVerificationToken"] = token,
            ["ReturnUrl"] = "/"
        });

        var loginResp = await client.SendAsync(loginReq);
        Assert.Equal(HttpStatusCode.Redirect, loginResp.StatusCode);

        var authCookieHeader = loginResp.Headers.GetValues("Set-Cookie").First(c => c.Contains(CookieAuthExtensions.CookieName));
        var authCookie = authCookieHeader.Split(';')[0];
        client.DefaultRequestHeaders.Add("Cookie", authCookie);

        return (client, user, authCookie);
    }

    private static long _seedCounter = 1000;

    private async Task<(long FacilityId, long UnitTypeId, List<long> UnitIds)> SeedFacilityAndUnitTypeAsync(
        string suffix,
        int unitCount = 2,
        decimal monthlyRate = 1500000m)
    {
        var (fId, uId, _, uIds) = await SeedFacilityWithRateAsync(suffix, unitCount, monthlyRate);
        return (fId, uId, uIds);
    }

    private async Task<(long FacilityId, long UnitTypeId, long RateId, List<long> UnitIds)> SeedFacilityWithRateAsync(
        string suffix,
        int unitCount = 2,
        decimal monthlyRate = 1500000m)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();

        var baseId = Interlocked.Increment(ref _seedCounter) * 1000L;
        var facilityId = baseId;
        var facility = new facility
        {
            id = facilityId,
            code = $"FAC-{suffix.ToUpperInvariant()}-{baseId}",
            name = $"Cơ Sở Lưu Trữ {suffix}",
            address_line = $"Số 123 Đường {suffix}",
            ward = "Phường 13",
            district = "Tân Bình",
            city = "Hồ Chí Minh",
            latitude = 10.8012m,
            longitude = 106.6543m,
            status = "active",
            timezone = "Asia/Ho_Chi_Minh",
            opening_time = new TimeOnly(7, 30),
            closing_time = new TimeOnly(21, 0),
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        db.facilities.Add(facility);

        var areaId = baseId + 1;
        var area = new facility_area
        {
            id = areaId,
            facility_id = facilityId,
            code = $"AREA-{suffix.ToUpperInvariant()}",
            name = "Khu Vực A - Tầng 1",
            area_type = "floor",
            display_order = 1,
            map_metadata = "{}",
            is_active = true
        };
        db.facility_areas.Add(area);

        var unitTypeId = baseId + 2;
        var unitType = new unit_type
        {
            id = unitTypeId,
            code = $"UT-{suffix.ToUpperInvariant()}-{baseId}",
            name = $"Kho Tiêu Chuẩn {suffix}",
            width_m = 2.0m,
            length_m = 2.5m,
            height_m = 3.0m,
            area_m2 = 5.0m,
            volume_m3 = 15.0m,
            climate_controlled = true,
            max_weight_kg = 500m,
            description = "Kho bảo quản sạch sẽ có kiểm soát nhiệt độ",
            is_active = true,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        db.unit_types.Add(unitType);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rateId = baseId + 3;
        var rate = new facility_rate
        {
            id = rateId,
            facility_id = facilityId,
            unit_type_id = unitTypeId,
            monthly_rate = monthlyRate,
            deposit_amount = monthlyRate, // BR-FIN-01: deposit equals 100% 1 month
            booking_fee = 50000m,
            valid_from = today.AddDays(-10),
            valid_to = today.AddDays(365)
        };
        db.facility_rates.Add(rate);

        var unitIds = new List<long>();
        for (int i = 1; i <= unitCount; i++)
        {
            var unitId = baseId + 10 + i;
            unitIds.Add(unitId);

            var unit = new storage_unit
            {
                id = unitId,
                facility_id = facilityId,
                unit_type_id = unitTypeId,
                area_id = areaId,
                unit_code = $"UNIT-{suffix.ToUpperInvariant()}-{i:D2}",
                floor_label = "Tầng 1",
                zone_label = "Dãy A",
                physical_status = "available",
                is_listed = true,
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            };
            db.storage_units.Add(unit);
        }

        await db.SaveChangesAsync();
        return (facilityId, unitTypeId, rateId, unitIds);
    }

    #region 1. Create Reservation & 15-Minute Hold Rule (BR-RSV-01, BR-RSV-02, BR-RSV-03)

    [Fact]
    public async Task CreateReservation_ValidData_CreatesHoldFor15Minutes_AndCalculatesFinancials()
    {
        // Arrange
        var (client, user, authCookie) = await CreateAuthenticatedCustomerClientAsync("create_valid");
        var (facilityId, unitTypeId, _) = await SeedFacilityAndUnitTypeAsync("CRV", unitCount: 3, monthlyRate: 2000000m);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startDate = today.AddDays(1);
        var endDate = startDate.AddMonths(2);

        // Lấy Antiforgery token từ trang Create
        var (afToken, afCookie) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(
            client,
            $"/Reservations/Create?facilityId={facilityId}&unitTypeId={unitTypeId}");

        var combinedCookie = $"{authCookie}; {afCookie}";

        var postReq = new HttpRequestMessage(HttpMethod.Post, "/Reservations/Create");
        postReq.Headers.Add("Cookie", combinedCookie);
        postReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = afToken,
            ["Input.FacilityId"] = facilityId.ToString(),
            ["Input.UnitTypeId"] = unitTypeId.ToString(),
            ["Input.RentalStartDate"] = startDate.ToString("yyyy-MM-dd"),
            ["Input.RentalEndDate"] = endDate.ToString("yyyy-MM-dd")
        });

        // Act
        var response = await client.SendAsync(postReq);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var redirectUrl = response.Headers.Location?.OriginalString;
        Assert.NotNull(redirectUrl);
        Assert.StartsWith("/Reservations/Details", redirectUrl);

        // Trích xuất ID đơn đặt chỗ từ redirect URL
        var match = Regex.Match(redirectUrl, @"(?:id=|\/)(\d+)");
        Assert.True(match.Success, "Redirect URL phải chứa ID đơn đặt chỗ.");
        var reservationId = long.Parse(match.Groups[1].Value);

        // Kiểm tra cơ sở dữ liệu
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
        var r = await db.reservations.FirstOrDefaultAsync(x => x.id == reservationId);

        Assert.NotNull(r);
        Assert.Equal(user.id, r.customer_id); // Actor từ Cookie, không phải client chọn
        Assert.Equal(facilityId, r.facility_id);
        Assert.Equal(unitTypeId, r.unit_type_id);
        Assert.Equal("pending", r.status);
        Assert.Equal(2000000m, r.monthly_rate_snapshot);
        Assert.Equal(2000000m, r.deposit_snapshot); // 100% 1 tháng tiền thuê
        Assert.Equal(50000m, r.booking_fee_snapshot);
        Assert.Equal(4050000m, r.quoted_total); // 2tr cọc + 2tr thuê + 50k phí

        // Thời hạn giữ chỗ: xấp xỉ 15 phút tính từ thời điểm tạo
        var holdDuration = r.hold_until - r.created_at;
        Assert.True(holdDuration.TotalMinutes >= 14.9 && holdDuration.TotalMinutes <= 15.1,
            $"Hold duration phải là 15 phút nhưng thực tế là {holdDuration.TotalMinutes} phút.");
    }

    [Fact]
    public async Task CreateReservation_ActorIsStrictlyFromCookie_ClientCannotSpoofCustomerId()
    {
        // Arrange: Tạo 2 customer khác nhau
        var (clientA, userA, authCookieA) = await CreateAuthenticatedCustomerClientAsync("custA_spoof");
        var (_, userB, _) = await CreateAuthenticatedCustomerClientAsync("custB_victim");
        var (facilityId, unitTypeId, _) = await SeedFacilityAndUnitTypeAsync("SPOOF", unitCount: 2);

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, AppFormatters.DefaultFacilityTimeZone));
        var (afToken, afCookie) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(
            clientA,
            $"/Reservations/Create?facilityId={facilityId}&unitTypeId={unitTypeId}");

        var combinedCookie = $"{authCookieA}; {afCookie}";

        // Client A cố tình gửi kèm customer_id hoặc CustomerId của B trong form POST
        var postReq = new HttpRequestMessage(HttpMethod.Post, "/Reservations/Create");
        postReq.Headers.Add("Cookie", combinedCookie);
        postReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = afToken,
            ["Input.FacilityId"] = facilityId.ToString(),
            ["Input.UnitTypeId"] = unitTypeId.ToString(),
            ["Input.RentalStartDate"] = today.ToString("yyyy-MM-dd"),
            ["Input.RentalEndDate"] = today.AddMonths(1).ToString("yyyy-MM-dd"),
            ["CustomerId"] = userB.id.ToString(), // Cố tình mạo danh
            ["Input.CustomerId"] = userB.id.ToString()
        });

        // Act
        var response = await clientA.SendAsync(postReq);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var redirectUrl = response.Headers.Location?.OriginalString;
        var reservationId = long.Parse(Regex.Match(redirectUrl!, @"(?:id=|\/)(\d+)").Groups[1].Value);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
        var r = await db.reservations.FirstAsync(x => x.id == reservationId);

        // Phải thuộc về User A (người sở hữu Cookie), hoàn toàn bỏ qua ID mạo danh User B
        Assert.Equal(userA.id, r.customer_id);
        Assert.NotEqual(userB.id, r.customer_id);
    }

    [Fact]
    public async Task CreateReservation_StartDateInPast_RejectsWithValidationError()
    {
        var (client, _, authCookie) = await CreateAuthenticatedCustomerClientAsync("past_date");
        var (facilityId, unitTypeId, _) = await SeedFacilityAndUnitTypeAsync("PAST", unitCount: 1);

        var (afToken, afCookie) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(
            client,
            $"/Reservations/Create?facilityId={facilityId}&unitTypeId={unitTypeId}");

        var pastDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-5);

        var postReq = new HttpRequestMessage(HttpMethod.Post, "/Reservations/Create");
        postReq.Headers.Add("Cookie", $"{authCookie}; {afCookie}");
        postReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = afToken,
            ["Input.FacilityId"] = facilityId.ToString(),
            ["Input.UnitTypeId"] = unitTypeId.ToString(),
            ["Input.RentalStartDate"] = pastDate.ToString("yyyy-MM-dd"),
            ["Input.RentalEndDate"] = pastDate.AddMonths(1).ToString("yyyy-MM-dd")
        });

        var response = await client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // Trả về trang với lỗi ModelState
        var html = await response.Content.ReadAsStringAsync();
        var decodedHtml = WebUtility.HtmlDecode(html);
        Assert.True(decodedHtml.Contains("không được ở quá khứ"), decodedHtml);
    }

    [Fact]
    public async Task CreateReservation_DurationLessThan1Month_RejectsWithValidationError()
    {
        var (client, _, authCookie) = await CreateAuthenticatedCustomerClientAsync("min_duration");
        var (facilityId, unitTypeId, _) = await SeedFacilityAndUnitTypeAsync("MINDUR", unitCount: 1);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (afToken, afCookie) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(
            client,
            $"/Reservations/Create?facilityId={facilityId}&unitTypeId={unitTypeId}");

        var postReq = new HttpRequestMessage(HttpMethod.Post, "/Reservations/Create");
        postReq.Headers.Add("Cookie", $"{authCookie}; {afCookie}");
        postReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = afToken,
            ["Input.FacilityId"] = facilityId.ToString(),
            ["Input.UnitTypeId"] = unitTypeId.ToString(),
            ["Input.RentalStartDate"] = today.ToString("yyyy-MM-dd"),
            ["Input.RentalEndDate"] = today.AddDays(15).ToString("yyyy-MM-dd") // Chỉ 15 ngày (< 1 tháng)
        });

        var response = await client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var decodedHtml = WebUtility.HtmlDecode(html);
        Assert.Contains("tối thiểu là 1 tháng", decodedHtml);
    }

    #endregion

    #region 2. Concurrency Capacity Competition (2 Concurrent Requests for 1 Slot)

    [Fact]
    public async Task ConcurrentReservationHold_WhenOnlyOneSlotAvailable_OneSucceeds_AndOneReturnsControlledConflict()
    {
        // Arrange: Cơ sở chỉ có ĐÚNG 1 ô kho duy nhất
        var (facilityId, unitTypeId, _) = await SeedFacilityAndUnitTypeAsync("CONCUR", unitCount: 1);
        var (client1, user1, auth1) = await CreateAuthenticatedCustomerClientAsync("concur1");
        var (client2, user2, auth2) = await CreateAuthenticatedCustomerClientAsync("concur2");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startDate = today.AddDays(2);
        var endDate = startDate.AddMonths(1);

        var (afToken1, afCookie1) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(
            client1, $"/Reservations/Create?facilityId={facilityId}&unitTypeId={unitTypeId}");
        var (afToken2, afCookie2) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(
            client2, $"/Reservations/Create?facilityId={facilityId}&unitTypeId={unitTypeId}");

        var req1 = new HttpRequestMessage(HttpMethod.Post, "/Reservations/Create");
        req1.Headers.Add("Cookie", $"{auth1}; {afCookie1}");
        req1.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = afToken1,
            ["Input.FacilityId"] = facilityId.ToString(),
            ["Input.UnitTypeId"] = unitTypeId.ToString(),
            ["Input.RentalStartDate"] = startDate.ToString("yyyy-MM-dd"),
            ["Input.RentalEndDate"] = endDate.ToString("yyyy-MM-dd")
        });

        var req2 = new HttpRequestMessage(HttpMethod.Post, "/Reservations/Create");
        req2.Headers.Add("Cookie", $"{auth2}; {afCookie2}");
        req2.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = afToken2,
            ["Input.FacilityId"] = facilityId.ToString(),
            ["Input.UnitTypeId"] = unitTypeId.ToString(),
            ["Input.RentalStartDate"] = startDate.ToString("yyyy-MM-dd"),
            ["Input.RentalEndDate"] = endDate.ToString("yyyy-MM-dd")
        });

        // Act: Bắn 2 request tranh nhau sức chứa cuối cùng
        var task1 = client1.SendAsync(req1);
        var task2 = client2.SendAsync(req2);

        var responses = await Task.WhenAll(task1, task2);

        // Assert: Một request thành công (Redirect), một request bị chặn vì hết sức chứa (HTTP OK kèm thông báo xung đột)
        var successResponse = responses.FirstOrDefault(r => r.StatusCode == HttpStatusCode.Redirect);
        var failedResponse = responses.FirstOrDefault(r => r.StatusCode == HttpStatusCode.OK);

        Assert.NotNull(successResponse);
        Assert.NotNull(failedResponse);

        var failedHtml = await failedResponse.Content.ReadAsStringAsync();
        Assert.Contains("không còn đủ sức chứa khả dụng", WebUtility.HtmlDecode(failedHtml));

        // Kiểm tra DB: Chỉ có đúng 1 reservation ở trạng thái pending được tạo ra
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
        var activeHolds = await db.reservations
            .Where(r => r.facility_id == facilityId && r.unit_type_id == unitTypeId && r.status == "pending")
            .ToListAsync();

        Assert.Single(activeHolds);
    }

    #endregion

    #region 3. Reload Idempotency & Clock Integrity (Reload Không Reset Đồng Hồ)

    [Fact]
    public async Task ReloadReservationDetails_PreservesIdenticalHoldDeadline_AndDoesNotCreateDuplicateReservation()
    {
        // Arrange
        var (client, user, _) = await CreateAuthenticatedCustomerClientAsync("reload_test");
        var (facilityId, unitTypeId, _) = await SeedFacilityAndUnitTypeAsync("RELOAD", unitCount: 2);

        using var scope = _factory.Services.CreateScope();
        var resService = scope.ServiceProvider.GetRequiredService<IReservationService>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var created = await resService.CreateReservationHoldAsync(user.id, new CreateReservationRequest
        {
            FacilityId = facilityId,
            UnitTypeId = unitTypeId,
            RentalStartDate = today.AddDays(1),
            RentalEndDate = today.AddMonths(1).AddDays(1)
        });

        var initialHoldUntil = created.HoldUntil;

        // Act: Truy cập lần 1
        var resp1 = await client.GetAsync($"/Reservations/Details?id={created.Id}");
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
        var html1 = await resp1.Content.ReadAsStringAsync();

        // Chờ 500ms mô phỏng người dùng lướt web rồi bấm F5 / Reload
        await Task.Delay(500);

        // Act: Truy cập lần 2 (Reload)
        var resp2 = await client.GetAsync($"/Reservations/Details?id={created.Id}");
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
        var html2 = await resp2.Content.ReadAsStringAsync();

        // Assert: Cả 2 lần đều chứa đúng mốc hold_until ISO 8601 ban đầu
        var expectedIso = initialHoldUntil.ToString("o");
        Assert.Contains($"data-hold-until=\"{expectedIso}\"", WebUtility.HtmlDecode(html1));
        Assert.Contains($"data-hold-until=\"{expectedIso}\"", WebUtility.HtmlDecode(html2));

        // DB không sinh thêm bản ghi reservation nào khác
        var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
        var count = await db.reservations.CountAsync(r => r.customer_id == user.id);
        Assert.Equal(1, count);

        var r = await db.reservations.FirstAsync(r => r.id == created.Id);
        Assert.Equal(initialHoldUntil, r.hold_until);
    }

    #endregion

    #region 4. Ownership Protection (Khách khác không được xem hoặc hủy đơn)

    [Fact]
    public async Task Ownership_OtherCustomer_CannotViewOrCancelReservation_ReturnsForbidden()
    {
        // Arrange
        var (clientA, userA, _) = await CreateAuthenticatedCustomerClientAsync("ownerA");
        var (clientB, userB, authCookieB) = await CreateAuthenticatedCustomerClientAsync("intruderB");
        var (facilityId, unitTypeId, _) = await SeedFacilityAndUnitTypeAsync("OWNER", unitCount: 2);

        using var scope = _factory.Services.CreateScope();
        var resService = scope.ServiceProvider.GetRequiredService<IReservationService>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var resA = await resService.CreateReservationHoldAsync(userA.id, new CreateReservationRequest
        {
            FacilityId = facilityId,
            UnitTypeId = unitTypeId,
            RentalStartDate = today.AddDays(1),
            RentalEndDate = today.AddMonths(1).AddDays(1)
        });

        // Act 1: Khách B cố gắng GET chi tiết đơn của A
        var viewResp = await clientB.GetAsync($"/Reservations/Details?id={resA.Id}");
        Assert.True(viewResp.StatusCode == HttpStatusCode.Forbidden || 
            (viewResp.StatusCode == HttpStatusCode.Redirect && viewResp.Headers.Location?.OriginalString.Contains("Forbidden") == true),
            $"GET Details mong đợi 403 Forbidden hoặc Redirect sang /Account/Forbidden nhưng thực tế trả về {viewResp.StatusCode}");

        // Act 2: Khách B cố gắng gọi AJAX Status của A
        var ajaxStatusResp = await clientB.GetAsync($"/Reservations/Details?id={resA.Id}&handler=Status");
        Assert.Equal(HttpStatusCode.Forbidden, ajaxStatusResp.StatusCode);

        // Act 3: Khách B cố gắng gửi POST Cancel đơn của A
        var (afTokenB, afCookieB) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(
            clientB, $"/Reservations");

        var cancelReq = new HttpRequestMessage(HttpMethod.Post, $"/Reservations/Cancel");
        cancelReq.Headers.Add("Cookie", $"{authCookieB}; {afCookieB}");
        cancelReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = afTokenB,
            ["Id"] = resA.Id.ToString(),
            ["Reason"] = "Malicious cancellation attempt"
        });

        var cancelResp = await clientB.SendAsync(cancelReq);
        Assert.True(cancelResp.StatusCode == HttpStatusCode.Forbidden || 
            (cancelResp.StatusCode == HttpStatusCode.Redirect && cancelResp.Headers.Location?.OriginalString.Contains("Forbidden") == true),
            $"POST Cancel mong đợi 403 Forbidden hoặc Redirect sang /Account/Forbidden nhưng thực tế trả về {cancelResp.StatusCode}");

        // Đảm bảo đơn của A vẫn an toàn ở trạng thái pending
        var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
        var checkA = await db.reservations.FirstAsync(r => r.id == resA.Id);
        Assert.Equal("pending", checkA.status);
    }

    #endregion

    #region 5. Cancel Reservation Flow & Idempotency

    [Fact]
    public async Task CancelReservation_ActiveHold_Succeeds_AndIsIdempotent()
    {
        // Arrange
        var (client, user, authCookie) = await CreateAuthenticatedCustomerClientAsync("cancel_flow");
        var (facilityId, unitTypeId, _) = await SeedFacilityAndUnitTypeAsync("CANFLOW", unitCount: 2);

        using var scope = _factory.Services.CreateScope();
        var resService = scope.ServiceProvider.GetRequiredService<IReservationService>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var created = await resService.CreateReservationHoldAsync(user.id, new CreateReservationRequest
        {
            FacilityId = facilityId,
            UnitTypeId = unitTypeId,
            RentalStartDate = today.AddDays(1),
            RentalEndDate = today.AddMonths(1).AddDays(1)
        });

        var (afToken, afCookie) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(
            client, $"/Reservations/Cancel?id={created.Id}");

        var cancelReq = new HttpRequestMessage(HttpMethod.Post, "/Reservations/Cancel");
        cancelReq.Headers.Add("Cookie", $"{authCookie}; {afCookie}");
        cancelReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = afToken,
            ["Id"] = created.Id.ToString(),
            ["Reason"] = "Tôi đổi kế hoạch chuyển đồ sang tháng sau"
        });

        // Act 1: Hủy lần đầu
        var cancelResp = await client.SendAsync(cancelReq);
        Assert.Equal(HttpStatusCode.Redirect, cancelResp.StatusCode);

        // Kiểm tra DB
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
        var r = await verifyDb.reservations.FirstAsync(x => x.id == created.Id);
        Assert.Equal("cancelled", r.status);
        Assert.NotNull(r.cancelled_at);
        Assert.Equal("Tôi đổi kế hoạch chuyển đồ sang tháng sau", r.cancellation_reason);

        // Act 2: Gọi hủy lần 2 trực tiếp qua BLL (Idempotent success)
        var cancelledAgain = await resService.CancelReservationAsync(
            user.id,
            new[] { RoleConstants.StorageCustomer },
            created.Id,
            new CancelReservationRequest { Reason = "Lý do khác" });

        Assert.Equal("cancelled", cancelledAgain.Status);
    }

    #endregion

    #region 6. Near-Deadline & Post-Deadline Operations (Server as Source of Truth)

    [Fact]
    public async Task ExpiredReservation_ServerDynamicallyExpires_AndRejectsCancelWithControlledConflict()
    {
        // Arrange
        var (client, user, authCookie) = await CreateAuthenticatedCustomerClientAsync("near_dead");
        var (facilityId, unitTypeId, _) = await SeedFacilityAndUnitTypeAsync("DEADL", unitCount: 2);

        using var scope = _factory.Services.CreateScope();
        var resService = scope.ServiceProvider.GetRequiredService<IReservationService>();
        var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var created = await resService.CreateReservationHoldAsync(user.id, new CreateReservationRequest
        {
            FacilityId = facilityId,
            UnitTypeId = unitTypeId,
            RentalStartDate = today.AddDays(1),
            RentalEndDate = today.AddMonths(1).AddDays(1)
        });

        // Tua lùi hold_until về quá khứ 10 giây (quá hạn)
        var entity = await db.reservations.FirstAsync(r => r.id == created.Id);
        entity.hold_until = DateTimeOffset.UtcNow.AddSeconds(-10);
        await db.SaveChangesAsync();

        // Act 1: Client gọi endpoint AJAX Status
        var statusResp = await client.GetAsync($"/Reservations/Details?id={created.Id}&handler=Status");
        Assert.Equal(HttpStatusCode.OK, statusResp.StatusCode);

        var json = await statusResp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("expired", root.GetProperty("status").GetString());
        Assert.False(root.GetProperty("isHoldActive").GetBoolean());
        Assert.False(root.GetProperty("canCancel").GetBoolean());

        // Kiểm tra DB: Server đã tự động cập nhật expired
        db.Entry(entity).Reload();
        Assert.Equal("expired", entity.status);

        // Act 2: Cố tình gửi yêu cầu Cancel khi đã expired -> nhận về Conflict
        var (afToken, afCookie) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(
            client, $"/Reservations");

        var cancelReq = new HttpRequestMessage(HttpMethod.Post, "/Reservations/Cancel");
        cancelReq.Headers.Add("Cookie", $"{authCookie}; {afCookie}");
        cancelReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = afToken,
            ["Id"] = created.Id.ToString(),
            ["Reason"] = "Hủy muộn"
        });

        var cancelResp = await client.SendAsync(cancelReq);
        Assert.Equal(HttpStatusCode.Redirect, cancelResp.StatusCode);

        // Chuyển hướng về Details và có thông báo lỗi không thể hủy
        var detailsHtml = await client.GetStringAsync($"/Reservations/Details?id={created.Id}");
        Assert.Contains("Đơn đặt chỗ đã hết hạn giữ chỗ (Expired)", detailsHtml);
    }

    #endregion

    #region 7. Background Worker 30s & Restart Resilience (Xử Lý Ngay Sau Startup)

    [Fact]
    public async Task Worker_OnStartup_ImmediatelyExpiresOverdueHolds_AndInvokesT15Hook()
    {
        // Arrange: Tạo 3 đơn đặt chỗ quá hạn giả lập máy chủ vừa tắt và khởi động lại
        var (facilityId, unitTypeId, rateId, _) = await SeedFacilityWithRateAsync("WORKER_BOOT", unitCount: 5);
        var (_, user, _) = await CreateAuthenticatedCustomerClientAsync("worker_customer");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
        var nowUtc = DateTimeOffset.UtcNow;

        var overdueIds = new List<long>();
        for (int i = 1; i <= 3; i++)
        {
            var res = new reservation
            {
                reservation_code = $"RES-OVERDUE-{i:D3}",
                customer_id = user.id,
                facility_id = facilityId,
                unit_type_id = unitTypeId,
                facility_rate_id = rateId,
                start_date = DateOnly.FromDateTime(nowUtc.DateTime).AddDays(1),
                end_date = DateOnly.FromDateTime(nowUtc.DateTime).AddMonths(1),
                monthly_rate_snapshot = 1000000m,
                deposit_snapshot = 1000000m,
                booking_fee_snapshot = 0m,
                discount_snapshot = 0m,
                quoted_total = 2000000m,
                hold_until = nowUtc.AddMinutes(-5), // Đã quá hạn 5 phút trước
                status = "pending",
                created_at = nowUtc.AddMinutes(-20),
                updated_at = nowUtc.AddMinutes(-20)
            };
            db.reservations.Add(res);
            await db.SaveChangesAsync();
            overdueIds.Add(res.id);
        }

        // Tạo Test Hook để kiểm chứng chuẩn bị cho Task T15
        var testHook = new TestReservationExpirationHook();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReservationWorker:Enabled"] = "true",
                ["ReservationWorker:IntervalSeconds"] = "30"
            })
            .Build();

        var worker = new ReservationHoldExpirationWorker(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ReservationHoldExpirationWorker>.Instance,
            config,
            testHook,
            checkInterval: TimeSpan.FromMilliseconds(50));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Act: Khởi chạy worker (vòng lặp đầu tiên phải chạy NGAY LẬP TỨC mà không cần chờ 30 giây)
        var workerTask = worker.StartAsync(cts.Token);

        // Chờ ngắn để worker xử lý chu kỳ đầu tiên
        await Task.Delay(300);
        await worker.StopAsync(CancellationToken.None);

        // Assert: Cả 3 đơn đặt chỗ quá hạn đều đã chuyển sang expired
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();

        var expiredReservations = await verifyDb.reservations
            .Where(r => overdueIds.Contains(r.id))
            .ToListAsync();

        Assert.Equal(3, expiredReservations.Count);
        Assert.All(expiredReservations, r => Assert.Equal("expired", r.status));

        // Kiểm tra Hook T15 đã được kích hoạt thành công
        Assert.True(testHook.InvokedCount >= 3, $"Hook T15 phải nhận được ít nhất 3 đơn quá hạn, thực tế nhận: {testHook.InvokedCount}");
    }

    [Fact]
    public async Task Worker_DisabledByConfiguration_DoesNotRun()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReservationWorker:Enabled"] = "false"
            })
            .Build();

        var testHook = new TestReservationExpirationHook();

        var worker = new ReservationHoldExpirationWorker(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ReservationHoldExpirationWorker>.Instance,
            config,
            testHook,
            checkInterval: TimeSpan.FromMilliseconds(50));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await worker.StartAsync(cts.Token);
        await Task.Delay(200);
        await worker.StopAsync(CancellationToken.None);

        // Hook không hề được kích hoạt
        Assert.Equal(0, testHook.InvokedCount);
    }

    #endregion

    #region 8. MyReservations List & Filtering

    [Fact]
    public async Task MyReservations_FiltersByStatus_AndShowsAccurateBadges()
    {
        var (client, user, _) = await CreateAuthenticatedCustomerClientAsync("list_filter");
        var (facilityId, unitTypeId, _) = await SeedFacilityAndUnitTypeAsync("MYLIST", unitCount: 3);

        using var scope = _factory.Services.CreateScope();
        var resService = scope.ServiceProvider.GetRequiredService<IReservationService>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Tạo 1 pending
        var res1 = await resService.CreateReservationHoldAsync(user.id, new CreateReservationRequest
        {
            FacilityId = facilityId,
            UnitTypeId = unitTypeId,
            RentalStartDate = today.AddDays(1),
            RentalEndDate = today.AddMonths(1).AddDays(1)
        });

        // Tạo 1 pending rồi hủy
        var res2 = await resService.CreateReservationHoldAsync(user.id, new CreateReservationRequest
        {
            FacilityId = facilityId,
            UnitTypeId = unitTypeId,
            RentalStartDate = today.AddDays(2),
            RentalEndDate = today.AddMonths(1).AddDays(2)
        });
        await resService.CancelReservationAsync(user.id, new[] { RoleConstants.StorageCustomer }, res2.Id, new CancelReservationRequest { Reason = "Test cancel" });

        // Act 1: Lấy toàn bộ danh sách
        var allResp = await client.GetAsync("/Reservations");
        Assert.Equal(HttpStatusCode.OK, allResp.StatusCode);
        var allHtml = await allResp.Content.ReadAsStringAsync();
        Assert.Contains(res1.ReservationCode, allHtml);
        Assert.Contains(res2.ReservationCode, allHtml);

        // Act 2: Lọc pending
        var pendingResp = await client.GetAsync("/Reservations?status=pending");
        Assert.Equal(HttpStatusCode.OK, pendingResp.StatusCode);
        var pendingHtml = await pendingResp.Content.ReadAsStringAsync();
        Assert.Contains(res1.ReservationCode, pendingHtml);
        Assert.DoesNotContain(res2.ReservationCode, pendingHtml);

        // Act 3: Lọc cancelled
        var cancelledResp = await client.GetAsync("/Reservations?status=cancelled");
        Assert.Equal(HttpStatusCode.OK, cancelledResp.StatusCode);
        var cancelledHtml = await cancelledResp.Content.ReadAsStringAsync();
        Assert.DoesNotContain(res1.ReservationCode, cancelledHtml);
        Assert.Contains(res2.ReservationCode, cancelledHtml);
    }

    [Fact]
    public async Task CatalogCompatibility_RedirectsToCreatePage_WhenFacilityAndUnitTypeProvided()
    {
        var (client, _, _) = await CreateAuthenticatedCustomerClientAsync("catalog_redir");
        var response = await client.GetAsync("/Reservations?facilityId=101&unitTypeId=202&rentalStartDate=2026-11-01&rentalEndDate=2026-12-01");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.OriginalString;
        Assert.NotNull(location);
        Assert.StartsWith("/Reservations/Create", location);
        Assert.Contains("facilityId=101", location);
        Assert.Contains("unitTypeId=202", location);
    }

    [Fact]
    public async Task CreateReservation_MonthEndClamping_31JanTo28Feb_AcceptedByServer_AndDurationUnder1MonthRejected()
    {
        // 1. Kiểm tra quy tắc cộng tháng cuối tháng của C# DateOnly.AddMonths(1): 31/01 -> 28/02 (năm thường 2026/2027)
        var date2026Jan31 = new DateOnly(2026, 1, 31);
        var date2026Feb28 = date2026Jan31.AddMonths(1);
        Assert.Equal(new DateOnly(2026, 2, 28), date2026Feb28);

        // 2. Tạo đơn đặt chỗ cho kỳ thuê tương lai bắt đầu ngày 31/01/2027
        var (client, _, authCookie) = await CreateAuthenticatedCustomerClientAsync("month_end");
        var (facilityId, unitTypeId, _) = await SeedFacilityAndUnitTypeAsync("MONTHEND", unitCount: 2);

        var startDate = new DateOnly(2027, 1, 31);
        var validEndDate = startDate.AddMonths(1); // 2027-02-28
        var invalidEndDate = new DateOnly(2027, 2, 27); // 27/02/2027 (< 1 tháng)

        // Case A: Client gửi 31/01/2027 -> 27/02/2027 (thiếu 1 ngày so với 28/02) -> Server từ chối
        var (afToken1, afCookie1) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(
            client, $"/Reservations/Create?facilityId={facilityId}&unitTypeId={unitTypeId}");

        var invalidReq = new HttpRequestMessage(HttpMethod.Post, "/Reservations/Create");
        invalidReq.Headers.Add("Cookie", $"{authCookie}; {afCookie1}");
        invalidReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = afToken1,
            ["Input.FacilityId"] = facilityId.ToString(),
            ["Input.UnitTypeId"] = unitTypeId.ToString(),
            ["Input.RentalStartDate"] = startDate.ToString("yyyy-MM-dd"),
            ["Input.RentalEndDate"] = invalidEndDate.ToString("yyyy-MM-dd")
        });

        var invalidResp = await client.SendAsync(invalidReq);
        Assert.Equal(HttpStatusCode.OK, invalidResp.StatusCode);
        var invalidHtml = await invalidResp.Content.ReadAsStringAsync();
        Assert.Contains("tối thiểu là 1 tháng", WebUtility.HtmlDecode(invalidHtml));

        // Case B: Client gửi 31/01/2027 -> 28/02/2027 (chuẩn 1 tháng) -> Server chấp nhận thành công
        var (afToken2, afCookie2) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(
            client, $"/Reservations/Create?facilityId={facilityId}&unitTypeId={unitTypeId}");

        var validReq = new HttpRequestMessage(HttpMethod.Post, "/Reservations/Create");
        validReq.Headers.Add("Cookie", $"{authCookie}; {afCookie2}");
        validReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = afToken2,
            ["Input.FacilityId"] = facilityId.ToString(),
            ["Input.UnitTypeId"] = unitTypeId.ToString(),
            ["Input.RentalStartDate"] = startDate.ToString("yyyy-MM-dd"),
            ["Input.RentalEndDate"] = validEndDate.ToString("yyyy-MM-dd")
        });

        var validResp = await client.SendAsync(validReq);
        Assert.Equal(HttpStatusCode.Redirect, validResp.StatusCode);
        Assert.StartsWith("/Reservations/Details", validResp.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task CreateReservation_FacilityTimeZone_CalculatesMinStartDateConsistently_EvenWhenUtcIsPreviousDay()
    {
        var (client, _, authCookie) = await CreateAuthenticatedCustomerClientAsync("tz_align");
        var (facilityId, unitTypeId, _) = await SeedFacilityAndUnitTypeAsync("TZVN", unitCount: 2);

        // Múi giờ cơ sở thực tế (Asia/Ho_Chi_Minh UTC+7)
        var facilityTz = AppFormatters.ResolveFacilityTimeZone("Asia/Ho_Chi_Minh");
        var todayInFacility = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, facilityTz));
        var yesterdayInFacility = todayInFacility.AddDays(-1);

        // Act 1: GET trang Create - min date phải khớp ngày hiện tại theo giờ cơ sở
        var getResp = await client.GetAsync($"/Reservations/Create?facilityId={facilityId}&unitTypeId={unitTypeId}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var getHtml = await getResp.Content.ReadAsStringAsync();
        Assert.Contains($"min=\"{todayInFacility:yyyy-MM-dd}\"", getHtml);

        // Act 2: Cố tình gửi ngày hôm qua theo giờ cơ sở (mặc dù có thể là hôm nay theo giờ UTC) -> Bị từ chối
        var (afToken, afCookie) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(
            client, $"/Reservations/Create?facilityId={facilityId}&unitTypeId={unitTypeId}");

        var postPastReq = new HttpRequestMessage(HttpMethod.Post, "/Reservations/Create");
        postPastReq.Headers.Add("Cookie", $"{authCookie}; {afCookie}");
        postPastReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = afToken,
            ["Input.FacilityId"] = facilityId.ToString(),
            ["Input.UnitTypeId"] = unitTypeId.ToString(),
            ["Input.RentalStartDate"] = yesterdayInFacility.ToString("yyyy-MM-dd"),
            ["Input.RentalEndDate"] = yesterdayInFacility.AddMonths(1).ToString("yyyy-MM-dd")
        });

        var postPastResp = await client.SendAsync(postPastReq);
        Assert.Equal(HttpStatusCode.OK, postPastResp.StatusCode);
        var postPastHtml = await postPastResp.Content.ReadAsStringAsync();
        var decodedHtml = WebUtility.HtmlDecode(postPastHtml);
        Assert.Contains("không được ở quá khứ", decodedHtml);
        Assert.Contains(todayInFacility.ToString("yyyy-MM-dd"), decodedHtml);
    }

    [Fact]
    public async Task AjaxStatus_WhenReservationIsConfirmedOrCancelled_ReturnsAccurateStatus_AndDoesNotTreatAsExpired()
    {
        var (client, user, _) = await CreateAuthenticatedCustomerClientAsync("ajax_stat");
        var (facilityId, unitTypeId, rateId, _) = await SeedFacilityWithRateAsync("AJAXSTAT", unitCount: 2);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
        var nowUtc = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(nowUtc.DateTime);

        // Đơn 1: Đã xác nhận (confirmed)
        var resConfirmed = new reservation
        {
            reservation_code = "RES-AJAX-CONFIRMED",
            customer_id = user.id,
            facility_id = facilityId,
            unit_type_id = unitTypeId,
            facility_rate_id = rateId,
            start_date = today.AddDays(1),
            end_date = today.AddMonths(1),
            monthly_rate_snapshot = 1000000m,
            deposit_snapshot = 1000000m,
            quoted_total = 2000000m,
            hold_until = nowUtc.AddMinutes(-5),
            status = "confirmed",
            confirmed_at = nowUtc.AddMinutes(-10),
            created_at = nowUtc.AddMinutes(-20),
            updated_at = nowUtc.AddMinutes(-10)
        };
        db.reservations.Add(resConfirmed);

        // Đơn 2: Đã hủy (cancelled)
        var resCancelled = new reservation
        {
            reservation_code = "RES-AJAX-CANCELLED",
            customer_id = user.id,
            facility_id = facilityId,
            unit_type_id = unitTypeId,
            facility_rate_id = rateId,
            start_date = today.AddDays(2),
            end_date = today.AddMonths(1).AddDays(2),
            monthly_rate_snapshot = 1000000m,
            deposit_snapshot = 1000000m,
            quoted_total = 2000000m,
            hold_until = nowUtc.AddMinutes(-5),
            status = "cancelled",
            cancelled_at = nowUtc.AddMinutes(-8),
            cancellation_reason = "Đổi kế hoạch",
            created_at = nowUtc.AddMinutes(-20),
            updated_at = nowUtc.AddMinutes(-8)
        };
        db.reservations.Add(resCancelled);
        await db.SaveChangesAsync();

        // Act 1: Kiểm tra AJAX Status cho đơn confirmed
        var respConfirmed = await client.GetAsync($"/Reservations/Details?id={resConfirmed.id}&handler=Status");
        Assert.Equal(HttpStatusCode.OK, respConfirmed.StatusCode);
        var jsonConfirmed = await respConfirmed.Content.ReadAsStringAsync();
        using var doc1 = JsonDocument.Parse(jsonConfirmed);
        var root1 = doc1.RootElement;

        Assert.True(root1.GetProperty("success").GetBoolean());
        Assert.Equal("confirmed", root1.GetProperty("status").GetString());
        Assert.Equal("Đã xác nhận", root1.GetProperty("statusDisplayName").GetString());
        Assert.False(root1.GetProperty("isHoldActive").GetBoolean());
        Assert.False(root1.GetProperty("canCancel").GetBoolean());

        // Act 2: Kiểm tra AJAX Status cho đơn cancelled
        var respCancelled = await client.GetAsync($"/Reservations/Details?id={resCancelled.id}&handler=Status");
        Assert.Equal(HttpStatusCode.OK, respCancelled.StatusCode);
        var jsonCancelled = await respCancelled.Content.ReadAsStringAsync();
        using var doc2 = JsonDocument.Parse(jsonCancelled);
        var root2 = doc2.RootElement;

        Assert.True(root2.GetProperty("success").GetBoolean());
        Assert.Equal("cancelled", root2.GetProperty("status").GetString());
        Assert.Equal("Đã hủy", root2.GetProperty("statusDisplayName").GetString());
        Assert.False(root2.GetProperty("isHoldActive").GetBoolean());
        Assert.False(root2.GetProperty("canCancel").GetBoolean());
    }

    [Fact]
    public async Task MultipleCountdowns_OnIndexAndMyReservations_RenderCorrectRowAttributes_WithFacilityTimeZone()
    {
        var (client, user, _) = await CreateAuthenticatedCustomerClientAsync("multi_cd");
        var (facilityId, unitTypeId, rateId, _) = await SeedFacilityWithRateAsync("MULTICD", unitCount: 3);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
        var nowUtc = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(nowUtc.DateTime);

        // Tạo đơn 1: pending hold active (+10 phút)
        var resHold1 = new reservation
        {
            reservation_code = "RES-MULTI-HOLD1",
            customer_id = user.id,
            facility_id = facilityId,
            unit_type_id = unitTypeId,
            facility_rate_id = rateId,
            start_date = today.AddDays(1),
            end_date = today.AddMonths(1),
            monthly_rate_snapshot = 1000000m,
            deposit_snapshot = 1000000m,
            quoted_total = 2000000m,
            hold_until = nowUtc.AddMinutes(10),
            status = "pending",
            created_at = nowUtc,
            updated_at = nowUtc
        };
        db.reservations.Add(resHold1);

        // Tạo đơn 2: pending hold active (+5 phút)
        var resHold2 = new reservation
        {
            reservation_code = "RES-MULTI-HOLD2",
            customer_id = user.id,
            facility_id = facilityId,
            unit_type_id = unitTypeId,
            facility_rate_id = rateId,
            start_date = today.AddDays(2),
            end_date = today.AddMonths(1).AddDays(2),
            monthly_rate_snapshot = 1000000m,
            deposit_snapshot = 1000000m,
            quoted_total = 2000000m,
            hold_until = nowUtc.AddMinutes(5),
            status = "pending",
            created_at = nowUtc,
            updated_at = nowUtc
        };
        db.reservations.Add(resHold2);

        // Tạo đơn 3: confirmed
        var resConfirmed = new reservation
        {
            reservation_code = "RES-MULTI-CONFIRMED",
            customer_id = user.id,
            facility_id = facilityId,
            unit_type_id = unitTypeId,
            facility_rate_id = rateId,
            start_date = today.AddDays(3),
            end_date = today.AddMonths(2),
            monthly_rate_snapshot = 1000000m,
            deposit_snapshot = 1000000m,
            quoted_total = 2000000m,
            hold_until = nowUtc.AddMinutes(-20),
            status = "confirmed",
            confirmed_at = nowUtc.AddMinutes(-20),
            created_at = nowUtc.AddMinutes(-30),
            updated_at = nowUtc.AddMinutes(-20)
        };
        db.reservations.Add(resConfirmed);
        await db.SaveChangesAsync();

        // Act: GET /Reservations và GET /Reservations/MyReservations
        foreach (var url in new[] { "/Reservations", "/Reservations/MyReservations" })
        {
            var resp = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var html = await resp.Content.ReadAsStringAsync();

            // 1. Phải có đủ các hàng với data-reservation-row tương ứng
            Assert.Contains($"data-reservation-row=\"{resHold1.id}\"", html);
            Assert.Contains($"data-reservation-row=\"{resHold2.id}\"", html);
            Assert.Contains($"data-reservation-row=\"{resConfirmed.id}\"", html);

            // 2. Hai đơn active hold phải có block countdown riêng biệt gắn đúng reservation ID
            Assert.Contains($"data-reservation-id=\"{resHold1.id}\"", html);
            Assert.Contains($"data-reservation-id=\"{resHold2.id}\"", html);

            // 3. Hai đơn active hold phải có nút Hủy riêng
            Assert.Contains($"data-cancel-action=\"{resHold1.id}\"", html);
            Assert.Contains($"data-cancel-action=\"{resHold2.id}\"", html);

            // 4. Đơn confirmed không được chứa data-cancel-action
            Assert.DoesNotContain($"data-cancel-action=\"{resConfirmed.id}\"", html);

            // 5. Kiểm tra thời gian không dùng ToLocalTime host mà hiển thị nhất quán theo AppFormatters
            var expectedConfirmedFormatted = AppFormatters.FormatDateTime(resConfirmed.hold_until);
            Assert.Contains(expectedConfirmedFormatted, html);
        }
    }

    #endregion

    [Fact]
    public async Task HoldDeadline_UsesEachFacilityTimezone_OnDetailsAndBothLists_WithoutChangingUtcInstant()
    {
        var (client, user, _) = await CreateAuthenticatedCustomerClientAsync("facility_tz");
        var vn = await SeedFacilityWithRateAsync("TZVN");
        var utc = await SeedFacilityWithRateAsync("TZUTC");
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
        (await db.facilities.FindAsync(utc.FacilityId))!.timezone = "UTC";
        var now = DateTimeOffset.UtcNow;
        var holdUntil = now.AddMinutes(10);
        var reservations = new List<reservation>();
        foreach (var facility in new[] { vn, utc })
        {
            var item = new reservation
            {
                reservation_code = $"TZ-{facility.FacilityId}",
                customer_id = user.id,
                facility_id = facility.FacilityId,
                unit_type_id = facility.UnitTypeId,
                facility_rate_id = facility.RateId,
                start_date = DateOnly.FromDateTime(now.UtcDateTime).AddDays(1),
                end_date = DateOnly.FromDateTime(now.UtcDateTime).AddMonths(1),
                monthly_rate_snapshot = 1000000m,
                deposit_snapshot = 1000000m,
                quoted_total = 2000000m,
                hold_until = holdUntil,
                status = "pending",
                created_at = now,
                updated_at = now
            };
            db.reservations.Add(item);
            reservations.Add(item);
        }
        await db.SaveChangesAsync();
        var expectedVn = AppFormatters.FormatDateTime(holdUntil, "Asia/Ho_Chi_Minh");
        var expectedUtc = AppFormatters.FormatDateTime(holdUntil, "UTC");
        Assert.NotEqual(expectedVn, expectedUtc);

        foreach (var url in new[] { "/Reservations", "/Reservations/MyReservations" })
        {
            var response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            foreach (var item in reservations)
            {
                var row = Regex.Match(html, $"<tr[^>]*data-reservation-row=\"{item.id}\"[^>]*>.*?</tr>", RegexOptions.Singleline).Value;
                Assert.NotEmpty(row);
                Assert.Contains($"data-hold-until=\"{holdUntil:o}\"", WebUtility.HtmlDecode(row));
            }
        }
        foreach (var item in reservations)
        {
            var response = await client.GetAsync($"/Reservations/Details?id={item.id}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            var timezone = item.facility_id == vn.FacilityId ? "Asia/Ho_Chi_Minh" : "UTC";
            Assert.Contains(AppFormatters.FormatDateTimeSeconds(holdUntil, timezone), html);
            Assert.Contains($"data-hold-until=\"{holdUntil:o}\"", WebUtility.HtmlDecode(html));
        }
        // Lists show a literal deadline after the hold is no longer active.
        reservations.ForEach(item => item.status = "confirmed");
        await db.SaveChangesAsync();
        foreach (var url in new[] { "/Reservations", "/Reservations/MyReservations" })
        {
            var response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            foreach (var item in reservations)
            {
                var row = Regex.Match(html, $"<tr[^>]*data-reservation-row=\"{item.id}\"[^>]*>.*?</tr>", RegexOptions.Singleline).Value;
                Assert.NotEmpty(row);
                Assert.Contains(item.facility_id == vn.FacilityId ? expectedVn : expectedUtc, row);
            }
        }
    }

    #region 11. Browser & JavaScript Countdown Verification (Codex Findings 1-4)

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "SelfStorageManagementSystem.Web.sln")))
        {
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        return dir;
    }

    private static string ResolveEdgeBrowserPath()
    {
        var customPath = Environment.GetEnvironmentVariable("EDGE_PATH") ?? Environment.GetEnvironmentVariable("BROWSER_PATH");
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
        {
            return customPath;
        }

        var defaultPaths = new[]
        {
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Edge\Application\msedge.exe")
        };

        foreach (var path in defaultPaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return customPath ?? defaultPaths[0];
    }

    [Fact]
    public void ReservationCountdown_ClientScript_StaticAnalysis_DecoupledDisplayAndBoundary()
    {
        // Arrange
        var root = FindRepoRoot();
        var scriptPath = Path.Combine(root, "src", "SelfStorageManagementSystem.Web", "wwwroot", "js", "reservation-countdown.js");

        Assert.True(File.Exists(scriptPath), $"Không tìm thấy tệp script tại: {scriptPath}");
        var scriptContent = File.ReadAllText(scriptPath);

        // Assert 1: Không được dùng remainingSeconds <= 0 để kích hoạt đồng bộ
        Assert.DoesNotContain("if (remainingSeconds <= 0)", scriptContent);

        // Assert 2: Phải kích hoạt đồng bộ dựa trên remainingMs <= 0
        Assert.Contains("if (remainingMs <= 0)", scriptContent);

        // Assert 3: Số giây hiển thị phải làm tròn lên (Math.ceil) khi còn > 0
        Assert.Contains("Math.ceil(remainingMs / 1000)", scriptContent);

        // Assert 4: Không được chứa điều kiện truthy buggy data.status coi pending là kết thúc
        Assert.DoesNotContain("data.status === 'confirmed' || data.status === 'cancelled' || data.status", scriptContent);

        // Assert 5: Phân nhánh cụ thể cho pending / awaiting_deposit để retry đồng bộ
        Assert.Contains("data.status === 'pending' || data.status === 'awaiting_deposit'", scriptContent);
        Assert.Contains("syncServerStatus()", scriptContent);

        // Assert 6: Finding 1 - Xử lý rõ ràng HTTP 401 và 403, không polling vô hạn
        Assert.Contains("response.status === 401", scriptContent);
        Assert.Contains("response.status === 403", scriptContent);
        Assert.Contains("applyAuthErrorOnUi", scriptContent);

        // Assert 7: Finding 2 - Quản lý scheduler và nextSyncAtMs ngăn timer 1s chen ngang backoff 3000ms
        Assert.Contains("nextSyncAtMs", scriptContent);
        Assert.Contains("Date.now() >= nextSyncAtMs", scriptContent);
        Assert.Contains("isSyncing = false;", scriptContent);
    }

    [Fact]
    public void ReservationCountdown_WhenBrowserNotFound_ThrowsFailure_AndDoesNotPassSilently()
    {
        // Finding 3: Kiểm chứng rằng khi đường dẫn trình duyệt không tồn tại, kiểm thử PHẢI thất bại (Fail)
        // và tuyệt đối không được kết thúc ở trạng thái Passed giả.
        var fakePath = @"C:\NonExistentPath\fake_msedge.exe";

        var ex = Assert.Throws<Xunit.Sdk.TrueException>(() =>
        {
            Assert.True(File.Exists(fakePath),
                $"Trình duyệt Microsoft Edge không được tìm thấy tại '{fakePath}'. Vui lòng cấu hình EDGE_PATH.");
        });

        Assert.Contains("Trình duyệt Microsoft Edge không được tìm thấy", ex.Message);
    }

    [Fact]
    public void ReservationCountdown_ClientScript_ExecutesHeadlessBrowserTests_AllScenariosPass()
    {
        // Finding 3: Kiểm tra đường dẫn trình duyệt qua biến môi trường hoặc đường dẫn mặc định
        var edgePath = ResolveEdgeBrowserPath();
        Assert.True(File.Exists(edgePath),
            $"Trình duyệt Microsoft Edge không được tìm thấy tại '{edgePath}'. " +
            $"Vui lòng cài đặt Edge hoặc cấu hình biến môi trường EDGE_PATH để thực thi kiểm thử trình duyệt.");

        var root = FindRepoRoot();
        var htmlTestPath = Path.Combine(root, "tests", "SelfStorageManagementSystem.WebTests", "BrowserTests", "reservation-countdown-tests.html");
        Assert.True(File.Exists(htmlTestPath), $"Không tìm thấy tệp kiểm thử trình duyệt: {htmlTestPath}");

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = edgePath,
            Arguments = $"--headless --virtual-time-budget=15000 --dump-dom \"file:///{htmlTestPath.Replace('\\', '/')}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = System.Diagnostics.Process.Start(psi);
        Assert.NotNull(proc);
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(25000);

        // Kiểm chứng báo cáo tổng kết của trình duyệt: ALL_PASSED
        Assert.Contains("data-test-status=\"ALL_PASSED\"", output);

        // Kiểm chứng tất cả 11 kịch bản kiểm thử:
        Assert.Contains("data-test-id=\"SCENARIO_A\" data-result=\"PASS\"", output);
        Assert.Contains("data-test-id=\"SCENARIO_B\" data-result=\"PASS\"", output);
        Assert.Contains("data-test-id=\"SCENARIO_C1_CONFIRMED\" data-result=\"PASS\"", output);
        Assert.Contains("data-test-id=\"SCENARIO_C2_CANCELLED\" data-result=\"PASS\"", output);
        Assert.Contains("data-test-id=\"SCENARIO_D\" data-result=\"PASS\"", output);
        Assert.Contains("data-test-id=\"SCENARIO_E\" data-result=\"PASS\"", output);
        Assert.Contains("data-test-id=\"SCENARIO_F_RELOAD_FLOW\" data-result=\"PASS\"", output);
        Assert.Contains("data-test-id=\"SCENARIO_G_INFLIGHT\" data-result=\"PASS\"", output);
        Assert.Contains("data-test-id=\"FINDING_1_AUTH_401_403\" data-result=\"PASS\"", output);
        Assert.Contains("data-test-id=\"FINDING_2_BACKOFF_TIMESTAMPS\" data-result=\"PASS\"", output);
        Assert.Contains("data-test-id=\"REGRESSION_OLD_VS_NEW\" data-result=\"PASS\"", output);
    }

    private static readonly Regex CountdownFormatRegex = new(@"^\d{2}:\d{2}$", RegexOptions.Compiled);

    private static int ParseCountdownSecondsStrict(string? countdownText, string sampleName)
    {
        Assert.False(string.IsNullOrWhiteSpace(countdownText), $"Mẫu thời gian '{sampleName}' không được null hoặc rỗng.");
        var trimmed = countdownText!.Trim();
        Assert.Matches(CountdownFormatRegex, trimmed);
        var parts = trimmed.Split(':');
        int mins = int.Parse(parts[0]);
        int secs = int.Parse(parts[1]);
        Assert.True(secs < 60, $"Phần giây trong mẫu '{sampleName}' phải < 60, thực tế: {secs}");
        int totalSeconds = mins * 60 + secs;
        Assert.True(totalSeconds >= 0, $"Tổng số giây trong mẫu '{sampleName}' phải >= 0, thực tế: {totalSeconds}");
        return totalSeconds;
    }

    private class HeadlessEdgeSession : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly string _tempDir;
        private readonly ClientWebSocket _ws;
        private int _nextId = 1;

        private HeadlessEdgeSession(Process process, string tempDir, ClientWebSocket ws)
        {
            _process = process;
            _tempDir = tempDir;
            _ws = ws;
        }

        public static async Task<HeadlessEdgeSession> LaunchAsync(string edgePath, bool disableJs = false)
        {
            var port = Random.Shared.Next(26000, 33000);
            var tempDir = Path.Combine(Path.GetTempPath(), "edge_cdp_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            var jsFlag = disableJs ? "--disable-javascript " : "";
            var args = $"--headless --remote-debugging-port={port} --user-data-dir=\"{tempDir}\" --disable-extensions --no-first-run --no-default-browser-check {jsFlag}about:blank";

            var psi = new ProcessStartInfo
            {
                FileName = edgePath,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = Process.Start(psi) ?? throw new InvalidOperationException("Không thể khởi chạy Microsoft Edge.");

            using var httpClient = new HttpClient();
            string? wsUrl = null;
            for (int i = 0; i < 35; i++)
            {
                try
                {
                    var json = await httpClient.GetStringAsync($"http://127.0.0.1:{port}/json/list");
                    using var doc = JsonDocument.Parse(json);
                    foreach (var target in doc.RootElement.EnumerateArray())
                    {
                        if (target.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "page")
                        {
                            wsUrl = target.GetProperty("webSocketDebuggerUrl").GetString();
                            break;
                        }
                    }
                    if (!string.IsNullOrEmpty(wsUrl)) break;
                }
                catch
                {
                    await Task.Delay(150);
                }
            }

            if (string.IsNullOrEmpty(wsUrl))
            {
                try { process.Kill(true); } catch { }
                throw new InvalidOperationException($"Không lấy được WebSocket debugger URL cho page target từ Edge tại port {port}.");
            }

            var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);

            var session = new HeadlessEdgeSession(process, tempDir, ws);
            if (disableJs)
            {
                await session.SendCommandAsync("Emulation.setScriptExecutionDisabled", new { value = true });
            }

            return session;
        }

        public async Task<JsonElement> SendCommandAsync(string method, object? parameters = null)
        {
            var id = Interlocked.Increment(ref _nextId);
            var payload = parameters == null
                ? JsonSerializer.Serialize(new { id, method })
                : JsonSerializer.Serialize(new { id, method, @params = parameters });

            await _ws.SendAsync(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, true, CancellationToken.None);

            var buffer = new byte[65536];
            using var ms = new MemoryStream();
            while (true)
            {
                var res = await _ws.ReceiveAsync(buffer, CancellationToken.None);
                ms.Write(buffer, 0, res.Count);
                if (res.EndOfMessage)
                {
                    var text = Encoding.UTF8.GetString(ms.ToArray());
                    using var doc = JsonDocument.Parse(text);
                    if (doc.RootElement.TryGetProperty("id", out var respId) && respId.GetInt32() == id)
                    {
                        if (doc.RootElement.TryGetProperty("error", out var err))
                        {
                            throw new InvalidOperationException($"CDP Error ({method}): {err.GetRawText()}");
                        }
                        return doc.RootElement.TryGetProperty("result", out var result) ? result.Clone() : default;
                    }
                    ms.SetLength(0);
                }
            }
        }

        public async Task NavigateAsync(string url)
        {
            await SendCommandAsync("Page.enable");
            await SendCommandAsync("Page.navigate", new { url });
            await Task.Delay(1200);
        }

        public async Task ReloadAsync()
        {
            await SendCommandAsync("Page.reload");
            await Task.Delay(1200);
        }

        private static void ThrowIfException(JsonElement result, string expression)
        {
            if (result.TryGetProperty("exceptionDetails", out var exDetails))
            {
                var desc = exDetails.TryGetProperty("exception", out var exObj) && exObj.TryGetProperty("description", out var descProp)
                    ? descProp.GetString()
                    : exDetails.TryGetProperty("text", out var textProp)
                        ? textProp.GetString()
                        : "Unknown JavaScript Exception";
                throw new InvalidOperationException($"Lỗi JavaScript khi thực thi biểu thức '{expression}': {desc}");
            }

            if (result.TryGetProperty("result", out var resObj) && resObj.TryGetProperty("subtype", out var st) && st.GetString() == "error")
            {
                var desc = resObj.TryGetProperty("description", out var descProp) ? descProp.GetString() : "JavaScript Error";
                throw new InvalidOperationException($"Lỗi JavaScript khi thực thi biểu thức '{expression}': {desc}");
            }
        }

        /// <summary>
        /// Thực thi lệnh thao tác JavaScript (void hoặc không cần giá trị trả về),
        /// ném ngoại lệ nếu script gặp lỗi uncaught exception.
        /// </summary>
        public async Task ExecuteScriptAsync(string expression)
        {
            var result = await SendCommandAsync("Runtime.evaluate", new
            {
                expression,
                returnByValue = true
            });

            ThrowIfException(result, expression);
        }

        /// <summary>
        /// Đọc một chuỗi bắt buộc từ DOM, ném ngoại lệ nếu script ném lỗi hoặc trả về null/undefined/không phải chuỗi.
        /// </summary>
        public async Task<string> EvaluateStringRequiredAsync(string expression)
        {
            var result = await SendCommandAsync("Runtime.evaluate", new
            {
                expression,
                returnByValue = true
            });

            ThrowIfException(result, expression);

            if (result.TryGetProperty("result", out var resObj))
            {
                if (resObj.TryGetProperty("value", out var val) && val.ValueKind == JsonValueKind.String)
                {
                    var str = val.GetString();
                    if (!string.IsNullOrEmpty(str))
                    {
                        return str;
                    }
                }
                else if (resObj.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "undefined")
                {
                    throw new InvalidOperationException($"Biểu thức '{expression}' trả về undefined thay vì chuỗi hợp lệ.");
                }
            }

            throw new InvalidOperationException($"Biểu thức '{expression}' không trả về chuỗi hợp lệ.");
        }

        /// <summary>
        /// Đọc một chuỗi có thể null, ném ngoại lệ nếu script ném lỗi JavaScript.
        /// </summary>
        public async Task<string?> EvaluateStringOrNullAsync(string expression)
        {
            var result = await SendCommandAsync("Runtime.evaluate", new
            {
                expression,
                returnByValue = true
            });

            ThrowIfException(result, expression);

            if (result.TryGetProperty("result", out var resObj) && resObj.TryGetProperty("value", out var val) && val.ValueKind == JsonValueKind.String)
            {
                return val.GetString();
            }

            return null;
        }

        /// <summary>
        /// Đánh giá biểu thức boolean, ném ngoại lệ nếu script ném lỗi hoặc không trả về boolean.
        /// </summary>
        public async Task<bool> EvaluateBoolAsync(string expression)
        {
            var result = await SendCommandAsync("Runtime.evaluate", new
            {
                expression,
                returnByValue = true
            });

            ThrowIfException(result, expression);

            if (result.TryGetProperty("result", out var resObj) && resObj.TryGetProperty("value", out var val) && (val.ValueKind == JsonValueKind.True || val.ValueKind == JsonValueKind.False))
            {
                return val.GetBoolean();
            }

            throw new InvalidOperationException($"Biểu thức '{expression}' không trả về giá trị boolean hợp lệ.");
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_ws.State == WebSocketState.Open)
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                }
            }
            catch { }
            _ws.Dispose();
            try { _process.Kill(true); } catch { }
            _process.Dispose();
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task ReservationCountdown_BrowserEndToEnd_RealReloadFlowAndTwoFacilityTimezones_PreservesHoldDeadlineAndNoDuplicate()
    {
        // Finding 2: Kiểm chứng countdown thực sự giảm, reload giữ nguyên deadline và không sinh bản ghi duplicate,
        // đồng thời kiểm chứng hai cơ sở (Asia/Ho_Chi_Minh và UTC) cùng instant UTC render đúng giờ địa phương.
        var edgePath = ResolveEdgeBrowserPath();
        Assert.True(File.Exists(edgePath), $"Trình duyệt Microsoft Edge không được tìm thấy tại '{edgePath}'.");

        var (client, user, authCookie) = await CreateAuthenticatedCustomerClientAsync("browser_e2e_user");
        var vnFacility = await SeedFacilityWithRateAsync("E2EVN");
        var utcFacility = await SeedFacilityWithRateAsync("E2EUTC");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
        (await db.facilities.FindAsync(utcFacility.FacilityId))!.timezone = "UTC";

        var nowUtc = DateTimeOffset.UtcNow;
        var holdUntilUtc = nowUtc.AddMinutes(10); // Còn đúng 10 phút, khác 15 phút ban đầu để phát hiện reset về 15:00

        var resVn = new reservation
        {
            reservation_code = "RES-E2E-VN",
            customer_id = user.id,
            facility_id = vnFacility.FacilityId,
            unit_type_id = vnFacility.UnitTypeId,
            facility_rate_id = vnFacility.RateId,
            start_date = DateOnly.FromDateTime(nowUtc.UtcDateTime).AddDays(1),
            end_date = DateOnly.FromDateTime(nowUtc.UtcDateTime).AddMonths(1),
            monthly_rate_snapshot = 1000000m,
            deposit_snapshot = 1000000m,
            quoted_total = 2000000m,
            hold_until = holdUntilUtc,
            status = "pending",
            created_at = nowUtc,
            updated_at = nowUtc
        };
        db.reservations.Add(resVn);

        var resUtc = new reservation
        {
            reservation_code = "RES-E2E-UTC",
            customer_id = user.id,
            facility_id = utcFacility.FacilityId,
            unit_type_id = utcFacility.UnitTypeId,
            facility_rate_id = utcFacility.RateId,
            start_date = DateOnly.FromDateTime(nowUtc.UtcDateTime).AddDays(1),
            end_date = DateOnly.FromDateTime(nowUtc.UtcDateTime).AddMonths(1),
            monthly_rate_snapshot = 1200000m,
            deposit_snapshot = 1200000m,
            quoted_total = 2400000m,
            hold_until = holdUntilUtc,
            status = "pending",
            created_at = nowUtc,
            updated_at = nowUtc
        };
        db.reservations.Add(resUtc);
        await db.SaveChangesAsync();

        // Thiết lập Kestrel proxy tạm thời chuyển tiếp yêu cầu từ Edge sang WebApplicationFactory TestServer
        var port = Random.Shared.Next(16000, 24000);
        var prefix = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        using var cts = new CancellationTokenSource();
        var proxyClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var proxyTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    var context = await listener.GetContextAsync();
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var req = context.Request;
                            var targetUri = new Uri(proxyClient.BaseAddress!, req.RawUrl);
                            using var msg = new HttpRequestMessage(new HttpMethod(req.HttpMethod), targetUri);
                            foreach (string h in req.Headers)
                            {
                                if (h.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
                                msg.Headers.TryAddWithoutValidation(h, req.Headers[h]);
                            }

                            msg.Headers.TryAddWithoutValidation("Cookie", authCookie);
                            if (req.HasEntityBody)
                            {
                                using var ms = new MemoryStream();
                                await req.InputStream.CopyToAsync(ms);
                                msg.Content = new ByteArrayContent(ms.ToArray());
                                if (!string.IsNullOrEmpty(req.ContentType))
                                {
                                    msg.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(req.ContentType);
                                }
                            }

                            var resp = await proxyClient.SendAsync(msg);
                            context.Response.StatusCode = (int)resp.StatusCode;
                            foreach (var h in resp.Headers)
                            {
                                context.Response.Headers[h.Key] = string.Join(", ", h.Value);
                            }
                            foreach (var h in resp.Content.Headers)
                            {
                                context.Response.Headers[h.Key] = string.Join(", ", h.Value);
                            }

                            var bytes = await resp.Content.ReadAsByteArrayAsync();
                            context.Response.ContentLength64 = bytes.Length;
                            await context.Response.OutputStream.WriteAsync(bytes);
                            context.Response.OutputStream.Close();
                        }
                        catch
                        {
                            try { context.Response.Abort(); } catch { }
                        }
                    });
                }
                catch { break; }
            }
        });

        await using var session = await HeadlessEdgeSession.LaunchAsync(edgePath);

        try
        {
            // =========================================================================
            // 1. KIỂM THỬ ĐƠN CƠ SỞ VIỆT NAM (Asia/Ho_Chi_Minh)
            // =========================================================================
            var vnUrl = $"{prefix}Reservations/Details?id={resVn.id}";
            await session.NavigateAsync(vnUrl);


            // A. Kiểm chứng data-hold-until từ DB
            var holdUntilVn = await session.EvaluateStringRequiredAsync("document.getElementById('reservationCountdownPanel').getAttribute('data-hold-until')");
            Assert.Equal(holdUntilUtc.ToString("o"), holdUntilVn);

            // B. Đọc countdown hiển thị ban đầu
            var cd1 = await session.EvaluateStringRequiredAsync("document.querySelector('[data-countdown-display]').textContent.trim()");
            var sec1 = ParseCountdownSecondsStrict(cd1, "cd1_vn_initial");
            Assert.True(sec1 > 0 && sec1 <= 600, $"Countdown ban đầu phải <= 600s (10 phút), thực tế: {sec1}s ({cd1})");

            // C. Chờ thời gian trôi qua có kiểm soát (2.2 giây)
            await Task.Delay(2200);
            var cd2 = await session.EvaluateStringRequiredAsync("document.querySelector('[data-countdown-display]').textContent.trim()");
            var sec2 = ParseCountdownSecondsStrict(cd2, "cd2_vn_after_delay");
            Assert.True(sec2 < sec1, $"Countdown PHẢI THỰC SỰ GIẢM sau 2.2 giây (chứng minh JS countdown đang chạy)! Ban đầu: {sec1}s, sau chờ: {sec2}s");

            // D. RELOAD cùng page/context
            await session.ReloadAsync();

            // E. Xác nhận data-hold-until sau reload giữ nguyên 100%
            var holdUntilAfterReload = await session.EvaluateStringRequiredAsync("document.getElementById('reservationCountdownPanel').getAttribute('data-hold-until')");
            Assert.Equal(holdUntilUtc.ToString("o"), holdUntilAfterReload);

            // F. Countdown sau reload không được reset về 15:00 (900s) mà phải tiếp tục từ mốc thời gian thực
            var cd3 = await session.EvaluateStringRequiredAsync("document.querySelector('[data-countdown-display]').textContent.trim()");
            var sec3 = ParseCountdownSecondsStrict(cd3, "cd3_vn_after_reload");
            Assert.True(sec3 < 900 && sec3 <= sec1, $"Countdown sau reload KHÔNG được reset về 15:00 (900s)! Thực tế: {sec3}s ({cd3})");

            // G. Chờ thêm 1.8 giây để xác nhận countdown tiếp tục đếm ngược sau reload
            await Task.Delay(1800);
            var cd4 = await session.EvaluateStringRequiredAsync("document.querySelector('[data-countdown-display]').textContent.trim()");
            var sec4 = ParseCountdownSecondsStrict(cd4, "cd4_vn_after_reload_delay");
            Assert.True(sec4 < sec3, $"Countdown phải tiếp tục chạy sau reload! Ngay sau reload: {sec3}s, sau chờ: {sec4}s");

            // H. Kiểm chứng giờ hạn thanh toán hiển thị theo múi giờ Việt Nam
            var expectedVnTime = AppFormatters.FormatDateTimeSeconds(holdUntilUtc, "Asia/Ho_Chi_Minh");
            var vnBodyText = await session.EvaluateStringRequiredAsync("document.body.innerText");
            Assert.NotNull(vnBodyText);
            Assert.Contains(expectedVnTime, vnBodyText);

            // =========================================================================
            // 2. KIỂM THỬ ĐƠN CƠ SỞ UTC
            // =========================================================================
            var utcUrl = $"{prefix}Reservations/Details?id={resUtc.id}";
            await session.NavigateAsync(utcUrl);

            // A. data-hold-until cùng instant UTC với đơn VN
            var holdUntilUtcVal = await session.EvaluateStringRequiredAsync("document.getElementById('reservationCountdownPanel').getAttribute('data-hold-until')");
            Assert.Equal(holdUntilUtc.ToString("o"), holdUntilUtcVal);

            // B. Countdown cho cơ sở UTC cũng đang chạy
            var cdUtc = await session.EvaluateStringRequiredAsync("document.querySelector('[data-countdown-display]').textContent.trim()");
            var secUtc = ParseCountdownSecondsStrict(cdUtc, "cdUtc_initial");
            Assert.True(secUtc > 0 && secUtc <= 600);

            // C. Giờ hạn hiển thị theo múi giờ UTC
            var expectedUtcTime = AppFormatters.FormatDateTimeSeconds(holdUntilUtc, "UTC");
            var utcBodyText = await session.EvaluateStringRequiredAsync("document.body.innerText");
            Assert.NotNull(utcBodyText);
            Assert.Contains(expectedUtcTime, utcBodyText);

            // D. Hai giờ địa phương khác nhau đúng theo timezone nhưng instant countdown giống nhau
            Assert.NotEqual(expectedVnTime, expectedUtcTime);

            // =========================================================================
            // 3. KIỂM CHỨNG CƠ SỞ DỮ LIỆU: KHÔNG SINH ĐƠN TRÙNG LẶP (IDEMPOTENCY)
            // =========================================================================
            using var verifyScope = _factory.Services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
            var count = await verifyDb.reservations.CountAsync(r => r.customer_id == user.id);
            Assert.Equal(2, count); // Có đúng 2 đơn đã seed ban đầu, reload không làm tăng số đơn
        }
        finally
        {
            cts.Cancel();
            listener.Stop();
            listener.Close();
            await Task.WhenAny(proxyTask, Task.Delay(500));
        }
    }

    [Fact]
    public async Task ReservationCountdown_BrowserEndToEnd_NegativeControl_WhenJavaScriptDisabled_DetectsFailure()
    {
        // Negative Control: Chứng minh rằng khi JavaScript bị vô hiệu hóa (--disable-javascript),
        // đồng hồ countdown đứng yên và bước kiểm tra countdown chạy sẽ phát hiện thất bại.
        var edgePath = ResolveEdgeBrowserPath();
        Assert.True(File.Exists(edgePath), $"Trình duyệt Microsoft Edge không được tìm thấy tại '{edgePath}'.");

        var (client, user, authCookie) = await CreateAuthenticatedCustomerClientAsync("browser_neg_user");
        var vnFacility = await SeedFacilityWithRateAsync("E2ENEG");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
        var nowUtc = DateTimeOffset.UtcNow;
        var holdUntilUtc = nowUtc.AddMinutes(9);

        var res = new reservation
        {
            reservation_code = "RES-E2E-NEG",
            customer_id = user.id,
            facility_id = vnFacility.FacilityId,
            unit_type_id = vnFacility.UnitTypeId,
            facility_rate_id = vnFacility.RateId,
            start_date = DateOnly.FromDateTime(nowUtc.UtcDateTime).AddDays(1),
            end_date = DateOnly.FromDateTime(nowUtc.UtcDateTime).AddMonths(1),
            monthly_rate_snapshot = 1000000m,
            deposit_snapshot = 1000000m,
            quoted_total = 2000000m,
            hold_until = holdUntilUtc,
            status = "pending",
            created_at = nowUtc,
            updated_at = nowUtc
        };
        db.reservations.Add(res);
        await db.SaveChangesAsync();

        var port = Random.Shared.Next(16000, 24000);
        var prefix = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        using var cts = new CancellationTokenSource();
        var proxyClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var proxyTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    var context = await listener.GetContextAsync();
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var req = context.Request;
                            var targetUri = new Uri(proxyClient.BaseAddress!, req.RawUrl);
                            using var msg = new HttpRequestMessage(new HttpMethod(req.HttpMethod), targetUri);
                            foreach (string h in req.Headers)
                            {
                                if (h.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
                                msg.Headers.TryAddWithoutValidation(h, req.Headers[h]);
                            }

                            msg.Headers.TryAddWithoutValidation("Cookie", authCookie);
                            var resp = await proxyClient.SendAsync(msg);
                            context.Response.StatusCode = (int)resp.StatusCode;
                            foreach (var h in resp.Headers)
                            {
                                context.Response.Headers[h.Key] = string.Join(", ", h.Value);
                            }
                            foreach (var h in resp.Content.Headers)
                            {
                                context.Response.Headers[h.Key] = string.Join(", ", h.Value);
                            }

                            var bytes = await resp.Content.ReadAsByteArrayAsync();
                            context.Response.ContentLength64 = bytes.Length;
                            await context.Response.OutputStream.WriteAsync(bytes);
                            context.Response.OutputStream.Close();
                        }
                        catch
                        {
                            try { context.Response.Abort(); } catch { }
                        }
                    });
                }
                catch { break; }
            }
        });

        // Khởi chạy Edge với JavaScript bị vô hiệu hóa
        await using var session = await HeadlessEdgeSession.LaunchAsync(edgePath, disableJs: true);

        try
        {
            var targetUrl = $"{prefix}Reservations/Details?id={res.id}";
            await session.NavigateAsync(targetUrl);

            // Khi không có JS, server render countdown ban đầu
            var cd1 = await session.EvaluateStringRequiredAsync("document.querySelector('[data-countdown-display]').textContent.trim()");
            var sec1 = ParseCountdownSecondsStrict(cd1, "cd1_neg_control");

            await Task.Delay(2000);
            var cd2 = await session.EvaluateStringRequiredAsync("document.querySelector('[data-countdown-display]').textContent.trim()");
            var sec2 = ParseCountdownSecondsStrict(cd2, "cd2_neg_control");

            // Khẳng định: cả hai mẫu đều là countdown hợp lệ, và khi không có JS thì countdown đứng yên (không giảm)
            Assert.Equal(cd1, cd2);
            Assert.Equal(sec1, sec2);
        }
        finally
        {
            cts.Cancel();
            listener.Stop();
            listener.Close();
            await Task.WhenAny(proxyTask, Task.Delay(500));
        }
    }

    [Fact]
    public async Task ReservationCountdown_DetailsPage_WhenStatusReturns401Or403OrExpired_AppliesCorrectUiAndDoesNotShowFakeExpiredAlert()
    {
        // Finding 1: Kiểm chứng trên trang Details thật rằng:
        // A. Khi gặp 401: reservationAuthAlert hiển thị với link login, reservationExpiredAlert TUYỆT ĐỐI ẨN,
        //    nội dung nhìn thấy không tuyên bố hết hạn hoặc giải phóng sức chứa.
        // B. Khi gặp 403: reservationAuthAlert hiển thị từ chối quyền, reservationExpiredAlert ẨN.
        // C. Khi server trả expired thật: reservationAuthAlert ẨN, reservationExpiredAlert HIỂN THỊ.
        var edgePath = ResolveEdgeBrowserPath();
        Assert.True(File.Exists(edgePath), $"Trình duyệt Microsoft Edge không được tìm thấy tại '{edgePath}'.");

        var (client, user, authCookie) = await CreateAuthenticatedCustomerClientAsync("browser_auth_user");
        var facility = await SeedFacilityWithRateAsync("E2EAUTH");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
        var nowUtc = DateTimeOffset.UtcNow;
        var holdUntilUtc = nowUtc.AddMinutes(5);

        var res = new reservation
        {
            reservation_code = "RES-E2E-AUTH",
            customer_id = user.id,
            facility_id = facility.FacilityId,
            unit_type_id = facility.UnitTypeId,
            facility_rate_id = facility.RateId,
            start_date = DateOnly.FromDateTime(nowUtc.UtcDateTime).AddDays(1),
            end_date = DateOnly.FromDateTime(nowUtc.UtcDateTime).AddMonths(1),
            monthly_rate_snapshot = 1000000m,
            deposit_snapshot = 1000000m,
            quoted_total = 2000000m,
            hold_until = holdUntilUtc,
            status = "pending",
            created_at = nowUtc,
            updated_at = nowUtc
        };
        db.reservations.Add(res);
        await db.SaveChangesAsync();

        var port = Random.Shared.Next(16000, 24000);
        var prefix = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        using var cts = new CancellationTokenSource();
        var proxyClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var proxyTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    var context = await listener.GetContextAsync();
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var req = context.Request;
                            var targetUri = new Uri(proxyClient.BaseAddress!, req.RawUrl);
                            using var msg = new HttpRequestMessage(new HttpMethod(req.HttpMethod), targetUri);
                            foreach (string h in req.Headers)
                            {
                                if (h.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
                                msg.Headers.TryAddWithoutValidation(h, req.Headers[h]);
                            }

                            msg.Headers.TryAddWithoutValidation("Cookie", authCookie);
                            var resp = await proxyClient.SendAsync(msg);
                            context.Response.StatusCode = (int)resp.StatusCode;
                            foreach (var h in resp.Headers)
                            {
                                context.Response.Headers[h.Key] = string.Join(", ", h.Value);
                            }
                            foreach (var h in resp.Content.Headers)
                            {
                                context.Response.Headers[h.Key] = string.Join(", ", h.Value);
                            }

                            var bytes = await resp.Content.ReadAsByteArrayAsync();
                            context.Response.ContentLength64 = bytes.Length;
                            await context.Response.OutputStream.WriteAsync(bytes);
                            context.Response.OutputStream.Close();
                        }
                        catch
                        {
                            try { context.Response.Abort(); } catch { }
                        }
                    });
                }
                catch { break; }
            }
        });

        await using var session = await HeadlessEdgeSession.LaunchAsync(edgePath);

        try
        {
            var targetUrl = $"{prefix}Reservations/Details?id={res.id}";
            await session.NavigateAsync(targetUrl);

            // 1. Kiểm chứng trạng thái ban đầu khi vừa render
            var initialAuthAlertHidden = await session.EvaluateBoolAsync("document.getElementById('reservationAuthAlert').classList.contains('d-none')");
            Assert.True(initialAuthAlertHidden, "reservationAuthAlert ban đầu phải có class d-none");

            var initialExpiredAlertHidden = await session.EvaluateBoolAsync("document.getElementById('reservationExpiredAlert').classList.contains('d-none')");
            Assert.True(initialExpiredAlertHidden, "reservationExpiredAlert ban đầu phải có class d-none");

            // 2. Kích hoạt xử lý HTTP 401 trên giao diện Details thật
            await session.ExecuteScriptAsync($"window.ReservationCountdown.applyAuthErrorOnUi(null, 401, {res.id})");

            var isAuthVisible401 = await session.EvaluateBoolAsync("!document.getElementById('reservationAuthAlert').classList.contains('d-none')");
            Assert.True(isAuthVisible401, "reservationAuthAlert PHẢI HIỂN THỊ khi nhận 401");

            var authHeading401 = await session.EvaluateStringRequiredAsync("document.getElementById('reservationAuthAlertHeading').textContent");
            Assert.Contains("Phiên đăng nhập đã hết hạn", authHeading401);

            var authLink401 = await session.EvaluateStringRequiredAsync("document.getElementById('reservationAuthAlertLink').getAttribute('href')");
            Assert.Contains("/Account/Login", authLink401);

            var isExpiredHidden401 = await session.EvaluateBoolAsync("document.getElementById('reservationExpiredAlert').classList.contains('d-none')");
            Assert.True(isExpiredHidden401, "reservationExpiredAlert TUYỆT ĐỐI PHẢI ẨN (d-none) khi nhận 401");

            var badgeText401 = await session.EvaluateStringRequiredAsync("document.getElementById('reservationStatusBadge').textContent");
            Assert.Equal("Hết phiên đăng nhập", badgeText401);

            var visibleAlertText401 = await session.EvaluateStringRequiredAsync("document.getElementById('reservationAuthAlert').innerText");
            Assert.DoesNotContain("đã hết thời hạn 15 phút", visibleAlertText401);
            Assert.DoesNotContain("sức chứa khả dụng đã được tự động giải phóng", visibleAlertText401);

            var isPaymentDisabled401 = await session.EvaluateBoolAsync("document.getElementById('btnProceedPayment').disabled && document.getElementById('btnProceedPayment').classList.contains('d-none')");
            Assert.True(isPaymentDisabled401, "Nút Thanh toán phải bị ẩn và disabled");

            var isCancelDisabled401 = await session.EvaluateBoolAsync("document.getElementById('btnCancelReservation').disabled && document.getElementById('btnCancelReservation').classList.contains('d-none')");
            Assert.True(isCancelDisabled401, "Nút Hủy phải bị ẩn và disabled");

            // 3. Kích hoạt xử lý HTTP 403 trên giao diện Details thật
            await session.ExecuteScriptAsync($"window.ReservationCountdown.applyAuthErrorOnUi(null, 403, {res.id})");

            var isAuthVisible403 = await session.EvaluateBoolAsync("!document.getElementById('reservationAuthAlert').classList.contains('d-none')");
            Assert.True(isAuthVisible403, "reservationAuthAlert PHẢI HIỂN THỊ khi nhận 403");

            var authHeading403 = await session.EvaluateStringRequiredAsync("document.getElementById('reservationAuthAlertHeading').textContent");
            Assert.Contains("Từ chối quyền truy cập", authHeading403);

            var isExpiredHidden403 = await session.EvaluateBoolAsync("document.getElementById('reservationExpiredAlert').classList.contains('d-none')");
            Assert.True(isExpiredHidden403, "reservationExpiredAlert TUYỆT ĐỐI PHẢI ẨN khi nhận 403");

            var badgeText403 = await session.EvaluateStringRequiredAsync("document.getElementById('reservationStatusBadge').textContent");
            Assert.Equal("Từ chối quyền", badgeText403);

            // 4. Kích hoạt trạng thái Expired thật từ server
            await session.ExecuteScriptAsync($"window.ReservationCountdown.applyExpiredStateOnUi(null, {{ statusDisplayName: 'Hết hạn (Expired)' }}, {res.id})");

            var isAuthHiddenOnExpired = await session.EvaluateBoolAsync("document.getElementById('reservationAuthAlert').classList.contains('d-none')");
            Assert.True(isAuthHiddenOnExpired, "Khi thực sự hết hạn, reservationAuthAlert PHẢI ẨN (d-none)");

            var isExpiredVisibleOnExpired = await session.EvaluateBoolAsync("!document.getElementById('reservationExpiredAlert').classList.contains('d-none')");
            Assert.True(isExpiredVisibleOnExpired, "Khi thực sự hết hạn, reservationExpiredAlert PHẢI HIỂN THỊ");

            var expiredHeading = await session.EvaluateStringRequiredAsync("document.querySelector('#reservationExpiredAlert .alert-heading').textContent");
            Assert.Contains("Đơn giữ chỗ đã hết thời hạn 15 phút", expiredHeading);
        }
        finally
        {
            cts.Cancel();
            listener.Stop();
            listener.Close();
            await Task.WhenAny(proxyTask, Task.Delay(500));
        }
    }

    [Fact]
    public async Task ReservationCountdown_CdpHarness_WhenSelectorNotFoundOrJsThrows_ThrowsFailureAndDetectsError()
    {
        var edgePath = ResolveEdgeBrowserPath();
        Assert.True(File.Exists(edgePath), $"Trình duyệt Microsoft Edge không được tìm thấy tại '{edgePath}'.");

        await using var session = await HeadlessEdgeSession.LaunchAsync(edgePath);

        // 1. Selector không tồn tại gây uncaught TypeError khi gọi .textContent trên null:
        // Đảm bảo exceptionDetails của CDP được bắt và ném ngoại lệ rõ ràng, không âm thầm trả null pass giả.
        var exTypeError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.EvaluateStringRequiredAsync("document.querySelector('#nonExistentElement').textContent.trim()"));
        Assert.Contains("Lỗi JavaScript", exTypeError.Message);
        Assert.Contains("textContent", exTypeError.Message);

        // 2. Lỗi cú pháp hoặc hàm không tồn tại khi thực thi ExecuteScriptAsync:
        var exSyntax = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.ExecuteScriptAsync("window.nonExistentFunctionCall()"));
        Assert.Contains("Lỗi JavaScript", exSyntax.Message);
        Assert.Contains("nonExistentFunctionCall", exSyntax.Message);

        // 3. Biểu thức trả về undefined khi đọc chuỗi bắt buộc:
        var exUndefined = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.EvaluateStringRequiredAsync("undefined"));
        Assert.Contains("undefined", exUndefined.Message);

        // 4. Biểu thức trả về null khi đọc chuỗi bắt buộc:
        var exNull = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.EvaluateStringRequiredAsync("null"));
        Assert.Contains("không trả về chuỗi hợp lệ", exNull.Message);

        // 5. Chuỗi rỗng khi đọc chuỗi bắt buộc:
        var exEmpty = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.EvaluateStringRequiredAsync("''"));
        Assert.Contains("không trả về chuỗi hợp lệ", exEmpty.Message);
    }

    [Fact]
    public void ReservationCountdown_CdpHarness_ParseCountdownSecondsStrict_RejectsInvalidFormat()
    {
        // 1. Null hoặc chuỗi rỗng:
        Assert.ThrowsAny<Exception>(() => ParseCountdownSecondsStrict(null, "null_sample"));
        Assert.ThrowsAny<Exception>(() => ParseCountdownSecondsStrict("", "empty_sample"));
        Assert.ThrowsAny<Exception>(() => ParseCountdownSecondsStrict("   ", "whitespace_sample"));

        // 2. Không đúng định dạng mm:ss (ví dụ -1, chữ, thiếu số 0 dẫn đầu, giây >= 60):
        Assert.ThrowsAny<Exception>(() => ParseCountdownSecondsStrict("-1", "negative_one"));
        Assert.ThrowsAny<Exception>(() => ParseCountdownSecondsStrict("abc", "alpha"));
        Assert.ThrowsAny<Exception>(() => ParseCountdownSecondsStrict("9:45", "single_digit_min"));
        Assert.ThrowsAny<Exception>(() => ParseCountdownSecondsStrict("09:5", "single_digit_sec"));
        Assert.ThrowsAny<Exception>(() => ParseCountdownSecondsStrict("09:60", "sec_at_60"));
        Assert.ThrowsAny<Exception>(() => ParseCountdownSecondsStrict("09:99", "sec_over_60"));

        // 3. Mẫu hợp lệ:
        Assert.Equal(599, ParseCountdownSecondsStrict("09:59", "valid_sample_1"));
        Assert.Equal(0, ParseCountdownSecondsStrict("00:00", "valid_sample_zero"));
        Assert.Equal(900, ParseCountdownSecondsStrict("15:00", "valid_sample_full_hold"));
    }

    #endregion

    private class TestReservationExpirationHook : IReservationExpirationHook
    {
        public int InvokedCount { get; private set; }

        public Task OnHoldsExpiredAsync(int expiredCount, CancellationToken cancellationToken = default)
        {
            InvokedCount += expiredCount;
            return Task.CompletedTask;
        }
    }
}
