using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace FastTaskManager.App.Services;

public sealed class GlobalHotKeyService : IDisposable
{
    private const int WmHotKey = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private HwndSource? _source;
    private IntPtr _handle;
    private Func<Task>? _handler;
    private int _hotKeyId;

    public void Register(IntPtr handle, ModifierKeys modifiers, Key key, Func<Task> handler)
    {
        Dispose();

        _handle = handle;
        _handler = handler;
        _hotKeyId = GetHashCode() & 0x7FFF;

        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WndProc);

        if (!RegisterHotKey(handle, _hotKeyId, (uint)modifiers, (uint)KeyInterop.VirtualKeyFromKey(key)))
            throw new InvalidOperationException("注册全局快捷键失败。快捷键可能已被其他程序占用。");
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero && _hotKeyId != 0)
            UnregisterHotKey(_handle, _hotKeyId);

        if (_source is not null)
            _source.RemoveHook(WndProc);

        _source = null;
        _handle = IntPtr.Zero;
        _handler = null;
        _hotKeyId = 0;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey && wParam.ToInt32() == _hotKeyId && _handler is not null)
        {
            handled = true;
            _ = Application.Current.Dispatcher.InvokeAsync(async () => await _handler());
        }

        return IntPtr.Zero;
    }
}
