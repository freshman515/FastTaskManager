using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using FastTaskManager.App.Models;

namespace FastTaskManager.App.Services;

public sealed class SystemTrayService
{
    private readonly Dictionary<string, string[]> _metadataNameCache = new(StringComparer.OrdinalIgnoreCase);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string? className, string? windowTitle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr address, nuint size, uint allocationType, uint protect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr address, nuint size, uint freeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr baseAddress, IntPtr buffer, nuint size, out nuint bytesRead);

    private const int TbButtonCount = 0x0418;
    private const int TbGetButton = 0x0417;

    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint MemRelease = 0x8000;
    private const uint PageReadWrite = 0x04;

    public IReadOnlySet<int> GetTrayProcessIds(IReadOnlyList<ProcessSnapshot> snapshots)
    {
        var result = new HashSet<int>();

        try
        {
            foreach (var toolbar in FindTrayToolbars())
            {
                foreach (var pid in ReadToolbarProcessIds(toolbar))
                    result.Add(pid);
            }
        }
        catch
        {
            return result;
        }

        foreach (var pid in MatchAutomationTrayProcesses(snapshots))
            result.Add(pid);

        return result;
    }

    private IEnumerable<int> MatchAutomationTrayProcesses(IReadOnlyList<ProcessSnapshot> snapshots)
    {
        var trayLabels = GetAutomationTrayLabels();
        if (trayLabels.Count == 0)
            yield break;

        foreach (var snapshot in snapshots)
        {
            var candidates = GetProcessCandidateNames(snapshot);
            if (candidates.Length == 0)
                continue;

            if (trayLabels.Any(label => candidates.Any(candidate => IsNameMatch(label, candidate))))
                yield return snapshot.ProcessId;
        }
    }

    private static bool IsNameMatch(string trayLabel, string candidate)
    {
        if (trayLabel.Length < 2 || candidate.Length < 2)
            return false;

        return trayLabel.Contains(candidate, StringComparison.OrdinalIgnoreCase)
               || candidate.Contains(trayLabel, StringComparison.OrdinalIgnoreCase);
    }

    private string[] GetProcessCandidateNames(ProcessSnapshot snapshot)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddCandidateName(names, snapshot.ProcessName);

        if (!string.IsNullOrWhiteSpace(snapshot.ExecutablePath) && snapshot.ExecutablePath != "访问受限")
        {
            AddCandidateName(names, Path.GetFileNameWithoutExtension(snapshot.ExecutablePath));

            if (!_metadataNameCache.TryGetValue(snapshot.ExecutablePath, out var metadataNames))
            {
                metadataNames = ReadMetadataNames(snapshot.ExecutablePath);
                _metadataNameCache[snapshot.ExecutablePath] = metadataNames;
            }

            foreach (var name in metadataNames)
                AddCandidateName(names, name);
        }

        return [.. names];
    }

    private static string[] ReadMetadataNames(string executablePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(executablePath);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddCandidateName(names, info.FileDescription);
            AddCandidateName(names, info.ProductName);
            AddCandidateName(names, info.CompanyName);
            return [.. names];
        }
        catch
        {
            return [];
        }
    }

    private static HashSet<string> GetAutomationTrayLabels()
    {
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var shellTray = FindWindow("Shell_TrayWnd", null);
            if (shellTray == IntPtr.Zero)
                return labels;

            var root = AutomationElement.FromHandle(shellTray);
            if (root is null)
                return labels;

            var buttons = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));

            foreach (AutomationElement button in buttons)
            {
                var className = button.Current.ClassName ?? string.Empty;
                if (!className.StartsWith("SystemTray.", StringComparison.OrdinalIgnoreCase))
                    continue;

                var label = NormalizeTrayLabel(button.Current.Name);
                if (string.IsNullOrWhiteSpace(label) || IsSystemTrayElement(label))
                    continue;

                labels.Add(label);
            }
        }
        catch
        {
            return labels;
        }

        return labels;
    }

    private static string NormalizeTrayLabel(string? rawLabel)
    {
        if (string.IsNullOrWhiteSpace(rawLabel))
            return string.Empty;

        var firstLine = rawLabel
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(firstLine))
            return string.Empty;

        return firstLine.Trim().TrimStart(':').Trim();
    }

    private static bool IsSystemTrayElement(string label)
    {
        return label.Equals("显示隐藏的图标", StringComparison.OrdinalIgnoreCase)
               || label.Equals("显示桌面", StringComparison.OrdinalIgnoreCase)
               || label.StartsWith("开始", StringComparison.OrdinalIgnoreCase)
               || label.StartsWith("网络 ", StringComparison.OrdinalIgnoreCase)
               || label.StartsWith("音量 ", StringComparison.OrdinalIgnoreCase)
               || label.StartsWith("电源 ", StringComparison.OrdinalIgnoreCase)
               || label.StartsWith("时钟 ", StringComparison.OrdinalIgnoreCase)
               || label.StartsWith("托盘输入指示器", StringComparison.OrdinalIgnoreCase)
               || label.StartsWith("剪贴板", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddCandidateName(ISet<string> names, string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return;

        var value = rawValue.Trim();
        if (value.Length < 2)
            return;

        names.Add(value);
    }

    private static IEnumerable<IntPtr> FindTrayToolbars()
    {
        var shellTray = FindWindow("Shell_TrayWnd", null);
        if (shellTray != IntPtr.Zero)
        {
            var trayNotify = FindWindowEx(shellTray, IntPtr.Zero, "TrayNotifyWnd", null);
            if (trayNotify != IntPtr.Zero)
            {
                var sysPager = FindWindowEx(trayNotify, IntPtr.Zero, "SysPager", null);
                var toolbar = sysPager != IntPtr.Zero
                    ? FindWindowEx(sysPager, IntPtr.Zero, "ToolbarWindow32", null)
                    : FindWindowEx(trayNotify, IntPtr.Zero, "ToolbarWindow32", null);

                if (toolbar != IntPtr.Zero)
                    yield return toolbar;
            }
        }

        var overflow = FindWindow("NotifyIconOverflowWindow", null);
        if (overflow != IntPtr.Zero)
        {
            var toolbar = FindWindowEx(overflow, IntPtr.Zero, "ToolbarWindow32", null);
            if (toolbar != IntPtr.Zero)
                yield return toolbar;
        }
    }

    private static IEnumerable<int> ReadToolbarProcessIds(IntPtr toolbarHandle)
    {
        GetWindowThreadProcessId(toolbarHandle, out var explorerPid);
        if (explorerPid == 0)
            yield break;

        var processHandle = OpenProcess(ProcessVmOperation | ProcessVmRead | ProcessQueryLimitedInformation, false, explorerPid);
        if (processHandle == IntPtr.Zero)
            yield break;

        var buttonSize = Marshal.SizeOf<TbButton>();
        var remoteBuffer = VirtualAllocEx(processHandle, IntPtr.Zero, (nuint)buttonSize, MemCommit | MemReserve, PageReadWrite);
        if (remoteBuffer == IntPtr.Zero)
        {
            CloseHandle(processHandle);
            yield break;
        }

        try
        {
            var buttonCount = SendMessage(toolbarHandle, TbButtonCount, IntPtr.Zero, IntPtr.Zero).ToInt32();
            for (var i = 0; i < buttonCount; i++)
            {
                if (SendMessage(toolbarHandle, TbGetButton, (IntPtr)i, remoteBuffer) == IntPtr.Zero)
                    continue;

                if (!TryReadStruct(processHandle, remoteBuffer, out TbButton button) || button.DwData == IntPtr.Zero)
                    continue;

                if (!TryReadStruct(processHandle, button.DwData, out TrayData trayData) || trayData.HWnd == IntPtr.Zero)
                    continue;

                GetWindowThreadProcessId(trayData.HWnd, out var pid);
                if (pid > 0)
                    yield return (int)pid;
            }
        }
        finally
        {
            VirtualFreeEx(processHandle, remoteBuffer, 0, MemRelease);
            CloseHandle(processHandle);
        }
    }

    private static bool TryReadStruct<T>(IntPtr processHandle, IntPtr address, out T value) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var localBuffer = Marshal.AllocHGlobal(size);

        try
        {
            if (!ReadProcessMemory(processHandle, address, localBuffer, (nuint)size, out var bytesRead) || bytesRead.ToUInt64() < (ulong)size)
            {
                value = default;
                return false;
            }

            value = Marshal.PtrToStructure<T>(localBuffer);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(localBuffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TbButton
    {
        public int IBitmap;
        public int IdCommand;
        public byte FsState;
        public byte FsStyle;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] BReserved;
        public IntPtr DwData;
        public IntPtr IString;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrayData
    {
        public IntPtr HWnd;
        public uint UId;
        public uint UCallbackMessage;
        public IntPtr Reserved0;
        public IntPtr Reserved1;
        public IntPtr HIcon;
    }
}
