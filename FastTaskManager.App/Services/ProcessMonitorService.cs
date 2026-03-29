using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FastTaskManager.App.Models;

namespace FastTaskManager.App.Services;

/// <summary>
/// 使用 NtQuerySystemInformation(SystemProcessInformation) 采集进程信息。
/// 一次内核调用返回所有进程的 CPU 时间、内存、线程数、句柄数，
/// 比 Process.GetProcesses() 逐个打开句柄快 10-50 倍。
/// </summary>
public sealed class ProcessMonitorService
{
    // ── P/Invoke ──────────────────────────────────────────────────────────────

    [DllImport("ntdll.dll")]
    private static extern uint NtQuerySystemInformation(
        uint SystemInformationClass,
        IntPtr SystemInformation,
        uint SystemInformationLength,
        out uint ReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

    [DllImport("ntdll.dll")]
    private static extern uint NtSuspendProcess(IntPtr hProcess);

    [DllImport("ntdll.dll")]
    private static extern uint NtResumeProcess(IntPtr hProcess);

    // ── 常量 ──────────────────────────────────────────────────────────────────

    private const uint SystemProcessInformation          = 5;
    private const uint STATUS_SUCCESS                    = 0x00000000;
    private const uint STATUS_INFO_LENGTH_MISMATCH       = 0xC0000004;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    // SYSTEM_PROCESS_INFORMATION (x64) 字段偏移
    // 全量文档见 ntpsapi.h / WinDbg "dt nt!_SYSTEM_PROCESS_INFORMATION"
    private const int Off_NextEntryOffset  =   0;   // ULONG
    private const int Off_NumberOfThreads  =   4;   // ULONG
    private const int Off_CreateTime       =  32;   // LARGE_INTEGER (100ns 单位)
    private const int Off_UserTime         =  40;   // LARGE_INTEGER
    private const int Off_KernelTime       =  48;   // LARGE_INTEGER
    private const int Off_ImageNameLength  =  56;   // USHORT  (UNICODE_STRING.Length，字节数)
    private const int Off_ImageNameBuffer  =  64;   // PWSTR   (UNICODE_STRING.Buffer，指向缓冲区内)
    private const int Off_UniqueProcessId  =  80;   // HANDLE  (实际为 32 位 PID)
    private const int Off_HandleCount      =  96;   // ULONG
    private const int Off_WorkingSetSize   = 144;   // SIZE_T

    // ── 状态 ─────────────────────────────────────────────────────────────────

    private Dictionary<int, CpuSample> _prevSamples = new();
    private readonly Dictionary<int, string> _pathCache = new();

    // ── 公开接口 ──────────────────────────────────────────────────────────────

    public IReadOnlyList<ProcessSnapshot> GetProcessesSnapshot()
    {
        var now = DateTime.UtcNow;
        var prevSamples = _prevSamples;     // 只读引用，在循环内安全访问

        var rawList = QueryAllProcesses();

        var snapshots  = new List<ProcessSnapshot>(rawList.Count);
        var newSamples = new Dictionary<int, CpuSample>(rawList.Count);
        var liveIds    = new HashSet<int>(rawList.Count);

        foreach (var r in rawList)
        {
            if (r.Pid == 0) continue; // System Idle Process：其 CPU% 表示空闲率，非占用，过滤掉

            var totalCpu = TimeSpan.FromTicks(r.UserTime + r.KernelTime);
            var cpu      = CalculateCpu(r.Pid, now, totalCpu, prevSamples);
            var path     = GetCachedPath(r.Pid);

            snapshots.Add(new ProcessSnapshot
            {
                ProcessId          = r.Pid,
                ProcessName        = r.Name,
                CpuUsagePercent    = cpu,
                TotalProcessorTime = totalCpu,
                WorkingSetBytes    = (long)r.WorkingSetSize,
                ThreadCount        = (int)r.ThreadCount,
                HandleCount        = (long)r.HandleCount,
                StartTime          = r.CreateTime > 0
                    ? DateTime.FromFileTimeUtc(r.CreateTime).ToLocalTime()
                    : null,
                ExecutablePath = path
            });

            newSamples[r.Pid] = new CpuSample(now, totalCpu);
            liveIds.Add(r.Pid);
        }

        _prevSamples = newSamples;

        foreach (var key in _pathCache.Keys.Where(id => !liveIds.Contains(id)).ToList())
            _pathCache.Remove(key);

        return snapshots
            .OrderByDescending(x => x.CpuUsagePercent)
            .ThenByDescending(x => x.WorkingSetBytes)
            .ThenBy(x => x.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void KillProcess(int processId, bool entireTree = true)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill(entireProcessTree: entireTree);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5) // ERROR_ACCESS_DENIED
        {
            throw new UnauthorizedAccessException($"拒绝访问进程 {processId}，请以管理员身份运行", ex);
        }
        catch (InvalidOperationException)
        {
            // 进程已退出，忽略
        }
        catch (ArgumentException)
        {
            // 找不到该 PID（进程已退出），忽略
        }
    }

    public void SuspendProcess(int processId)
    {
        const uint PROCESS_SUSPEND_RESUME = 0x0800;
        var handle = OpenProcess(PROCESS_SUSPEND_RESUME, false, (uint)processId);
        if (handle == IntPtr.Zero)
            throw new UnauthorizedAccessException($"拒绝访问进程 {processId}");
        try     { NtSuspendProcess(handle); }
        finally { CloseHandle(handle); }
    }

    public void ResumeProcess(int processId)
    {
        const uint PROCESS_SUSPEND_RESUME = 0x0800;
        var handle = OpenProcess(PROCESS_SUSPEND_RESUME, false, (uint)processId);
        if (handle == IntPtr.Zero)
            throw new UnauthorizedAccessException($"拒绝访问进程 {processId}");
        try     { NtResumeProcess(handle); }
        finally { CloseHandle(handle); }
    }

    public void SetProcessPriority(int processId, ProcessPriorityClass priority)
    {
        using var process = Process.GetProcessById(processId);
        process.PriorityClass = priority;
    }

    // ── 私有方法 ──────────────────────────────────────────────────────────────

    /// <summary>获取可执行文件完整路径（永久缓存，路径不会变化）。</summary>
    private string GetCachedPath(int pid)
    {
        if (_pathCache.TryGetValue(pid, out var cached)) return cached;

        // 这两个进程没有普通路径
        if (pid == 0) return _pathCache[0] = "System Idle Process";
        if (pid == 4) return _pathCache[4] = "ntoskrnl.exe";

        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (handle == IntPtr.Zero)
            return _pathCache[pid] = "访问受限";

        try
        {
            var sb   = new StringBuilder(1024);
            var size = (uint)sb.Capacity;
            return _pathCache[pid] = QueryFullProcessImageName(handle, 0, sb, ref size)
                ? sb.ToString(0, (int)size)
                : "访问受限";
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static double CalculateCpu(
        int pid, DateTime now, TimeSpan totalCpu,
        Dictionary<int, CpuSample> prev)
    {
        if (!prev.TryGetValue(pid, out var p)) return 0;
        var elapsedMs = (now - p.Time).TotalMilliseconds;
        if (elapsedMs <= 0) return 0;
        var cpuMs = (totalCpu - p.TotalCpu).TotalMilliseconds;
        if (cpuMs <= 0) return 0;
        return Math.Clamp(cpuMs / elapsedMs / Environment.ProcessorCount * 100d, 0d, 100d);
    }

    // ── NtQuerySystemInformation 核心 ─────────────────────────────────────────

    private static List<RawEntry> QueryAllProcesses()
    {
        var size = 1024 * 1024; // 初始 1 MB；700 进程约需 200-400 KB

        while (true)
        {
            var buf = Marshal.AllocHGlobal(size);
            var status = NtQuerySystemInformation(SystemProcessInformation, buf, (uint)size, out var needed);

            if (status == STATUS_INFO_LENGTH_MISMATCH)
            {
                Marshal.FreeHGlobal(buf);
                size = (int)needed + 65536;
                continue;
            }

            if (status != STATUS_SUCCESS)
            {
                Marshal.FreeHGlobal(buf);
                throw new InvalidOperationException($"NtQuerySystemInformation 失败: 0x{status:X8}");
            }

            try   { return ParseBuffer(buf); }
            finally { Marshal.FreeHGlobal(buf); }
        }
    }

    /// <summary>
    /// 遍历 SYSTEM_PROCESS_INFORMATION 链表，提取所需字段。
    /// 使用 Marshal.ReadXxx 安全读取非托管内存，无需 unsafe 块。
    /// </summary>
    private static List<RawEntry> ParseBuffer(IntPtr buf)
    {
        var result = new List<RawEntry>(512);
        var ptr = buf;

        while (true)
        {
            var nextOff    = (uint) Marshal.ReadInt32  (ptr, Off_NextEntryOffset);
            var threads    = (uint) Marshal.ReadInt32  (ptr, Off_NumberOfThreads);
            var createTime =        Marshal.ReadInt64  (ptr, Off_CreateTime);
            var userTime   =        Marshal.ReadInt64  (ptr, Off_UserTime);
            var kernelTime =        Marshal.ReadInt64  (ptr, Off_KernelTime);
            var nameLen    = (int)(ushort)Marshal.ReadInt16(ptr, Off_ImageNameLength);
            var nameBuf    =        Marshal.ReadIntPtr (ptr, Off_ImageNameBuffer);
            var pid        =        Marshal.ReadInt32  (ptr, Off_UniqueProcessId);
            var handles    = (uint) Marshal.ReadInt32  (ptr, Off_HandleCount);
            var ws         = unchecked((ulong)Marshal.ReadInt64(ptr, Off_WorkingSetSize));

            // 从缓冲区内的 UNICODE_STRING.Buffer 直接构造字符串
            string name;
            if (nameBuf != IntPtr.Zero && nameLen > 0)
            {
                name = Marshal.PtrToStringUni(nameBuf, nameLen / 2)!;
                // 去掉 .exe 后缀以匹配 Process.ProcessName 行为
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    name = name[..^4];
            }
            else
            {
                name = pid == 0 ? "Idle" : "System";
            }

            result.Add(new RawEntry(pid, name, threads, createTime, userTime, kernelTime, handles, ws));

            if (nextOff == 0) break;
            ptr = IntPtr.Add(ptr, (int)nextOff);
        }

        return result;
    }

    // ── 私有数据类型 ──────────────────────────────────────────────────────────

    private readonly record struct CpuSample(DateTime Time, TimeSpan TotalCpu);

    private readonly record struct RawEntry(
        int    Pid,
        string Name,
        uint   ThreadCount,
        long   CreateTime,
        long   UserTime,
        long   KernelTime,
        uint   HandleCount,
        ulong  WorkingSetSize);
}
