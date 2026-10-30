using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.StorageUnits;
using SelfStorageManagementSystem.Web.Hubs;

namespace SelfStorageManagementSystem.Web.Realtime;

/// <summary>
/// Implementation of IWebRealtimePublisher broadcasting events via IHubContext&lt;StorageUnitHub&gt;.
/// Ensures messages conform strictly to Contract v1 section 8.3:
/// - Receiver_CreateStorageUnit(StorageUnitRealtimeEventDto)
/// - Receiver_UpdateStorageUnit(StorageUnitRealtimeEventDto)
/// - Receiver_DeleteStorageUnit(string unitId)
/// - CatalogInvalidated(long facilityId, long? unitTypeId, string timestampUtc)
/// - ReservationChanged(long reservationId, long facilityId, string status, string timestampUtc)
/// - PaymentChanged(long paymentId, long reservationId, string status, string timestampUtc)
/// - CheckInChanged(long checkInId, long reservationId, long unitId, string status, string timestampUtc)
/// - NotificationsChanged(long userId, long notificationId, string timestampUtc)
/// - AccountChanged(long userId, string action, string timestampUtc)
/// </summary>
public class StorageUnitRealtimePublisher : IWebRealtimePublisher
{
    private readonly IHubContext<StorageUnitHub> _hubContext;
    private readonly StorageUnitHubScopeRevocationService _revocationService;
    private readonly ILogger<StorageUnitRealtimePublisher> _logger;

    public StorageUnitRealtimePublisher(
        IHubContext<StorageUnitHub> hubContext,
        StorageUnitHubScopeRevocationService revocationService,
        ILogger<StorageUnitRealtimePublisher> logger)
    {
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        _revocationService = revocationService ?? throw new ArgumentNullException(nameof(revocationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #region Storage Unit CRUD Broadcasts (Preserved from T13)

    public async Task PublishCreatedAsync(StorageUnitAdminDto unit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var eventDto = StorageUnitRealtimeEventDto.FromAdminDto(unit);

        _logger.LogInformation("Broadcasting Receiver_CreateStorageUnit for unit {UnitCode} (ID: {UnitId}, Facility: {FacilityId})", unit.UnitCode, unit.Id, unit.FacilityId);

        // T13-R4-F02 / T13-R5-F01 / T13-R7-F01: Broadcast protection (Fail-Closed).
        try
        {
            await _revocationService.PruneConnectionsForBroadcastAsync(unit.FacilityId, includeAdminGlobal: true, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Pruning connections for facility {FacilityId} canceled before publish.", unit.FacilityId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fail-closed broadcast guard triggered: failed to verify/prune connections for facility {FacilityId} and admin_global. All CRUD broadcast blocked to prevent unauthorized leakage.", unit.FacilityId);

            // Catalog invalidation is public safe event (only facilityId, unitTypeId, timestampUtc)
            await _hubContext.Clients.All.SendAsync("CatalogInvalidated", new
            {
                facilityId = unit.FacilityId,
                unitTypeId = (long?)unit.UnitTypeId,
                timestampUtc = DateTimeOffset.UtcNow.ToString("o")
            }, cancellationToken);

            throw new RealtimePublishDegradedException("Create", unit.FacilityId, unit.Id, $"Không thể đối soát quyền hạn kết nối trước khi phát sự kiện tạo mới cho cơ sở {unit.FacilityId}. Luồng phát realtime đã bị chặn hoàn toàn để bảo đảm an toàn dữ liệu.", ex);
        }

        // T13-F01 / T13-R7-F01: Broadcast CRUD event only to verified facility group and verified system-wide admin group
        var authorizedGroups = new[] { $"facility_{unit.FacilityId}", "admin_global" };
        await _hubContext.Clients.Groups(authorizedGroups).SendAsync("Receiver_CreateStorageUnit", eventDto, cancellationToken);

        // Catalog invalidation event for capacity listeners (public safe event for customer/catalog)
        await _hubContext.Clients.All.SendAsync("CatalogInvalidated", new
        {
            facilityId = unit.FacilityId,
            unitTypeId = (long?)unit.UnitTypeId,
            timestampUtc = DateTimeOffset.UtcNow.ToString("o")
        }, cancellationToken);
    }

    public async Task PublishUpdatedAsync(StorageUnitAdminDto unit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var eventDto = StorageUnitRealtimeEventDto.FromAdminDto(unit);

        _logger.LogInformation("Broadcasting Receiver_UpdateStorageUnit for unit {UnitCode} (ID: {UnitId}, Facility: {FacilityId})", unit.UnitCode, unit.Id, unit.FacilityId);

        // T13-R4-F02 / T13-R5-F01 / T13-R7-F01: Broadcast protection (Fail-Closed)
        try
        {
            await _revocationService.PruneConnectionsForBroadcastAsync(unit.FacilityId, includeAdminGlobal: true, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Pruning connections for facility {FacilityId} canceled before publish.", unit.FacilityId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fail-closed broadcast guard triggered: failed to verify/prune connections for facility {FacilityId} and admin_global. All CRUD broadcast blocked to prevent unauthorized leakage.", unit.FacilityId);

            await _hubContext.Clients.All.SendAsync("CatalogInvalidated", new
            {
                facilityId = unit.FacilityId,
                unitTypeId = (long?)unit.UnitTypeId,
                timestampUtc = DateTimeOffset.UtcNow.ToString("o")
            }, cancellationToken);

            throw new RealtimePublishDegradedException("Update", unit.FacilityId, unit.Id, $"Không thể đối soát quyền hạn kết nối trước khi phát sự kiện cập nhật cho cơ sở {unit.FacilityId}. Luồng phát realtime đã bị chặn hoàn toàn để bảo đảm an toàn dữ liệu.", ex);
        }

        // T13-F01 / T13-R7-F01: Broadcast CRUD event only to verified facility group and verified system-wide admin group
        var authorizedGroups = new[] { $"facility_{unit.FacilityId}", "admin_global" };
        await _hubContext.Clients.Groups(authorizedGroups).SendAsync("Receiver_UpdateStorageUnit", eventDto, cancellationToken);

        // Catalog invalidation event for capacity listeners
        await _hubContext.Clients.All.SendAsync("CatalogInvalidated", new
        {
            facilityId = unit.FacilityId,
            unitTypeId = (long?)unit.UnitTypeId,
            timestampUtc = DateTimeOffset.UtcNow.ToString("o")
        }, cancellationToken);
    }

    public async Task PublishDeletedAsync(long unitId, long facilityId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Broadcasting Receiver_DeleteStorageUnit for unit ID {UnitId} at facility {FacilityId}", unitId, facilityId);

        // T13-R4-F02 / T13-R5-F01 / T13-R7-F01: Broadcast protection (Fail-Closed)
        try
        {
            await _revocationService.PruneConnectionsForBroadcastAsync(facilityId, includeAdminGlobal: true, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Pruning connections for facility {FacilityId} canceled before publish.", facilityId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fail-closed broadcast guard triggered: failed to verify/prune connections for facility {FacilityId} and admin_global. All CRUD broadcast blocked to prevent unauthorized leakage.", facilityId);

            await _hubContext.Clients.All.SendAsync("CatalogInvalidated", new
            {
                facilityId = facilityId,
                unitTypeId = (long?)null,
                timestampUtc = DateTimeOffset.UtcNow.ToString("o")
            }, cancellationToken);

            throw new RealtimePublishDegradedException("Delete", facilityId, unitId, $"Không thể đối soát quyền hạn kết nối trước khi phát sự kiện xóa cho cơ sở {facilityId}. Luồng phát realtime đã bị chặn hoàn toàn để bảo đảm an toàn dữ liệu.", ex);
        }

        // T13-F01 / T13-R7-F01: Broadcast CRUD delete event only to verified facility group and verified system-wide admin group
        var authorizedGroups = new[] { $"facility_{facilityId}", "admin_global" };
        await _hubContext.Clients.Groups(authorizedGroups).SendAsync("Receiver_DeleteStorageUnit", unitId.ToString(), cancellationToken);

        // Catalog invalidation event for capacity listeners
        await _hubContext.Clients.All.SendAsync("CatalogInvalidated", new
        {
            facilityId = facilityId,
            unitTypeId = (long?)null,
            timestampUtc = DateTimeOffset.UtcNow.ToString("o")
        }, cancellationToken);
    }

    public async Task PublishDeletedAsync(long unitId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Broadcasting Receiver_DeleteStorageUnit fallback for unit ID {UnitId}", unitId);

        // T13-R7-F01: Guard fallback broadcast against demoted or locked admin connections
        try
        {
            await _revocationService.PruneUnverifiedAdminConnectionsAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fail-closed broadcast guard triggered: failed to verify admin_global connections for fallback delete.");
            throw new RealtimePublishDegradedException(
                "Delete",
                0,
                unitId,
                "Không thể đối soát quyền hạn kết nối admin_global trước khi phát sự kiện xóa fallback. Luồng phát đã bị chặn để bảo đảm an toàn dữ liệu.",
                ex);
        }

        // Broadcast to verified admin_global only
        await _hubContext.Clients.Group("admin_global").SendAsync("Receiver_DeleteStorageUnit", unitId.ToString(), cancellationToken);
    }

    #endregion

    #region Application Realtime Broadcasts (Task T15)

    public async Task PublishCatalogInvalidatedAsync(
        long facilityId,
        long? unitTypeId = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Broadcasting CatalogInvalidated for Facility {FacilityId}, UnitType {UnitTypeId}", facilityId, unitTypeId);

        var payload = new
        {
            facilityId = facilityId,
            unitTypeId = unitTypeId,
            timestampUtc = DateTimeOffset.UtcNow.ToString("o")
        };

        try
        {
            // CatalogInvalidated contains no sensitive or private data (only public facility/unitTypeId capacity invalidation).
            // It is broadcast to all connected clients so customers viewing /Catalog or /Catalog/Details receive it.
            // Individual clients filter by facilityId if viewing a specific facility.
            await _hubContext.Clients.All.SendAsync("CatalogInvalidated", payload, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast CatalogInvalidated for Facility {FacilityId}", facilityId);
            throw new RealtimePublishDegradedException("CatalogInvalidated", facilityId, 0, "Không thể phát sự kiện CatalogInvalidated qua SignalR.", ex);
        }
    }

    public async Task PublishReservationChangedAsync(
        long reservationId,
        long facilityId,
        long? customerUserId,
        string status,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Broadcasting ReservationChanged for Reservation {ReservationId}, Facility {FacilityId}, Customer {CustomerId}, Status {Status}",
            reservationId, facilityId, customerUserId, status);

        try
        {
            if (facilityId > 0)
            {
                await _revocationService.PruneConnectionsForBroadcastAsync(facilityId, includeAdminGlobal: true, cancellationToken);
            }
            else
            {
                await _revocationService.PruneUnverifiedAdminConnectionsAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Pruning connections for ReservationChanged {ReservationId} canceled.", reservationId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fail-closed broadcast guard triggered: failed to verify/prune connections for ReservationChanged {ReservationId}. Broadcast blocked to prevent unauthorized leakage.", reservationId);
            throw new RealtimePublishDegradedException("ReservationChanged", facilityId, reservationId,
                $"Không thể đối soát quyền hạn kết nối trước khi phát sự kiện đặt chỗ cho cơ sở {facilityId}. Luồng phát realtime đã bị chặn hoàn toàn để bảo đảm an toàn dữ liệu.", ex);
        }

        var targetGroups = new List<string>();
        if (facilityId > 0)
        {
            targetGroups.Add($"facility_{facilityId}");
        }
        targetGroups.Add("admin_global");
        if (customerUserId.HasValue && customerUserId.Value > 0)
        {
            targetGroups.Add($"user_{customerUserId.Value}");
        }

        var payload = new
        {
            reservationId = reservationId,
            facilityId = facilityId,
            status = status,
            timestampUtc = DateTimeOffset.UtcNow.ToString("o")
        };

        try
        {
            // Strictly send only to authorized scoped groups (admin, assigned staff, owner). NEVER broadcast ReservationChanged to Clients.All.
            await _hubContext.Clients.Groups(targetGroups).SendAsync("ReservationChanged", payload, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast ReservationChanged for Reservation {ReservationId}", reservationId);
            throw new RealtimePublishDegradedException("ReservationChanged", facilityId, reservationId, "Không thể phát sự kiện ReservationChanged qua SignalR.", ex);
        }
    }

    public async Task PublishPaymentChangedAsync(
        long paymentId,
        long reservationId,
        long facilityId,
        long? customerUserId,
        string status,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Broadcasting PaymentChanged for Payment {PaymentId}, Reservation {ReservationId}, Facility {FacilityId}, Customer {CustomerId}, Status {Status}",
            paymentId, reservationId, facilityId, customerUserId, status);

        try
        {
            if (facilityId > 0)
            {
                await _revocationService.PruneConnectionsForBroadcastAsync(facilityId, includeAdminGlobal: true, cancellationToken);
            }
            else
            {
                await _revocationService.PruneUnverifiedAdminConnectionsAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Pruning connections for PaymentChanged {PaymentId} canceled.", paymentId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fail-closed broadcast guard triggered: failed to verify/prune connections for PaymentChanged {PaymentId}. Broadcast blocked to prevent unauthorized leakage.", paymentId);
            throw new RealtimePublishDegradedException("PaymentChanged", facilityId, paymentId,
                $"Không thể đối soát quyền hạn kết nối trước khi phát sự kiện thanh toán cho cơ sở {facilityId}. Luồng phát realtime đã bị chặn hoàn toàn để bảo đảm an toàn dữ liệu.", ex);
        }

        var targetGroups = new List<string>();
        if (facilityId > 0)
        {
            targetGroups.Add($"facility_{facilityId}");
        }
        targetGroups.Add("admin_global");
        if (customerUserId.HasValue && customerUserId.Value > 0)
        {
            targetGroups.Add($"user_{customerUserId.Value}");
        }

        var payload = new
        {
            paymentId = paymentId,
            reservationId = reservationId,
            status = status,
            timestampUtc = DateTimeOffset.UtcNow.ToString("o")
        };

        try
        {
            await _hubContext.Clients.Groups(targetGroups).SendAsync("PaymentChanged", payload, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast PaymentChanged for Payment {PaymentId}", paymentId);
            throw new RealtimePublishDegradedException("PaymentChanged", facilityId, paymentId, "Không thể phát sự kiện PaymentChanged qua SignalR.", ex);
        }
    }

    public async Task PublishCheckInChangedAsync(
        long checkInId,
        long reservationId,
        long unitId,
        long facilityId,
        long? customerUserId,
        string status,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Broadcasting CheckInChanged for CheckIn {CheckInId}, Reservation {ReservationId}, Unit {UnitId}, Facility {FacilityId}, Customer {CustomerId}, Status {Status}",
            checkInId, reservationId, unitId, facilityId, customerUserId, status);

        try
        {
            if (facilityId > 0)
            {
                await _revocationService.PruneConnectionsForBroadcastAsync(facilityId, includeAdminGlobal: true, cancellationToken);
            }
            else
            {
                await _revocationService.PruneUnverifiedAdminConnectionsAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Pruning connections for CheckInChanged {CheckInId} canceled.", checkInId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fail-closed broadcast guard triggered: failed to verify/prune connections for CheckInChanged {CheckInId}. Broadcast blocked to prevent unauthorized leakage.", checkInId);
            throw new RealtimePublishDegradedException("CheckInChanged", facilityId, checkInId,
                $"Không thể đối soát quyền hạn kết nối trước khi phát sự kiện tiếp nhận quầy cho cơ sở {facilityId}. Luồng phát realtime đã bị chặn hoàn toàn để bảo đảm an toàn dữ liệu.", ex);
        }

        var targetGroups = new List<string>();
        if (facilityId > 0)
        {
            targetGroups.Add($"facility_{facilityId}");
        }
        targetGroups.Add("admin_global");
        if (customerUserId.HasValue && customerUserId.Value > 0)
        {
            targetGroups.Add($"user_{customerUserId.Value}");
        }

        // Zero PIN, zero sensitive credentials in event payload (Contract v1)
        var payload = new
        {
            checkInId = checkInId,
            reservationId = reservationId,
            unitId = unitId,
            status = status,
            timestampUtc = DateTimeOffset.UtcNow.ToString("o")
        };

        try
        {
            await _hubContext.Clients.Groups(targetGroups).SendAsync("CheckInChanged", payload, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast CheckInChanged for CheckIn {CheckInId}", checkInId);
            throw new RealtimePublishDegradedException("CheckInChanged", facilityId, checkInId, "Không thể phát sự kiện CheckInChanged qua SignalR.", ex);
        }
    }

    public async Task PublishNotificationsChangedAsync(
        long userId,
        long notificationId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Broadcasting NotificationsChanged for User {UserId}, Notification {NotificationId}", userId, notificationId);

        var payload = new
        {
            userId = userId,
            notificationId = notificationId,
            timestampUtc = DateTimeOffset.UtcNow.ToString("o")
        };

        // Strictly targeted to user_{userId}
        try
        {
            await _hubContext.Clients.Group($"user_{userId}").SendAsync("NotificationsChanged", payload, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast NotificationsChanged for User {UserId}", userId);
            throw new RealtimePublishDegradedException("NotificationsChanged", 0, notificationId, "Không thể phát sự kiện NotificationsChanged qua SignalR.", ex);
        }
    }

    public async Task PublishAccountChangedAsync(
        long userId,
        string action = "updated",
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Broadcasting AccountChanged for User {UserId}, Action {Action}", userId, action);

        var payload = new
        {
            userId = userId,
            action = action,
            timestampUtc = DateTimeOffset.UtcNow.ToString("o")
        };

        try
        {
            await _hubContext.Clients.Groups("admin_global", $"user_{userId}").SendAsync("AccountChanged", payload, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast AccountChanged for User {UserId}", userId);
            throw new RealtimePublishDegradedException("AccountChanged", 0, userId, "Không thể phát sự kiện AccountChanged qua SignalR.", ex);
        }
    }

    #endregion
}
