using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Reservations;
using SelfStorageManagementSystem.Web.Realtime;

namespace SelfStorageManagementSystem.Web.Workers;

/// <summary>
/// Realtime hook implementation triggered by ReservationHoldExpirationWorker after overdue holds are released.
/// Broadcasts CatalogInvalidated and ReservationChanged("expired") events with authentic reservation identities (F02).
/// </summary>
public class ReservationExpirationRealtimeHook : IReservationExpirationHook
{
    private readonly IWebRealtimePublisher _publisher;
    private readonly ILogger<ReservationExpirationRealtimeHook> _logger;

    public ReservationExpirationRealtimeHook(
        IWebRealtimePublisher publisher,
        ILogger<ReservationExpirationRealtimeHook> logger)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task OnHoldsExpiredAsync(int expiredCount, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public async Task OnHoldsExpiredWithDetailsAsync(
        IReadOnlyList<ExpiredReservationHoldInfo> expiredHolds,
        CancellationToken cancellationToken = default)
    {
        if (expiredHolds == null || expiredHolds.Count == 0) return;

        _logger.LogInformation("ReservationExpirationRealtimeHook triggered for {ExpiredCount} expired holds.", expiredHolds.Count);

        // 1. Invalidate catalog capacity for each affected facility
        var distinctFacilityIds = expiredHolds.Select(h => h.FacilityId).Distinct().Where(id => id > 0);
        foreach (var facilityId in distinctFacilityIds)
        {
            try
            {
                await _publisher.PublishCatalogInvalidatedAsync(facilityId, null, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to broadcast CatalogInvalidated for facility {FacilityId} on expiration.", facilityId);
            }
        }

        // 2. Broadcast reservation expiration for each individual reservation with exact IDs and customer scope
        foreach (var hold in expiredHolds)
        {
            try
            {
                await _publisher.PublishReservationChangedAsync(
                    hold.ReservationId,
                    hold.FacilityId,
                    hold.CustomerUserId,
                    "expired",
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to broadcast ReservationChanged for expired hold {ReservationId}.", hold.ReservationId);
            }
        }
    }
}
