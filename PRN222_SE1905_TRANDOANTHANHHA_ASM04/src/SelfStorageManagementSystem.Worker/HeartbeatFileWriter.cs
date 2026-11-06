using System.Text;

namespace SelfStorageManagementSystem.Worker;

public sealed class HeartbeatFileWriter(string directory, ILogger<HeartbeatFileWriter> logger)
{
    public string PrimaryPath { get; } = Path.Combine(directory, $"{WorkerOptions.ServiceName}.txt");
    public string FallbackPath { get; } = Path.Combine(directory, $"{WorkerOptions.ServiceName}.fallback.log");

    public async Task WriteAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await AppendAsync(PrimaryPath, message, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await AppendAsync(FallbackPath, message, cancellationToken);
            logger.LogWarning("Heartbeat redirected to {FallbackPath}; primary log {PrimaryPath} unavailable ({ErrorType}).",
                FallbackPath, PrimaryPath, ex.GetType().Name);
        }
    }

    private static async Task AppendAsync(string path, string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write,
            FileShare.ReadWrite, 4096, FileOptions.Asynchronous);
        var bytes = Encoding.UTF8.GetBytes(message + Environment.NewLine);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
