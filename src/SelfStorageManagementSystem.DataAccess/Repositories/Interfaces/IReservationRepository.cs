using SelfStorageManagementSystem.DataAccess.Entities;

namespace SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;

public interface IReservationRepository : IRepository<reservation>
{
    Task<reservation?> GetByIdWithDetailsAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<reservation> Items, int TotalCount)> GetPagedByCustomerAsync(
        long customerId,
        string? status,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<facility_rate?> GetActiveFacilityRateAsync(
        long facilityId,
        long unitTypeId,
        DateOnly rentalDate,
        CancellationToken cancellationToken = default);

    Task<customer_profile?> GetCustomerProfileAsync(
        long userId,
        CancellationToken cancellationToken = default);

    Task<facility?> GetFacilityAsync(
        long facilityId,
        CancellationToken cancellationToken = default);

    Task<unit_type?> GetUnitTypeAsync(
        long unitTypeId,
        CancellationToken cancellationToken = default);

    Task<int> GetAvailableCapacityAsync(
        long facilityId,
        long unitTypeId,
        DateOnly startDate,
        DateOnly endDate,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<reservation> CreateReservationHoldWithLockAsync(
        reservation newReservation,
        CancellationToken cancellationToken = default);

    Task<reservation> CancelReservationAsync(
        long reservationId,
        string? cancellationReason,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<int> ExpireOverdueReservationHoldsAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<bool> TryExpireSingleReservationIfOverdueAsync(
        long reservationId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);
}
