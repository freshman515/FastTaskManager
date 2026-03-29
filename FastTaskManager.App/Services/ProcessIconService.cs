using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FastTaskManager.App.Services;

public static class ProcessIconService
{
    private static readonly object _gate = new();
    private static readonly Dictionary<string, ImageSource?> _cache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _loading = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> _noIconPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "访问受限", "ntoskrnl.exe", "System Idle Process", ""
    };

    public static ImageSource? GetIcon(string? executablePath)
    {
        if (string.IsNullOrEmpty(executablePath) || _noIconPaths.Contains(executablePath))
            return null;

        lock (_gate)
        {
            if (_cache.TryGetValue(executablePath, out var cached))
                return cached;
        }

        var icon = LoadIcon(executablePath);
        lock (_gate)
            return _cache[executablePath] = icon;
    }

    public static ImageSource? GetCachedIcon(string? executablePath)
    {
        if (string.IsNullOrEmpty(executablePath) || _noIconPaths.Contains(executablePath))
            return null;

        lock (_gate)
            return _cache.TryGetValue(executablePath, out var cached) ? cached : null;
    }

    public static void EnsureIconLoadedAsync(string? executablePath, Action<string, ImageSource?> onLoaded)
    {
        if (string.IsNullOrEmpty(executablePath) || _noIconPaths.Contains(executablePath))
            return;

        lock (_gate)
        {
            if (_cache.TryGetValue(executablePath, out var cached))
            {
                onLoaded(executablePath, cached);
                return;
            }

            if (!_loading.Add(executablePath))
                return;
        }

        _ = Task.Run(() => LoadIcon(executablePath)).ContinueWith(task =>
        {
            var icon = task.Status == TaskStatus.RanToCompletion ? task.Result : null;

            lock (_gate)
            {
                _loading.Remove(executablePath);
                _cache[executablePath] = icon;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null)
                return;

            _ = dispatcher.InvokeAsync(() => onLoaded(executablePath, icon));
        }, TaskScheduler.Default);
    }

    private static ImageSource? LoadIcon(string executablePath)
    {
        ImageSource? icon = null;
        try
        {
            if (File.Exists(executablePath))
            {
                using var drawingIcon = System.Drawing.Icon.ExtractAssociatedIcon(executablePath);
                if (drawingIcon is not null)
                {
                    var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                        drawingIcon.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    bitmapSource.Freeze();
                    icon = bitmapSource;
                }
            }
        }
        catch
        {
        }

        return icon;
    }
}
