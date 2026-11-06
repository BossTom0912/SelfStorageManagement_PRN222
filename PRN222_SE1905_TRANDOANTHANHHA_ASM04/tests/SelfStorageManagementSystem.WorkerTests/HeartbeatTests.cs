using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.EventLog;
using SelfStorageManagementSystem.Worker;
using Xunit;

namespace SelfStorageManagementSystem.WorkerTests;

public sealed class HeartbeatTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("vu632-test-").FullName;
    private HeartbeatFileWriter CreateWriter() => new(directory, NullLogger<HeartbeatFileWriter>.Instance);

    [Fact]
    public async Task WritesEachHeartbeatToPrimary()
    {
        var writer = CreateWriter();
        await writer.WriteAsync("first", default);
        await writer.WriteAsync("second", default);
        Assert.Equal(new[] { "first", "second" }, await File.ReadAllLinesAsync(writer.PrimaryPath));
        Assert.False(File.Exists(writer.FallbackPath));
    }

    [Fact]
    public async Task LockedPrimaryPersistsToFallbackAndRecoversAfterUnlock()
    {
        var writer = CreateWriter();
        await writer.WriteAsync("before", default);
        using (var locked = new FileStream(writer.PrimaryPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await writer.WriteAsync("during lock", default);
            Assert.Equal(new[] { "during lock" }, await File.ReadAllLinesAsync(writer.FallbackPath));
        }
        await writer.WriteAsync("after unlock", default);
        Assert.Equal(new[] { "before", "after unlock" }, await File.ReadAllLinesAsync(writer.PrimaryPath));
        Assert.Equal(new[] { "during lock" }, await File.ReadAllLinesAsync(writer.FallbackPath));
    }

    [Fact]
    public async Task ReaderCanStayOpenWhileHeartbeatsAreAppended()
    {
        var writer = CreateWriter();
        await writer.WriteAsync("first", default);
        using var reader = new FileStream(writer.PrimaryPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        await writer.WriteAsync("second", default);
        Assert.Equal(2, (await File.ReadAllLinesAsync(writer.PrimaryPath)).Length);
        Assert.False(File.Exists(writer.FallbackPath));
    }

    [Fact]
    public async Task CancellationDoesNotCreateAnyLog()
    {
        var writer = CreateWriter();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.WriteAsync("cancelled", cancellation.Token));
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task BothDestinationsBlockedReportsFailure()
    {
        var writer = CreateWriter();
        using var primary = new FileStream(writer.PrimaryPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var fallback = new FileStream(writer.FallbackPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await Assert.ThrowsAsync<IOException>(() => writer.WriteAsync("blocked", default));
    }

    [Fact]
    public void EventLogProviderAcceptsInformationButRejectsDebug()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddEventLog();
        WorkerLogging.ConfigureEventLog(builder.Logging);
        using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILogger<HeartbeatWorker>>();
        Assert.True(logger.IsEnabled(LogLevel.Information));
        Assert.False(logger.IsEnabled(LogLevel.Debug));
        var settings = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<EventLogSettings>>().Value;
        Assert.Equal("Vu632", settings.SourceName);
    }

    [Fact]
    public async Task FiniteWorkerStopsAndPersistsAllCycles()
    {
        var builder = Host.CreateApplicationBuilder(new[] { "--Worker:IntervalSeconds=1", "--Worker:MaxCycles=3" });
        builder.Logging.ClearProviders();
        builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection("Worker"));
        builder.Services.AddSingleton(CreateWriter());
        builder.Services.AddHostedService<HeartbeatWorker>();
        using var host = builder.Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.RunAsync(timeout.Token);
        var lines = await File.ReadAllLinesAsync(CreateWriter().PrimaryPath);
        Assert.Equal(3, lines.Length);
        Assert.Contains("Heartbeat 3 from Vu632", lines[2]);
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
