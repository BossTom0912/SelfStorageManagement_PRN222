using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

namespace SelfStorageManagementSystem.Worker;

public sealed class ReservationExpirationWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<ReservationExpirationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, configuration.GetValue<int?>("ReservationWorker:IntervalSeconds") ?? 30));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IReservationService>();
                var count = await service.ExpireOverdueHoldsAsync(stoppingToken);
                logger.LogInformation("Reservation maintenance completed; expired holds: {Count}.", count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Do not log connection strings or exception messages that may contain credentials.
                logger.LogError("Reservation maintenance failed ({ErrorType}); retrying next cycle.", ex.GetType().Name);
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
