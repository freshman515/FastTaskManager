using System.Collections.ObjectModel;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using FastTaskManager.App.Infrastructure;
using FastTaskManager.App.Models;

namespace FastTaskManager.App.ViewModels;

public sealed class PerformanceViewModel : ObservableObject
{
    private const int HistoryLength = 48;
    private const double ChartWidth = 640;
    private const double ChartHeight = 220;

    private readonly ObservableCollection<PerformanceMetricItem> _performanceMetrics = [];
    private readonly Dictionary<string, Queue<double>> _metricHistory = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _networkChartCeilings = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _cpuModel;
    private readonly string _cpuBaseSpeedText;
    private readonly string _gpuName;
    private readonly Dictionary<string, NetworkCounterSample> _previousNetworkSamples = new(StringComparer.OrdinalIgnoreCase);
    private PerformanceMetricItem? _selectedPerformanceMetric;

    public PerformanceViewModel()
    {
        _cpuModel = GetCpuModel();
        _cpuBaseSpeedText = GetCpuBaseSpeedText();
        _gpuName = GetGpuName();

        _performanceMetrics.Add(new PerformanceMetricItem("CPU", "CPU", CreateBrush("#00C2D8"), CreateBrush("#1400C2D8")));
        _performanceMetrics.Add(new PerformanceMetricItem("MEMORY", "内存", CreateBrush("#4AA3FF"), CreateBrush("#184AA3FF")));
        _performanceMetrics.Add(new PerformanceMetricItem("DISK", "磁盘 0 (C:)", CreateBrush("#8BC34A"), CreateBrush("#168BC34A")));
        _performanceMetrics.Add(new PerformanceMetricItem("GPU", "GPU 0", CreateBrush("#C77DFF"), CreateBrush("#18C77DFF")));

        SelectedPerformanceMetric = _performanceMetrics[0];
    }

    public IEnumerable<PerformanceMetricItem> PerformanceMetrics => _performanceMetrics;

    public PerformanceMetricItem? SelectedPerformanceMetric
    {
        get => _selectedPerformanceMetric;
        set => SetProperty(ref _selectedPerformanceMetric, value);
    }

    public void ApplyRealtimeSnapshot(IReadOnlyList<ProcessSnapshot> snapshots, string nowText)
    {
        var totalCpu = Math.Clamp(snapshots.Sum(x => x.CpuUsagePercent), 0d, 100d);
        var memory = GetMemoryStatus();
        var drive = GetSystemDriveSnapshot();
        var networks = GetNetworkSnapshots();
        SyncNetworkMetrics(networks);

        UpdateMetric(GetMetric("CPU"), totalCpu, _cpuModel, $"{totalCpu:F0}%", $"基准速度 {_cpuBaseSpeedText}",
            "逻辑处理器", Environment.ProcessorCount.ToString(), "系统运行时间", GetSystemUptimeText(), "最近更新", nowText);

        var availableMemoryGb = Math.Max(0, memory.TotalGb - memory.UsedGb);
        UpdateMetric(GetMetric("MEMORY"), memory.LoadPct, $"{memory.UsedGb:F1} / {memory.TotalGb:F1} GB", $"{memory.LoadPct}%", "物理内存",
            "已使用", $"{memory.UsedGb:F1} GB", "可用", $"{availableMemoryGb:F1} GB", "总计", $"{memory.TotalGb:F1} GB");

        UpdateMetric(GetMetric("DISK"), drive.UsedPercent, drive.Name, $"{drive.UsedPercent:F0}%", $"{drive.UsedGb:F0} / {drive.TotalGb:F0} GB",
            "已使用", $"{drive.UsedGb:F0} GB", "可用", $"{drive.FreeGb:F0} GB", "文件系统", drive.FileSystem);

        foreach (var network in networks)
        {
            UpdateMetric(GetMetric(network.Key), network.UsagePercent, network.Name, network.ThroughputText, network.TransferDetailText,
                "链路速度", network.LinkSpeedText, "发送 / 接收", network.TransferDetailText, "状态", network.StatusText);
        }

        UpdateMetric(GetMetric("GPU"), 0, _gpuName, "0%", "当前版本暂未接入 GPU 占用计数器",
            "适配器", _gpuName, "状态", "仅展示设备信息", "最近更新", nowText);
    }

    private void SyncNetworkMetrics(IReadOnlyList<NetworkSnapshot> networks)
    {
        var desiredKeys = new HashSet<string>(networks.Select(item => item.Key), StringComparer.OrdinalIgnoreCase);
        var selectedKey = SelectedPerformanceMetric?.Key;

        for (var index = _performanceMetrics.Count - 1; index >= 0; index--)
        {
            var metric = _performanceMetrics[index];
            if (!metric.Key.StartsWith("NETWORK:", StringComparison.OrdinalIgnoreCase))
                continue;

            if (desiredKeys.Contains(metric.Key))
                continue;

            _performanceMetrics.RemoveAt(index);
            _metricHistory.Remove(metric.Key);
            _networkChartCeilings.Remove(metric.Key);
        }

        var gpuIndex = _performanceMetrics
            .Select((metric, index) => (metric, index))
            .First(item => string.Equals(item.metric.Key, "GPU", StringComparison.OrdinalIgnoreCase))
            .index;

        for (var i = 0; i < networks.Count; i++)
        {
            var network = networks[i];
            var existingIndex = -1;
            for (var metricIndex = 0; metricIndex < _performanceMetrics.Count; metricIndex++)
            {
                if (!string.Equals(_performanceMetrics[metricIndex].Key, network.Key, StringComparison.OrdinalIgnoreCase))
                    continue;

                existingIndex = metricIndex;
                break;
            }

            if (existingIndex >= 0)
            {
                var targetIndex = Math.Min(gpuIndex + i, _performanceMetrics.Count - 1);
                if (existingIndex != targetIndex)
                    _performanceMetrics.Move(existingIndex, targetIndex);
                continue;
            }

            _performanceMetrics.Insert(gpuIndex + i, new PerformanceMetricItem(
                network.Key,
                network.AdapterTypeTitle,
                CreateBrush("#F06292"),
                CreateBrush("#18F06292")));
        }

        if (SelectedPerformanceMetric is null || !_performanceMetrics.Contains(SelectedPerformanceMetric))
            SelectedPerformanceMetric = _performanceMetrics.FirstOrDefault();
        else if (selectedKey is not null)
            SelectedPerformanceMetric = _performanceMetrics.FirstOrDefault(item => string.Equals(item.Key, selectedKey, StringComparison.OrdinalIgnoreCase)) ?? SelectedPerformanceMetric;
    }

    private void UpdateMetric(
        PerformanceMetricItem metric,
        double sampleValue,
        string subtitle,
        string valueText,
        string detailText,
        string primaryLabel,
        string primaryValue,
        string secondaryLabel,
        string secondaryValue,
        string tertiaryLabel,
        string tertiaryValue)
    {
        var history = GetHistory(metric.Key);
        history.Enqueue(Math.Clamp(sampleValue, 0, 100));
        while (history.Count > HistoryLength)
            history.Dequeue();

        var chartPoints = BuildChartPoints(history);
        var areaPoints = BuildAreaPoints(chartPoints);

        metric.Update(
            subtitle,
            valueText,
            detailText,
            primaryLabel,
            primaryValue,
            secondaryLabel,
            secondaryValue,
            tertiaryLabel,
            tertiaryValue,
            chartPoints,
            areaPoints);
    }

    private Queue<double> GetHistory(string key)
    {
        if (_metricHistory.TryGetValue(key, out var existing))
            return existing;

        var history = new Queue<double>(HistoryLength);
        for (var i = 0; i < HistoryLength; i++)
            history.Enqueue(0);

        _metricHistory[key] = history;
        return history;
    }

    private static PointCollection BuildChartPoints(IEnumerable<double> values)
    {
        var samples = values.ToList();
        if (samples.Count == 0)
            return [new Point(0, ChartHeight), new Point(ChartWidth, ChartHeight)];

        var step = samples.Count == 1 ? ChartWidth : ChartWidth / (samples.Count - 1);
        var points = new PointCollection(samples.Count);

        for (var i = 0; i < samples.Count; i++)
        {
            var x = step * i;
            var y = ChartHeight - (samples[i] / 100d * ChartHeight);
            points.Add(new Point(x, y));
        }

        return points;
    }

    private static PointCollection BuildAreaPoints(PointCollection chartPoints)
    {
        if (chartPoints.Count == 0)
            return [new Point(0, ChartHeight), new Point(ChartWidth, ChartHeight)];

        var points = new PointCollection(chartPoints.Count + 2) { new Point(0, ChartHeight) };
        foreach (var point in chartPoints)
            points.Add(point);
        points.Add(new Point(ChartWidth, ChartHeight));
        return points;
    }

    private PerformanceMetricItem GetMetric(string key) =>
        _performanceMetrics.First(metric => string.Equals(metric.Key, key, StringComparison.OrdinalIgnoreCase));

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private static (uint LoadPct, double UsedGb, double TotalGb) GetMemoryStatus()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status)) return (0, 0, 0);

        var totalGb = status.ullTotalPhys / 1024.0 / 1024.0 / 1024.0;
        var usedGb = (status.ullTotalPhys - status.ullAvailPhys) / 1024.0 / 1024.0 / 1024.0;
        return (status.dwMemoryLoad, usedGb, totalGb);
    }

    private static DriveSnapshot GetSystemDriveSnapshot()
    {
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        var drive = DriveInfo.GetDrives()
            .FirstOrDefault(item => item.IsReady && string.Equals(item.Name, systemRoot, StringComparison.OrdinalIgnoreCase));

        if (drive is null)
            return new DriveSnapshot("系统盘", "未知", 0, 0, 0, 0);

        var totalGb = drive.TotalSize / 1024d / 1024d / 1024d;
        var freeGb = drive.TotalFreeSpace / 1024d / 1024d / 1024d;
        var usedGb = totalGb - freeGb;
        var usedPercent = totalGb <= 0 ? 0 : usedGb / totalGb * 100d;
        return new DriveSnapshot($"系统盘 {drive.Name.TrimEnd('\\')}", drive.DriveFormat, usedPercent, usedGb, freeGb, totalGb);
    }

    private IReadOnlyList<NetworkSnapshot> GetNetworkSnapshots()
    {
        var now = DateTime.UtcNow;

        var interfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(item => item.NetworkInterfaceType != NetworkInterfaceType.Loopback
                           && item.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            .OrderBy(item => GetNetworkSortOrder(item))
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var activeIds = interfaces.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var staleId in _previousNetworkSamples.Keys.Where(id => !activeIds.Contains(id)).ToList())
            _previousNetworkSamples.Remove(staleId);

        if (interfaces.Count == 0)
        {
            return
            [
                new NetworkSnapshot("NETWORK:NONE", "网络", "未连接", "0 bps", "0 bps / 0 bps", "0 Mbps", "无活动接口", 0)
            ];
        }

        var result = new List<NetworkSnapshot>(interfaces.Count);
        var ethernetCounter = 0;
        for (var i = 0; i < interfaces.Count; i++)
        {
            var nic = interfaces[i];
            var stats = nic.GetIPStatistics();

            double sentPerSecond = 0;
            double receivedPerSecond = 0;

            if (_previousNetworkSamples.TryGetValue(nic.Id, out var previous))
            {
                var elapsedSeconds = (now - previous.Time).TotalSeconds;
                if (elapsedSeconds > 0)
                {
                    sentPerSecond = Math.Max(0, (stats.BytesSent - previous.BytesSent) / elapsedSeconds);
                    receivedPerSecond = Math.Max(0, (stats.BytesReceived - previous.BytesReceived) / elapsedSeconds);
                }
            }

            _previousNetworkSamples[nic.Id] = new NetworkCounterSample(nic.Id, stats.BytesSent, stats.BytesReceived, now);

            var totalBytesPerSecond = sentPerSecond + receivedPerSecond;
            var totalBitsPerSecond = totalBytesPerSecond * 8d;
            var chartCeilingBitsPerSecond = GetNetworkChartCeiling($"NETWORK:{nic.Id}", totalBitsPerSecond);
            var usagePercent = chartCeilingBitsPerSecond > 0
                ? Math.Clamp(totalBitsPerSecond / chartCeilingBitsPerSecond * 100d, 0d, 100d)
                : 0d;

            result.Add(new NetworkSnapshot(
                $"NETWORK:{nic.Id}",
                GetAdapterTypeTitle(nic, ref ethernetCounter),
                nic.Name,
                FormatBitRate(totalBitsPerSecond),
                $"{FormatBitRate(sentPerSecond * 8d)} / {FormatBitRate(receivedPerSecond * 8d)}",
                nic.Speed > 0 ? $"{nic.Speed / 1_000_000d:F0} Mbps" : "未知",
                GetNetworkStatusText(nic),
                usagePercent));
        }

        return result;
    }

    private static int GetNetworkSortOrder(NetworkInterface nic)
    {
        return nic.NetworkInterfaceType switch
        {
            NetworkInterfaceType.Ethernet => 0,
            NetworkInterfaceType.GigabitEthernet => 0,
            NetworkInterfaceType.Wireless80211 => 1,
            _ => 2
        };
    }

    private static string GetAdapterTypeTitle(NetworkInterface nic, ref int ethernetCounter)
    {
        if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            return "Wi-Fi";

        ethernetCounter++;
        return ethernetCounter == 1 ? "以太网" : $"以太网 {ethernetCounter - 1}";
    }

    private static string GetNetworkStatusText(NetworkInterface nic)
    {
        return nic.OperationalStatus switch
        {
            OperationalStatus.Up => "已连接",
            OperationalStatus.Dormant => "休眠",
            OperationalStatus.LowerLayerDown => "链路断开",
            OperationalStatus.NotPresent => "设备缺失",
            OperationalStatus.Down => "未连接",
            _ => nic.OperationalStatus.ToString()
        };
    }

    private double GetNetworkChartCeiling(string key, double currentBitsPerSecond)
    {
        const double minimumCeiling = 256 * 1024d;

        var desired = Math.Max(minimumCeiling, currentBitsPerSecond * 1.25d);
        if (!_networkChartCeilings.TryGetValue(key, out var existing))
            return _networkChartCeilings[key] = desired;

        var next = currentBitsPerSecond > existing
            ? Math.Max(desired, existing)
            : Math.Max(minimumCeiling, existing * 0.92d);

        if (desired > next)
            next = desired;

        return _networkChartCeilings[key] = next;
    }

    private static string FormatBitRate(double bitsPerSecond)
    {
        if (bitsPerSecond >= 1000 * 1000 * 1000)
            return $"{bitsPerSecond / 1000d / 1000d / 1000d:F2} Gbps";
        if (bitsPerSecond >= 1000 * 1000)
            return $"{bitsPerSecond / 1000d / 1000d:F2} Mbps";
        if (bitsPerSecond >= 1000)
            return $"{bitsPerSecond / 1000d:F0} Kbps";
        return $"{bitsPerSecond:F0} bps";
    }

    private static string GetSystemUptimeText()
    {
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        return uptime.TotalHours >= 24
            ? $"{(int)uptime.TotalDays} 天 {uptime.Hours} 小时"
            : $"{uptime.Hours} 小时 {uptime.Minutes} 分钟";
    }

    private static string GetCpuModel()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        return key?.GetValue("ProcessorNameString") as string ?? "处理器";
    }

    private static string GetCpuBaseSpeedText()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        if (key?.GetValue("~MHz") is int mhz && mhz > 0)
            return $"{mhz / 1000d:F2} GHz";
        return "未知";
    }

    private static string GetGpuName()
    {
        using var classKey = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");

        if (classKey is null)
            return "图形适配器";

        foreach (var subKeyName in classKey.GetSubKeyNames().OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            using var subKey = classKey.OpenSubKey(subKeyName);
            var name = subKey?.GetValue("DriverDesc") as string
                ?? subKey?.GetValue("HardwareInformation.AdapterString") as string;

            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }

        return "图形适配器";
    }

    private static SolidColorBrush CreateBrush(string colorHex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(colorHex)!;
        brush.Freeze();
        return brush;
    }

    private readonly record struct DriveSnapshot(
        string Name,
        string FileSystem,
        double UsedPercent,
        double UsedGb,
        double FreeGb,
        double TotalGb);

    private readonly record struct NetworkSnapshot(
        string Key,
        string AdapterTypeTitle,
        string Name,
        string ThroughputText,
        string TransferDetailText,
        string LinkSpeedText,
        string StatusText,
        double UsagePercent);

    private readonly record struct NetworkCounterSample(
        string InterfaceId,
        long BytesSent,
        long BytesReceived,
        DateTime Time);
}
