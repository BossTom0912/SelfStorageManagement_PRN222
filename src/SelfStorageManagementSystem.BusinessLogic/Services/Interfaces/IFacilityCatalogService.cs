using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Catalog;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Catalog;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

public interface IFacilityCatalogService
{
    Task<PagedResult<FacilityCatalogDto>> GetFacilitiesAsync(
        GetFacilitiesCatalogRequest request,
        CancellationToken cancellationToken = default);

    Task<FacilityCatalogDto> GetFacilityByIdAsync(
        long facilityId,
        CancellationToken cancellationToken = default);

    Task<List<FacilityUnitTypeCatalogDto>> GetFacilityUnitTypesAsync(
        long facilityId,
        GetFacilityUnitTypesRequest request,
        CancellationToken cancellationToken = default);

    Task<PagedResult<AvailableStorageUnitDto>> GetAvailableUnitsAsync(
        long facilityId,
        GetAvailableUnitsRequest request,
        CancellationToken cancellationToken = default);

    Task<FacilityFloorMapDto> GetFacilityFloorMapAsync(
        long facilityId,
        GetFacilityFloorMapRequest request,
        CancellationToken cancellationToken = default);
}
