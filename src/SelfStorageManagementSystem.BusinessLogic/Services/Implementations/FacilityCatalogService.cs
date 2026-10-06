using Microsoft.Extensions.Logging;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Catalog;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Catalog;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Implementations;

public class FacilityCatalogService : IFacilityCatalogService
{
    private readonly IFacilityCatalogRepository _catalogRepository;
    private readonly ILogger<FacilityCatalogService> _logger;

    public FacilityCatalogService(
        IFacilityCatalogRepository catalogRepository,
        ILogger<FacilityCatalogService> logger)
    {
        _catalogRepository = catalogRepository;
        _logger = logger;
    }

    public async Task<PagedResult<FacilityCatalogDto>> GetFacilitiesAsync(
        GetFacilitiesCatalogRequest request,
        CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _catalogRepository.GetPagedActiveFacilitiesAsync(
            request.City,
            request.District,
            request.SearchTerm,
            request.PageNumber,
            request.PageSize,
            cancellationToken);

        var dtos = items.Select(MapToFacilityCatalogDto).ToList();

        return new PagedResult<FacilityCatalogDto>(
            dtos,
            totalCount,
            request.PageNumber,
            request.PageSize);
    }

    public async Task<FacilityCatalogDto> GetFacilityByIdAsync(
        long facilityId,
        CancellationToken cancellationToken = default)
    {
        var facility = await _catalogRepository.GetActiveFacilityByIdAsync(facilityId, cancellationToken);
        if (facility == null)
        {
            throw new NotFoundException($"Cơ sở với mã ID {facilityId} không tồn tại hoặc hiện không mở cửa hoạt động.");
        }

        return MapToFacilityCatalogDto(facility);
    }

    public async Task<List<FacilityUnitTypeCatalogDto>> GetFacilityUnitTypesAsync(
        long facilityId,
        GetFacilityUnitTypesRequest request,
        CancellationToken cancellationToken = default)
    {
        var facility = await _catalogRepository.GetActiveFacilityByIdAsync(facilityId, cancellationToken);
        if (facility == null)
        {
            throw new NotFoundException($"Cơ sở với mã ID {facilityId} không tồn tại hoặc hiện không mở cửa hoạt động.");
        }

        ValidateFilterParameters(request.MaxPrice, request.MinAreaM2, request.MaxAreaM2);

        var (startDate, endDate) = ValidateAndResolveRentalDates(
            facility.timezone,
            request.RentalStartDate,
            request.RentalEndDate);

        var rates = await _catalogRepository.GetActiveFacilityRatesAsync(
            facilityId,
            startDate,
            request.MaxPrice,
            request.ClimateControlled,
            request.MinAreaM2,
            request.MaxAreaM2,
            cancellationToken);

        var result = new List<FacilityUnitTypeCatalogDto>();
        var nowUtc = DateTimeOffset.UtcNow;
        var unitTypeIds = rates.Select(r => r.unit_type_id).Distinct();
        var capacityMap = await ComputeUnitTypeCapacitiesAsync(facilityId, startDate, endDate, nowUtc, unitTypeIds, cancellationToken);

        foreach (var rate in rates)
        {
            var unitType = rate.unit_type;

            var totalListed = await _catalogRepository.CountTotalListedUnitsAsync(
                facilityId,
                unitType.id,
                cancellationToken);

            var estimatedAvailable = capacityMap.TryGetValue(unitType.id, out var cap) ? cap : 0;

            result.Add(new FacilityUnitTypeCatalogDto
            {
                UnitTypeId = unitType.id,
                FacilityId = facilityId,
                Code = unitType.code,
                Name = unitType.name,
                WidthM = unitType.width_m,
                LengthM = unitType.length_m,
                HeightM = unitType.height_m,
                AreaM2 = unitType.area_m2,
                VolumeM3 = unitType.volume_m3,
                ClimateControlled = unitType.climate_controlled,
                MaxWeightKg = unitType.max_weight_kg,
                Description = unitType.description,
                MonthlyRate = rate.monthly_rate,
                DepositAmount = rate.deposit_amount,
                BookingFee = rate.booking_fee,
                RateValidFrom = rate.valid_from,
                RateValidTo = rate.valid_to,
                TotalListedUnits = totalListed,
                EstimatedAvailableUnits = estimatedAvailable,
                AvailabilityNote = $"Số ô trống tham khảo tại thời điểm tra cứu ({estimatedAvailable} ô khả dụng). " +
                                   "Khách hàng đăng ký đặt chỗ theo Loại kho (BR-RSV-03); số lượng thực tế sẽ được giữ chỗ xác thực tại bước đặt chỗ (Chức năng 3)."
            });
        }

        return result;
    }

    public async Task<PagedResult<AvailableStorageUnitDto>> GetAvailableUnitsAsync(
        long facilityId,
        GetAvailableUnitsRequest request,
        CancellationToken cancellationToken = default)
    {
        var facility = await _catalogRepository.GetActiveFacilityByIdAsync(facilityId, cancellationToken);
        if (facility == null)
        {
            throw new NotFoundException($"Cơ sở với mã ID {facilityId} không tồn tại hoặc hiện không mở cửa hoạt động.");
        }

        ValidateFilterParameters(request.MaxPrice, request.MinAreaM2, request.MaxAreaM2);

        var (startDate, endDate) = ValidateAndResolveRentalDates(
            facility.timezone,
            request.RentalStartDate,
            request.RentalEndDate);

        // Fetch rates matching filters to discover active unit types with valid rates
        var activeRates = await _catalogRepository.GetActiveFacilityRatesAsync(
            facilityId,
            startDate,
            request.MaxPrice,
            request.ClimateControlled,
            request.MinAreaM2,
            request.MaxAreaM2,
            cancellationToken);

        var nowUtc = DateTimeOffset.UtcNow;
        var candidateTypeIds = request.UnitTypeId.HasValue
            ? activeRates.Where(r => r.unit_type_id == request.UnitTypeId.Value).Select(r => r.unit_type_id).Distinct().ToList()
            : activeRates.Select(r => r.unit_type_id).Distinct().ToList();

        var capacityMap = await ComputeUnitTypeCapacitiesAsync(facilityId, startDate, endDate, nowUtc, candidateTypeIds, cancellationToken);

        // Consistent contract according to BR-RSV-03:
        // A unit is only available for booking if its unit type has capacity > 0
        var eligibleUnitTypeIds = candidateTypeIds.Where(tid => capacityMap.TryGetValue(tid, out var cap) && cap > 0).ToList();

        if (eligibleUnitTypeIds.Count == 0)
        {
            return new PagedResult<AvailableStorageUnitDto>(
                new List<AvailableStorageUnitDto>(),
                0,
                request.PageNumber,
                request.PageSize);
        }

        var (units, totalCount) = await _catalogRepository.GetPagedAvailableUnitsAsync(
            facilityId,
            eligibleUnitTypeIds,
            request.AreaId,
            request.ClimateControlled,
            request.MaxPrice,
            request.MinAreaM2,
            request.MaxAreaM2,
            startDate,
            endDate,
            request.PageNumber,
            request.PageSize,
            cancellationToken);

        var ratesMap = activeRates
            .GroupBy(r => r.unit_type_id)
            .ToDictionary(g => g.Key, g => g.First().monthly_rate);

        var dtos = units.Select(u => new AvailableStorageUnitDto
        {
            Id = u.id,
            FacilityId = u.facility_id,
            UnitTypeId = u.unit_type_id,
            UnitTypeName = u.unit_type?.name ?? string.Empty,
            UnitCode = u.unit_code,
            AreaId = u.area_id,
            AreaName = u.area?.name,
            FloorLabel = u.floor_label,
            ZoneLabel = u.zone_label,
            PhysicalStatus = u.physical_status,
            IsListed = u.is_listed,
            WidthM = u.unit_type?.width_m ?? 0,
            LengthM = u.unit_type?.length_m ?? 0,
            HeightM = u.unit_type?.height_m ?? 0,
            AreaM2 = u.unit_type?.area_m2,
            VolumeM3 = u.unit_type?.volume_m3,
            ClimateControlled = u.unit_type?.climate_controlled ?? false,
            MonthlyRate = ratesMap.TryGetValue(u.unit_type_id, out var mr) ? mr : null,
            HasMapPosition = u.unit_map_position != null,
            MapX = u.unit_map_position?.x,
            MapY = u.unit_map_position?.y,
            MapWidth = u.unit_map_position?.width,
            MapHeight = u.unit_map_position?.height
        }).ToList();

        return new PagedResult<AvailableStorageUnitDto>(
            dtos,
            totalCount,
            request.PageNumber,
            request.PageSize);
    }

    public async Task<FacilityFloorMapDto> GetFacilityFloorMapAsync(
        long facilityId,
        GetFacilityFloorMapRequest request,
        CancellationToken cancellationToken = default)
    {
        var facility = await _catalogRepository.GetActiveFacilityByIdAsync(facilityId, cancellationToken);
        if (facility == null)
        {
            throw new NotFoundException($"Cơ sở với mã ID {facilityId} không tồn tại hoặc hiện không mở cửa hoạt động.");
        }

        var (startDate, endDate) = ValidateAndResolveRentalDates(
            facility.timezone,
            request.RentalStartDate,
            request.RentalEndDate);

        var areas = await _catalogRepository.GetFacilityAreasAsync(facilityId, cancellationToken);
        var allUnits = await _catalogRepository.GetAllUnitsForFloorMapAsync(
            facilityId,
            request.AreaId,
            cancellationToken);

        var activeRates = await _catalogRepository.GetActiveFacilityRatesAsync(
            facilityId,
            startDate,
            null,
            null,
            null,
            null,
            cancellationToken);

        var ratesMap = activeRates
            .GroupBy(r => r.unit_type_id)
            .ToDictionary(g => g.Key, g => g.First().monthly_rate);

        var nowUtc = DateTimeOffset.UtcNow;
        var unitTypeIds = activeRates.Select(r => r.unit_type_id).Distinct();
        var unitTypeCapacityMap = await ComputeUnitTypeCapacitiesAsync(facilityId, startDate, endDate, nowUtc, unitTypeIds, cancellationToken);

        var areaDtos = areas.Select(a => new FacilityAreaDto
        {
            Id = a.id,
            Code = a.code,
            Name = a.name,
            AreaType = a.area_type,
            DisplayOrder = a.display_order,
            ParentAreaId = a.parent_area_id
        }).ToList();

        var unitMapItems = new List<UnitMapItemDto>();
        int withMapCount = 0;
        int withoutMapCount = 0;

        foreach (var u in allUnits)
        {
            if (u.unit_map_position == null)
            {
                withoutMapCount++;
                continue;
            }

            withMapCount++;

            // Evaluate display status strictly without disclosing tenant identity or internal notes
            string displayStatus;

            var hasActiveAllocation = u.unit_allocations.Any(a =>
                a.status == "active" &&
                a.allocation_start_date < endDate &&
                a.allocation_end_date > startDate);

            var hasBlockingMaintenance = u.maintenance_work_orders.Any(m =>
                m.blocks_booking &&
                m.status != "completed" &&
                m.status != "cancelled");

            if (!u.is_listed)
            {
                displayStatus = "unavailable";
            }
            else if (u.physical_status == "occupied" || hasActiveAllocation)
            {
                displayStatus = "occupied";
            }
            else if (u.physical_status == "reserved")
            {
                displayStatus = "reserved";
            }
            else if (u.physical_status == "maintenance" || hasBlockingMaintenance)
            {
                displayStatus = "maintenance";
            }
            else if (u.physical_status == "available")
            {
                displayStatus = "available";
            }
            else
            {
                displayStatus = "unavailable";
            }

            // Consistency with unit-type level booking capacity & valid rate (Finding C / BR-RSV-03):
            // Unit can only proceed to reservation if its type is active, has a valid rate, and has capacity > 0.
            bool isTypeActive = u.unit_type?.is_active ?? false;
            bool hasValidRate = ratesMap.ContainsKey(u.unit_type_id);
            bool hasBookingCapacity = unitTypeCapacityMap.TryGetValue(u.unit_type_id, out var cap) && cap > 0;

            bool canSelectToProceed = (displayStatus == "available") && isTypeActive && hasValidRate && hasBookingCapacity;

            var pos = u.unit_map_position;
            unitMapItems.Add(new UnitMapItemDto
            {
                UnitId = u.id,
                AreaId = pos.area_id,
                AreaName = u.area?.name ?? string.Empty,
                UnitCode = u.unit_code,
                UnitTypeId = u.unit_type_id,
                UnitTypeName = u.unit_type?.name ?? string.Empty,
                AreaM2 = u.unit_type?.area_m2,
                VolumeM3 = u.unit_type?.volume_m3,
                ClimateControlled = u.unit_type?.climate_controlled ?? false,
                X = pos.x,
                Y = pos.y,
                Width = pos.width,
                Height = pos.height,
                RotationDegrees = pos.rotation_degrees,
                DisplayStatus = displayStatus,
                CanSelectToProceed = canSelectToProceed,
                MonthlyRate = ratesMap.TryGetValue(u.unit_type_id, out var mr) ? mr : null
            });
        }

        return new FacilityFloorMapDto
        {
            FacilityId = facility.id,
            FacilityName = facility.name,
            Areas = areaDtos,
            Units = unitMapItems,
            TotalUnitsWithMap = withMapCount,
            TotalUnitsWithoutMap = withoutMapCount
        };
    }

    private static FacilityCatalogDto MapToFacilityCatalogDto(facility f)
    {
        return new FacilityCatalogDto
        {
            Id = f.id,
            Code = f.code,
            Name = f.name,
            AddressLine = f.address_line,
            Ward = f.ward,
            District = f.district,
            City = f.city,
            Latitude = f.latitude,
            Longitude = f.longitude,
            Timezone = f.timezone,
            OpeningTime = f.opening_time,
            ClosingTime = f.closing_time,
            Status = f.status
        };
    }

    public static DateOnly GetCurrentDateInFacilityTimezone(string timezoneId)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
            var nowInTz = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            return DateOnly.FromDateTime(nowInTz);
        }
        catch
        {
            // Default to UTC+7 (Asia/Ho_Chi_Minh)
            var offset = TimeSpan.FromHours(7);
            var nowOffset = DateTimeOffset.UtcNow.ToOffset(offset);
            return DateOnly.FromDateTime(nowOffset.DateTime);
        }
    }

    private static (DateOnly StartDate, DateOnly EndDate) ValidateAndResolveRentalDates(
        string facilityTimezone,
        DateOnly? rentalStartDate,
        DateOnly? rentalEndDate)
    {
        var todayInTz = GetCurrentDateInFacilityTimezone(facilityTimezone);

        var startDate = rentalStartDate ?? todayInTz;
        if (startDate < todayInTz)
        {
            throw new BadRequestException("Ngày bắt đầu thuê không được nhỏ hơn ngày hiện tại theo giờ cơ sở.");
        }

        var endDate = rentalEndDate ?? startDate.AddMonths(1);

        if (endDate <= startDate)
        {
            throw new BadRequestException("Ngày kết thúc thuê phải lớn hơn ngày bắt đầu thuê.");
        }

        // BR-RSV-02: Min 1 month, Max 12 months
        if (endDate < startDate.AddMonths(1))
        {
            throw new BadRequestException("Thời hạn thuê tối thiểu là 1 tháng theo quy định BR-RSV-02.");
        }

        if (endDate > startDate.AddMonths(12))
        {
            throw new BadRequestException("Thời hạn thuê tối đa cho mỗi lần đặt là 12 tháng theo quy định BR-RSV-02.");
        }

        return (startDate, endDate);
    }

    private static void ValidateFilterParameters(decimal? maxPrice, decimal? minAreaM2, decimal? maxAreaM2)
    {
        if (maxPrice.HasValue && maxPrice.Value < 0)
        {
            throw new BadRequestException("Giá tối đa không được là số âm.");
        }

        if (minAreaM2.HasValue && minAreaM2.Value < 0)
        {
            throw new BadRequestException("Diện tích tối thiểu không được là số âm.");
        }

        if (maxAreaM2.HasValue && maxAreaM2.Value < 0)
        {
            throw new BadRequestException("Diện tích tối đa không được là số âm.");
        }

        if (minAreaM2.HasValue && maxAreaM2.HasValue && minAreaM2.Value > maxAreaM2.Value)
        {
            throw new BadRequestException("Diện tích tối thiểu không được lớn hơn diện tích tối đa.");
        }
    }

    private async Task<Dictionary<long, int>> ComputeUnitTypeCapacitiesAsync(
        long facilityId,
        DateOnly startDate,
        DateOnly endDate,
        DateTimeOffset nowUtc,
        IEnumerable<long> unitTypeIds,
        CancellationToken cancellationToken)
    {
        var capacityMap = new Dictionary<long, int>();
        foreach (var unitTypeId in unitTypeIds)
        {
            var candidateUnits = await _catalogRepository.CountCandidateAvailableUnitsAsync(
                facilityId,
                unitTypeId,
                startDate,
                endDate,
                cancellationToken);

            var unassignedReservations = await _catalogRepository.CountUnassignedActiveReservationsAsync(
                facilityId,
                unitTypeId,
                startDate,
                endDate,
                nowUtc,
                cancellationToken);

            capacityMap[unitTypeId] = Math.Max(0, candidateUnits - unassignedReservations);
        }

        return capacityMap;
    }
}


