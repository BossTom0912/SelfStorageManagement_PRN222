using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Catalog;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Implementations;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Repositories.Implementations;
using Xunit;

namespace SelfStorageManagementSystem.Tests;

public class FacilityCatalogServiceTests
{
    private SelfStorageDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<SelfStorageDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new SelfStorageDbContext(options);
    }

    [Fact]
    public async Task GetFacilities_ShouldOnlyReturnActiveFacilities_AndFilterByCity()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());

        var activeHcm = new facility
        {
            id = 1,
            code = "FAC-HCM-01",
            name = "Kho Quận 1 Sài Gòn",
            address_line = "123 Lê Lợi",
            district = "Quận 1",
            city = "Hồ Chí Minh",
            status = "active",
            timezone = "Asia/Ho_Chi_Minh"
        };
        var activeHn = new facility
        {
            id = 2,
            code = "FAC-HN-01",
            name = "Kho Cầu Giấy Hà Nội",
            address_line = "456 Cầu Giấy",
            district = "Cầu Giấy",
            city = "Hà Nội",
            status = "active",
            timezone = "Asia/Ho_Chi_Minh"
        };
        var closedFacility = new facility
        {
            id = 3,
            code = "FAC-HCM-CLOSED",
            name = "Kho Đóng Cửa",
            address_line = "789 Nguyễn Huệ",
            district = "Quận 1",
            city = "Hồ Chí Minh",
            status = "closed",
            timezone = "Asia/Ho_Chi_Minh"
        };

        context.facilities.AddRange(activeHcm, activeHn, closedFacility);
        await context.SaveChangesAsync();

        var repo = new FacilityCatalogRepository(context);
        var service = new FacilityCatalogService(repo, NullLogger<FacilityCatalogService>.Instance);

        // Act 1: Get all active
        var allActive = await service.GetFacilitiesAsync(new GetFacilitiesCatalogRequest { PageNumber = 1, PageSize = 10 });
        Assert.Equal(2, allActive.TotalCount);
        Assert.DoesNotContain(allActive.Items, f => f.Status != "active");

        // Act 2: Filter by City HCM
        var hcmOnly = await service.GetFacilitiesAsync(new GetFacilitiesCatalogRequest
        {
            City = "Hồ Chí Minh",
            PageNumber = 1,
            PageSize = 10
        });
        Assert.Single(hcmOnly.Items);
        Assert.Equal("FAC-HCM-01", hcmOnly.Items.First().Code);
    }

    [Fact]
    public async Task GetFacilityUnitTypes_ShouldOnlyIncludeValidRates_AndExcludeExpiredRates()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());

        var fac = new facility { id = 1, code = "F1", name = "Facility 1", address_line = "A", city = "HCM", status = "active", timezone = "Asia/Ho_Chi_Minh" };
        var typeActive = new unit_type { id = 10, code = "UT-10", name = "Kho Nhỏ", width_m = 2, length_m = 2, height_m = 2.5m, is_active = true };
        var typeInactive = new unit_type { id = 20, code = "UT-20", name = "Kho Cũ", width_m = 3, length_m = 3, height_m = 2.5m, is_active = false };

        var today = FacilityCatalogService.GetCurrentDateInFacilityTimezone("Asia/Ho_Chi_Minh");

        // Valid rate for typeActive
        var validRate = new facility_rate
        {
            id = 100,
            facility_id = 1,
            unit_type_id = 10,
            monthly_rate = 1500000m,
            deposit_amount = 1500000m,
            booking_fee = 50000m,
            valid_from = today.AddDays(-10),
            valid_to = null
        };

        // Expired rate
        var expiredRate = new facility_rate
        {
            id = 101,
            facility_id = 1,
            unit_type_id = 10,
            monthly_rate = 999999m,
            deposit_amount = 999999m,
            valid_from = today.AddYears(-2),
            valid_to = today.AddDays(-1)
        };

        // Rate for inactive unit type
        var inactiveTypeRate = new facility_rate
        {
            id = 102,
            facility_id = 1,
            unit_type_id = 20,
            monthly_rate = 2000000m,
            deposit_amount = 2000000m,
            valid_from = today.AddDays(-10),
            valid_to = null
        };

        context.facilities.Add(fac);
        context.unit_types.AddRange(typeActive, typeInactive);
        context.facility_rates.AddRange(validRate, expiredRate, inactiveTypeRate);
        await context.SaveChangesAsync();

        var repo = new FacilityCatalogRepository(context);
        var service = new FacilityCatalogService(repo, NullLogger<FacilityCatalogService>.Instance);

        var result = await service.GetFacilityUnitTypesAsync(1, new GetFacilityUnitTypesRequest
        {
            RentalStartDate = today,
            RentalEndDate = today.AddMonths(1)
        });

        Assert.Single(result);
        Assert.Equal("UT-10", result.First().Code);
        Assert.Equal(1500000m, result.First().MonthlyRate);
    }

    [Fact]
    public async Task GetFacilityUnitTypes_ShouldValidateBR_RSV_02_RentalDurationConstraints()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
        var fac = new facility { id = 1, code = "F1", name = "Facility 1", address_line = "A", city = "HCM", status = "active", timezone = "Asia/Ho_Chi_Minh" };
        context.facilities.Add(fac);
        await context.SaveChangesAsync();

        var repo = new FacilityCatalogRepository(context);
        var service = new FacilityCatalogService(repo, NullLogger<FacilityCatalogService>.Instance);

        var today = FacilityCatalogService.GetCurrentDateInFacilityTimezone("Asia/Ho_Chi_Minh");

        // Start date in past
        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.GetFacilityUnitTypesAsync(1, new GetFacilityUnitTypesRequest
            {
                RentalStartDate = today.AddDays(-1),
                RentalEndDate = today.AddMonths(1)
            }));

        // End date <= start date
        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.GetFacilityUnitTypesAsync(1, new GetFacilityUnitTypesRequest
            {
                RentalStartDate = today,
                RentalEndDate = today
            }));

        // Less than 1 month (violates BR-RSV-02 min 1 month)
        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.GetFacilityUnitTypesAsync(1, new GetFacilityUnitTypesRequest
            {
                RentalStartDate = today,
                RentalEndDate = today.AddDays(15)
            }));

        // More than 12 months (violates BR-RSV-02 max 12 months)
        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.GetFacilityUnitTypesAsync(1, new GetFacilityUnitTypesRequest
            {
                RentalStartDate = today,
                RentalEndDate = today.AddMonths(13)
            }));
    }

    [Fact]
    public async Task GetFacilityUnitTypes_ShouldDeductUnassignedActiveReservationsFromEstimatedAvailable()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
        var today = FacilityCatalogService.GetCurrentDateInFacilityTimezone("Asia/Ho_Chi_Minh");

        var fac = new facility { id = 1, code = "F1", name = "Facility 1", address_line = "A", city = "HCM", status = "active", timezone = "Asia/Ho_Chi_Minh" };
        var uType = new unit_type { id = 10, code = "UT-10", name = "Kho Vừa", width_m = 2, length_m = 3, height_m = 3, is_active = true };
        var rate = new facility_rate { id = 100, facility_id = 1, unit_type_id = 10, monthly_rate = 2000000m, deposit_amount = 2000000m, valid_from = today.AddDays(-5) };

        // 3 storage units physically available
        var u1 = new storage_unit { id = 101, facility_id = 1, unit_type_id = 10, unit_code = "U101", physical_status = "available", is_listed = true };
        var u2 = new storage_unit { id = 102, facility_id = 1, unit_type_id = 10, unit_code = "U102", physical_status = "available", is_listed = true };
        var u3 = new storage_unit { id = 103, facility_id = 1, unit_type_id = 10, unit_code = "U103", physical_status = "available", is_listed = true };

        // 1 active unassigned confirmed reservation overlapping the rental window
        var r1 = new reservation
        {
            id = 501,
            reservation_code = "RES-001",
            facility_id = 1,
            unit_type_id = 10,
            facility_rate_id = 100,
            start_date = today,
            end_date = today.AddMonths(2),
            status = "confirmed",
            hold_until = DateTimeOffset.UtcNow.AddMinutes(15)
        };

        // 1 expired hold reservation (should NOT deduct)
        var rExpired = new reservation
        {
            id = 502,
            reservation_code = "RES-EXPIRED",
            facility_id = 1,
            unit_type_id = 10,
            facility_rate_id = 100,
            start_date = today,
            end_date = today.AddMonths(2),
            status = "pending",
            hold_until = DateTimeOffset.UtcNow.AddMinutes(-5) // Expired hold
        };

        context.facilities.Add(fac);
        context.unit_types.Add(uType);
        context.facility_rates.Add(rate);
        context.storage_units.AddRange(u1, u2, u3);
        context.reservations.AddRange(r1, rExpired);
        await context.SaveChangesAsync();

        var repo = new FacilityCatalogRepository(context);
        var service = new FacilityCatalogService(repo, NullLogger<FacilityCatalogService>.Instance);

        var result = await service.GetFacilityUnitTypesAsync(1, new GetFacilityUnitTypesRequest
        {
            RentalStartDate = today,
            RentalEndDate = today.AddMonths(1)
        });

        Assert.Single(result);
        var item = result.First();
        Assert.Equal(3, item.TotalListedUnits);
        // 3 candidates - 1 active confirmed unassigned reservation = 2 estimated available
        Assert.Equal(2, item.EstimatedAvailableUnits);
        Assert.Contains("tham khảo", item.AvailabilityNote);
    }

    [Fact]
    public async Task GetAvailableUnits_ShouldFilterStrictlyAccordingTo_BR_OPS_02()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
        var today = FacilityCatalogService.GetCurrentDateInFacilityTimezone("Asia/Ho_Chi_Minh");
        var startDate = today.AddDays(1);
        var endDate = startDate.AddMonths(1);

        var fac1 = new facility { id = 1, code = "F1", name = "Facility 1", address_line = "A", city = "HCM", status = "active", timezone = "Asia/Ho_Chi_Minh" };
        var fac2 = new facility { id = 2, code = "F2", name = "Facility 2", address_line = "B", city = "HN", status = "active", timezone = "Asia/Ho_Chi_Minh" };
        var uType = new unit_type { id = 10, code = "UT-10", name = "Kho Tiêu Chuẩn", width_m = 2, length_m = 2, height_m = 2, is_active = true };

        // 1. Valid Available unit
        var uValid = new storage_unit { id = 1, facility_id = 1, unit_type_id = 10, unit_code = "A-01", physical_status = "available", is_listed = true };

        // 2. Unlisted unit (is_listed = false)
        var uUnlisted = new storage_unit { id = 2, facility_id = 1, unit_type_id = 10, unit_code = "A-02", physical_status = "available", is_listed = false };

        // 3. Occupied (In-Use) unit
        var uOccupied = new storage_unit { id = 3, facility_id = 1, unit_type_id = 10, unit_code = "A-03", physical_status = "occupied", is_listed = true };

        // 4. Reserved unit
        var uReserved = new storage_unit { id = 4, facility_id = 1, unit_type_id = 10, unit_code = "A-04", physical_status = "reserved", is_listed = true };

        // 5. Maintenance unit
        var uMaint = new storage_unit { id = 5, facility_id = 1, unit_type_id = 10, unit_code = "A-05", physical_status = "maintenance", is_listed = true };

        // 6. Out of service unit
        var uOutOfService = new storage_unit { id = 6, facility_id = 1, unit_type_id = 10, unit_code = "A-06", physical_status = "out_of_service", is_listed = true };

        // 7. Unit with blocking maintenance work order
        var uWorkOrder = new storage_unit { id = 7, facility_id = 1, unit_type_id = 10, unit_code = "A-07", physical_status = "available", is_listed = true };
        var mwo = new maintenance_work_order
        {
            id = 70,
            work_order_no = "MWO-01",
            facility_id = 1,
            storage_unit_id = 7,
            title = "Sửa cửa cuốn",
            description = "Hỏng khóa",
            blocks_booking = true,
            status = "in_progress",
            priority = "normal",
            opened_by = 1
        };

        // 8. Unit with active overlapping unit allocation
        var uAlloc = new storage_unit { id = 8, facility_id = 1, unit_type_id = 10, unit_code = "A-08", physical_status = "available", is_listed = true };
        var alloc = new unit_allocation
        {
            id = 80,
            storage_unit_id = 8,
            allocation_kind = "rental",
            allocation_start_date = startDate.AddDays(-5),
            allocation_end_date = startDate.AddDays(10), // Overlaps [startDate, endDate)
            status = "active"
        };

        // 9. Unit from different facility
        var uFac2 = new storage_unit { id = 9, facility_id = 2, unit_type_id = 10, unit_code = "B-01", physical_status = "available", is_listed = true };

        var rate = new facility_rate
        {
            id = 100,
            facility_id = 1,
            unit_type_id = 10,
            monthly_rate = 1500000m,
            deposit_amount = 1500000m,
            valid_from = startDate.AddDays(-10)
        };

        context.facilities.AddRange(fac1, fac2);
        context.unit_types.Add(uType);
        context.facility_rates.Add(rate);
        context.storage_units.AddRange(uValid, uUnlisted, uOccupied, uReserved, uMaint, uOutOfService, uWorkOrder, uAlloc, uFac2);
        context.maintenance_work_orders.Add(mwo);
        context.unit_allocations.Add(alloc);
        await context.SaveChangesAsync();

        var repo = new FacilityCatalogRepository(context);
        var service = new FacilityCatalogService(repo, NullLogger<FacilityCatalogService>.Instance);

        var response = await service.GetAvailableUnitsAsync(1, new GetAvailableUnitsRequest
        {
            RentalStartDate = startDate,
            RentalEndDate = endDate,
            PageNumber = 1,
            PageSize = 10
        });

        // ONLY uValid (A-01) must be in the available units list!
        Assert.Single(response.Items);
        Assert.Equal("A-01", response.Items.First().UnitCode);
        Assert.DoesNotContain(response.Items, u => u.UnitCode != "A-01");
    }

    [Fact]
    public async Task GetFacilityFloorMap_ShouldReflectSimplifiedVisualStatus_WithoutLeakingTenantInfo()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
        var today = FacilityCatalogService.GetCurrentDateInFacilityTimezone("Asia/Ho_Chi_Minh");
        var startDate = today.AddDays(1);
        var endDate = startDate.AddMonths(1);

        var fac = new facility { id = 1, code = "F1", name = "Facility 1", address_line = "A", city = "HCM", status = "active", timezone = "Asia/Ho_Chi_Minh" };
        var area1 = new facility_area { id = 10, facility_id = 1, code = "FL1", name = "Tầng 1", area_type = "floor", is_active = true, map_metadata = "{}" };
        var uType = new unit_type { id = 20, code = "UT-1", name = "Loại 1", width_m = 2, length_m = 2, height_m = 2, is_active = true };

        var rate = new facility_rate
        {
            id = 200,
            facility_id = 1,
            unit_type_id = 20,
            monthly_rate = 1800000m,
            deposit_amount = 1800000m,
            valid_from = startDate.AddDays(-5)
        };

        var uAvailable = new storage_unit { id = 1, facility_id = 1, unit_type_id = 20, area_id = 10, unit_code = "A-1", physical_status = "available", is_listed = true };
        var pos1 = new unit_map_position { unit_id = 1, area_id = 10, x = 10, y = 10, width = 50, height = 50, metadata = "{}" };

        var uOccupied = new storage_unit { id = 2, facility_id = 1, unit_type_id = 20, area_id = 10, unit_code = "A-2", physical_status = "occupied", is_listed = true };
        var pos2 = new unit_map_position { unit_id = 2, area_id = 10, x = 70, y = 10, width = 50, height = 50, metadata = "{}" };

        var uMaint = new storage_unit { id = 3, facility_id = 1, unit_type_id = 20, area_id = 10, unit_code = "A-3", physical_status = "maintenance", is_listed = true };
        var pos3 = new unit_map_position { unit_id = 3, area_id = 10, x = 130, y = 10, width = 50, height = 50, metadata = "{}" };

        // Unit without map position
        var uUnmapped = new storage_unit { id = 4, facility_id = 1, unit_type_id = 20, area_id = 10, unit_code = "A-4", physical_status = "available", is_listed = true };

        context.facilities.Add(fac);
        context.facility_areas.Add(area1);
        context.unit_types.Add(uType);
        context.facility_rates.Add(rate);
        context.storage_units.AddRange(uAvailable, uOccupied, uMaint, uUnmapped);
        context.unit_map_positions.AddRange(pos1, pos2, pos3);
        await context.SaveChangesAsync();

        var repo = new FacilityCatalogRepository(context);
        var service = new FacilityCatalogService(repo, NullLogger<FacilityCatalogService>.Instance);

        var floorMap = await service.GetFacilityFloorMapAsync(1, new GetFacilityFloorMapRequest
        {
            RentalStartDate = startDate,
            RentalEndDate = endDate
        });

        Assert.Equal(3, floorMap.TotalUnitsWithMap);
        Assert.Equal(1, floorMap.TotalUnitsWithoutMap);
        Assert.Equal(3, floorMap.Units.Count);

        var mapItem1 = floorMap.Units.First(u => u.UnitCode == "A-1");
        Assert.Equal("available", mapItem1.DisplayStatus);
        Assert.True(mapItem1.CanSelectToProceed);

        var mapItem2 = floorMap.Units.First(u => u.UnitCode == "A-2");
        Assert.Equal("occupied", mapItem2.DisplayStatus);
        Assert.False(mapItem2.CanSelectToProceed);

        var mapItem3 = floorMap.Units.First(u => u.UnitCode == "A-3");
        Assert.Equal("maintenance", mapItem3.DisplayStatus);
        Assert.False(mapItem3.CanSelectToProceed);
    }

    [Fact]
    public async Task GetAvailableUnits_ShouldExcludeUnits_WhenUnitTypeIsInactive_OrNoValidRate()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
        var today = FacilityCatalogService.GetCurrentDateInFacilityTimezone("Asia/Ho_Chi_Minh");
        var startDate = today.AddDays(1);
        var endDate = startDate.AddMonths(1);

        var fac = new facility { id = 1, code = "F1", name = "Facility 1", address_line = "A", city = "HCM", status = "active", timezone = "Asia/Ho_Chi_Minh" };
        var activeTypeWithRate = new unit_type { id = 10, code = "UT-ACT", name = "Loại Active", is_active = true };
        var inactiveType = new unit_type { id = 20, code = "UT-INACT", name = "Loại Inactive", is_active = false };
        var activeTypeNoRate = new unit_type { id = 30, code = "UT-NORATE", name = "Loại No Rate", is_active = true };

        // Rate only for type 10
        var rate10 = new facility_rate
        {
            id = 100,
            facility_id = 1,
            unit_type_id = 10,
            monthly_rate = 1000000m,
            deposit_amount = 1000000m,
            valid_from = startDate.AddDays(-10)
        };
        // Rate for inactive type 20
        var rate20 = new facility_rate
        {
            id = 200,
            facility_id = 1,
            unit_type_id = 20,
            monthly_rate = 2000000m,
            deposit_amount = 2000000m,
            valid_from = startDate.AddDays(-10)
        };

        var u1 = new storage_unit { id = 1, facility_id = 1, unit_type_id = 10, unit_code = "U-VALID", physical_status = "available", is_listed = true };
        var u2 = new storage_unit { id = 2, facility_id = 1, unit_type_id = 20, unit_code = "U-INACT", physical_status = "available", is_listed = true };
        var u3 = new storage_unit { id = 3, facility_id = 1, unit_type_id = 30, unit_code = "U-NORATE", physical_status = "available", is_listed = true };

        context.facilities.Add(fac);
        context.unit_types.AddRange(activeTypeWithRate, inactiveType, activeTypeNoRate);
        context.facility_rates.AddRange(rate10, rate20);
        context.storage_units.AddRange(u1, u2, u3);
        await context.SaveChangesAsync();

        var repo = new FacilityCatalogRepository(context);
        var service = new FacilityCatalogService(repo, NullLogger<FacilityCatalogService>.Instance);

        var result = await service.GetAvailableUnitsAsync(1, new GetAvailableUnitsRequest
        {
            RentalStartDate = startDate,
            RentalEndDate = endDate
        });

        Assert.Single(result.Items);
        Assert.Equal("U-VALID", result.Items.First().UnitCode);
    }

    [Fact]
    public async Task GetFacilityFloorMap_ShouldSetCanSelectToProceedFalse_WhenUnitTypeBookingCapacityIsZero()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
        var today = FacilityCatalogService.GetCurrentDateInFacilityTimezone("Asia/Ho_Chi_Minh");
        var startDate = today.AddDays(1);
        var endDate = startDate.AddMonths(1);

        var fac = new facility { id = 1, code = "F1", name = "Facility 1", address_line = "A", city = "HCM", status = "active", timezone = "Asia/Ho_Chi_Minh" };
        var area = new facility_area { id = 10, facility_id = 1, code = "A1", name = "Khu 1", area_type = "floor", map_metadata = "{}", is_active = true };
        var uType = new unit_type { id = 10, code = "UT-10", name = "Loại 10", is_active = true };
        var rate = new facility_rate { id = 100, facility_id = 1, unit_type_id = 10, monthly_rate = 1000000m, deposit_amount = 1000000m, valid_from = startDate.AddDays(-5) };

        // 1 physically available unit
        var u1 = new storage_unit { id = 1, facility_id = 1, unit_type_id = 10, area_id = 10, unit_code = "U-1", physical_status = "available", is_listed = true };
        var pos1 = new unit_map_position { unit_id = 1, area_id = 10, x = 10, y = 10, width = 50, height = 50, metadata = "{}" };

        // 1 active unassigned reservation exhausting the capacity for this unit type
        var res = new reservation
        {
            id = 501,
            reservation_code = "RES-501",
            facility_id = 1,
            unit_type_id = 10,
            facility_rate_id = 100,
            start_date = startDate,
            end_date = endDate,
            status = "confirmed",
            hold_until = DateTimeOffset.UtcNow.AddHours(1)
        };

        context.facilities.Add(fac);
        context.facility_areas.Add(area);
        context.unit_types.Add(uType);
        context.facility_rates.Add(rate);
        context.storage_units.Add(u1);
        context.unit_map_positions.Add(pos1);
        context.reservations.Add(res);
        await context.SaveChangesAsync();

        var repo = new FacilityCatalogRepository(context);
        var service = new FacilityCatalogService(repo, NullLogger<FacilityCatalogService>.Instance);

        var map = await service.GetFacilityFloorMapAsync(1, new GetFacilityFloorMapRequest
        {
            RentalStartDate = startDate,
            RentalEndDate = endDate
        });

        var unitItem = Assert.Single(map.Units);
        Assert.Equal("available", unitItem.DisplayStatus);
        // Finding C: Because capacity is 0 (1 physical candidate - 1 unassigned active reservation = 0), CanSelectToProceed must be false!
        Assert.False(unitItem.CanSelectToProceed);
    }

    [Fact]
    public async Task GetFacilityUnitTypes_ShouldFilterAndValidateArea_Correctly()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
        var today = FacilityCatalogService.GetCurrentDateInFacilityTimezone("Asia/Ho_Chi_Minh");
        var startDate = today.AddDays(1);
        var endDate = startDate.AddMonths(1);

        var fac = new facility { id = 1, code = "F1", name = "Facility 1", address_line = "A", city = "HCM", status = "active", timezone = "Asia/Ho_Chi_Minh" };
        var utSmall = new unit_type { id = 10, code = "UT-S", name = "Nhỏ", area_m2 = 4.0m, is_active = true };
        var utLarge = new unit_type { id = 20, code = "UT-L", name = "Lớn", area_m2 = 16.0m, is_active = true };

        var rateSmall = new facility_rate { id = 100, facility_id = 1, unit_type_id = 10, monthly_rate = 1000000m, deposit_amount = 1000000m, valid_from = startDate.AddDays(-5) };
        var rateLarge = new facility_rate { id = 200, facility_id = 1, unit_type_id = 20, monthly_rate = 3000000m, deposit_amount = 3000000m, valid_from = startDate.AddDays(-5) };

        context.facilities.Add(fac);
        context.unit_types.AddRange(utSmall, utLarge);
        context.facility_rates.AddRange(rateSmall, rateLarge);
        await context.SaveChangesAsync();

        var repo = new FacilityCatalogRepository(context);
        var service = new FacilityCatalogService(repo, NullLogger<FacilityCatalogService>.Instance);

        // Validation test: minArea > maxArea
        await Assert.ThrowsAsync<BadRequestException>(() => service.GetFacilityUnitTypesAsync(1, new GetFacilityUnitTypesRequest
        {
            RentalStartDate = startDate,
            RentalEndDate = endDate,
            MinAreaM2 = 10.0m,
            MaxAreaM2 = 5.0m
        }));

        // Filter test: area >= 10.0m2
        var largeOnly = await service.GetFacilityUnitTypesAsync(1, new GetFacilityUnitTypesRequest
        {
            RentalStartDate = startDate,
            RentalEndDate = endDate,
            MinAreaM2 = 10.0m
        });

        var item = Assert.Single(largeOnly);
        Assert.Equal("UT-L", item.Code);
    }

    [Fact]
    public async Task GetAvailableUnits_WhenCapacityIsZeroDueToActiveUnassignedReservation_ShouldExcludeUnits()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
        var today = FacilityCatalogService.GetCurrentDateInFacilityTimezone("Asia/Ho_Chi_Minh");
        var startDate = today.AddDays(1);
        var endDate = startDate.AddMonths(1);

        var fac = new facility { id = 1, code = "F1", name = "Facility 1", address_line = "A", city = "HCM", status = "active", timezone = "Asia/Ho_Chi_Minh" };
        var uType = new unit_type { id = 10, code = "UT-10", name = "Kho Nhỏ", width_m = 2, length_m = 2, height_m = 2.5m, is_active = true };
        var rate = new facility_rate
        {
            id = 100,
            facility_id = 1,
            unit_type_id = 10,
            monthly_rate = 1500000m,
            deposit_amount = 1500000m,
            booking_fee = 50000m,
            valid_from = startDate.AddDays(-10),
            valid_to = null
        };

        // 1 physical unit that has physical_status == "available"
        var u1 = new storage_unit
        {
            id = 1,
            facility_id = 1,
            unit_type_id = 10,
            unit_code = "U101",
            physical_status = "available",
            is_listed = true
        };

        // 1 active unassigned reservation overlapping rental dates -> capacity becomes 0 (1 physical - 1 reservation = 0)
        var res = new reservation
        {
            id = 501,
            reservation_code = "RES-501",
            facility_id = 1,
            unit_type_id = 10,
            facility_rate_id = 100,
            start_date = startDate,
            end_date = endDate,
            status = "confirmed",
            hold_until = DateTimeOffset.UtcNow.AddHours(1)
        };

        context.facilities.Add(fac);
        context.unit_types.Add(uType);
        context.facility_rates.Add(rate);
        context.storage_units.Add(u1);
        context.reservations.Add(res);
        await context.SaveChangesAsync();

        var repo = new FacilityCatalogRepository(context);
        var service = new FacilityCatalogService(repo, NullLogger<FacilityCatalogService>.Instance);

        var result = await service.GetAvailableUnitsAsync(1, new GetAvailableUnitsRequest
        {
            RentalStartDate = startDate,
            RentalEndDate = endDate,
            PageNumber = 1,
            PageSize = 10
        });

        // Consistent contract: since unit type capacity is 0, this unit must not be listed as available for booking
        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetAvailableUnits_WithCandidateUnitsAndUnassignedReservations_ShouldReturnCandidatesWhileCapacityReflectsRemainingSlots()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
        var today = FacilityCatalogService.GetCurrentDateInFacilityTimezone("Asia/Ho_Chi_Minh");
        var startDate = today.AddDays(1);
        var endDate = startDate.AddMonths(1);

        var fac = new facility { id = 1, code = "F1", name = "Facility 1", address_line = "A", city = "HCM", status = "active", timezone = "Asia/Ho_Chi_Minh" };
        var uType = new unit_type { id = 10, code = "UT-10", name = "Kho Tiêu Chuẩn", width_m = 2, length_m = 2, height_m = 2.5m, is_active = true };
        var rate = new facility_rate
        {
            id = 100,
            facility_id = 1,
            unit_type_id = 10,
            monthly_rate = 1500000m,
            deposit_amount = 1500000m,
            booking_fee = 50000m,
            valid_from = startDate.AddDays(-10),
            valid_to = null
        };

        // 3 physical storage units available
        var u1 = new storage_unit { id = 1, facility_id = 1, unit_type_id = 10, unit_code = "U101", physical_status = "available", is_listed = true };
        var u2 = new storage_unit { id = 2, facility_id = 1, unit_type_id = 10, unit_code = "U102", physical_status = "available", is_listed = true };
        var u3 = new storage_unit { id = 3, facility_id = 1, unit_type_id = 10, unit_code = "U103", physical_status = "available", is_listed = true };

        // 2 active unassigned reservations overlapping rental dates
        var res1 = new reservation
        {
            id = 501,
            reservation_code = "RES-501",
            facility_id = 1,
            unit_type_id = 10,
            facility_rate_id = 100,
            start_date = startDate,
            end_date = endDate,
            status = "confirmed",
            hold_until = DateTimeOffset.UtcNow.AddHours(1)
        };
        var res2 = new reservation
        {
            id = 502,
            reservation_code = "RES-502",
            facility_id = 1,
            unit_type_id = 10,
            facility_rate_id = 100,
            start_date = startDate,
            end_date = endDate,
            status = "confirmed",
            hold_until = DateTimeOffset.UtcNow.AddHours(1)
        };

        context.facilities.Add(fac);
        context.unit_types.Add(uType);
        context.facility_rates.Add(rate);
        context.storage_units.AddRange(u1, u2, u3);
        context.reservations.AddRange(res1, res2);
        await context.SaveChangesAsync();

        var repo = new FacilityCatalogRepository(context);
        var service = new FacilityCatalogService(repo, NullLogger<FacilityCatalogService>.Instance);

        // 1. Check Unit Type Booking Capacity
        var unitTypes = await service.GetFacilityUnitTypesAsync(1, new GetFacilityUnitTypesRequest
        {
            RentalStartDate = startDate,
            RentalEndDate = endDate
        });
        var typeDto = Assert.Single(unitTypes);
        // Booking capacity is 3 physical - 2 reservations = 1 slot remaining
        Assert.Equal(1, typeDto.EstimatedAvailableUnits);

        // 2. Check Available Storage Units (Physical Candidates)
        var result = await service.GetAvailableUnitsAsync(1, new GetAvailableUnitsRequest
        {
            RentalStartDate = startDate,
            RentalEndDate = endDate,
            PageNumber = 1,
            PageSize = 10
        });

        // Per BR-RSV-03: all 3 units are physical candidate units eligible for assignment upon check-in;
        // TotalCount = 3 represents the number of physical candidate units, NOT 3 remaining booking slots.
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.Items.Count);
        Assert.Contains(result.Items, u => u.UnitCode == "U101");
        Assert.Contains(result.Items, u => u.UnitCode == "U102");
        Assert.Contains(result.Items, u => u.UnitCode == "U103");
    }
}



