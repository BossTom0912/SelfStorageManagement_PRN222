using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

namespace SelfStorageManagementSystem.Presentation.Workers;

/// <summary>
/// Background worker processing overdue reservation holds per BR-RSV-01.
/// Automatically transitions holds older than hold_until from 'pending'/'awaiting_deposit' to 'expired'.
/// Runs on a scoped lifetime per cycle, never injecting DbContext directly into worker.
/// Resilient against restarts: on first execution cycle after boot, it immediately processes any holds
/// that expired while the server was offline.
/// </summary>
public class ReservationHoldExpirationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReservationHoldExpirationWorker> _logger;
    private readonly TimeSpan _checkInterval;

    public ReservationHoldExpirationWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<ReservationHoldExpirationWorker> logger,
        TimeSpan? checkInterval = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _checkInterval = checkInterval ?? TimeSpan.FromSeconds(30);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Reservation hold expiration worker started. Checking every {Interval} seconds.", _checkInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var reservationService = scope.ServiceProvider.GetRequiredService<IReservationService>();

                var expiredCount = await reservationService.ExpireOverdueHoldsAsync(stoppingToken);
                if (expiredCount > 0)
                {
                    _logger.LogInformation("Processed and expired {Count} overdue reservation hold(s).", expiredCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error occurred in reservation hold expiration worker.");
            }

            try
            {
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Reservation hold expiration worker stopped.");
    }
}
