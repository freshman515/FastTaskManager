namespace FastTaskManager.App.Models;

public sealed class WindowSnapshot
{
    public required nint Handle { get; init; }
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public required string Title { get; init; }
    public required string ExecutablePath { get; init; }
}
