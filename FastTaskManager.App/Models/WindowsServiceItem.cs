using FastTaskManager.App.Infrastructure;

namespace FastTaskManager.App.Models;

public sealed class WindowsServiceItem : ObservableObject
{
    private string _statusText = "未知";
    private bool _isRunning;
    private int? _processId;

    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public string Description { get; init; } = string.Empty;
    public string GroupText { get; init; } = string.Empty;
    public string StartModeText { get; init; } = string.Empty;

    public int? ProcessId
    {
        get => _processId;
        set
        {
            if (SetProperty(ref _processId, value))
                OnPropertyChanged(nameof(ProcessIdText));
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        set => SetProperty(ref _isRunning, value);
    }

    public string ProcessIdText => ProcessId.HasValue && ProcessId.Value > 0 ? ProcessId.Value.ToString() : string.Empty;
    public bool CanStart => !IsRunning;
    public bool CanStop => IsRunning;
    public bool CanRestart => IsRunning;
}
