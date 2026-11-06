using Microsoft.Extensions.Options;

namespace SelfStorageManagementSystem.Worker;

public sealed class HeartbeatWorker(
    ILogger<HeartbeatWorker> logger,
    IOptions<WorkerOptions> options,
    IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var logFilePath = Path.Combine(AppContext.BaseDirectory, $"{WorkerOptions.ServiceName}.txt");
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.IntervalSeconds));
        var cycle = 0;
        try
        {
            do
            {
                cycle++;
                var message = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Heartbeat {cycle} from {WorkerOptions.ServiceName}";
                logger.LogInformation("ASM04 TRANDOANTHANHHA heartbeat {Cycle}; interval {IntervalSeconds}s", cycle, settings.IntervalSeconds);
                try
                {
                    await File.AppendAllTextAsync(logFilePath, message + Environment.NewLine, stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning("Could not write to {FilePath}: {Error}", logFilePath, ex.Message);
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
