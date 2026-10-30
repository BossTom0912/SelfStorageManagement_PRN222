using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SelfStorageManagementSystem.Web.Realtime;

/// <summary>
/// T13-R3-F01: Background worker periodically reconciling active WebSocket connections
/// at staff facility assignment boundaries (starts_at / ends_at).
/// Ensures that naturally expired assignments or newly activated future assignments
/// have their SignalR groups revoked or granted without waiting for admin DB mutations or client reconnects.
/// </summary>
public class StorageUnitScopeSyncWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IStorageUnitHubConnectionTracker _tracker;
    private readonly ILogger<StorageUnitScopeSyncWorker> _logger;

    /// <summary>
    /// Sync loop interval. Default is 1 second to ensure timely group revoking upon assignment expiry.
    /// Can be adjusted in unit/integration tests for faster execution.
    /// </summary>
    public static TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(1);

    public StorageUnitScopeSyncWorker(
        IServiceScopeFactory scopeFactory,
        IStorageUnitHubConnectionTracker tracker,
        ILogger<StorageUnitScopeSyncWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("StorageUnitScopeSyncWorker started with check interval {Interval}.", Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);

                var activeUserIds = _tracker.GetActiveUserIds();
                if (activeUserIds.Count > 0)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var revocationService = scope.ServiceProvider.GetRequiredService<StorageUnitHubScopeRevocationService>();
                    await revocationService.ReconcileAllActiveUsersAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in StorageUnitScopeSyncWorker loop.");
            }
        }

        _logger.LogInformation("StorageUnitScopeSyncWorker stopped.");
    }
}
