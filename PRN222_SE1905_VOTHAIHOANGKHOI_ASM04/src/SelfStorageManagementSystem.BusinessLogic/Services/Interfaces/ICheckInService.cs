using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.CheckIn;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.CheckIn;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

public interface ICheckInService
{
    // Staff operations
    Task<CheckInSearchResponse> SearchCheckInAsync(
        string code,
        long currentUserId,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken = default);

    Task<List<EligibleUnitResponse>> GetEligibleUnitsAsync(
        long reservationId,
        long currentUserId,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken = default);

    Task<IdentityVerificationResponse> VerifyIdentityAsync(
        long reservationId,
        VerifyIdentityRequest request,
        long currentUserId,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken = default);

    Task<UnitAllocationResponse> AssignUnitAsync(
        long reservationId,
        AssignUnitRequest request,
        long currentUserId,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken = default);

    Task<UnitAllocationResponse> ReleaseUnitAsync(
        long reservationId,
        ReleaseUnitRequest request,
        long currentUserId,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken = default);

    Task<InspectionResponse> PerformInspectionAsync(
        long reservationId,
        UnitInspectionRequest request,
        long currentUserId,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken = default);

    Task<HandoverCompleteResponse> CompleteCheckInAsync(
        long reservationId,
        CompleteCheckInRequest request,
        long currentUserId,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken = default);

    Task<PinReissueResponse> ReissuePinAsync(
        long reservationId,
        ReissuePinRequest request,
        long currentUserId,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken = default);

    // Customer operations
    Task<PagedResult<CustomerUnitListItemResponse>> GetCustomerUnitsAsync(
        long currentUserId,
        PagedRequest pagedRequest,
        CancellationToken cancellationToken = default);

    Task<CustomerHandoverResponse> GetCustomerHandoverAsync(
        long currentUserId,
        long agreementId,
        CancellationToken cancellationToken = default);

    Task<PagedResult<CustomerNotificationResponse>> GetCustomerNotificationsAsync(
        long currentUserId,
        PagedRequest pagedRequest,
        CancellationToken cancellationToken = default);
}
