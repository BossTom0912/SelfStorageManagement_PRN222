using SelfStorageManagementSystem.DataAccess.Entities;

namespace SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;

public interface ICheckInRepository : IRepository<reservation>
{
    Task<reservation?> GetReservationForCheckInAsync(
        string codeOrId,
        CancellationToken cancellationToken = default);

    Task<List<storage_unit>> GetEligibleUnitsAsync(
        long facilityId,
        long unitTypeId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);

    Task<Common.VerifyIdentityOutcome> VerifyIdentityWithLockAsync(
        long reservationId,
        long staffUserId,
        string method,
        string? documentNumber,
        bool isConfirmedMatch,
        string? notes,
        Action<Common.StaffCounterPermissionSnapshot, long>? validatePermission = null,
        CancellationToken cancellationToken = default);

    Task<unit_allocation> AssignUnitWithLockAsync(
        long reservationId,
        long storageUnitId,
        long staffUserId,
        Action<Common.StaffCounterPermissionSnapshot, long>? validatePermission = null,
        CancellationToken cancellationToken = default);

    Task<unit_allocation> ReleaseUnitWithLockAsync(
        long reservationId,
        long currentAllocationId,
        string reason,
        long staffUserId,
        Action<Common.StaffCounterPermissionSnapshot, long>? validatePermission = null,
        CancellationToken cancellationToken = default);

    Task<inspection> PerformInspectionWithLockAsync(
        long reservationId,
        long currentAllocationId,
        long staffUserId,
        IReadOnlyList<(string ItemName, string Condition, string? Notes)> items,
        string? overallSummary,
        bool isUnsafe = false,
        Action<Common.StaffCounterPermissionSnapshot, long>? validatePermission = null,
        CancellationToken cancellationToken = default);

    Task<(handover_record Handover, string? Pin, long? CredentialId, DateTimeOffset? CredentialExpiresAt, bool IsAlreadyCompleted, long? NotificationId)> CompleteCheckInWithLockAsync(
        long reservationId,
        long currentAllocationId,
        long currentInspectionId,
        bool customerAcknowledged,
        string? notes,
        long staffUserId,
        string? pinHmacSecret,
        Action<Common.StaffCounterPermissionSnapshot, long>? validatePermission = null,
        CancellationToken cancellationToken = default);

    Task<(long AgreementId, long OldCredentialId, long NewCredentialId, string Pin, DateTimeOffset IssuedAt, DateTimeOffset? ExpiresAt)> ReissuePinWithLockAsync(
        long reservationId,
        long currentCredentialId,
        string documentNumber,
        bool isConfirmedMatch,
        string? reason,
        long staffUserId,
        string? pinHmacSecret,
        Action<Common.StaffCounterPermissionSnapshot, long>? validatePermission = null,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<rental_agreement> Items, int TotalCount)> GetCustomerAgreementsAsync(
        long customerId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<rental_agreement?> GetCustomerHandoverAgreementAsync(
        long customerId,
        long agreementId,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<notification> Items, int TotalCount)> GetCustomerNotificationsAsync(
        long customerId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);
}
