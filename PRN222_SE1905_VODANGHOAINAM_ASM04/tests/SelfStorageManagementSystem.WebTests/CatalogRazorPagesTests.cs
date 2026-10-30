using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.WebTests.Fixtures;
using Xunit;

namespace SelfStorageManagementSystem.WebTests;

public class CatalogRazorPagesTests : IClassFixture<WebTestFixture>
{
    private readonly WebTestFixture _factory;
    private readonly HttpClient _client;

    public CatalogRazorPagesTests(WebTestFixture factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    private async Task<(long FacilityId, long UnitTypeId, List<long> UnitIds)> SeedFacilityWithUnitsAsync(
        string suffix,
        int unitCount = 3,
        int mapPositionCount = 2,
        bool hasRates = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();

        var facilityId = Math.Abs(Guid.NewGuid().GetHashCode());
        var facility = new facility
        {
            id = facilityId,
            code = $"FAC-{suffix.ToUpperInvariant()}",
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

        var areaId = facilityId * 10 + 1;
        var area = new facility_area
        {
            id = areaId,
            facility_id = facilityId,
            code = $"AREA-{suffix.ToUpperInvariant()}",
            name = $"Khu Vực A - Tầng 1",
            area_type = "floor",
            display_order = 1,
            map_metadata = "{}",
            is_active = true
        };
        db.facility_areas.Add(area);

        var unitTypeId = facilityId * 10 + 2;
        var unitType = new unit_type
        {
            id = unitTypeId,
            code = $"UT-{suffix.ToUpperInvariant()}",
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
        if (hasRates)
        {
            var rateId = facilityId * 10 + 3;
            var rate = new facility_rate
            {
                id = rateId,
                facility_id = facilityId,
                unit_type_id = unitTypeId,
                monthly_rate = 1200000m,
                deposit_amount = 1200000m,
                booking_fee = 50000m,
                valid_from = today.AddDays(-10),
                valid_to = today.AddDays(365)
            };
            db.facility_rates.Add(rate);
        }

        var unitIds = new List<long>();
        for (int i = 1; i <= unitCount; i++)
        {
            var unitId = facilityId * 100 + i;
            unitIds.Add(unitId);

            var unit = new storage_unit
            {
                id = unitId,
                facility_id = facilityId,
                unit_type_id = unitTypeId,
                area_id = areaId,
                unit_code = $"TB-{suffix.ToUpperInvariant()}-{i:D2}",
                floor_label = "Tầng 1",
                zone_label = "Dãy A",
                physical_status = "available",
                is_listed = true,
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            };
            db.storage_units.Add(unit);

            if (i <= mapPositionCount)
            {
                var mapPos = new unit_map_position
                {
                    unit_id = unitId,
                    area_id = areaId,
                    x = 10m * i,
                    y = 15m,
                    width = 12m,
                    height = 20m,
                    rotation_degrees = 0,
                    metadata = "{}"
                };
                db.unit_map_positions.Add(mapPos);
            }
        }

        await db.SaveChangesAsync();
        return (facilityId, unitTypeId, unitIds);
    }

    [Fact]
    public async Task CatalogIndex_PublicAccess_ReturnsOk_WithFacilityListAndFilters()
    {
        // Arrange
        var suffix = Guid.NewGuid().ToString("N")[..6];
        await SeedFacilityWithUnitsAsync(suffix);

        // Act - Truy cập trang danh mục kho công khai (không cần đăng nhập)
        var response = await _client.GetAsync("/Catalog");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var decoded = WebUtility.HtmlDecode(html);

        Assert.Contains("Hệ Thống Cơ Sở Kho Tự Quản", decoded);
        Assert.Contains("Danh Mục Cơ Sở Lưu Trữ", decoded);
        Assert.Contains("Từ khóa tìm kiếm", decoded);
        Assert.Contains("Tỉnh / Thành phố", decoded);
        Assert.Contains("Quận / Huyện", decoded);
        Assert.Contains($"Cơ Sở Lưu Trữ {suffix}", decoded);
        Assert.Contains($"FAC-{suffix.ToUpperInvariant()}", decoded);
        Assert.Contains("/Catalog/Details?facilityId=", html);
    }

    [Fact]
    public async Task CatalogIndex_PreservesQueryFilters_AndGeneratesPaginationUrls()
    {
        // Arrange
        var suffix = Guid.NewGuid().ToString("N")[..6];
        await SeedFacilityWithUnitsAsync(suffix);

        // Act - Gửi yêu cầu tìm kiếm với Query String
        var query = $"/Catalog?City=Hồ%20Chí%20Minh&District=Tân%20Bình&SearchTerm={suffix}&PageNumber=1&PageSize=9";
        var response = await _client.GetAsync(query);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var decoded = WebUtility.HtmlDecode(html);

        // Form giữ nguyên giá trị bộ lọc trong ô input
        Assert.Contains($"value=\"Hồ Chí Minh\"", decoded);
        Assert.Contains($"value=\"Tân Bình\"", decoded);
        Assert.Contains($"value=\"{suffix}\"", decoded);
        Assert.Contains($"Cơ Sở Lưu Trữ {suffix}", decoded);
    }

    [Fact]
    public async Task CatalogIndex_WhenNoFacilitiesMatch_RendersEmptyState()
    {
        // Act - Tìm kiếm từ khóa không tồn tại
        var response = await _client.GetAsync("/Catalog?SearchTerm=NON_EXISTENT_FACILITY_KEYWORD_XYZ999");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var decoded = WebUtility.HtmlDecode(html);

        Assert.Contains("Không tìm thấy cơ sở kho nào phù hợp", decoded);
        Assert.Contains("Xóa toàn bộ bộ lọc", decoded);
        Assert.Contains("href=\"/Catalog\"", html);
    }

    [Fact]
    public async Task CatalogDetails_NonExistentFacility_Returns404AndFriendlyBody()
    {
        // Act - Truy vấn ID cơ sở không tồn tại
        var response = await _client.GetAsync("/Catalog/Details?facilityId=999999999");

        // Assert - HTTP 404 NotFound nhưng vẫn hiển thị giao diện thân thiện (Finding 4)
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var decoded = WebUtility.HtmlDecode(html);

        Assert.Contains("Không tìm thấy cơ sở kho", decoded);
        Assert.Contains("Quay lại danh mục kho", decoded);
        Assert.Contains("href=\"/Catalog\"", html);
    }

    [Fact]
    public async Task CatalogDetails_MissingFacilityId_Returns404AndFriendlyBody()
    {
        // Act - Không truyền facilityId hoặc facilityId <= 0
        var response = await _client.GetAsync("/Catalog/Details");

        // Assert - HTTP 404 NotFound kèm giao diện hướng dẫn thân thiện (Finding 4)
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var decoded = WebUtility.HtmlDecode(html);

        Assert.Contains("Không tìm thấy cơ sở kho", decoded);
        Assert.Contains("Mã cơ sở lưu trữ không hợp lệ hoặc không được cung cấp", decoded);
        Assert.Contains("Quay lại danh mục kho", decoded);
        Assert.Contains("href=\"/Catalog\"", html);
    }

    [Fact]
    public async Task CatalogDetails_RendersFacilityInfo_UnitTypes_PricingAndCapacityNotes()
    {
        // Arrange
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var (facilityId, unitTypeId, _) = await SeedFacilityWithUnitsAsync(suffix, unitCount: 3, mapPositionCount: 2);

        // Act - Truy cập chi tiết cơ sở
        var response = await _client.GetAsync($"/Catalog/Details?facilityId={facilityId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var decoded = WebUtility.HtmlDecode(html);

        // 1. Thông tin cơ sở
        Assert.Contains($"Cơ Sở Lưu Trữ {suffix}", decoded);
        Assert.Contains($"FAC-{suffix.ToUpperInvariant()}", decoded);
        Assert.Contains("07:30", decoded);
        Assert.Contains("21:00", decoded);
        Assert.Contains("Asia/Ho_Chi_Minh", decoded);
        Assert.Contains("10.8012", decoded);
        Assert.Contains("106.6543", decoded);

        // 2. Tab Loại kho & Bảng giá
        Assert.Contains($"Kho Tiêu Chuẩn {suffix}", decoded);
        Assert.Contains($"UT-{suffix.ToUpperInvariant()}", decoded);
        Assert.Contains("1.200.000", decoded); // Định dạng tiền tệ VND từ AppFormatters
        Assert.Contains("50.000", decoded); // Phí giữ chỗ
        Assert.Contains("Có điều hòa", decoded);
        Assert.Contains("Quy tắc đặt chỗ (BR-RSV-03)", decoded);
        Assert.Contains("3 / 3 ô khả dụng", decoded);

        // 3. Tab Danh sách ô ứng viên
        Assert.Contains($"TB-{suffix.ToUpperInvariant()}-01", decoded);
        Assert.Contains($"TB-{suffix.ToUpperInvariant()}-02", decoded);
        Assert.Contains($"TB-{suffix.ToUpperInvariant()}-03", decoded);
    }

    [Fact]
    public async Task CatalogDetails_DistinguishesUnitTypeCapacityHold_FromPhysicalUnitStatus()
    {
        // Arrange - Chuẩn bị 3 ô kho đều có physical_status = available
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var (facilityId, unitTypeId, unitIds) = await SeedFacilityWithUnitsAsync(suffix, unitCount: 3, mapPositionCount: 2);

        // Tạo 1 đơn giữ chỗ (active hold reservation) theo Loại kho
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            
            var reservation = new reservation
            {
                id = Math.Abs(Guid.NewGuid().GetHashCode()),
                reservation_code = $"RSV-{suffix.ToUpperInvariant()}-HOLD",
                customer_id = 1,
                facility_id = facilityId,
                unit_type_id = unitTypeId,
                start_date = today,
                end_date = today.AddMonths(1),
                status = "confirmed", // Giữ chỗ hợp lệ chưa phân công ô vật lý
                monthly_rate_snapshot = 1200000m,
                deposit_snapshot = 1200000m,
                booking_fee_snapshot = 50000m,
                hold_until = DateTimeOffset.UtcNow.AddMinutes(15),
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            };
            db.reservations.Add(reservation);
            await db.SaveChangesAsync();
        }

        // Act - Tải lại trang Catalog Details
        var response = await _client.GetAsync($"/Catalog/Details?facilityId={facilityId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var decoded = WebUtility.HtmlDecode(html);

        // 1. Sức chứa loại kho đã bị trừ đi 1 unassigned hold: từ 3 còn 2 ô khả dụng!
        Assert.Contains("2 / 3 ô khả dụng", decoded);

        // 2. CỰC KỲ QUAN TRỌNG THEO YÊU CẦU ĐẶC TẢ T07:
        // Cả 3 ô vật lý trong DB vẫn giữ nguyên physical_status = available!
        // Giữ chỗ theo Loại kho KHÔNG ĐƯỢC đánh dấu bất kỳ ô vật lý cụ thể nào là reserved!
        using (var verifyScope = _factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
            var unitsInDb = await db.storage_units.Where(u => u.facility_id == facilityId).ToListAsync();
            Assert.Equal(3, unitsInDb.Count);
            foreach (var u in unitsInDb)
            {
                Assert.Equal("available", u.physical_status);
            }
        }

        // 3. Trên sơ đồ mặt bằng SVG, các ô vẫn có trạng thái 'available', không có ô nào bị biến thành 'unit-status-reserved'!
        Assert.Contains("unit-status-available", html);
        Assert.DoesNotContain("unit-status-reserved", html);
    }

    [Fact]
    public async Task CatalogDetails_FloorMap_UsesOnlyRealSavedCoordinates_NoFakeCoordinates()
    {
        // Arrange - Seed 2 ô có map position và 1 ô KHÔNG có map position
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var (facilityId, _, _) = await SeedFacilityWithUnitsAsync(suffix, unitCount: 3, mapPositionCount: 2);

        // Act
        var response = await _client.GetAsync($"/Catalog/Details?facilityId={facilityId}&ActiveTab=floor-map");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var decoded = WebUtility.HtmlDecode(html);

        // 1. Kiểm tra SVG chứa đúng tọa độ thực tế đã lưu (x="10.00" y="15.00" width="12.00" height="20.00")
        Assert.Contains("id=\"facilityFloorMapSvg\"", html);
        Assert.Contains("x=\"10.00\"", html);
        Assert.Contains("y=\"15.00\"", html);
        Assert.Contains("width=\"12.00\"", html);
        Assert.Contains("height=\"20.00\"", html);

        // 2. Không bịa tọa độ: Thống kê hiển thị rõ ràng chỉ có 2 ô trên sơ đồ, 1 ô chưa định vị
        Assert.Contains("Sơ đồ: <strong>2</strong> ô", html);
        Assert.Contains("Chưa định vị: 1 ô", decoded);
    }

    [Fact]
    public async Task CatalogDetails_WhenFacilityHasNoUnits_RendersGracefully_WithEmptyState()
    {
        // Arrange - Seed cơ sở không có bất kỳ ô kho nào
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var (facilityId, _, _) = await SeedFacilityWithUnitsAsync(suffix, unitCount: 0, mapPositionCount: 0);

        // Act
        var response = await _client.GetAsync($"/Catalog/Details?facilityId={facilityId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var decoded = WebUtility.HtmlDecode(html);

        Assert.Contains($"Cơ Sở Lưu Trữ {suffix}", decoded);
        Assert.Contains("Chưa có sơ đồ mặt bằng", decoded);
        Assert.Contains("Không có ô kho ứng viên khả dụng", decoded);
    }

    [Fact]
    public async Task CatalogDetails_WhenUnitTypeHasNoRate_HandlesSafePriceDisplay()
    {
        // Arrange - Seed loại kho nhưng không có biểu giá
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var (facilityId, _, _) = await SeedFacilityWithUnitsAsync(suffix, unitCount: 1, mapPositionCount: 1, hasRates: false);

        // Act
        var response = await _client.GetAsync($"/Catalog/Details?facilityId={facilityId}");

        // Assert - Trang tải an toàn, không ném lỗi 500
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CatalogDetails_ReservationLink_ForGuest_PreservesInternalReturnUrl()
    {
        // Arrange
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var (facilityId, unitTypeId, _) = await SeedFacilityWithUnitsAsync(suffix);

        // Act - Truy cập với tư cách khách chưa đăng nhập
        var response = await _client.GetAsync($"/Catalog/Details?facilityId={facilityId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        // Nút đặt chỗ dẫn đến trang đăng nhập kèm ReturnUrl nội bộ chuẩn
        Assert.Contains("/Account/Login?returnUrl=", html);
        Assert.Contains(WebUtility.UrlEncode($"/Reservations?facilityId={facilityId}&unitTypeId={unitTypeId}"), html);
    }

    [Fact]
    public async Task CatalogDetails_ReservationLink_ForCustomer_NavigatesDirectlyToReservation()
    {
        // Arrange
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var (facilityId, unitTypeId, _) = await SeedFacilityWithUnitsAsync(suffix);

        // Act - Khách hàng đã đăng nhập (Role: storage_customer)
        var request = new HttpRequestMessage(HttpMethod.Get, $"/Catalog/Details?facilityId={facilityId}");
        request.Headers.Add("X-Test-Role", RoleConstants.StorageCustomer);
        request.Headers.Add("X-Test-User", "customer_test@selfstorage.com");

        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var decoded = WebUtility.HtmlDecode(html);

        // Người dùng đã đăng nhập được dẫn trực tiếp đến trang đặt chỗ
        Assert.Contains($"/Reservations?facilityId={facilityId}&unitTypeId={unitTypeId}", decoded);
    }

    [Fact]
    public async Task CatalogDetails_HostileUnitCode_IsHtmlEncodedInSvg()
    {
        // Arrange - Seed cơ sở với ô kho có mã unit_code chứa mã độc XSS
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var hostileCode = "<script>alert('xss-svg')</script>";
        var (facilityId, unitTypeId, unitIds) = await SeedFacilityWithUnitsAsync(suffix, unitCount: 1, mapPositionCount: 1);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
            var unit = await db.storage_units.FindAsync(unitIds[0]);
            unit!.unit_code = hostileCode;
            await db.SaveChangesAsync();
        }

        // Act - Truy vấn chi tiết cơ sở
        var response = await _client.GetAsync($"/Catalog/Details?facilityId={facilityId}");

        // Assert - FINDING 1: Không được xuất mã script thô vào SVG markup
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("<script>alert('xss-svg')</script>", html);
        Assert.Contains("&lt;script&gt;alert(&#x27;xss-svg&#x27;)&lt;/script&gt;", html);
    }

    [Fact]
    public async Task CatalogFloorMap_HostileUnitTypeName_DoesNotCreateExecutableMarkup()
    {
        // Arrange - Seed cơ sở với loại kho có tên chứa payload HTML độc hại
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var hostileTypeName = "\"><img src=x onerror=alert('xss-type')>";
        var (facilityId, unitTypeId, unitIds) = await SeedFacilityWithUnitsAsync(suffix, unitCount: 1, mapPositionCount: 1);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
            var ut = await db.unit_types.FindAsync(unitTypeId);
            ut!.name = hostileTypeName;
            await db.SaveChangesAsync();
        }

        // Act - Tải trang Catalog Details và file JavaScript catalog.js
        var response = await _client.GetAsync($"/Catalog/Details?facilityId={facilityId}");
        var jsResponse = await _client.GetAsync("/js/catalog.js");

        // Assert - FINDING 2: Thuộc tính dữ liệu server-side và DOM client-side không tạo sink thực thi mã độc
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        // 1. Dữ liệu trên thẻ SVG rect được HTML attribute encode an toàn
        Assert.DoesNotContain("\"><img src=x onerror=alert('xss-type')>", html);
        Assert.Contains("&quot;&gt;&lt;img src=x onerror=alert(&#x27;xss-type&#x27;)&gt;", html);

        // 2. Client script catalog.js tuyệt đối không sử dụng các sink nguy hiểm .html(), innerHTML, insertAdjacentHTML
        Assert.Equal(HttpStatusCode.OK, jsResponse.StatusCode);
        var jsCode = await jsResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(".html(", jsCode);
        Assert.DoesNotContain("innerHTML", jsCode);
        Assert.DoesNotContain("insertAdjacentHTML", jsCode);
    }

    [Fact]
    public async Task CatalogDetails_PastStartDate_ReservationUrlUsesNormalizedDates()
    {
        // Arrange
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var (facilityId, unitTypeId, _) = await SeedFacilityWithUnitsAsync(suffix);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SelfStorageManagementSystem.Web.Helpers.AppFormatters.DefaultFacilityTimeZone));

        // Act - Truy vấn ngày bắt đầu trong quá khứ
        var response = await _client.GetAsync($"/Catalog/Details?facilityId={facilityId}&RentalStartDate=2020-01-01&RentalEndDate=2020-02-01");

        // Assert - FINDING 3: UI, query và link đặt chỗ phải sử dụng ngày đã chuẩn hóa
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var decoded = WebUtility.HtmlDecode(html);

        Assert.Contains("Ngày bắt đầu thuê không được nhỏ hơn ngày hiện tại", decoded);
        Assert.DoesNotContain("rentalStartDate=2020-01-01", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RentalStartDate=2020-01-01", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"rentalStartDate={today:yyyy-MM-dd}", html);
    }

    [Fact]
    public async Task CatalogDetails_InvalidDuration_ReservationUrlUsesNormalizedDates()
    {
        // Arrange
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var (facilityId, unitTypeId, _) = await SeedFacilityWithUnitsAsync(suffix);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startDate = today.AddDays(5);
        var invalidShortEndDate = startDate.AddDays(10); // Vi phạm BR-RSV-02: thời hạn tối thiểu 1 tháng

        // Act - Truy vấn khoảng thời gian thuê dưới 1 tháng
        var response = await _client.GetAsync($"/Catalog/Details?facilityId={facilityId}&RentalStartDate={startDate:yyyy-MM-dd}&RentalEndDate={invalidShortEndDate:yyyy-MM-dd}");

        // Assert - FINDING 3: Link đặt chỗ được điều chỉnh sang thời hạn tối thiểu hợp lệ
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var decoded = WebUtility.HtmlDecode(html);

        Assert.Contains("Thời hạn thuê tối thiểu là 1 tháng theo quy định BR-RSV-02", decoded);
        Assert.DoesNotContain($"rentalEndDate={invalidShortEndDate:yyyy-MM-dd}", html, StringComparison.OrdinalIgnoreCase);

        var expectedNormalizedEndDate = startDate.AddMonths(1);
        Assert.Contains($"rentalEndDate={expectedNormalizedEndDate:yyyy-MM-dd}", html);
    }
}
