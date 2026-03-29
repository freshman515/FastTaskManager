using System.Runtime.InteropServices;
using FastTaskManager.App.Infrastructure;
using FastTaskManager.App.Models;
using FastTaskManager.App.Services;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace FastTaskManager.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

    private readonly AppShellService _appShellService;
    private readonly ProcessMonitorService _processMonitorService;
    private readonly ProcessCategoryService _processCategoryService;
    private readonly SystemTrayService _systemTrayService;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly RelayCommand<string> _navigateCommand;
    private readonly RelayCommand _toggleNavigationCommand;
    private readonly AsyncCommand _refreshCommand;

    private CancellationTokenSource? _refreshLoopCts;
    private AppSection _currentSection = AppSection.Processes;
    private object _currentPage = null!;
    private string _statusMessage = "准备就绪";
    private string _toastMessage = string.Empty;
    private Brush _toastAccentBrush = Brushes.DeepSkyBlue;
    private Brush _toastBadgeBackground = Brushes.Transparent;
    private Brush _toastBadgeBorderBrush = Brushes.Transparent;
    private bool _isNavigationCollapsed;
    private bool _isToastVisible;
    private CancellationTokenSource? _toastCts;

    public MainWindowViewModel(
        AppShellService appShellService,
        ProcessMonitorService processMonitorService,
        ProcessCategoryService processCategoryService,
        SystemTrayService systemTrayService,
        ProcessesViewModel processesPage,
        PerformanceViewModel performancePage,
        StartupAppsViewModel startupAppsPage,
        ServicesViewModel servicesPage,
        SettingsViewModel settingsPage)
    {
        _appShellService = appShellService;
        _processMonitorService = processMonitorService;
        _processCategoryService = processCategoryService;
        _systemTrayService = systemTrayService;

        ProcessesPage = processesPage;
        PerformancePage = performancePage;
        StartupAppsPage = startupAppsPage;
        ServicesPage = servicesPage;
        SettingsPage = settingsPage;
        _currentPage = ProcessesPage;

        _navigateCommand = new RelayCommand<string>(NavigateToSection);
        _toggleNavigationCommand = new RelayCommand(ToggleNavigation);
        _refreshCommand = new AsyncCommand(RefreshAllAsync);

        StatusMessage = _appShellService.CurrentStatusMessage;
        _appShellService.StatusMessageChanged += HandleStatusMessageChanged;
        _appShellService.ToastRequested += HandleToastRequested;
        _appShellService.RealtimeRefreshRequested += RefreshRealtimeAsync;
    }

    public ProcessesViewModel ProcessesPage { get; }
    public PerformanceViewModel PerformancePage { get; }
    public StartupAppsViewModel StartupAppsPage { get; }
    public ServicesViewModel ServicesPage { get; }
    public SettingsViewModel SettingsPage { get; }

    public RelayCommand<string> NavigateCommand => _navigateCommand;
    public RelayCommand ToggleNavigationCommand => _toggleNavigationCommand;
    public AsyncCommand RefreshCommand => _refreshCommand;
    public object CurrentPage => _currentPage;

    public bool IsNavigationCollapsed
    {
        get => _isNavigationCollapsed;
        private set
        {
            if (!SetProperty(ref _isNavigationCollapsed, value)) return;
            OnPropertyChanged(nameof(NavigationColumnWidth));
            OnPropertyChanged(nameof(NavigationSpacerWidth));
            OnPropertyChanged(nameof(NavigationPanelPadding));
            OnPropertyChanged(nameof(NavigationHeaderVisibility));
            OnPropertyChanged(nameof(NavigationBadgeVisibility));
            OnPropertyChanged(nameof(NavigationLabelVisibility));
            OnPropertyChanged(nameof(NavigationToggleIcon));
        }
    }

    public GridLength NavigationColumnWidth => IsNavigationCollapsed
        ? new GridLength(84)
        : new GridLength(228);

    public GridLength NavigationSpacerWidth => IsNavigationCollapsed
        ? new GridLength(12)
        : new GridLength(20);

    public Thickness NavigationPanelPadding => IsNavigationCollapsed
        ? new Thickness(10, 12, 10, 12)
        : new Thickness(12);

    public Visibility NavigationHeaderVisibility => IsNavigationCollapsed
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility NavigationBadgeVisibility => IsNavigationCollapsed
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility NavigationLabelVisibility => IsNavigationCollapsed
        ? Visibility.Collapsed
        : Visibility.Visible;

    public string NavigationToggleIcon => IsNavigationCollapsed ? "&#xE76B;" : "&#xE76C;";

    public AppSection CurrentSection
    {
        get => _currentSection;
        private set
        {
            if (!SetProperty(ref _currentSection, value)) return;
            _currentPage = value switch
            {
                AppSection.Performance => PerformancePage,
                AppSection.StartupApps => StartupAppsPage,
                AppSection.Services => ServicesPage,
                AppSection.Settings => SettingsPage,
                _ => ProcessesPage
            };
            OnPropertyChanged(nameof(IsProcessesPage));
            OnPropertyChanged(nameof(IsPerformancePage));
            OnPropertyChanged(nameof(IsStartupAppsPage));
            OnPropertyChanged(nameof(IsServicesPage));
            OnPropertyChanged(nameof(IsSettingsPage));
            OnPropertyChanged(nameof(CurrentSectionBadgeText));
            OnPropertyChanged(nameof(CurrentPage));
        }
    }

    public bool IsProcessesPage => CurrentSection == AppSection.Processes;
    public bool IsPerformancePage => CurrentSection == AppSection.Performance;
    public bool IsStartupAppsPage => CurrentSection == AppSection.StartupApps;
    public bool IsServicesPage => CurrentSection == AppSection.Services;
    public bool IsSettingsPage => CurrentSection == AppSection.Settings;

    public string CurrentSectionBadgeText => CurrentSection switch
    {
        AppSection.Performance => "性能",
        AppSection.StartupApps => "启动应用",
        AppSection.Services => "服务",
        AppSection.Settings => "设置",
        _ => "进程"
    };

    public void ShowSettings() => CurrentSection = AppSection.Settings;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string ToastMessage
    {
        get => _toastMessage;
        private set => SetProperty(ref _toastMessage, value);
    }

    public bool IsToastVisible
    {
        get => _isToastVisible;
        private set => SetProperty(ref _isToastVisible, value);
    }

    public Brush ToastAccentBrush
    {
        get => _toastAccentBrush;
        private set => SetProperty(ref _toastAccentBrush, value);
    }

    public Brush ToastBadgeBackground
    {
        get => _toastBadgeBackground;
        private set => SetProperty(ref _toastBadgeBackground, value);
    }

    public Brush ToastBadgeBorderBrush
    {
        get => _toastBadgeBorderBrush;
        private set => SetProperty(ref _toastBadgeBorderBrush, value);
    }

    public async Task InitializeAsync()
    {
        if (_refreshLoopCts is not null) return;

        _refreshLoopCts = new CancellationTokenSource();
        await RefreshAllAsync();
        _ = RunRefreshLoopAsync(_refreshLoopCts.Token);
    }

    public Task RefreshProcessesAsync() => RefreshRealtimeAsync();

    public async ValueTask DisposeAsync()
    {
        if (_refreshLoopCts is null) return;

        _appShellService.StatusMessageChanged -= HandleStatusMessageChanged;
        _appShellService.ToastRequested -= HandleToastRequested;
        _appShellService.RealtimeRefreshRequested -= RefreshRealtimeAsync;
        _toastCts?.Cancel();
        _toastCts?.Dispose();
        _refreshLoopCts.Cancel();
        _refreshLoopCts.Dispose();
        _refreshLoopCts = null;
        await Task.CompletedTask;
    }

    private async Task RunRefreshLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(RefreshInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await RefreshRealtimeAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshAllAsync()
    {
        await RefreshRealtimeAsync();
        await StartupAppsPage.RefreshAsync();

        if (ServicesPage.HasLoaded)
            _ = ServicesPage.RefreshAsync();
    }

    private async Task RefreshRealtimeAsync()
    {
        if (!await _refreshLock.WaitAsync(0)) return;

        try
        {
            _appShellService.SetStatusMessage("正在采集系统信息...");
            var start = DateTime.Now;
            var (snapshots, categoryMap, trayProcessIds) = await Task.Run(() =>
            {
                var snaps = _processMonitorService.GetProcessesSnapshot();
                var cats  = _processCategoryService.Classify(snaps);
                var tray  = _systemTrayService.GetTrayProcessIds(snaps);
                return (snaps, cats, tray);
            });
            var memory = GetMemoryStatus();
            var nowText = DateTime.Now.ToString("HH:mm:ss");

            await ProcessesPage.ApplyRealtimeSnapshotAsync(snapshots, categoryMap, trayProcessIds, memory, nowText);
            PerformancePage.ApplyRealtimeSnapshot(snapshots, nowText);

            var elapsedMs = (DateTime.Now - start).TotalMilliseconds;
            _appShellService.SetStatusMessage($"已刷新 {snapshots.Count} 个进程，耗时 {elapsedMs:F0} ms");
        }
        catch (Exception ex)
        {
            _appShellService.SetStatusMessage($"刷新失败：{ex.Message}");
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private void NavigateToSection(string? section)
    {
        if (!Enum.TryParse<AppSection>(section, true, out var parsed))
            return;

        CurrentSection = parsed;

        if (parsed == AppSection.StartupApps && !StartupAppsPage.HasLoaded)
            _ = StartupAppsPage.RefreshAsync();
    }

    private void ToggleNavigation() => IsNavigationCollapsed = !IsNavigationCollapsed;

    private void HandleStatusMessageChanged(string message) => StatusMessage = message;

    private void HandleToastRequested(ToastNotification notification)
    {
        _toastCts?.Cancel();
        _toastCts?.Dispose();
        _toastCts = new CancellationTokenSource();

        ToastMessage = notification.Message;
        ApplyToastStyle(notification.Kind);
        IsToastVisible = true;
        _ = HideToastAsync(_toastCts.Token);
    }

    private async Task HideToastAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2.2), ct);
            IsToastVisible = false;
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await Task.Delay(160, CancellationToken.None);
        if (!ct.IsCancellationRequested)
            ToastMessage = string.Empty;
    }

    private void ApplyToastStyle(ToastKind kind)
    {
        (ToastAccentBrush, ToastBadgeBackground, ToastBadgeBorderBrush) = kind switch
        {
            ToastKind.Success => (Brushes.MediumSeaGreen, CreateBrush("#1A163A27"), CreateBrush("#3348A36D")),
            ToastKind.Warning => (Brushes.DarkOrange, CreateBrush("#1AF59E0B"), CreateBrush("#33F59E0B")),
            ToastKind.Error => (Brushes.IndianRed, CreateBrush("#1AE11D48"), CreateBrush("#33E11D48")),
            _ => (Brushes.DeepSkyBlue, CreateBrush("#1A0EA5E9"), CreateBrush("#330EA5E9"))
        };
    }

    private static Brush CreateBrush(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private static (uint LoadPct, double UsedGb, double TotalGb) GetMemoryStatus()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status)) return (0, 0, 0);

        var totalGb = status.ullTotalPhys / 1024.0 / 1024.0 / 1024.0;
        var usedGb = (status.ullTotalPhys - status.ullAvailPhys) / 1024.0 / 1024.0 / 1024.0;
        return (status.dwMemoryLoad, usedGb, totalGb);
    }
}
