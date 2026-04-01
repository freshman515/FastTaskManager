using FastTaskManager.App.Infrastructure;

namespace FastTaskManager.App.Models;

public sealed class WindowsServiceItem : ObservableObject
{
    private string _statusText = "未知";
    private bool _isRunning;
    private bool _isPaused;
    private int? _processId;

    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public string Description { get; init; } = string.Empty;
    public string GroupText { get; init; } = string.Empty;
    public string PortText { get; init; } = string.Empty;
    public string StartModeText { get; init; } = string.Empty;
    public bool CanPauseAndContinue { get; init; }

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
        set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanStop));
                OnPropertyChanged(nameof(CanRestart));
                OnPropertyChanged(nameof(CanPause));
                OnPropertyChanged(nameof(CanResume));
            }
        }
    }

    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            if (SetProperty(ref _isPaused, value))
            {
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanStop));
                OnPropertyChanged(nameof(CanRestart));
                OnPropertyChanged(nameof(CanPause));
                OnPropertyChanged(nameof(CanResume));
            }
        }
    }

    public string ProcessIdText => ProcessId.HasValue && ProcessId.Value > 0 ? ProcessId.Value.ToString() : string.Empty;
    public bool CanStart => !IsRunning && !IsPaused;
    public bool CanStop => IsRunning || IsPaused;
    public bool CanRestart => IsRunning || IsPaused;
    public bool CanPause => CanPauseAndContinue && IsRunning;
    public bool CanResume => CanPauseAndContinue && IsPaused;
}
