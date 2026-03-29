using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using FastTaskManager.App.Models;

namespace FastTaskManager.App.Services;

public sealed class StartupAppsService
{
    private const string StartupApprovedRunPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string StartupApprovedFolderPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    public IReadOnlyList<StartupAppItem> GetStartupApps()
    {
        var approvalMap = BuildApprovalMap();
        var items = new Dictionary<string, StartupAppItem>(StringComparer.OrdinalIgnoreCase);

        LoadRegistryEntries(
            Registry.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            "注册表（当前用户）",
            StartupApprovedRunPath,
            approvalMap,
            items);

        LoadRegistryEntries(
            Registry.LocalMachine,
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            "注册表（本机）",
            StartupApprovedRunPath,
            approvalMap,
            items);

        LoadStartupFolderEntries(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            "启动文件夹（当前用户）",
            StartupApprovedFolderPath,
            approvalMap,
            items);

        LoadStartupFolderEntries(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
            "启动文件夹（所有用户）",
            StartupApprovedFolderPath,
            approvalMap,
            items);

        return items.Values
            .OrderByDescending(item => item.IsEnabled)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void SetStartupAppEnabled(StartupAppItem item, bool isEnabled)
    {
        ArgumentNullException.ThrowIfNull(item);

        var root = item.IsCurrentUser ? Registry.CurrentUser : Registry.LocalMachine;
        using var key = root.CreateSubKey(item.ApprovalRegistryPath, writable: true);
        if (key is null)
            throw new InvalidOperationException("无法打开启动项状态注册表。请确认当前账户具有足够权限。");

        var raw = key.GetValue(item.ApprovalValueName) as byte[];
        if (raw is null || raw.Length == 0)
            raw = new byte[12];

        raw[0] = isEnabled ? (byte)0x02 : (byte)0x03;
        key.SetValue(item.ApprovalValueName, raw, RegistryValueKind.Binary);
    }

    private static Dictionary<string, bool> BuildApprovalMap()
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        LoadApprovalEntries(Registry.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
            result);
        LoadApprovalEntries(Registry.LocalMachine,
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
            result);
        LoadApprovalEntries(Registry.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder",
            result);
        LoadApprovalEntries(Registry.LocalMachine,
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder",
            result);

        return result;
    }

    private static void LoadApprovalEntries(RegistryKey root, string path, IDictionary<string, bool> target)
    {
        using var key = root.OpenSubKey(path);
        if (key is null) return;

        foreach (var name in key.GetValueNames())
        {
            if (key.GetValue(name) is not byte[] raw || raw.Length == 0)
                continue;

            target[name] = raw[0] == 0x02;
        }
    }

    private static void LoadRegistryEntries(
        RegistryKey root,
        string path,
        string source,
        string approvalPath,
        IReadOnlyDictionary<string, bool> approvalMap,
        IDictionary<string, StartupAppItem> items)
    {
        using var key = root.OpenSubKey(path);
        if (key is null) return;

        foreach (var valueName in key.GetValueNames())
        {
            if (key.GetValue(valueName) is not string command || string.IsNullOrWhiteSpace(command))
                continue;

            var executablePath = TryExtractExecutablePath(command);
            var publisher = TryResolvePublisher(executablePath);
            var item = new StartupAppItem
            {
                Name = valueName,
                Publisher = publisher,
                IsEnabled = approvalMap.TryGetValue(valueName, out var enabled) ? enabled : true,
                StartupImpactText = "未测量",
                SourceText = source,
                CommandText = command,
                ApprovalRegistryPath = approvalPath,
                ApprovalValueName = valueName,
                LocationPath = executablePath ?? string.Empty,
                IsCurrentUser = ReferenceEquals(root, Registry.CurrentUser)
            };

            items[BuildKey(item.Name, item.CommandText)] = item;
        }
    }

    private static void LoadStartupFolderEntries(
        string folderPath,
        string source,
        string approvalPath,
        IReadOnlyDictionary<string, bool> approvalMap,
        IDictionary<string, StartupAppItem> items)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return;

        foreach (var file in Directory.EnumerateFiles(folderPath))
        {
            var fileName = Path.GetFileName(file);
            var name = Path.GetFileNameWithoutExtension(file);
            var publisher = TryResolvePublisher(file);

            var item = new StartupAppItem
            {
                Name = name,
                Publisher = publisher,
                IsEnabled = approvalMap.TryGetValue(fileName, out var enabledByFileName)
                    ? enabledByFileName
                    : approvalMap.TryGetValue(name, out var enabledByName) ? enabledByName : true,
                StartupImpactText = "未测量",
                SourceText = source,
                CommandText = file,
                ApprovalRegistryPath = approvalPath,
                ApprovalValueName = fileName,
                LocationPath = file,
                IsCurrentUser = string.Equals(folderPath, Environment.GetFolderPath(Environment.SpecialFolder.Startup), StringComparison.OrdinalIgnoreCase)
            };

            items[BuildKey(item.Name, item.CommandText)] = item;
        }
    }

    private static string BuildKey(string name, string command) => $"{name}|{command}";

    private static string TryResolvePublisher(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            return "未知";

        try
        {
            var companyName = FileVersionInfo.GetVersionInfo(executablePath).CompanyName;
            return string.IsNullOrWhiteSpace(companyName) ? "未知" : companyName;
        }
        catch
        {
            return "未知";
        }
    }

    private static string? TryExtractExecutablePath(string command)
    {
        var value = command.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (value[0] == '"')
        {
            var endQuote = value.IndexOf('"', 1);
            if (endQuote > 1)
                return value[1..endQuote];
        }

        var extensions = new[] { ".exe", ".bat", ".cmd", ".ps1", ".ahk" };
        foreach (var extension in extensions)
        {
            var idx = value.IndexOf(extension, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                return value[..(idx + extension.Length)];
        }

        var firstSegment = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return firstSegment;
    }
}
