namespace FastTaskManager.App.Models;

public sealed class ProcessSnapshot
{
    public required int ProcessId { get; init; }

    public required string ProcessName { get; init; }

    public required double CpuUsagePercent { get; init; }

    public required TimeSpan TotalProcessorTime { get; init; }

    public required long WorkingSetBytes { get; init; }

    public required int ThreadCount { get; init; }

    public long? HandleCount { get; init; }

    public DateTime? StartTime { get; init; }

    public string ExecutablePath { get; init; } = "访问受限";
}
