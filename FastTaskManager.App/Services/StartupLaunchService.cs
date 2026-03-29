using Microsoft.Win32;

namespace FastTaskManager.App.Services;

public sealed class StartupLaunchService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppValueName = "FastTaskManager";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        var value = key?.GetValue(AppValueName) as string;
        return !string.IsNullOrWhiteSpace(value);
    }

    public void SetEnabled(bool isEnabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("无法打开开机启动注册表项。");

        if (!isEnabled)
        {
            key.DeleteValue(AppValueName, false);
            return;
        }

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
            throw new InvalidOperationException("无法获取当前程序路径，无法设置开机自启动。");

        key.SetValue(AppValueName, $"\"{processPath}\"", RegistryValueKind.String);
    }
}
