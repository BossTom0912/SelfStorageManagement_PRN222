using SelfStorageManagementSystem.DataAccess.Entities;

namespace SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;

public interface IFacilityCatalogRepository : IRepository<facility>
{
    Task<(IReadOnlyList<facility> Facilities, int TotalCount)> GetPagedActiveFacilitiesAsync(
        string? city,
        string? district,
        string? searchTerm,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<facility?> GetActiveFacilityByIdAsync(
        long facilityId,
        CancellationToken cancellationToken = default);

    Task<List<facility_rate>> GetActiveFacilityRatesAsync(
        long facilityId,
        DateOnly rentalDate,
        decimal? maxMonthlyRate = null,
        bool? climateControlled = null,
        decimal? minAreaM2 = null,
        decimal? maxAreaM2 = null,
        CancellationToken cancellationToken = default);

    Task<int> CountTotalListedUnitsAsync(
        long facilityId,
        long unitTypeId,
        CancellationToken cancellationToken = default);

    Task<int> CountCandidateAvailableUnitsAsync(
        long facilityId,
        long unitTypeId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);

    Task<int> CountUnassignedActiveReservationsAsync(
        long facilityId,
        long unitTypeId,
        DateOnly startDate,
        DateOnly endDate,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<storage_unit> Units, int TotalCount)> GetPagedAvailableUnitsAsync(
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
        CancellationToken cancellationToken = default);

    Task<List<facility_area>> GetFacilityAreasAsync(
        long facilityId,
        CancellationToken cancellationToken = default);

    Task<List<unit_map_position>> GetUnitMapPositionsAsync(
        long facilityId,
        long? areaId,
        CancellationToken cancellationToken = default);

    Task<List<storage_unit>> GetAllUnitsForFloorMapAsync(
        long facilityId,
        long? areaId,
        CancellationToken cancellationToken = default);

    Task<bool> HasActiveAllocationsOverlappingAsync(
        long storageUnitId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);

    Task<bool> HasBlockingMaintenanceOrderAsync(
        long storageUnitId,
        CancellationToken cancellationToken = default);
}
