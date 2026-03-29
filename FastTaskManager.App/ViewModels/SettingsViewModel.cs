using FastTaskManager.App.Infrastructure;
using FastTaskManager.App.Models;
using FastTaskManager.App.Services;

namespace FastTaskManager.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private const int DefaultMainWindowLeft = 120;
    private const int DefaultMainWindowTop = 80;
    private const int DefaultMainWindowWidth = 1440;
    private const int DefaultMainWindowHeight = 900;

    private readonly AppSettingsService _settingsService;
    private readonly ThemeService _themeService;
    private readonly StartupLaunchService _startupLaunchService;
    private readonly DialogService _dialogService;
    private readonly AppSettings _settings;
    private readonly RelayCommand _resetMainWindowPlacementCommand;

    public SettingsViewModel(
        AppSettingsService settingsService,
        ThemeService themeService,
        StartupLaunchService startupLaunchService,
        DialogService dialogService,
        AppSettings settings)
    {
        _settingsService = settingsService;
        _themeService = themeService;
        _startupLaunchService = startupLaunchService;
        _dialogService = dialogService;
        _settings = settings;
        _resetMainWindowPlacementCommand = new RelayCommand(ResetMainWindowPlacement);

        var launchAtStartup = _startupLaunchService.IsEnabled();
        if (_settings.LaunchAtStartup != launchAtStartup)
        {
            _settings.LaunchAtStartup = launchAtStartup;
            Save();
        }
    }

    public AppStartupMode StartupMode
    {
        get => _settings.StartupMode;
        set
        {
            if (_settings.StartupMode == value)
                return;

            _settings.StartupMode = value;
            Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMainWindowStartup));
            OnPropertyChanged(nameof(IsQuickLauncherStartup));
            OnPropertyChanged(nameof(StartupModeDescription));
        }
    }

    public AppTheme Theme
    {
        get => _settings.Theme;
        set
        {
            if (_settings.Theme == value)
                return;

            _settings.Theme = value;
            _themeService.ApplyTheme(value);
            Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDarkTheme));
            OnPropertyChanged(nameof(IsLightTheme));
            OnPropertyChanged(nameof(ThemeDescription));
        }
    }

    public bool LaunchAtStartup
    {
        get => _settings.LaunchAtStartup;
        set
        {
            if (_settings.LaunchAtStartup == value)
                return;

            try
            {
                _startupLaunchService.SetEnabled(value);
                _settings.LaunchAtStartup = value;
                Save();
                OnPropertyChanged();
                OnPropertyChanged(nameof(LaunchAtStartupDescription));
            }
            catch (Exception ex)
            {
                _ = _dialogService.ShowErrorAsync("设置失败", $"无法更新开机自启动设置。\n\n{ex.Message}");
            }
        }
    }

    public bool UseCustomMainWindowPlacement
    {
        get => _settings.UseCustomMainWindowPlacement;
        set
        {
            if (_settings.UseCustomMainWindowPlacement == value)
                return;

            _settings.UseCustomMainWindowPlacement = value;
            Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(MainWindowPlacementDescription));
        }
    }

    public bool MainWindowStartMaximized
    {
        get => _settings.MainWindowStartMaximized;
        set
        {
            if (_settings.MainWindowStartMaximized == value)
                return;

            _settings.MainWindowStartMaximized = value;
            Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(MainWindowPlacementDescription));
        }
    }

    public int MainWindowLeft
    {
        get => _settings.MainWindowLeft;
        set
        {
            if (_settings.MainWindowLeft == value)
                return;

            _settings.MainWindowLeft = value;
            Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(MainWindowPlacementDescription));
        }
    }

    public int MainWindowTop
    {
        get => _settings.MainWindowTop;
        set
        {
            if (_settings.MainWindowTop == value)
                return;

            _settings.MainWindowTop = value;
            Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(MainWindowPlacementDescription));
        }
    }

    public int MainWindowWidth
    {
        get => _settings.MainWindowWidth;
        set
        {
            if (_settings.MainWindowWidth == value)
                return;

            _settings.MainWindowWidth = value;
            Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(MainWindowPlacementDescription));
        }
    }

    public int MainWindowHeight
    {
        get => _settings.MainWindowHeight;
        set
        {
            if (_settings.MainWindowHeight == value)
                return;

            _settings.MainWindowHeight = value;
            Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(MainWindowPlacementDescription));
        }
    }

    public bool IsMainWindowStartup => StartupMode == AppStartupMode.MainWindow;
    public bool IsQuickLauncherStartup => StartupMode == AppStartupMode.QuickLauncherOnly;
    public bool IsDarkTheme => Theme == AppTheme.Dark;
    public bool IsLightTheme => Theme == AppTheme.Light;
    public RelayCommand ResetMainWindowPlacementCommand => _resetMainWindowPlacementCommand;

    public string StartupModeDescription => StartupMode == AppStartupMode.QuickLauncherOnly
        ? "启动后直接进入 QuickLauncher，不自动显示主窗口。"
        : "启动后显示完整主窗口，QuickLauncher 仍可通过热键打开。";

    public string ThemeDescription => Theme == AppTheme.Light
        ? "浅色主题适合明亮环境，所有页面和弹窗会同步切换。"
        : "深色主题保留当前视觉风格，降低夜间使用眩光。";

    public string LaunchAtStartupDescription => LaunchAtStartup
        ? "已启用开机自启动。登录 Windows 后会自动启动 FastTaskManager，并沿用当前启动方式设置。"
        : "当前未启用开机自启动。需要手动打开 FastTaskManager。";

    public string MainWindowPlacementDescription => UseCustomMainWindowPlacement
        ? $"启动时使用自定义窗口参数：位置 ({MainWindowLeft}, {MainWindowTop})，大小 {MainWindowWidth} x {MainWindowHeight}，默认{(MainWindowStartMaximized ? "最大化打开" : "普通窗口打开")}。"
        : $"未启用固定位置和大小，主窗口按系统默认方式打开，默认{(MainWindowStartMaximized ? "最大化显示" : "以普通窗口显示")}。";

    private void Save() => _settingsService.Save(_settings);

    private void ResetMainWindowPlacement()
    {
        _settings.MainWindowLeft = DefaultMainWindowLeft;
        _settings.MainWindowTop = DefaultMainWindowTop;
        _settings.MainWindowWidth = DefaultMainWindowWidth;
        _settings.MainWindowHeight = DefaultMainWindowHeight;
        _settings.MainWindowStartMaximized = true;
        _settings.UseCustomMainWindowPlacement = false;
        Save();
        OnPropertyChanged(nameof(UseCustomMainWindowPlacement));
        OnPropertyChanged(nameof(MainWindowStartMaximized));
        OnPropertyChanged(nameof(MainWindowLeft));
        OnPropertyChanged(nameof(MainWindowTop));
        OnPropertyChanged(nameof(MainWindowWidth));
        OnPropertyChanged(nameof(MainWindowHeight));
        OnPropertyChanged(nameof(MainWindowPlacementDescription));
    }
}
