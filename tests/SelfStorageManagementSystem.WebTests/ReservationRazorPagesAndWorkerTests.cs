using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
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

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
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
