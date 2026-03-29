using System.Runtime.InteropServices;
using System.Text;
using FastTaskManager.App.Models;

namespace FastTaskManager.App.Services;

/// <summary>
/// 将进程快照分类为：应用、后台进程、Windows系统进程。
/// 可在后台线程调用。
/// </summary>
public sealed class ProcessCategoryService
{
    // ── P/Invoke ──────────────────────────────────────────────────────────────

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private const uint GW_OWNER = 4;

    // ── 常量 ──────────────────────────────────────────────────────────────────

    private static readonly string _windowsDir =
        Environment.GetFolderPath(Environment.SpecialFolder.Windows)
                   .TrimEnd('\\') + "\\";

    private static readonly HashSet<string> _knownSystemNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Registry", "Memory Compression", "Secure System",
            "smss", "csrss", "wininit", "winlogon", "lsass", "services",
            "ntoskrnl"
        };

    // ── 公开接口 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 对快照列表批量分类，返回 PID → ProcessCategory 映射。
    /// </summary>
    public IReadOnlyDictionary<int, ProcessCategory> Classify(IReadOnlyList<ProcessSnapshot> snapshots)
    {
        var appPids = GetVisibleWindowPids();
        var result  = new Dictionary<int, ProcessCategory>(snapshots.Count);

        foreach (var s in snapshots)
            result[s.ProcessId] = DetermineCategory(s, appPids);

        return result;
    }

    // ── 私有方法 ──────────────────────────────────────────────────────────────

    private static HashSet<int> GetVisibleWindowPids()
    {
        var pids = new HashSet<int>();
        var sb   = new StringBuilder(256);

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            // 跳过被其他窗口拥有的顶层窗口（如工具窗口、弹出菜单）
            if (GetWindow(hWnd, GW_OWNER) != IntPtr.Zero) return true;
            sb.Clear();
            if (GetWindowText(hWnd, sb, sb.Capacity) == 0) return true;

            GetWindowThreadProcessId(hWnd, out var pid);
            if (pid != 0) pids.Add((int)pid);
            return true;
        }, IntPtr.Zero);

        return pids;
    }

    private static ProcessCategory DetermineCategory(ProcessSnapshot s, HashSet<int> appPids)
    {
        if (IsWindowsProcess(s)) return ProcessCategory.Windows;
        if (appPids.Contains(s.ProcessId)) return ProcessCategory.App;
        return ProcessCategory.Background;
    }

    private static bool IsWindowsProcess(ProcessSnapshot s)
    {
        if (s.ProcessId <= 4) return true;
        if (_knownSystemNames.Contains(s.ProcessName)) return true;

        var path = s.ExecutablePath;
        return !string.IsNullOrEmpty(path)
               && path != "访问受限"
               && path.StartsWith(_windowsDir, StringComparison.OrdinalIgnoreCase);
    }
}
