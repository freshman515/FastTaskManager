using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using FastTaskManager.App.ViewModels;

namespace FastTaskManager.App.Services;

public sealed class WindowCoordinator
{
    private readonly IServiceProvider _serviceProvider;

    public WindowCoordinator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void ShowSettingsWindow()
    {
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        var mainWindowViewModel = _serviceProvider.GetRequiredService<MainWindowViewModel>();

        mainWindowViewModel.ShowSettings();

        ShowMainWindow();
    }

    public void ShowMainWindow()
    {
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();

        if (!mainWindow.IsVisible)
            mainWindow.Show();

        if (mainWindow.WindowState == WindowState.Minimized)
            mainWindow.WindowState = WindowState.Normal;

        mainWindow.Activate();
    }

    public async Task ShowQuickLauncherAsync()
    {
        var quickLauncherWindow = _serviceProvider.GetRequiredService<QuickLauncherWindow>();
        await quickLauncherWindow.ShowLauncherAsync();
    }

    public void ShutdownApplication()
    {
        if (Application.Current is App app)
        {
            app.ExitFromTray();
            return;
        }

        Application.Current.Shutdown();
    }
}
