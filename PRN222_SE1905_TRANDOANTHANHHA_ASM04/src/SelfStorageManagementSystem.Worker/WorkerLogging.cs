using Microsoft.Extensions.Logging.EventLog;

namespace SelfStorageManagementSystem.Worker;

public static class WorkerLogging
{
    public static void ConfigureEventLog(ILoggingBuilder logging)
    {
        // The provider filter runs before EventLogSettings.Filter.
        logging.AddFilter<EventLogLoggerProvider>(null, LogLevel.Information);
        logging.Services.Configure<EventLogSettings>(settings =>
        {
            settings.SourceName = WorkerOptions.ServiceName;
            settings.Filter = (_, level) => level >= LogLevel.Information;
        });
    }
}
