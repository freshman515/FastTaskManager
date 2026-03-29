using System.Windows;
using System.Windows.Controls;
using FastTaskManager.App.Models;
using FastTaskManager.App.ViewModels;

namespace FastTaskManager.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private SettingsViewModel? ViewModel => DataContext as SettingsViewModel;

    private void MainWindowStartup_Checked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
            ViewModel.StartupMode = AppStartupMode.MainWindow;
    }

    private void QuickLauncherStartup_Checked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
            ViewModel.StartupMode = AppStartupMode.QuickLauncherOnly;
    }

    private void DarkTheme_Checked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
            ViewModel.Theme = AppTheme.Dark;
    }

    private void LightTheme_Checked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
            ViewModel.Theme = AppTheme.Light;
    }
}
