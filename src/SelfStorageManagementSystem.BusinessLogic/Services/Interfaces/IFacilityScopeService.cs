using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Admin;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

public interface IFacilityScopeService
{
    Task<bool> HasAccessToFacilityAsync(
        long userId,
        long facilityId,
        string requiredRole,
        CancellationToken cancellationToken = default);

    Task<List<long>> GetAccessibleFacilityIdsAsync(
        long userId,
        CancellationToken cancellationToken = default);

    Task<List<FacilityAssignmentDto>> GetActiveAssignmentsForUserAsync(
        long userId,
        CancellationToken cancellationToken = default);
}
