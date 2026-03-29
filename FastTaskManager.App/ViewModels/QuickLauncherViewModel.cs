using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using FastTaskManager.App.Infrastructure;
using FastTaskManager.App.Models;
using FastTaskManager.App.Services;
using Hardcodet.Wpf.TaskbarNotification;

namespace FastTaskManager.App.ViewModels;

public sealed class QuickLauncherViewModel : ObservableObject
{
    private readonly ProcessMonitorService _processMonitorService;
    private readonly SystemTrayService _systemTrayService;
    private readonly WindowSearchService _windowSearchService;
    private readonly AppShellService _appShellService;
    private readonly DialogService _dialogService;
    private readonly TrayService _trayService;
    private readonly RangeObservableCollection<ProcessDisplayItem> _results = [];

    private IReadOnlyList<ProcessSnapshot> _lastSnapshots = Array.Empty<ProcessSnapshot>();
    private IReadOnlyList<WindowSnapshot> _lastWindows = Array.Empty<WindowSnapshot>();
    private HashSet<int> _trayProcessIds = [];
    private ProcessDisplayItem? _selectedProcess;
    private string _searchText = string.Empty;
    private LauncherActionContext _currentAction = new("open", string.Empty, string.Empty);
    private string _toastMessage = string.Empty;
    private Brush _toastAccentBrush = Brushes.DeepSkyBlue;
    private Brush _toastBadgeBackground = Brushes.Transparent;
    private Brush _toastBadgeBorderBrush = Brushes.Transparent;
    private bool _isToastVisible;
    private CancellationTokenSource? _toastCts;

    private readonly AsyncCommand _executeSelectedCommand;
    private readonly AsyncCommand _activateSelectedProcessCommand;
    private readonly AsyncCommand _killSelectedProcessCommand;
    private readonly AsyncCommand _killProcessOnlyCommand;
    private readonly AsyncCommand _suspendProcessCommand;
    private readonly AsyncCommand _resumeProcessCommand;
    private readonly RelayCommand<string> _setPriorityCommand;
    private readonly RelayCommand _openFileLocationCommand;
    private readonly RelayCommand _copyNameCommand;
    private readonly RelayCommand _copyPidCommand;
    private readonly RelayCommand _copyPathCommand;

    public QuickLauncherViewModel(
        ProcessMonitorService processMonitorService,
        SystemTrayService systemTrayService,
        WindowSearchService windowSearchService,
        AppShellService appShellService,
        DialogService dialogService,
        TrayService trayService)
    {
        _processMonitorService = processMonitorService;
        _systemTrayService = systemTrayService;
        _windowSearchService = windowSearchService;
        _appShellService = appShellService;
        _dialogService = dialogService;
        _trayService = trayService;
        _appShellService.ToastRequested += HandleToastRequested;

        Func<bool> hasSelection = () => SelectedProcess is not null;
        Func<bool> hasIndividualSelection = () => SelectedProcess is { IsGroup: false };

        _executeSelectedCommand = new AsyncCommand(ExecutePrimaryActionAsync, hasSelection);
        _activateSelectedProcessCommand = new AsyncCommand(ActivateSelectedProcessAsync, hasIndividualSelection);
        _killSelectedProcessCommand = new AsyncCommand(() => KillAsync(entireTree: true), hasSelection);
        _killProcessOnlyCommand = new AsyncCommand(() => KillAsync(entireTree: false), hasSelection);
        _suspendProcessCommand = new AsyncCommand(SuspendAsync, hasIndividualSelection);
        _resumeProcessCommand = new AsyncCommand(ResumeAsync, hasIndividualSelection);
        _setPriorityCommand = new RelayCommand<string>(SetPriority, _ => SelectedProcess is { IsGroup: false });
        _openFileLocationCommand = new RelayCommand(OpenFileLocation, hasIndividualSelection);
        _copyNameCommand = new RelayCommand(() => SafeCopy(SelectedProcess?.DisplayName), hasSelection);
        _copyPidCommand = new RelayCommand(() => SafeCopy(SelectedProcess?.PidText), hasIndividualSelection);
        _copyPathCommand = new RelayCommand(() => SafeCopy(SelectedProcess?.PathText), hasIndividualSelection);
    }

    public IEnumerable<ProcessDisplayItem> Results => _results;
    public AsyncCommand ExecuteSelectedCommand => _executeSelectedCommand;
    public AsyncCommand ActivateSelectedProcessCommand => _activateSelectedProcessCommand;
    public AsyncCommand KillSelectedProcessCommand => _killSelectedProcessCommand;
    public AsyncCommand KillProcessOnlyCommand => _killProcessOnlyCommand;
    public AsyncCommand SuspendProcessCommand => _suspendProcessCommand;
    public AsyncCommand ResumeProcessCommand => _resumeProcessCommand;
    public RelayCommand<string> SetPriorityCommand => _setPriorityCommand;
    public RelayCommand OpenFileLocationCommand => _openFileLocationCommand;
    public RelayCommand CopyNameCommand => _copyNameCommand;
    public RelayCommand CopyPidCommand => _copyPidCommand;
    public RelayCommand CopyPathCommand => _copyPathCommand;

    public ProcessDisplayItem? SelectedProcess
    {
        get => _selectedProcess;
        set
        {
            if (SetProperty(ref _selectedProcess, value))
            {
                OnPropertyChanged(nameof(SelectedPrimaryActionText));
                RaiseSelectionCommands();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value)) return;
            OnPropertyChanged(nameof(HasQuery));
            RebuildResults();
        }
    }

    public bool HasQuery => !string.IsNullOrWhiteSpace(_searchText);
    public bool HasResults => _results.Count > 0;
    public string SelectedPrimaryActionText =>
        _currentAction.Verb == "open"
            ? SelectedProcess?.IsWindowMatch == true ? "切换到窗口" : "结束进程"
            : _currentAction.Verb switch
            {
                "kill" => "结束进程",
                "killtree" => "结束进程树",
                "suspend" => "暂停进程",
                "resume" => "恢复进程",
                "path" => "打开文件位置",
                "priority" => $"设置优先级 {_currentAction.Priority}",
                _ => "切换到窗口"
            };
    public string ModeHintText => _currentAction.Verb switch
    {
        "kill" => "Kill",
        "killtree" => "Kill Tree",
        "suspend" => "Suspend",
        "resume" => "Resume",
        "path" => "Open Path",
        "priority" => $"Priority {_currentAction.Priority}",
        _ => "Ctrl+Space"
    };

    public string ToastMessage
    {
        get => _toastMessage;
        private set => SetProperty(ref _toastMessage, value);
    }

    public bool IsToastVisible
    {
        get => _isToastVisible;
        private set => SetProperty(ref _isToastVisible, value);
    }

    public Brush ToastAccentBrush
    {
        get => _toastAccentBrush;
        private set => SetProperty(ref _toastAccentBrush, value);
    }

    public Brush ToastBadgeBackground
    {
        get => _toastBadgeBackground;
        private set => SetProperty(ref _toastBadgeBackground, value);
    }

    public Brush ToastBadgeBorderBrush
    {
        get => _toastBadgeBorderBrush;
        private set => SetProperty(ref _toastBadgeBorderBrush, value);
    }

    public async Task OpenAsync(string? initialQuery = null)
    {
        SearchText = initialQuery ?? string.Empty;
        await RefreshAsync();
    }

    public void Close()
    {
        SearchText = string.Empty;
        SelectedProcess = null;
        _toastCts?.Cancel();
        IsToastVisible = false;
        ToastMessage = string.Empty;
    }

    public async Task RefreshAsync()
    {
        var snapshots = await Task.Run(() => _processMonitorService.GetProcessesSnapshot());
        _lastSnapshots = snapshots;
        _lastWindows = await Task.Run(() => _windowSearchService.GetVisibleWindows(snapshots));
        _trayProcessIds = [.. _systemTrayService.GetTrayProcessIds(snapshots)];
        RebuildResults();
    }

    public void SelectNext()
    {
        if (_results.Count == 0)
            return;

        var currentIndex = SelectedProcess is null ? -1 : _results.IndexOf(SelectedProcess);
        var nextIndex = Math.Min(currentIndex + 1, _results.Count - 1);
        SelectedProcess = _results[nextIndex];
    }

    public void SelectPrevious()
    {
        if (_results.Count == 0)
            return;

        var currentIndex = SelectedProcess is null ? _results.Count : _results.IndexOf(SelectedProcess);
        var previousIndex = Math.Max(currentIndex - 1, 0);
        SelectedProcess = _results[previousIndex];
    }

    private void RebuildResults()
    {
        if (string.IsNullOrWhiteSpace(_searchText))
        {
            _results.ReplaceAll([]);
            OnPropertyChanged(nameof(HasResults));
            SelectedProcess = null;
            return;
        }

        _currentAction = ParseActionContext(_searchText);
        OnPropertyChanged(nameof(SelectedPrimaryActionText));
        OnPropertyChanged(nameof(ModeHintText));

        var effectiveQuery = string.IsNullOrWhiteSpace(_currentAction.Query)
            ? _searchText.Trim()
            : _currentAction.Query;

        var previousSelectionPid = SelectedProcess?.ProcessId;
        var previousDisplayKey = SelectedProcess?.DisplayKey;
        var processItems = _lastSnapshots
            .Where(snapshot => MatchesSearch(snapshot, effectiveQuery))
            .OrderByDescending(snapshot => GetSearchScore(snapshot, effectiveQuery))
            .ThenByDescending(x => x.CpuUsagePercent)
            .ThenByDescending(x => x.WorkingSetBytes)
            .Take(8)
            .Select(snapshot =>
            {
                var item = ProcessDisplayItem.Individual(snapshot);
                item.SetIcon(ProcessIconService.GetCachedIcon(snapshot.ExecutablePath));
                item.IsTrayProcess = _trayProcessIds.Contains(snapshot.ProcessId);
                return item;
            });

        var processMap = _lastSnapshots.ToDictionary(snapshot => snapshot.ProcessId);
        var windowItems = _lastWindows
            .Where(window => MatchesWindowSearch(window, effectiveQuery))
            .OrderByDescending(window => GetWindowSearchScore(window, effectiveQuery))
            .Take(8)
            .Select(window =>
            {
                if (!processMap.TryGetValue(window.ProcessId, out var snapshot))
                    return null;

                var item = ProcessDisplayItem.WindowMatch(snapshot, window);
                item.SetIcon(ProcessIconService.GetCachedIcon(snapshot.ExecutablePath));
                item.IsTrayProcess = _trayProcessIds.Contains(snapshot.ProcessId);
                return item;
            })
            .Where(item => item is not null)
            .Cast<ProcessDisplayItem>();

        var orderedItems = _currentAction.Verb == "open"
            ? windowItems.Concat(processItems)
            : processItems.Concat(windowItems);

        var items = orderedItems
            .DistinctBy(item => item.DisplayKey)
            .Take(12)
            .ToList();

        _results.ReplaceAll(items);
        OnPropertyChanged(nameof(HasResults));
        QueueMissingIcons();

        SelectedProcess = previousDisplayKey is not null
            ? _results.FirstOrDefault(item => item.DisplayKey == previousDisplayKey)
                ?? (previousSelectionPid.HasValue ? _results.FirstOrDefault(item => item.ProcessId == previousSelectionPid.Value) : null)
                ?? _results.FirstOrDefault()
            : _results.FirstOrDefault();
    }

    private static bool MatchesSearch(ProcessSnapshot snapshot, string text) =>
        snapshot.ProcessName.Contains(text, StringComparison.OrdinalIgnoreCase)
        || snapshot.ProcessId.ToString().Contains(text, StringComparison.OrdinalIgnoreCase)
        || snapshot.ExecutablePath.Contains(text, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesWindowSearch(WindowSnapshot window, string text) =>
        window.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
        || window.ProcessName.Contains(text, StringComparison.OrdinalIgnoreCase)
        || window.ProcessId.ToString().Contains(text, StringComparison.OrdinalIgnoreCase)
        || window.ExecutablePath.Contains(text, StringComparison.OrdinalIgnoreCase);

    private void QueueMissingIcons()
    {
        foreach (var item in _results)
        {
            if (item.Icon is not null || string.IsNullOrWhiteSpace(item.ExecutablePath) || item.ExecutablePath == "访问受限")
                continue;

            ProcessIconService.EnsureIconLoadedAsync(item.ExecutablePath, (path, icon) =>
            {
                foreach (var result in _results)
                {
                    if (!string.Equals(result.ExecutablePath, path, StringComparison.OrdinalIgnoreCase))
                        continue;

                    result.SetIcon(icon);
                }
            });
        }
    }

    private static double GetImpactScore(ProcessSnapshot snapshot)
    {
        var memoryGb = snapshot.WorkingSetBytes / 1024d / 1024d / 1024d;
        return snapshot.CpuUsagePercent * 3d
               + Math.Min(memoryGb, 4d) * 18d
               + Math.Min(snapshot.ThreadCount / 12d, 20d);
    }

    private static double GetSearchScore(ProcessSnapshot snapshot, string query)
    {
        var score = GetImpactScore(snapshot);
        score += ScoreMatch(snapshot.ProcessName, query, 120, 80, 35);
        score += ScoreMatch(snapshot.ProcessId.ToString(), query, 90, 60, 25);
        score += ScoreMatch(snapshot.ExecutablePath, query, 40, 25, 10);
        return score;
    }

    private static double GetWindowSearchScore(WindowSnapshot window, string query)
    {
        return ScoreMatch(window.Title, query, 180, 120, 60)
               + ScoreMatch(window.ProcessName, query, 50, 35, 15)
               + ScoreMatch(window.ProcessId.ToString(), query, 30, 20, 10);
    }

    private static double ScoreMatch(string source, string query, double exact, double startsWith, double contains)
    {
        if (string.IsNullOrWhiteSpace(source))
            return 0d;

        if (string.Equals(source, query, StringComparison.OrdinalIgnoreCase))
            return exact;

        if (source.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return startsWith;

        return source.Contains(query, StringComparison.OrdinalIgnoreCase) ? contains : 0d;
    }

    private static LauncherActionContext ParseActionContext(string rawQuery)
    {
        var text = rawQuery.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return new LauncherActionContext("open", string.Empty, string.Empty);

        if (text.StartsWith('>'))
            text = text[1..].Trim();

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return new LauncherActionContext("open", string.Empty, string.Empty);

        var verb = NormalizeVerb(parts[0]);
        if (verb is null)
            return new LauncherActionContext("open", rawQuery.Trim(), string.Empty);

        if (verb == "priority")
        {
            if (parts.Length == 1)
                return new LauncherActionContext("priority", string.Empty, "Normal");

            var normalizedPriority = NormalizePriority(parts[1]);
            if (normalizedPriority is not null)
            {
                var query = parts.Length > 2 ? string.Join(' ', parts[2..]) : string.Empty;
                return new LauncherActionContext("priority", query, normalizedPriority);
            }

            return new LauncherActionContext("priority", string.Join(' ', parts[1..]), "Normal");
        }

        var remainingQuery = parts.Length > 1 ? string.Join(' ', parts[1..]) : string.Empty;
        return new LauncherActionContext(verb, remainingQuery, string.Empty);
    }

    private static string? NormalizeVerb(string rawVerb) => rawVerb.ToLowerInvariant() switch
    {
        "open" or "switch" or "activate" or "打开" or "切换" => "open",
        "kill" or "end" or "结束" => "kill",
        "killtree" or "tree" or "结束树" => "killtree",
        "suspend" or "pause" or "暂停" => "suspend",
        "resume" or "恢复" => "resume",
        "path" or "locate" or "定位" => "path",
        "priority" or "prio" or "优先级" => "priority",
        _ => null
    };

    private static string? NormalizePriority(string rawPriority) => rawPriority.ToLowerInvariant() switch
    {
        "high" or "高" => "High",
        "normal" or "默认" or "正常" => "Normal",
        "low" or "低" => "BelowNormal",
        "idle" => "Idle",
        _ => null
    };

    private async Task ActivateSelectedProcessAsync()
    {
        if (SelectedProcess is not { IsGroup: false, ProcessId: int pid })
            return;

        try
        {
            if (SelectedProcess.IsWindowMatch && SelectedProcess.WindowHandle != 0)
            {
                if (_windowSearchService.ActivateWindow(SelectedProcess.WindowHandle))
                {
                    _appShellService.SetStatusMessage($"已切换到窗口 {SelectedProcess.DisplayName}");
                    return;
                }

                _appShellService.SetStatusMessage($"窗口 {SelectedProcess.DisplayName} 激活失败");
                return;
            }

            if (TryActivateProcessWindow(pid, out var processName))
            {
                _appShellService.SetStatusMessage($"已切换到 {processName} ({pid})");
                return;
            }

            _appShellService.SetStatusMessage($"进程 {SelectedProcess.DisplayName} 没有可激活的窗口");
        }
        catch (Exception ex)
        {
            _appShellService.SetStatusMessage($"切换进程失败：{ex.Message}");
        }

        await Task.CompletedTask;
    }

    private async Task ExecutePrimaryActionAsync()
    {
        if (_currentAction.Verb == "open")
        {
            if (SelectedProcess?.IsWindowMatch == true)
            {
                await ActivateSelectedProcessAsync();
                return;
            }

            await KillAsync(entireTree: false);
            return;
        }

        switch (_currentAction.Verb)
        {
            case "kill":
                await KillAsync(entireTree: false);
                break;
            case "killtree":
                await KillAsync(entireTree: true);
                break;
            case "suspend":
                await SuspendAsync();
                break;
            case "resume":
                await ResumeAsync();
                break;
            case "path":
                OpenFileLocation();
                break;
            case "priority":
                SetPriority(_currentAction.Priority);
                break;
            default:
                await ActivateSelectedProcessAsync();
                break;
        }
    }

    private async Task KillAsync(bool entireTree)
    {
        if (SelectedProcess is null)
            return;

        var process = SelectedProcess;
        var message = entireTree
            ? $"确定要结束进程树 \"{process.DisplayName}\" (PID {process.ProcessId}) 及其所有子进程吗？"
            : $"确定要结束进程 \"{process.DisplayName}\" (PID {process.ProcessId}) 吗？";

        var confirmed = await _dialogService.ShowConfirmationAsync(
            entireTree ? "结束进程树" : "结束进程",
            message,
            primaryButtonText: entireTree ? "结束进程树" : "结束进程",
            secondaryButtonText: "取消",
            isDanger: true);

        if (!confirmed)
        {
            _trayService.ShowBalloon("结束进程", "已取消结束进程操作", BalloonIcon.Warning);
            return;
        }

        try
        {
            await Task.Run(() => _processMonitorService.KillProcess(process.ProcessId!.Value, entireTree));
            _appShellService.SetStatusMessage($"已结束进程 {process.DisplayName} ({process.ProcessId})");
            _trayService.ShowBalloon("结束进程", $"已结束 {process.DisplayName} 进程", BalloonIcon.Info);
            await _appShellService.RequestRealtimeRefreshAsync();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("操作失败", $"结束进程失败：{ex.Message}");
            _appShellService.SetStatusMessage($"结束进程失败：{process.DisplayName}");
            _trayService.ShowBalloon("结束进程失败", $"{process.DisplayName} 结束失败", BalloonIcon.Error);
        }
    }

    private async Task SuspendAsync()
    {
        if (SelectedProcess is not { IsGroup: false, ProcessId: int pid }) return;
        try
        {
            await Task.Run(() => _processMonitorService.SuspendProcess(pid));
            _appShellService.SetStatusMessage($"已暂停进程 {SelectedProcess.DisplayName} ({pid})");
        }
        catch (Exception ex)
        {
            _appShellService.SetStatusMessage($"暂停失败：{ex.Message}");
        }
    }

    private async Task ResumeAsync()
    {
        if (SelectedProcess is not { IsGroup: false, ProcessId: int pid }) return;
        try
        {
            await Task.Run(() => _processMonitorService.ResumeProcess(pid));
            _appShellService.SetStatusMessage($"已恢复进程 {SelectedProcess.DisplayName} ({pid})");
        }
        catch (Exception ex)
        {
            _appShellService.SetStatusMessage($"恢复失败：{ex.Message}");
        }
    }

    private void SetPriority(string? priority)
    {
        if (SelectedProcess is not { IsGroup: false, ProcessId: int pid } || priority is null) return;

        var priorityClass = priority switch
        {
            "RealTime" => ProcessPriorityClass.RealTime,
            "High" => ProcessPriorityClass.High,
            "AboveNormal" => ProcessPriorityClass.AboveNormal,
            "BelowNormal" => ProcessPriorityClass.BelowNormal,
            "Idle" => ProcessPriorityClass.Idle,
            _ => ProcessPriorityClass.Normal
        };

        try
        {
            _processMonitorService.SetProcessPriority(pid, priorityClass);
            _appShellService.SetStatusMessage($"已将 {SelectedProcess.DisplayName} 优先级设为 {priority}");
        }
        catch (Exception ex)
        {
            _appShellService.SetStatusMessage($"设置优先级失败：{ex.Message}");
        }
    }

    private void OpenFileLocation()
    {
        var path = SelectedProcess?.PathText;
        if (string.IsNullOrEmpty(path) || path == "访问受限") return;

        try
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
            _appShellService.SetStatusMessage($"已在资源管理器中定位：{path}");
        }
        catch (Exception ex)
        {
            _appShellService.SetStatusMessage($"打开位置失败：{ex.Message}");
        }
    }

    private static void SafeCopy(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, copy: true);
                return;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Thread.Sleep(20);
            }
        }
    }

    private void RaiseSelectionCommands()
    {
        _executeSelectedCommand.RaiseCanExecuteChanged();
        _activateSelectedProcessCommand.RaiseCanExecuteChanged();
        _killSelectedProcessCommand.RaiseCanExecuteChanged();
        _killProcessOnlyCommand.RaiseCanExecuteChanged();
        _suspendProcessCommand.RaiseCanExecuteChanged();
        _resumeProcessCommand.RaiseCanExecuteChanged();
        _setPriorityCommand.RaiseCanExecuteChanged();
        _openFileLocationCommand.RaiseCanExecuteChanged();
        _copyNameCommand.RaiseCanExecuteChanged();
        _copyPidCommand.RaiseCanExecuteChanged();
        _copyPathCommand.RaiseCanExecuteChanged();
    }

    private void HandleToastRequested(ToastNotification notification)
    {
        _toastCts?.Cancel();
        _toastCts?.Dispose();
        _toastCts = new CancellationTokenSource();

        ToastMessage = notification.Message;
        ApplyToastStyle(notification.Kind);
        IsToastVisible = true;
        _ = HideToastAsync(_toastCts.Token);
    }

    private async Task HideToastAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2.2), ct);
            IsToastVisible = false;
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await Task.Delay(160, CancellationToken.None);
        if (!ct.IsCancellationRequested)
            ToastMessage = string.Empty;
    }

    private void ApplyToastStyle(ToastKind kind)
    {
        (ToastAccentBrush, ToastBadgeBackground, ToastBadgeBorderBrush) = kind switch
        {
            ToastKind.Success => (Brushes.MediumSeaGreen, CreateBrush("#1A163A27"), CreateBrush("#3348A36D")),
            ToastKind.Warning => (Brushes.DarkOrange, CreateBrush("#1AF59E0B"), CreateBrush("#33F59E0B")),
            ToastKind.Error => (Brushes.IndianRed, CreateBrush("#1AE11D48"), CreateBrush("#33E11D48")),
            _ => (Brushes.DeepSkyBlue, CreateBrush("#1A0EA5E9"), CreateBrush("#330EA5E9"))
        };
    }

    private static Brush CreateBrush(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }

    private bool TryActivateProcessWindow(int pid, out string processName)
    {
        using var process = Process.GetProcessById(pid);
        processName = process.ProcessName;
        if (process.MainWindowHandle == IntPtr.Zero)
            return false;

        return _windowSearchService.ActivateWindow(process.MainWindowHandle);
    }

    private readonly record struct LauncherActionContext(string Verb, string Query, string Priority);
}
