using System.ServiceProcess;
using System.Text.RegularExpressions;
using FastTaskManager.App.Models;
using Microsoft.Win32;

namespace FastTaskManager.App.Services;

public sealed class WindowsServicesService
{
    private static readonly Regex ServiceGroupRegex = new(@"(?:^|\s)-k\s+(\S+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly Dictionary<string, ServiceMetadata> _metadataCache = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<WindowsServiceItem> GetServices()
    {
        var result = new List<WindowsServiceItem>();

        foreach (var controller in ServiceController.GetServices())
        {
            using (controller)
            {
                try
                {
                    var metadata = GetMetadata(controller.ServiceName);
                    var status = controller.Status;

                    result.Add(new WindowsServiceItem
                    {
                        Name = controller.ServiceName,
                        DisplayName = controller.DisplayName,
                        Description = metadata.Description,
                        GroupText = metadata.GroupText,
                        StartModeText = metadata.StartModeText,
                        StatusText = NormalizeState(status),
                        IsRunning = status == ServiceControllerStatus.Running,
                        ProcessId = null
                    });
                }
                catch
                {
                }
            }
        }

        return result
            .OrderByDescending(item => item.IsRunning)
            .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task StartServiceAsync(string serviceName)
    {
        using var controller = new ServiceController(serviceName);
        if (controller.Status == ServiceControllerStatus.Running)
            return;

        await Task.Run(() =>
        {
            controller.Start();
            controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
        });
    }

    public async Task StopServiceAsync(string serviceName)
    {
        using var controller = new ServiceController(serviceName);
        if (controller.Status == ServiceControllerStatus.Stopped)
            return;

        await Task.Run(() =>
        {
            controller.Stop();
            controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
        });
    }

    public async Task RestartServiceAsync(string serviceName)
    {
        using var controller = new ServiceController(serviceName);

        await Task.Run(() =>
        {
            if (controller.Status != ServiceControllerStatus.Stopped)
            {
                controller.Stop();
                controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
            }

            controller.Start();
            controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
        });
    }

    private ServiceMetadata GetMetadata(string serviceName)
    {
        if (_metadataCache.TryGetValue(serviceName, out var metadata))
            return metadata;

        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
        if (key is null)
            return _metadataCache[serviceName] = ServiceMetadata.Empty;

        var description = key.GetValue("Description")?.ToString() ?? string.Empty;
        var imagePath = key.GetValue("ImagePath")?.ToString() ?? string.Empty;
        var startType = key.GetValue("Start") switch
        {
            int value => NormalizeStartMode(value),
            _ => string.Empty
        };

        metadata = new ServiceMetadata(description, ParseServiceGroup(imagePath), startType);
        _metadataCache[serviceName] = metadata;
        return metadata;
    }

    private static string ParseServiceGroup(string? pathName)
    {
        if (string.IsNullOrWhiteSpace(pathName))
            return string.Empty;

        var match = ServiceGroupRegex.Match(pathName);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static string NormalizeState(ServiceControllerStatus state) => state switch
    {
        ServiceControllerStatus.Running => "正在运行",
        ServiceControllerStatus.Stopped => "已停止",
        ServiceControllerStatus.Paused => "已暂停",
        ServiceControllerStatus.StartPending => "正在启动",
        ServiceControllerStatus.StopPending => "正在停止",
        ServiceControllerStatus.PausePending => "正在暂停",
        ServiceControllerStatus.ContinuePending => "正在恢复",
        _ => "未知"
    };

    private static string NormalizeStartMode(int startMode) => startMode switch
    {
        2 => "自动",
        3 => "手动",
        4 => "禁用",
        _ => string.Empty
    };

    private readonly record struct ServiceMetadata(string Description, string GroupText, string StartModeText)
    {
        public static ServiceMetadata Empty => new(string.Empty, string.Empty, string.Empty);
    }
}
