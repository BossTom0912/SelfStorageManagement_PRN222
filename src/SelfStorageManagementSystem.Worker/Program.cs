using Microsoft.Extensions.Logging.EventLog;
using SelfStorageManagementSystem.BusinessLogic.DependencyInjection;
using SelfStorageManagementSystem.Worker;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Services.AddWindowsService(options => options.ServiceName = WorkerOptions.ServiceName);
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff ";
});
builder.Services.Configure<EventLogSettings>(options =>
{
    options.SourceName = WorkerOptions.ServiceName;
    options.Filter = (_, level) => level >= LogLevel.Information;
});
builder.Services.AddOptions<WorkerOptions>()
    .Bind(builder.Configuration.GetSection("Worker"))
    .Validate(options => options.IntervalSeconds is >= 1 and <= 3600 && options.MaxCycles >= 0,
        "Worker interval must be 1..3600 seconds and MaxCycles must be nonnegative.")
    .ValidateOnStart();
builder.Services.AddHostedService<HeartbeatWorker>();

// Database maintenance is opt-in; console/service smoke checks only emit heartbeat logs.
if (builder.Configuration.GetValue<bool>("ReservationWorker:Enabled"))
{
    builder.Services.AddSelfStorageWebComposition(builder.Configuration);
    builder.Services.AddHostedService<ReservationExpirationWorker>();
}

await builder.Build().RunAsync();
