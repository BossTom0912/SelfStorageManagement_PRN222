using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Catalog;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using Xunit;

namespace SelfStorageManagementSystem.Tests;

public class CatalogIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CatalogIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task SeedCatalogDataAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();

        if (db.facilities.Any(f => f.code == "INTEG-FAC-01"))
            return;

        var facility = new facility
        {
            id = 901,
            code = "INTEG-FAC-01",
            name = "Cơ sở Tân Bình Demo",
            address_line = "100 Hoàng Văn Thụ",
            district = "Tân Bình",
            city = "Hồ Chí Minh",
            status = "active",
            timezone = "Asia/Ho_Chi_Minh",
            opening_time = new TimeOnly(7, 0),
            closing_time = new TimeOnly(22, 0)
        };
        db.facilities.Add(facility);

        var area = new facility_area
        {
            id = 901,
            facility_id = 901,
            code = "TB-Z1",
            name = "Khu Vực A - Tầng 1",
            area_type = "floor",
            display_order = 1,
            map_metadata = "{}",
            is_active = true
        };
        db.facility_areas.Add(area);

        var unitType = new unit_type
        {
            id = 901,
            code = "INTEG-UT-S",
            name = "Kho Nhỏ Tiêu Chuẩn",
            width_m = 2.0m,
            length_m = 2.5m,
            height_m = 2.8m,
            area_m2 = 5.0m,
            climate_controlled = true,
            is_active = true
        };
        db.unit_types.Add(unitType);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rate = new facility_rate
        {
            id = 901,
            facility_id = 901,
            unit_type_id = 901,
            monthly_rate = 1200000m,
            deposit_amount = 1200000m,
            booking_fee = 50000m,
            valid_from = today.AddDays(-10)
        };
        db.facility_rates.Add(rate);

        var unit1 = new storage_unit
        {
            id = 9001,
            facility_id = 901,
            unit_type_id = 901,
            area_id = 901,
            unit_code = "TB-101",
            floor_label = "Tầng 1",
            zone_label = "Khu A",
            physical_status = "available",
            is_listed = true
        };
        var unit2 = new storage_unit
        {
            id = 9002,
            facility_id = 901,
            unit_type_id = 901,
            area_id = 901,
            unit_code = "TB-102",
            floor_label = "Tầng 1",
            zone_label = "Khu A",
            physical_status = "occupied",
            is_listed = true
        };
        db.storage_units.AddRange(unit1, unit2);

        var pos1 = new unit_map_position
        {
            unit_id = 9001,
            area_id = 901,
            x = 20,
            y = 30,
            width = 60,
            height = 40,
            metadata = "{}"
        };
        var pos2 = new unit_map_position
        {
            unit_id = 9002,
            area_id = 901,
            x = 90,
            y = 30,
            width = 60,
            height = 40,
            metadata = "{}"
        };
        db.unit_map_positions.AddRange(pos1, pos2);

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetFacilities_Endpoint_ShouldReturnSuccess_WithoutAuthentication()
    {
        await SeedCatalogDataAsync();

        var response = await _client.GetAsync("/api/facilities?city=Hồ Chí Minh&pageNumber=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<FacilityCatalogDto>>>();
        Assert.NotNull(body);
        Assert.True(body.Success);
        Assert.NotNull(body.Data);
        Assert.Contains(body.Data.Items, f => f.Code == "INTEG-FAC-01");
    }

    [Fact]
    public async Task GetFacilityById_Endpoint_ShouldReturnDetails_WhenFacilityExists()
    {
        await SeedCatalogDataAsync();

        var response = await _client.GetAsync("/api/facilities/901");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<FacilityCatalogDto>>();
        Assert.NotNull(body);
        Assert.True(body.Success);
        Assert.Equal("INTEG-FAC-01", body.Data?.Code);
        Assert.Equal("Cơ sở Tân Bình Demo", body.Data?.Name);
    }

    [Fact]
    public async Task GetFacilityUnitTypes_Endpoint_ShouldReturnTypesAndPricing()
    {
        await SeedCatalogDataAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var response = await _client.GetAsync($"/api/facilities/901/unit-types?rentalStartDate={today:yyyy-MM-dd}&rentalEndDate={today.AddMonths(1):yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<List<FacilityUnitTypeCatalogDto>>>();
        Assert.NotNull(body);
        Assert.True(body.Success);
        Assert.NotNull(body.Data);
        Assert.Contains(body.Data, t => t.Code == "INTEG-UT-S" && t.MonthlyRate == 1200000m);
    }

    [Fact]
    public async Task GetAvailableUnits_Endpoint_ShouldReturnAvailableUnits_AndExcludeOccupied()
    {
        await SeedCatalogDataAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var response = await _client.GetAsync($"/api/facilities/901/units/available?rentalStartDate={today:yyyy-MM-dd}&rentalEndDate={today.AddMonths(1):yyyy-MM-dd}&pageNumber=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<AvailableStorageUnitDto>>>();
        Assert.NotNull(body);
        Assert.True(body.Success);
        Assert.NotNull(body.Data);

        // TB-101 is available -> present
        Assert.Contains(body.Data.Items, u => u.UnitCode == "TB-101");
        // TB-102 is occupied -> ABSENT (BR-OPS-02)
        Assert.DoesNotContain(body.Data.Items, u => u.UnitCode == "TB-102");
    }

    [Fact]
    public async Task GetFloorMap_Endpoint_ShouldReturnMapCoordinatesAndDisplayStatus()
    {
        await SeedCatalogDataAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var response = await _client.GetAsync($"/api/facilities/901/floor-map?rentalStartDate={today:yyyy-MM-dd}&rentalEndDate={today.AddMonths(1):yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<FacilityFloorMapDto>>();
        Assert.NotNull(body);
        Assert.True(body.Success);
        Assert.NotNull(body.Data);

        var map = body.Data;
        Assert.Equal("Cơ sở Tân Bình Demo", map.FacilityName);
        Assert.Equal(2, map.TotalUnitsWithMap);

        var u1 = map.Units.First(u => u.UnitCode == "TB-101");
        Assert.Equal("available", u1.DisplayStatus);
        Assert.True(u1.CanSelectToProceed);

        var u2 = map.Units.First(u => u.UnitCode == "TB-102");
        Assert.Equal("occupied", u2.DisplayStatus);
        Assert.False(u2.CanSelectToProceed);
    }

    [Fact]
    public async Task GetFacilityUnitTypes_Endpoint_ShouldFilterByArea_AndValidateNegativeParameters()
    {
        await SeedCatalogDataAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        // 1. Validation test: negative area returns BadRequest (400)
        var negativeAreaResp = await _client.GetAsync($"/api/facilities/901/unit-types?rentalStartDate={today:yyyy-MM-dd}&rentalEndDate={today.AddMonths(1):yyyy-MM-dd}&minAreaM2=-5");
        Assert.Equal(HttpStatusCode.BadRequest, negativeAreaResp.StatusCode);

        // 2. Filter test: area filter matching seeded type (width 2.0 * length 2.5 = 5.0m2)
        var matchResp = await _client.GetAsync($"/api/facilities/901/unit-types?rentalStartDate={today:yyyy-MM-dd}&rentalEndDate={today.AddMonths(1):yyyy-MM-dd}&minAreaM2=4.0&maxAreaM2=6.0");
        Assert.Equal(HttpStatusCode.OK, matchResp.StatusCode);
        var matchBody = await matchResp.Content.ReadFromJsonAsync<ApiResponse<List<FacilityUnitTypeCatalogDto>>>();
        Assert.NotNull(matchBody?.Data);
        Assert.NotEmpty(matchBody.Data);

        // 3. Filter test: area filter out of range
        var mismatchResp = await _client.GetAsync($"/api/facilities/901/unit-types?rentalStartDate={today:yyyy-MM-dd}&rentalEndDate={today.AddMonths(1):yyyy-MM-dd}&minAreaM2=20.0");
        Assert.Equal(HttpStatusCode.OK, mismatchResp.StatusCode);
        var mismatchBody = await mismatchResp.Content.ReadFromJsonAsync<ApiResponse<List<FacilityUnitTypeCatalogDto>>>();
        Assert.NotNull(mismatchBody?.Data);
        Assert.Empty(mismatchBody.Data);
    }
}

