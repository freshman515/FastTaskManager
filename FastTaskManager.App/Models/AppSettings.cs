namespace FastTaskManager.App.Models;

public sealed class AppSettings
{
    public AppStartupMode StartupMode { get; set; } = AppStartupMode.MainWindow;

    public bool LaunchAtStartup { get; set; }

    public AppTheme Theme { get; set; } = AppTheme.Dark;

    public bool UseCustomMainWindowPlacement { get; set; }

    public bool MainWindowStartMaximized { get; set; } = true;

    public int MainWindowLeft { get; set; } = 120;

    public int MainWindowTop { get; set; } = 80;

    public int MainWindowWidth { get; set; } = 1440;

    public int MainWindowHeight { get; set; } = 900;
}
