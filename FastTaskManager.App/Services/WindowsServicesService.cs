using System.ServiceProcess;
using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Management;
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
                        IsPaused = status == ServiceControllerStatus.Paused,
                        CanPauseAndContinue = controller.CanPauseAndContinue,
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
        try
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
        catch (Exception ex)
        {
            throw CreateServiceOperationException("启动", serviceName, ex);
        }
    }

    public ServiceStartDiagnostic DiagnoseStartFailure(string serviceName)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return DiagnoseStartFailureCore(serviceName, visited);
    }

    public async Task SetStartModeAsync(string serviceName, ServiceStartMode startMode)
    {
        await Task.Run(() =>
        {
            using var service = new ManagementObject($"Win32_Service.Name='{serviceName}'");
            var targetMode = startMode switch
            {
                ServiceStartMode.Automatic => "Automatic",
                ServiceStartMode.Disabled => "Disabled",
                _ => "Manual"
            };

            var result = Convert.ToUInt32(service.InvokeMethod("ChangeStartMode", [targetMode]) ?? 1u);
            if (result != 0)
                throw new InvalidOperationException($"修改服务 \"{serviceName}\" 的启动类型失败，错误码：{result}。");

            _metadataCache.Remove(serviceName);
        });
    }

    public async Task StopServiceAsync(string serviceName)
    {
        try
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
        catch (Exception ex)
        {
            throw CreateServiceOperationException("停止", serviceName, ex);
        }
    }

    public async Task RestartServiceAsync(string serviceName)
    {
        try
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
        catch (Exception ex)
        {
            throw CreateServiceOperationException("重启", serviceName, ex);
        }
    }

    public async Task PauseServiceAsync(string serviceName)
    {
        try
        {
            using var controller = new ServiceController(serviceName);
            if (!controller.CanPauseAndContinue || controller.Status == ServiceControllerStatus.Paused)
                return;

            await Task.Run(() =>
            {
                controller.Pause();
                controller.WaitForStatus(ServiceControllerStatus.Paused, TimeSpan.FromSeconds(15));
            });
        }
        catch (Exception ex)
        {
            throw CreateServiceOperationException("暂停", serviceName, ex);
        }
    }

    public async Task ResumeServiceAsync(string serviceName)
    {
        try
        {
            using var controller = new ServiceController(serviceName);
            if (!controller.CanPauseAndContinue || controller.Status == ServiceControllerStatus.Running)
                return;

            await Task.Run(() =>
            {
                controller.Continue();
                controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
            });
        }
        catch (Exception ex)
        {
            throw CreateServiceOperationException("恢复", serviceName, ex);
        }
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

    private static Exception CreateServiceOperationException(string operation, string serviceName, Exception exception)
    {
        if (TryGetWin32ErrorCode(exception, out var errorCode))
        {
            return errorCode switch
            {
                5 => new UnauthorizedAccessException($"无法{operation}服务 \"{serviceName}\"，当前进程没有管理员权限。请以管理员身份重新启动应用后再试。", exception),
                1058 => new InvalidOperationException($"无法{operation}服务 \"{serviceName}\"，该服务已被禁用。", exception),
                1060 => new InvalidOperationException($"无法{operation}服务 \"{serviceName}\"，系统中找不到该服务。", exception),
                _ => new InvalidOperationException($"无法{operation}服务 \"{serviceName}\"：{exception.Message}", exception)
            };
        }

        return new InvalidOperationException($"无法{operation}服务 \"{serviceName}\"：{exception.Message}", exception);
    }

    private static bool TryGetWin32ErrorCode(Exception exception, out int errorCode)
    {
        if (exception is Win32Exception win32Exception)
        {
            errorCode = win32Exception.NativeErrorCode;
            return true;
        }

        if (exception.InnerException is not null)
            return TryGetWin32ErrorCode(exception.InnerException, out errorCode);

        errorCode = 0;
        return false;
    }

    private ServiceStartDiagnostic DiagnoseStartFailureCore(string serviceName, HashSet<string> visited)
    {
        if (!visited.Add(serviceName))
            return ServiceStartDiagnostic.Empty;

        using var controller = new ServiceController(serviceName);
        var metadata = GetMetadata(serviceName);
        var disabledDependencies = new List<DisabledDependency>();

        foreach (var dependency in controller.ServicesDependedOn)
        {
            using (dependency)
            {
                var dependencyDiagnostic = DiagnoseStartFailureCore(dependency.ServiceName, visited);
                if (dependencyDiagnostic.IsServiceDisabled)
                {
                    disabledDependencies.Add(new DisabledDependency(
                        dependency.ServiceName,
                        SafeGetDisplayName(dependency),
                        dependencyDiagnostic.ServiceStartModeText));
                }

                if (dependencyDiagnostic.DisabledDependencies.Count > 0)
                    disabledDependencies.AddRange(dependencyDiagnostic.DisabledDependencies);
            }
        }

        return new ServiceStartDiagnostic(
            serviceName,
            SafeGetDisplayName(controller),
            string.Equals(metadata.StartModeText, "禁用", StringComparison.Ordinal),
            metadata.StartModeText,
            disabledDependencies
                .DistinctBy(item => item.ServiceName, StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    private static string SafeGetDisplayName(ServiceController controller)
    {
        try
        {
            return controller.DisplayName;
        }
        catch
        {
            return controller.ServiceName;
        }
    }

    private readonly record struct ServiceMetadata(string Description, string GroupText, string StartModeText)
    {
        public static ServiceMetadata Empty => new(string.Empty, string.Empty, string.Empty);
    }

    public sealed record ServiceStartDiagnostic(
        string ServiceName,
        string DisplayName,
        bool IsServiceDisabled,
        string ServiceStartModeText,
        IReadOnlyList<DisabledDependency> DisabledDependencies)
    {
        public static ServiceStartDiagnostic Empty => new(string.Empty, string.Empty, false, string.Empty, []);
    }

    public sealed record DisabledDependency(string ServiceName, string DisplayName, string StartModeText);

    public enum ServiceStartMode
    {
        Manual,
        Automatic,
        Disabled
    }
}
