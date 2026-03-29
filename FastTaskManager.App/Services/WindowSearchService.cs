using System.Runtime.InteropServices;
using System.Text;
using FastTaskManager.App.Models;

namespace FastTaskManager.App.Services;

public sealed class WindowSearchService
{
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

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private const uint GW_OWNER = 4;
    private const int SwRestore = 9;

    public IReadOnlyList<WindowSnapshot> GetVisibleWindows(IReadOnlyList<ProcessSnapshot> snapshots)
    {
        var snapshotMap = snapshots.ToDictionary(x => x.ProcessId);
        var windows = new List<WindowSnapshot>();
        var titleBuilder = new StringBuilder(512);

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
                return true;

            if (GetWindow(hWnd, GW_OWNER) != IntPtr.Zero)
                return true;

            titleBuilder.Clear();
            if (GetWindowText(hWnd, titleBuilder, titleBuilder.Capacity) == 0)
                return true;

            var title = titleBuilder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(title))
                return true;

            GetWindowThreadProcessId(hWnd, out var pidValue);
            if (pidValue == 0)
                return true;

            var pid = (int)pidValue;
            snapshotMap.TryGetValue(pid, out var snapshot);

            windows.Add(new WindowSnapshot
            {
                Handle = hWnd,
                ProcessId = pid,
                ProcessName = snapshot?.ProcessName ?? $"PID {pid}",
                Title = title,
                ExecutablePath = snapshot?.ExecutablePath ?? "访问受限"
            });

            return true;
        }, IntPtr.Zero);

        return windows
            .OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool ActivateWindow(nint windowHandle)
    {
        if (windowHandle == 0)
            return false;

        var handle = new IntPtr(windowHandle);
        if (IsIconic(handle))
            ShowWindowAsync(handle, SwRestore);

        return SetForegroundWindow(handle);
    }
}
