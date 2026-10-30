namespace SelfStorageManagementSystem.Worker;

public sealed class WorkerOptions
{
    public const string ServiceName = "PRN222_TRANDOANTHANHHA_ASM04";

    public int IntervalSeconds { get; set; } = 5;

    // Zero runs continuously. A finite count supports an unattended console smoke check.
    public int MaxCycles { get; set; }
}
