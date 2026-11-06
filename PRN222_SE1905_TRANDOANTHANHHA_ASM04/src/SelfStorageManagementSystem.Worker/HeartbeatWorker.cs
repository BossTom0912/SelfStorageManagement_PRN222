using Microsoft.Extensions.Options;

namespace SelfStorageManagementSystem.Worker;

public sealed class HeartbeatWorker(
    ILogger<HeartbeatWorker> logger,
    HeartbeatFileWriter fileWriter,
    IOptions<WorkerOptions> options,
    IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.IntervalSeconds));
        var cycle = 0;
        try
        {
            do
            {
                cycle++;
                var message = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Heartbeat {cycle} from {WorkerOptions.ServiceName}";
                logger.LogInformation("ASM04 {ServiceName} heartbeat {Cycle}; interval {IntervalSeconds}s", WorkerOptions.ServiceName, cycle, settings.IntervalSeconds);
                try
                {
                    await fileWriter.WriteAsync(message, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Could not persist heartbeat to {PrimaryPath} or {FallbackPath}.", fileWriter.PrimaryPath, fileWriter.FallbackPath);
                }

                if (settings.MaxCycles > 0 && cycle >= settings.MaxCycles)
                {
                    lifetime.StopApplication();
                    break;
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Ctrl+C and Service Control Manager stop requests are normal shutdowns.
        }
        finally
        {
            logger.LogInformation("ASM04 worker stopped after {Cycles} heartbeat(s).", cycle);
        }
    }
}
