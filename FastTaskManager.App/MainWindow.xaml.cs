using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using FastTaskManager.App.Models;
using FastTaskManager.App.Services;
using FastTaskManager.App.ViewModels;
using Hardcodet.Wpf.TaskbarNotification;

namespace FastTaskManager.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly QuickLauncherWindow _quickLauncherWindow;
    private readonly GlobalHotKeyService _globalHotKeyService;
    private readonly WindowCoordinator _windowCoordinator;
    private readonly TaskbarIcon _tray;
    private readonly AppSettings _appSettings;
    private readonly bool _registerHotKey;
    private readonly bool _keepRunningWhenClosed;
    private bool _allowClose;

    public MainWindow(
        MainWindowViewModel viewModel,
        QuickLauncherWindow quickLauncherWindow,
        GlobalHotKeyService globalHotKeyService,
        WindowCoordinator windowCoordinator,
        TrayService trayService,
        AppSettings appSettings)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _quickLauncherWindow = quickLauncherWindow;
        _globalHotKeyService = globalHotKeyService;
        _windowCoordinator = windowCoordinator;
        _appSettings = appSettings;
        _tray = (TaskbarIcon)FindResource("MyNotifyIcon");
        trayService.Tray = _tray;
        _tray.TrayMouseDoubleClick += Tray_TrayMouseDoubleClick;
        _registerHotKey = appSettings.StartupMode == AppStartupMode.MainWindow;
        _keepRunningWhenClosed = appSettings.StartupMode == AppStartupMode.QuickLauncherOnly;
        ApplyStartupWindowSettings();
        DataContext = viewModel;
        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_registerHotKey)
            _quickLauncherWindow.Owner = this;

        await _viewModel.InitializeAsync();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (!_registerHotKey)
            return;

        var helper = new WindowInteropHelper(this);
        _globalHotKeyService.Register(helper.Handle, ModifierKeys.Control, Key.Space, ToggleQuickLauncherAsync);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_keepRunningWhenClosed && !_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    public void CloseForShutdown()
    {
        _allowClose = true;
        Close();
    }

    private async Task ToggleQuickLauncherAsync()
    {
        if (_quickLauncherWindow.IsVisible)
        {
            _quickLauncherWindow.HideLauncher();
            return;
        }

        await _quickLauncherWindow.ShowLauncherAsync();
    }

    private void ApplyStartupWindowSettings()
    {
        WindowState = WindowState.Normal;

        if (_appSettings.UseCustomMainWindowPlacement)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = _appSettings.MainWindowLeft;
            Top = _appSettings.MainWindowTop;
            Width = _appSettings.MainWindowWidth;
            Height = _appSettings.MainWindowHeight;
        }

        if (_appSettings.MainWindowStartMaximized)
            WindowState = WindowState.Maximized;
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        _tray.TrayMouseDoubleClick -= Tray_TrayMouseDoubleClick;
        _tray.Dispose();
        _globalHotKeyService.Dispose();
        _quickLauncherWindow.CloseForShutdown();
        await _viewModel.DisposeAsync();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => SystemCommands.MinimizeWindow(this);

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(this);
        else
            SystemCommands.MaximizeWindow(this);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => SystemCommands.CloseWindow(this);

    private void Tray_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
        => ShowWindow();

    private void ShowWindow_Click(object sender, RoutedEventArgs e)
        => ShowWindow();

    private void Exit_Click(object sender, RoutedEventArgs e)
        => _windowCoordinator.ShutdownApplication();

    private void ShowWindow()
        => _windowCoordinator.ShowMainWindow();

}
