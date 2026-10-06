using Microsoft.EntityFrameworkCore;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;

namespace SelfStorageManagementSystem.DataAccess.Repositories.Implementations;

public class FacilityCatalogRepository : GenericRepository<facility>, IFacilityCatalogRepository
{
    public FacilityCatalogRepository(SelfStorageDbContext context) : base(context)
    {
    }

    public async Task<(IReadOnlyList<facility> Facilities, int TotalCount)> GetPagedActiveFacilitiesAsync(
        string? city,
        string? district,
        string? searchTerm,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _context.facilities
            .AsNoTracking()
            .Where(f => f.status == "active");

        if (!string.IsNullOrWhiteSpace(city))
        {
            var normalizedCity = city.Trim().ToLower();
            query = query.Where(f => f.city.ToLower().Contains(normalizedCity));
        }

        if (!string.IsNullOrWhiteSpace(district))
        {
            var normalizedDistrict = district.Trim().ToLower();
            query = query.Where(f => f.district != null && f.district.ToLower().Contains(normalizedDistrict));
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim().ToLower();
            query = query.Where(f =>
                f.name.ToLower().Contains(term) ||
                f.code.ToLower().Contains(term) ||
                f.address_line.ToLower().Contains(term) ||
                (f.district != null && f.district.ToLower().Contains(term)) ||
                f.city.ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(f => f.name)
            .ThenBy(f => f.id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<facility?> GetActiveFacilityByIdAsync(
        long facilityId,
        CancellationToken cancellationToken = default)
    {
        return await _context.facilities
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.id == facilityId && f.status == "active", cancellationToken);
    }

    public async Task<List<facility_rate>> GetActiveFacilityRatesAsync(
        long facilityId,
        DateOnly rentalDate,
        decimal? maxMonthlyRate = null,
        bool? climateControlled = null,
        decimal? minAreaM2 = null,
        decimal? maxAreaM2 = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.facility_rates
            .AsNoTracking()
            .Include(fr => fr.unit_type)
            .Where(fr =>
                fr.facility_id == facilityId &&
                fr.unit_type.is_active &&
                fr.valid_from <= rentalDate &&
                (fr.valid_to == null || rentalDate < fr.valid_to));

        if (maxMonthlyRate.HasValue)
        {
            query = query.Where(fr => fr.monthly_rate <= maxMonthlyRate.Value);
        }

        if (climateControlled.HasValue)
        {
            query = query.Where(fr => fr.unit_type.climate_controlled == climateControlled.Value);
        }

        if (minAreaM2.HasValue)
        {
            query = query.Where(fr => fr.unit_type.area_m2 >= minAreaM2.Value);
        }

        if (maxAreaM2.HasValue)
        {
            query = query.Where(fr => fr.unit_type.area_m2 <= maxAreaM2.Value);
        }

        return await query
            .OrderBy(fr => fr.monthly_rate)
            .ThenBy(fr => fr.unit_type.name)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountTotalListedUnitsAsync(
        long facilityId,
        long unitTypeId,
        CancellationToken cancellationToken = default)
    {
        return await _context.storage_units
            .AsNoTracking()
            .CountAsync(u =>
                u.facility_id == facilityId &&
                u.unit_type_id == unitTypeId &&
                u.is_listed,
                cancellationToken);
    }

    public async Task<int> CountCandidateAvailableUnitsAsync(
        long facilityId,
        long unitTypeId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        return await _context.storage_units
            .AsNoTracking()
            .Where(u =>
                u.facility_id == facilityId &&
                u.unit_type_id == unitTypeId &&
                u.unit_type.is_active &&
                u.is_listed &&
                u.physical_status == "available" &&
                !u.maintenance_work_orders.Any(m =>
                    m.blocks_booking &&
                    m.status != "completed" &&
                    m.status != "cancelled") &&
                !u.unit_allocations.Any(a =>
                    a.status == "active" &&
                    a.allocation_start_date < endDate &&
                    a.allocation_end_date > startDate))
            .CountAsync(cancellationToken);
    }

    public async Task<int> CountUnassignedActiveReservationsAsync(
        long facilityId,
        long unitTypeId,
        DateOnly startDate,
        DateOnly endDate,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        return await _context.reservations
            .AsNoTracking()
            .Where(r =>
                r.facility_id == facilityId &&
                r.unit_type_id == unitTypeId &&
                r.start_date < endDate &&
                r.end_date > startDate &&
                (r.status == "confirmed" ||
                 ((r.status == "pending" || r.status == "awaiting_deposit") && r.hold_until > nowUtc)) &&
                !_context.unit_allocations.Any(ua => ua.reservation_id == r.id && ua.status == "active"))
            .CountAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<storage_unit> Units, int TotalCount)> GetPagedAvailableUnitsAsync(
        long facilityId,
        IReadOnlyList<long>? eligibleUnitTypeIds,
        long? areaId,
        bool? climateControlled,
        decimal? maxMonthlyRate,
        decimal? minAreaM2,
        decimal? maxAreaM2,
        DateOnly startDate,
        DateOnly endDate,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _context.storage_units
            .AsNoTracking()
            .Include(u => u.unit_type)
            .Include(u => u.area)
            .Include(u => u.unit_map_position)
            .Where(u =>
                u.facility_id == facilityId &&
                u.unit_type.is_active &&
                u.is_listed &&
                u.physical_status == "available" &&
                !u.maintenance_work_orders.Any(m =>
                    m.blocks_booking &&
                    m.status != "completed" &&
                    m.status != "cancelled") &&
                !u.unit_allocations.Any(a =>
                    a.status == "active" &&
                    a.allocation_start_date < endDate &&
                    a.allocation_end_date > startDate) &&
                _context.facility_rates.Any(fr =>
                    fr.facility_id == facilityId &&
                    fr.unit_type_id == u.unit_type_id &&
                    fr.valid_from <= startDate &&
                    (fr.valid_to == null || startDate < fr.valid_to) &&
                    (!maxMonthlyRate.HasValue || fr.monthly_rate <= maxMonthlyRate.Value)));

        if (eligibleUnitTypeIds != null)
        {
            query = query.Where(u => eligibleUnitTypeIds.Contains(u.unit_type_id));
        }

        if (areaId.HasValue)
        {
            query = query.Where(u => u.area_id == areaId.Value);
        }

        if (climateControlled.HasValue)
        {
            query = query.Where(u => u.unit_type.climate_controlled == climateControlled.Value);
        }

        if (minAreaM2.HasValue)
        {
            query = query.Where(u => u.unit_type.area_m2 >= minAreaM2.Value);
        }

        if (maxAreaM2.HasValue)
        {
            query = query.Where(u => u.unit_type.area_m2 <= maxAreaM2.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(u => u.unit_code)
            .ThenBy(u => u.id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<List<facility_area>> GetFacilityAreasAsync(
        long facilityId,
        CancellationToken cancellationToken = default)
    {
        return await _context.facility_areas
            .AsNoTracking()
            .Where(a => a.facility_id == facilityId && a.is_active)
            .OrderBy(a => a.display_order)
            .ThenBy(a => a.name)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<unit_map_position>> GetUnitMapPositionsAsync(
        long facilityId,
        long? areaId,
        CancellationToken cancellationToken = default)
    {
        var query = _context.unit_map_positions
            .AsNoTracking()
            .Include(p => p.unit)
                .ThenInclude(u => u.unit_type)
            .Include(p => p.area)
            .Where(p => p.unit.facility_id == facilityId);

        if (areaId.HasValue)
        {
            query = query.Where(p => p.area_id == areaId.Value);
        }

        return await query
            .OrderBy(p => p.unit.unit_code)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<storage_unit>> GetAllUnitsForFloorMapAsync(
        long facilityId,
        long? areaId,
        CancellationToken cancellationToken = default)
    {
        var query = _context.storage_units
            .AsNoTracking()
            .Include(u => u.unit_type)
            .Include(u => u.area)
            .Include(u => u.unit_map_position)
            .Include(u => u.unit_allocations)
            .Include(u => u.maintenance_work_orders)
            .Where(u => u.facility_id == facilityId);

        if (areaId.HasValue)
        {
            query = query.Where(u => u.area_id == areaId.Value);
        }

        return await query
            .OrderBy(u => u.unit_code)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> HasActiveAllocationsOverlappingAsync(
        long storageUnitId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        return await _context.unit_allocations
            .AsNoTracking()
            .AnyAsync(a =>
                a.storage_unit_id == storageUnitId &&
                a.status == "active" &&
                a.allocation_start_date < endDate &&
                a.allocation_end_date > startDate,
                cancellationToken);
    }

    public async Task<bool> HasBlockingMaintenanceOrderAsync(
        long storageUnitId,
        CancellationToken cancellationToken = default)
    {
        return await _context.maintenance_work_orders
            .AsNoTracking()
            .AnyAsync(m =>
                m.storage_unit_id == storageUnitId &&
                m.blocks_booking &&
                m.status != "completed" &&
                m.status != "cancelled",
                cancellationToken);
    }
}
