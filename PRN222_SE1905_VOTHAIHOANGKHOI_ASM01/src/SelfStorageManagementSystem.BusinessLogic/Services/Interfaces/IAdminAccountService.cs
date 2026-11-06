using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Admin;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Admin;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

public interface IAdminAccountService
{
    Task<PagedResult<UserAccountDto>> GetAccountsAsync(
        GetAccountsRequest request,
        CancellationToken cancellationToken = default);

    Task<UserAccountDto> GetAccountByIdAsync(
        long userId,
        CancellationToken cancellationToken = default);

    Task<UserAccountDto> CreateStaffAccountAsync(
        CreateStaffAccountRequest request,
        long actorUserId,
        string? ipAddress = null,
        string? requestId = null,
        CancellationToken cancellationToken = default);

    Task<UserAccountDto> UpdateUserStatusAsync(
        long userId,
        UpdateUserStatusRequest request,
        long actorUserId,
        string? ipAddress = null,
        string? requestId = null,
        CancellationToken cancellationToken = default);

    Task<UserAccountDto> ManageUserRolesAsync(
        long userId,
        ManageUserRolesRequest request,
        long actorUserId,
        string? ipAddress = null,
        string? requestId = null,
        CancellationToken cancellationToken = default);

    Task<FacilityAssignmentDto> AssignFacilityAsync(
        long userId,
        CreateFacilityAssignmentRequest request,
        long actorUserId,
        string? ipAddress = null,
        string? requestId = null,
        CancellationToken cancellationToken = default);

    Task TerminateFacilityAssignmentAsync(
        long assignmentId,
        long actorUserId,
        string? ipAddress = null,
        string? requestId = null,
        CancellationToken cancellationToken = default);

    Task<List<FacilityLookupDto>> GetFacilitiesAsync(
        CancellationToken cancellationToken = default);
}
