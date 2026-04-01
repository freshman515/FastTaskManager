using System.Diagnostics;
using System.Windows;
using FastTaskManager.App.Infrastructure;
using FastTaskManager.App.Models;
using FastTaskManager.App.Services;
using Hardcodet.Wpf.TaskbarNotification;
using System.Windows.Media;

namespace FastTaskManager.App.ViewModels;

public sealed class ProcessesViewModel : ObservableObject
{
    private readonly ProcessMonitorService _processMonitorService;
    private readonly AppShellService _appShellService;
    private readonly DialogService _dialogService;
    private readonly TrayService _trayService;
    private readonly RangeObservableCollection<ProcessDisplayItem> _displayItems = [];
    private readonly HashSet<string> _expandedGroups = new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<ProcessSnapshot>? _lastSnapshots;
    private IReadOnlyDictionary<int, ProcessCategory> _categoryMap = new Dictionary<int, ProcessCategory>();
    private HashSet<int> _trayProcessIds = [];
    private ProcessCategory _activeFilter = ProcessCategory.All;
    private int _appCount;
    private int _backgroundCount;
    private int _windowsCount;
    private int _trayCount;

    private ProcessDisplayItem? _selectedProcess;
    private string _searchText = string.Empty;
    private int _processCount;
    private string _totalCpuText = "0.0%";
    private string _totalMemoryText = "0%";
    private string _totalMemoryDetail = "0 / 0 GB";
    private string _lastUpdatedText = "-";
    private string _systemAssessmentText = "系统状态：稳定";
    private string _systemAssessmentDetail = "当前没有明显瓶颈";
    private Brush _systemAssessmentBrush = Brushes.LimeGreen;

    private readonly AsyncCommand _killSelectedProcessCommand;
    private readonly AsyncCommand _killProcessOnlyCommand;
    private readonly AsyncCommand _suspendProcessCommand;
    private readonly AsyncCommand _resumeProcessCommand;
    private readonly RelayCommand<string> _setPriorityCommand;
    private readonly RelayCommand _openFileLocationCommand;
    private readonly RelayCommand _copyNameCommand;
    private readonly RelayCommand _copyPidCommand;
    private readonly RelayCommand _copyPathCommand;
    private readonly RelayCommand<string> _setFilterCommand;
    private readonly AsyncCommand _refreshCommand;

    public ProcessesViewModel(
        ProcessMonitorService processMonitorService,
        AppShellService appShellService,
        DialogService dialogService,
        TrayService trayService)
    {
        _processMonitorService = processMonitorService;
        _appShellService = appShellService;
        _dialogService = dialogService;
        _trayService = trayService;

        Func<bool> hasSelection = () => SelectedProcess is not null;
        Func<bool> hasIndividualSelection = () => SelectedProcess is { IsGroup: false };

        _killSelectedProcessCommand = new AsyncCommand(() => KillAsync(entireTree: true), hasSelection);
        _killProcessOnlyCommand = new AsyncCommand(() => KillAsync(entireTree: false), hasSelection);
        _suspendProcessCommand = new AsyncCommand(SuspendAsync, hasIndividualSelection);
        _resumeProcessCommand = new AsyncCommand(ResumeAsync, hasIndividualSelection);
        _setPriorityCommand = new RelayCommand<string>(SetPriority, _ => SelectedProcess is { IsGroup: false });
        _openFileLocationCommand = new RelayCommand(OpenFileLocation, hasIndividualSelection);
        _copyNameCommand = new RelayCommand(() => SafeCopy(SelectedProcess?.DisplayName), hasSelection);
        _copyPidCommand = new RelayCommand(() => SafeCopy(SelectedProcess?.PidText), hasIndividualSelection);
        _copyPathCommand = new RelayCommand(() => SafeCopy(SelectedProcess?.PathText), hasIndividualSelection);
        _refreshCommand = new AsyncCommand(RefreshAsync);
        _setFilterCommand = new RelayCommand<string>(filter =>
        {
            if (Enum.TryParse<ProcessCategory>(filter, out var cat) && cat != _activeFilter)
            {
                _activeFilter = cat;
                OnPropertyChanged(nameof(IsFilterAll));
                OnPropertyChanged(nameof(IsFilterApp));
                OnPropertyChanged(nameof(IsFilterBackground));
                OnPropertyChanged(nameof(IsFilterWindows));
                OnPropertyChanged(nameof(IsFilterTray));
                if (_lastSnapshots is not null)
                    _ = RebuildDisplayItemsAsync(_lastSnapshots);
            }
        });
    }

    public IEnumerable<ProcessDisplayItem> DisplayItems => _displayItems;
    public RelayCommand<string> SetFilterCommand => _setFilterCommand;

    public bool IsFilterAll        => _activeFilter == ProcessCategory.All;
    public bool IsFilterApp        => _activeFilter == ProcessCategory.App;
    public bool IsFilterBackground => _activeFilter == ProcessCategory.Background;
    public bool IsFilterWindows    => _activeFilter == ProcessCategory.Windows;
    public bool IsFilterTray       => _activeFilter == ProcessCategory.Tray;

    public string AppTabText        => $"应用  {_appCount}";
    public string BackgroundTabText => $"后台进程  {_backgroundCount}";
    public string WindowsTabText    => $"Windows 进程  {_windowsCount}";
    public string TrayTabText       => $"托盘  {_trayCount}";

    public AsyncCommand KillSelectedProcessCommand => _killSelectedProcessCommand;
    public AsyncCommand KillProcessOnlyCommand => _killProcessOnlyCommand;
    public AsyncCommand SuspendProcessCommand => _suspendProcessCommand;
    public AsyncCommand ResumeProcessCommand => _resumeProcessCommand;
    public RelayCommand<string> SetPriorityCommand => _setPriorityCommand;
    public RelayCommand OpenFileLocationCommand => _openFileLocationCommand;
    public RelayCommand CopyNameCommand => _copyNameCommand;
    public RelayCommand CopyPidCommand => _copyPidCommand;
    public RelayCommand CopyPathCommand => _copyPathCommand;
    public AsyncCommand RefreshCommand => _refreshCommand;

    public ProcessDisplayItem? SelectedProcess
    {
        get => _selectedProcess;
        set
        {
            if (SetProperty(ref _selectedProcess, value))
                RaiseSelectionCommands();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value)) return;
            if (_lastSnapshots is not null)
                _ = RebuildDisplayItemsAsync(_lastSnapshots);
        }
    }

    public int ProcessCount
    {
        get => _processCount;
        private set => SetProperty(ref _processCount, value);
    }

    public string TotalCpuText
    {
        get => _totalCpuText;
        private set => SetProperty(ref _totalCpuText, value);
    }

    public string TotalMemoryText
    {
        get => _totalMemoryText;
        private set => SetProperty(ref _totalMemoryText, value);
    }

    public string TotalMemoryDetail
    {
        get => _totalMemoryDetail;
        private set => SetProperty(ref _totalMemoryDetail, value);
    }

    public string LastUpdatedText
    {
        get => _lastUpdatedText;
        private set => SetProperty(ref _lastUpdatedText, value);
    }

    public string SystemAssessmentText
    {
        get => _systemAssessmentText;
        private set => SetProperty(ref _systemAssessmentText, value);
    }

    public string SystemAssessmentDetail
    {
        get => _systemAssessmentDetail;
        private set => SetProperty(ref _systemAssessmentDetail, value);
    }

    public Brush SystemAssessmentBrush
    {
        get => _systemAssessmentBrush;
        private set => SetProperty(ref _systemAssessmentBrush, value);
    }

    public async Task ApplyRealtimeSnapshotAsync(
        IReadOnlyList<ProcessSnapshot> snapshots,
        IReadOnlyDictionary<int, ProcessCategory> categoryMap,
        IReadOnlySet<int> trayProcessIds,
        (uint LoadPct, double UsedGb, double TotalGb) memory,
        string nowText)
    {
        _categoryMap = categoryMap;
        _trayProcessIds = [.. trayProcessIds];
        UpdateCategoryCounts(snapshots);
        await RebuildDisplayItemsAsync(snapshots);
        ProcessCount = snapshots.Count;
        var totalCpu = Math.Clamp(snapshots.Sum(x => x.CpuUsagePercent), 0d, 100d);
        TotalCpuText = $"{totalCpu:F1}%";
        TotalMemoryText = $"{memory.LoadPct}%";
        TotalMemoryDetail = $"{memory.UsedGb:F1} / {memory.TotalGb:F1} GB";
        LastUpdatedText = nowText;
        UpdateAssessment(totalCpu, memory.LoadPct, nowText);
    }

    public Task RefreshAsync() => _appShellService.RequestRealtimeRefreshAsync();

    private void UpdateCategoryCounts(IReadOnlyList<ProcessSnapshot> snapshots)
    {
        _appCount = 0;
        _backgroundCount = 0;
        _windowsCount = 0;
        _trayCount = 0;
        foreach (var s in snapshots)
        {
            if (!_categoryMap.TryGetValue(s.ProcessId, out var cat)) continue;
            if (cat == ProcessCategory.App)        _appCount++;
            else if (cat == ProcessCategory.Background) _backgroundCount++;
            else if (cat == ProcessCategory.Windows)    _windowsCount++;

            if (_trayProcessIds.Contains(s.ProcessId))
                _trayCount++;
        }
        OnPropertyChanged(nameof(AppTabText));
        OnPropertyChanged(nameof(BackgroundTabText));
        OnPropertyChanged(nameof(WindowsTabText));
        OnPropertyChanged(nameof(TrayTabText));
    }

    public void ToggleGroupExpand(ProcessDisplayItem group)
    {
        if (!group.IsGroup) return;

        if (_expandedGroups.Contains(group.GroupKey))
            _expandedGroups.Remove(group.GroupKey);
        else
            _expandedGroups.Add(group.GroupKey);

        if (_lastSnapshots is not null)
            _ = RebuildDisplayItemsAsync(_lastSnapshots);
    }

    private async Task RebuildDisplayItemsAsync(IReadOnlyList<ProcessSnapshot> snapshots)
    {
        _lastSnapshots = snapshots;

        int? selectedPid = _selectedProcess is { IsGroup: false } ? _selectedProcess.ProcessId : null;
        string? selectedGroup = _selectedProcess is { IsGroup: true } ? _selectedProcess.GroupKey : null;
        var categoryMap = _categoryMap;
        var trayProcessIds = _trayProcessIds.ToHashSet();
        var activeFilter = _activeFilter;
        var searchText = _searchText;
        var expandedGroups = _expandedGroups.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var newItems = await Task.Run(() => BuildDisplayList(snapshots, categoryMap, trayProcessIds, activeFilter, searchText, expandedGroups));
        SyncDisplayItems(newItems);
        QueueMissingIcons();

        SelectedProcess = _displayItems.FirstOrDefault(item =>
            (selectedPid.HasValue && !item.IsGroup && item.ProcessId == selectedPid) ||
            (selectedGroup is not null && item.IsGroup && item.GroupKey == selectedGroup));
    }

    private void SyncDisplayItems(IReadOnlyList<ProcessDisplayItem> newItems)
    {
        if (_displayItems.Count != newItems.Count)
        {
            _displayItems.ReplaceAll(newItems);
            return;
        }

        for (var i = 0; i < newItems.Count; i++)
        {
            if (!string.Equals(_displayItems[i].DisplayKey, newItems[i].DisplayKey, StringComparison.Ordinal))
            {
                _displayItems.ReplaceAll(newItems);
                return;
            }
        }

        for (var i = 0; i < newItems.Count; i++)
            _displayItems[i].UpdateFrom(newItems[i]);
    }

    private static List<ProcessDisplayItem> BuildDisplayList(
        IReadOnlyList<ProcessSnapshot> snapshots,
        IReadOnlyDictionary<int, ProcessCategory> categoryMap,
        IReadOnlySet<int> trayProcessIds,
        ProcessCategory activeFilter,
        string searchText,
        HashSet<string> expandedGroups)
    {
        // 先按分类过滤
        IEnumerable<ProcessSnapshot> source = activeFilter switch
        {
            ProcessCategory.All => snapshots,
            ProcessCategory.Tray => snapshots.Where(s => trayProcessIds.Contains(s.ProcessId)),
            _ => snapshots.Where(s => categoryMap.TryGetValue(s.ProcessId, out var c) && c == activeFilter)
        };

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            return source
                .Where(s => MatchesSearch(s, searchText))
                .OrderByDescending(GetImpactScore)
                .ThenByDescending(x => x.CpuUsagePercent)
                .ThenByDescending(x => x.WorkingSetBytes)
                .Select(snapshot =>
                {
                    var item = ProcessDisplayItem.Individual(snapshot);
                    item.Category = categoryMap.TryGetValue(snapshot.ProcessId, out var category)
                        ? category
                        : ProcessCategory.All;
                    item.SetIcon(ProcessIconService.GetCachedIcon(snapshot.ExecutablePath));
                    item.IsTrayProcess = trayProcessIds.Contains(snapshot.ProcessId);
                    return item;
                })
                .ToList();
        }

        var sourceList = source.ToList();
        var result = new List<ProcessDisplayItem>(sourceList.Count);
        var groups = sourceList
            .GroupBy(s => s.ProcessName, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Sum(GetImpactScore))
            .ThenByDescending(g => g.Sum(x => x.CpuUsagePercent))
            .ThenByDescending(g => g.Sum(x => x.WorkingSetBytes))
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var members = group
                .OrderByDescending(GetImpactScore)
                .ThenByDescending(x => x.CpuUsagePercent)
                .ThenByDescending(x => x.WorkingSetBytes)
                .ToList();

            // 取第一个有效路径的图标作为分组/单个进程图标
            var iconPath = members.Select(m => m.ExecutablePath)
                .FirstOrDefault(p => !string.IsNullOrEmpty(p) && p != "访问受限");
            ImageSource? groupIcon = ProcessIconService.GetCachedIcon(iconPath);

            if (members.Count == 1)
            {
                var single = ProcessDisplayItem.Individual(members[0]);
                single.Category = categoryMap.TryGetValue(members[0].ProcessId, out var category)
                    ? category
                    : ProcessCategory.All;
                single.SetIcon(groupIcon);
                single.IsTrayProcess = trayProcessIds.Contains(members[0].ProcessId);
                result.Add(single);
                continue;
            }

            var expanded = expandedGroups.Contains(group.Key);
            var groupItem = ProcessDisplayItem.Group(group.Key, members, expanded);
            groupItem.Category = ResolveGroupCategory(members, categoryMap);
            groupItem.SetIcon(groupIcon);
            groupItem.ExecutablePath = iconPath ?? string.Empty;
            groupItem.IsTrayProcess = members.Any(member => trayProcessIds.Contains(member.ProcessId));
            result.Add(groupItem);

            if (!expanded) continue;
            foreach (var member in members)
            {
                var child = ProcessDisplayItem.Individual(member, isChild: true);
                child.Category = categoryMap.TryGetValue(member.ProcessId, out var category)
                    ? category
                    : ProcessCategory.All;
                child.SetIcon(ProcessIconService.GetCachedIcon(member.ExecutablePath));
                child.IsTrayProcess = trayProcessIds.Contains(member.ProcessId);
                result.Add(child);
            }
        }

        return result;
    }

    private static bool MatchesSearch(ProcessSnapshot snapshot, string text) =>
        snapshot.ProcessName.Contains(text, StringComparison.OrdinalIgnoreCase)
        || snapshot.ProcessId.ToString().Contains(text, StringComparison.OrdinalIgnoreCase)
        || snapshot.ExecutablePath.Contains(text, StringComparison.OrdinalIgnoreCase);

    private static ProcessCategory ResolveGroupCategory(
        IReadOnlyList<ProcessSnapshot> members,
        IReadOnlyDictionary<int, ProcessCategory> categoryMap)
    {
        ProcessCategory? category = null;

        foreach (var member in members)
        {
            var current = categoryMap.TryGetValue(member.ProcessId, out var mapped)
                ? mapped
                : ProcessCategory.All;

            if (category is null)
            {
                category = current;
                continue;
            }

            if (category != current)
                return ProcessCategory.All;
        }

        return category ?? ProcessCategory.All;
    }

    private void QueueMissingIcons()
    {
        foreach (var item in _displayItems)
        {
            if (item.Icon is not null || string.IsNullOrWhiteSpace(item.ExecutablePath) || item.ExecutablePath == "访问受限")
                continue;

            ProcessIconService.EnsureIconLoadedAsync(item.ExecutablePath, (path, icon) =>
            {
                foreach (var displayItem in _displayItems)
                {
                    if (!string.Equals(displayItem.ExecutablePath, path, StringComparison.OrdinalIgnoreCase))
                        continue;

                    displayItem.SetIcon(icon);
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

    private void UpdateAssessment(double totalCpu, uint memoryLoad, string nowText)
    {
        if (memoryLoad > 80)
        {
            SystemAssessmentText = "系统状态：⚠️ 内存瓶颈";
            SystemAssessmentDetail = $"内存占用 {memoryLoad}% ，优先检查内存大户 · {nowText}";
            SystemAssessmentBrush = Brushes.Orange;
            return;
        }

        if (totalCpu > 80)
        {
            SystemAssessmentText = "系统状态：🔥 CPU 瓶颈";
            SystemAssessmentDetail = $"CPU 占用 {totalCpu:F0}% ，优先处理高 CPU 进程 · {nowText}";
            SystemAssessmentBrush = Brushes.OrangeRed;
            return;
        }

        SystemAssessmentText = "系统状态：正常";
        SystemAssessmentDetail = $"资源负载平稳 · 最近更新 {nowText}";
        SystemAssessmentBrush = Brushes.LimeGreen;
    }

    private async Task KillAsync(bool entireTree)
    {
        if (SelectedProcess is null) return;
        var process = SelectedProcess;

        if (process.IsGroup)
        {
            var pids = process.GroupChildPids;
            var message = $"确定要结束 \"{process.GroupKey}\" 的全部 {pids.Count} 个进程吗？";
            var confirmed = await _dialogService.ShowConfirmationAsync(
                "结束进程组",
                message,
                primaryButtonText: "全部结束",
                secondaryButtonText: "取消",
                isDanger: true);

            if (!confirmed)
            {
                _trayService.ShowBalloon("结束进程", "已取消结束进程组操作", BalloonIcon.Warning);
                return;
            }

            var errors = new List<string>();
            foreach (var pid in pids)
            {
                try
                {
                    await Task.Run(() => _processMonitorService.KillProcess(pid, entireTree));
                }
                catch (Exception ex)
                {
                    errors.Add($"PID {pid}: {ex.Message}");
                }
            }

            if (errors.Count == 0)
            {
                _appShellService.SetStatusMessage($"已结束进程组 {process.GroupKey}（{pids.Count} 个进程）");
                _trayService.ShowBalloon("结束进程", $"已结束 {process.GroupKey} 进程组", BalloonIcon.Info);
            }
            else
            {
                _appShellService.SetStatusMessage($"进程组 {process.GroupKey} 部分结束失败 — {string.Join("；", errors.Take(3))}");
                _trayService.ShowBalloon("结束进程", $"进程组 {process.GroupKey} 部分结束失败", BalloonIcon.Warning);
            }
        }
        else
        {
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
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("操作失败", $"结束进程失败：{ex.Message}");
                _appShellService.SetStatusMessage($"结束进程失败：{process.DisplayName}");
                _trayService.ShowBalloon("结束进程失败", $"{process.DisplayName} 结束失败", BalloonIcon.Error);
            }
        }

        await _appShellService.RequestRealtimeRefreshAsync();
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
}
