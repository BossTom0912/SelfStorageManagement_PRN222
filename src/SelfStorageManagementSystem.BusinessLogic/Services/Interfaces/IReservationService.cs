using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Reservations;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Reservations;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

public interface IReservationService
{
    Task<ReservationDetailResponse> CreateReservationHoldAsync(
        long currentUserId,
        CreateReservationRequest request,
        CancellationToken cancellationToken = default);

    Task<ReservationDetailResponse> GetReservationByIdAsync(
        long currentUserId,
        IReadOnlyList<string> currentUserRoles,
        long reservationId,
        CancellationToken cancellationToken = default);

    Task<PagedResult<ReservationListItemResponse>> GetMyReservationsAsync(
        long currentUserId,
        GetMyReservationsRequest request,
        CancellationToken cancellationToken = default);

    Task<ReservationDetailResponse> CancelReservationAsync(
        long currentUserId,
        IReadOnlyList<string> currentUserRoles,
        long reservationId,
        CancelReservationRequest? request,
        CancellationToken cancellationToken = default);

    Task<int> ExpireOverdueHoldsAsync(
        CancellationToken cancellationToken = default);
}
