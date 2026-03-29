using FastTaskManager.App.Infrastructure;
using System.Windows.Media;

namespace FastTaskManager.App.Models;

public sealed class ProcessDisplayItem : ObservableObject
{
    private const double OneGb = 1024d * 1024d * 1024d;

    private static readonly Brush CpuCoolBrush = CreateBrush("#164A8C");
    private static readonly Brush CpuWarmBrush = CreateBrush("#D97706");
    private static readonly Brush CpuHotBrush = CreateBrush("#C2410C");
    private static readonly Brush CpuCriticalBrush = CreateBrush("#B91C1C");
    private static readonly Brush MemoryLowBrush = CreateBrush("#1E293B");
    private static readonly Brush MemoryHighBrush = CreateBrush("#2563EB");
    private static readonly Brush NeutralValueBrush = CreateBrush("#6B7F94");

    private string _displayName = "";
    private double _cpuPercent;
    private long _memoryBytes;
    private bool _isExpanded;
    private int _threadCount;
    private long? _handleCount;
    private DateTime? _startTime;
    private string _executablePath = "";
    private IReadOnlyList<int> _groupChildPids = Array.Empty<int>();
    private bool _isTrayProcess;
    private bool _isWindowMatch;
    private nint _windowHandle;

    public bool IsGroup { get; init; }
    public string DisplayKey { get; init; } = "";
    public ImageSource? Icon { get; set; }
    public bool IsChild { get; init; }
    public int? ProcessId { get; init; }
    public string GroupKey { get; init; } = "";
    public ProcessCategory Category { get; set; }

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public double CpuPercent
    {
        get => _cpuPercent;
        set
        {
            if (!SetProperty(ref _cpuPercent, value)) return;
            RaiseComputedChanges();
        }
    }

    public long MemoryBytes
    {
        get => _memoryBytes;
        set
        {
            if (!SetProperty(ref _memoryBytes, value)) return;
            RaiseComputedChanges();
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public IReadOnlyList<int> GroupChildPids
    {
        get => _groupChildPids;
        set => SetProperty(ref _groupChildPids, value);
    }

    public int ThreadCount
    {
        get => _threadCount;
        set
        {
            if (!SetProperty(ref _threadCount, value)) return;
            RaiseComputedChanges();
        }
    }

    public long? HandleCount
    {
        get => _handleCount;
        set
        {
            if (!SetProperty(ref _handleCount, value)) return;
            OnPropertyChanged(nameof(HandleText));
        }
    }

    public DateTime? StartTime
    {
        get => _startTime;
        set
        {
            if (!SetProperty(ref _startTime, value)) return;
            OnPropertyChanged(nameof(StartTimeText));
            OnPropertyChanged(nameof(UptimeText));
        }
    }

    public string ExecutablePath
    {
        get => _executablePath;
        set => SetProperty(ref _executablePath, value);
    }

    public bool IsTrayProcess
    {
        get => _isTrayProcess;
        set
        {
            if (!SetProperty(ref _isTrayProcess, value)) return;
            OnPropertyChanged(nameof(TrayBadgeText));
            OnPropertyChanged(nameof(QuickBadgeText));
            OnPropertyChanged(nameof(CategoryText));
            OnPropertyChanged(nameof(ImpactScore));
            OnPropertyChanged(nameof(ImpactText));
        }
    }

    public bool IsWindowMatch
    {
        get => _isWindowMatch;
        set
        {
            if (!SetProperty(ref _isWindowMatch, value)) return;
            OnPropertyChanged(nameof(QuickBadgeText));
        }
    }

    public nint WindowHandle
    {
        get => _windowHandle;
        set => SetProperty(ref _windowHandle, value);
    }

    public string CpuText => $"{_cpuPercent:F1}%";
    public string MemoryText => $"{_memoryBytes / 1024d / 1024d:F0} MB";
    public string PidText => IsGroup ? "" : (ProcessId?.ToString() ?? "");
    public string ThreadText => IsGroup ? "" : _threadCount.ToString();
    public string HandleText => IsGroup ? "" : (_handleCount?.ToString() ?? "-");
    public string StartTimeText => IsGroup ? "" : (_startTime?.ToString("HH:mm:ss") ?? "-");
    public string UptimeText => IsGroup || _startTime is null
        ? ""
        : FormatDuration(DateTime.Now - _startTime.Value);
    public string PathText => IsGroup ? "" : _executablePath;
    public string TrayBadgeText => IsTrayProcess ? "托盘" : "";
    public string QuickBadgeText => IsWindowMatch ? "窗口" : TrayBadgeText;
    public string CategoryText => IsTrayProcess
        ? (IsGroup ? "托盘组" : "托盘")
        : Category switch
        {
            ProcessCategory.App => IsGroup ? "应用组" : "应用",
            ProcessCategory.Background => IsGroup ? "后台组" : "后台",
            ProcessCategory.Windows => IsGroup ? "系统组" : "系统",
            _ => IsGroup ? "分组" : "其他"
        };
    public double CpuHeatPercent => Math.Clamp(_cpuPercent, 0d, 100d);
    public double MemoryHeatPercent => Math.Clamp(_memoryBytes / OneGb * 100d, 0d, 100d);
    public Brush CpuHeatBrush => _cpuPercent switch
    {
        >= 80 => CpuCriticalBrush,
        >= 50 => CpuHotBrush,
        >= 20 => CpuWarmBrush,
        _ => CpuCoolBrush
    };
    public Brush MemoryHeatBrush => MemoryHeatPercent >= 100 ? MemoryHighBrush : MemoryLowBrush;
    public Brush CpuTextBrush => _cpuPercent switch
    {
        >= 80 => CpuCriticalBrush,
        >= 20 => CpuHotBrush,
        _ => NeutralValueBrush
    };
    public Brush MemoryTextBrush => _memoryBytes >= OneGb ? MemoryHighBrush : NeutralValueBrush;
    public bool HasHighCpu => _cpuPercent > 20d;
    public bool HasHighMemory => _memoryBytes >= OneGb;
    public bool HasHighThreads => _threadCount > 200;
    public double ImpactScore => Math.Round(Math.Clamp(_cpuPercent * 3d + Math.Min(_memoryBytes / OneGb, 4d) * 18d + Math.Min(_threadCount / 12d, 20d) + (IsTrayProcess ? 5d : 0d), 0d, 999d), 1);
    public string ImpactText => ImpactScore >= 80 ? "高" : ImpactScore >= 35 ? "中" : "低";
    public bool HasAnomaly => _cpuPercent > 20d || _memoryBytes >= OneGb || _threadCount > 200;
    public string AnomalyText
    {
        get
        {
            var parts = new List<string>(3);
            if (_cpuPercent > 20d) parts.Add("🔥");
            if (_memoryBytes >= OneGb) parts.Add("🐘");
            if (_threadCount > 200) parts.Add("⚠️");
            return string.Concat(parts);
        }
    }

    public static ProcessDisplayItem Individual(ProcessSnapshot s, bool isChild = false)
    {
        var item = new ProcessDisplayItem
        {
            IsGroup = false,
            IsChild = isChild,
            ProcessId = s.ProcessId,
            GroupKey = s.ProcessName,
            DisplayKey = $"P:{s.ProcessId}",
        };
        item.DisplayName = s.ProcessName;
        item.Category = ProcessCategory.All;
        item.CpuPercent = Math.Round(s.CpuUsagePercent, 1);
        item.MemoryBytes = s.WorkingSetBytes;
        item.ThreadCount = s.ThreadCount;
        item.HandleCount = s.HandleCount;
        item.StartTime = s.StartTime;
        item.ExecutablePath = s.ExecutablePath;
        return item;
    }

    public static ProcessDisplayItem Group(string name, IList<ProcessSnapshot> members, bool isExpanded)
    {
        var item = new ProcessDisplayItem
        {
            IsGroup = true,
            GroupKey = name,
            DisplayKey = $"G:{name}",
        };
        item.DisplayName = $"{name} ({members.Count})";
        item.Category = ProcessCategory.All;
        item.CpuPercent = Math.Round(members.Sum(x => x.CpuUsagePercent), 1);
        item.MemoryBytes = members.Sum(x => x.WorkingSetBytes);
        item.ThreadCount = members.Sum(x => x.ThreadCount);
        item.HandleCount = members.Sum(x => x.HandleCount ?? 0);
        item.IsExpanded = isExpanded;
        item.GroupChildPids = members.Select(m => m.ProcessId).ToArray();
        return item;
    }

    public static ProcessDisplayItem WindowMatch(ProcessSnapshot snapshot, WindowSnapshot window)
    {
        var item = new ProcessDisplayItem
        {
            IsGroup = false,
            IsChild = false,
            IsWindowMatch = true,
            WindowHandle = window.Handle,
            ProcessId = snapshot.ProcessId,
            GroupKey = snapshot.ProcessName,
            DisplayKey = $"W:{window.Handle}",
        };
        item.DisplayName = window.Title;
        item.Category = ProcessCategory.All;
        item.CpuPercent = Math.Round(snapshot.CpuUsagePercent, 1);
        item.MemoryBytes = snapshot.WorkingSetBytes;
        item.ThreadCount = snapshot.ThreadCount;
        item.HandleCount = snapshot.HandleCount;
        item.StartTime = snapshot.StartTime;
        item.ExecutablePath = snapshot.ExecutablePath;
        return item;
    }

    public void UpdateFrom(ProcessDisplayItem source)
    {
        DisplayName = source.DisplayName;
        SetIcon(source.Icon);
        Category = source.Category;
        CpuPercent = source.CpuPercent;
        MemoryBytes = source.MemoryBytes;
        ThreadCount = source.ThreadCount;
        HandleCount = source.HandleCount;
        StartTime = source.StartTime;
        ExecutablePath = source.ExecutablePath;
        GroupChildPids = source.GroupChildPids;
        IsExpanded = source.IsExpanded;
        IsTrayProcess = source.IsTrayProcess;
        IsWindowMatch = source.IsWindowMatch;
        WindowHandle = source.WindowHandle;
    }

    public void SetIcon(ImageSource? icon)
    {
        if (ReferenceEquals(Icon, icon))
            return;

        Icon = icon;
        OnPropertyChanged(nameof(Icon));
    }

    private void RaiseComputedChanges()
    {
        OnPropertyChanged(nameof(CpuText));
        OnPropertyChanged(nameof(MemoryText));
        OnPropertyChanged(nameof(ThreadText));
        OnPropertyChanged(nameof(CpuHeatPercent));
        OnPropertyChanged(nameof(MemoryHeatPercent));
        OnPropertyChanged(nameof(CpuHeatBrush));
        OnPropertyChanged(nameof(MemoryHeatBrush));
        OnPropertyChanged(nameof(CpuTextBrush));
        OnPropertyChanged(nameof(MemoryTextBrush));
        OnPropertyChanged(nameof(UptimeText));
        OnPropertyChanged(nameof(HasHighCpu));
        OnPropertyChanged(nameof(HasHighMemory));
        OnPropertyChanged(nameof(HasHighThreads));
        OnPropertyChanged(nameof(ImpactScore));
        OnPropertyChanged(nameof(ImpactText));
        OnPropertyChanged(nameof(AnomalyText));
        OnPropertyChanged(nameof(HasAnomaly));
    }

    private static Brush CreateBrush(string colorHex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(colorHex)!;
        brush.Freeze();
        return brush;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
            return $"{(int)duration.TotalDays}天 {duration.Hours}时";

        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}时 {duration.Minutes}分";

        if (duration.TotalMinutes >= 1)
            return $"{(int)duration.TotalMinutes}分 {duration.Seconds}秒";

        return $"{Math.Max(0, duration.Seconds)}秒";
    }
}
