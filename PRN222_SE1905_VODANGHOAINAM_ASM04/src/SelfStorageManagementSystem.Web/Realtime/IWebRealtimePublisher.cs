using System.Threading;
using System.Threading.Tasks;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.StorageUnits;

namespace SelfStorageManagementSystem.Web.Realtime;

/// <summary>
/// Web realtime publisher interface for broadcasting business events across the entire application:
/// 1. CatalogInvalidated(long facilityId, long? unitTypeId, string timestampUtc)
/// 2. ReservationChanged(long reservationId, long facilityId, string status, string timestampUtc)
/// 3. PaymentChanged(long paymentId, long reservationId, string status, string timestampUtc)
/// 4. CheckInChanged(long checkInId, long reservationId, long unitId, string status, string timestampUtc)
/// 5. NotificationsChanged(long userId, long notificationId, string timestampUtc)
/// 6. AccountChanged(long userId, string action, string timestampUtc)
/// Extends IStorageUnitRealtimePublisher to preserve all existing T13 CRUD capabilities.
/// </summary>
public interface IWebRealtimePublisher : IStorageUnitRealtimePublisher
{
    Task PublishCatalogInvalidatedAsync(
        long facilityId,
        long? unitTypeId = null,
        CancellationToken cancellationToken = default);

    Task PublishReservationChangedAsync(
        long reservationId,
        long facilityId,
        long? customerUserId,
        string status,
        CancellationToken cancellationToken = default);

    Task PublishPaymentChangedAsync(
        long paymentId,
        long reservationId,
        long facilityId,
        long? customerUserId,
        string status,
        CancellationToken cancellationToken = default);

    Task PublishCheckInChangedAsync(
        long checkInId,
        long reservationId,
        long unitId,
        long facilityId,
        long? customerUserId,
        string status,
        CancellationToken cancellationToken = default);

    Task PublishNotificationsChangedAsync(
        long userId,
        long notificationId,
        CancellationToken cancellationToken = default);

    Task PublishAccountChangedAsync(
        long userId,
        string action = "updated",
        CancellationToken cancellationToken = default);
}
