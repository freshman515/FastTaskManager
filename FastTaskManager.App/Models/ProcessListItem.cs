using FastTaskManager.App.Infrastructure;

namespace FastTaskManager.App.Models;

public sealed class ProcessListItem : ObservableObject
{
    private string _processName;
    private double _cpuUsagePercent;
    private long _workingSetBytes;
    private int _threadCount;
    private long? _handleCount;
    private DateTime? _startTime;
    private string _executablePath;

    public ProcessListItem(ProcessSnapshot snapshot)
    {
        ProcessId = snapshot.ProcessId;
        _processName = snapshot.ProcessName;
        _cpuUsagePercent = snapshot.CpuUsagePercent;
        _workingSetBytes = snapshot.WorkingSetBytes;
        _threadCount = snapshot.ThreadCount;
        _handleCount = snapshot.HandleCount;
        _startTime = snapshot.StartTime;
        _executablePath = snapshot.ExecutablePath;
    }

    public int ProcessId { get; }

    public string ProcessName
    {
        get => _processName;
        private set => SetProperty(ref _processName, value);
    }

    public double CpuUsagePercent
    {
        get => _cpuUsagePercent;
        private set
        {
            if (SetProperty(ref _cpuUsagePercent, value))
            {
                OnPropertyChanged(nameof(CpuUsageText));
            }
        }
    }

    public long WorkingSetBytes
    {
        get => _workingSetBytes;
        private set
        {
            if (SetProperty(ref _workingSetBytes, value))
            {
                OnPropertyChanged(nameof(MemoryUsageText));
            }
        }
    }

    public int ThreadCount
    {
        get => _threadCount;
        private set => SetProperty(ref _threadCount, value);
    }

    public long? HandleCount
    {
        get => _handleCount;
        private set
        {
            if (SetProperty(ref _handleCount, value))
            {
                OnPropertyChanged(nameof(HandleCountText));
            }
        }
    }

    public DateTime? StartTime
    {
        get => _startTime;
        private set
        {
            if (SetProperty(ref _startTime, value))
            {
                OnPropertyChanged(nameof(StartTimeText));
            }
        }
    }

    public string ExecutablePath
    {
        get => _executablePath;
        private set => SetProperty(ref _executablePath, value);
    }

    public string CpuUsageText => $"{CpuUsagePercent:F1}%";

    public string MemoryUsageText => $"{WorkingSetBytes / 1024d / 1024d:F1} MB";

    public string HandleCountText => HandleCount?.ToString() ?? "-";

    public string StartTimeText => StartTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";

    public void Update(ProcessSnapshot snapshot)
    {
        ProcessName = snapshot.ProcessName;

        var roundedCpu = Math.Round(snapshot.CpuUsagePercent, 1);
        if (Math.Abs(CpuUsagePercent - roundedCpu) >= 0.1d)
        {
            CpuUsagePercent = roundedCpu;
        }

        const long memoryDeltaThreshold = 256 * 1024;
        if (Math.Abs(WorkingSetBytes - snapshot.WorkingSetBytes) >= memoryDeltaThreshold)
        {
            WorkingSetBytes = snapshot.WorkingSetBytes;
        }

        if (ThreadCount != snapshot.ThreadCount)
        {
            ThreadCount = snapshot.ThreadCount;
        }

        if (HandleCount != snapshot.HandleCount)
        {
            HandleCount = snapshot.HandleCount;
        }

        if (StartTime != snapshot.StartTime)
        {
            StartTime = snapshot.StartTime;
        }

        if (!string.Equals(ExecutablePath, snapshot.ExecutablePath, StringComparison.Ordinal))
        {
            ExecutablePath = snapshot.ExecutablePath;
        }
    }
}
