using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FastTaskManager.App.Models;
using FastTaskManager.App.Services;
using FastTaskManager.App.ViewModels;

namespace FastTaskManager.App;

public partial class QuickLauncherWindow : Window
{
    private bool _allowClose;
    private readonly WindowCoordinator _windowCoordinator;
    private readonly bool _manageOwnHotKey;
    private readonly GlobalHotKeyService _globalHotKeyService;

    public QuickLauncherWindow(
        QuickLauncherViewModel viewModel,
        WindowCoordinator windowCoordinator,
        GlobalHotKeyService globalHotKeyService,
        AppSettings appSettings)
    {
        InitializeComponent();
        DataContext = viewModel;
        _windowCoordinator = windowCoordinator;
        _globalHotKeyService = globalHotKeyService;
        _manageOwnHotKey = appSettings.StartupMode == AppStartupMode.QuickLauncherOnly;
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
    }

    private QuickLauncherViewModel ViewModel => (QuickLauncherViewModel)DataContext;

    public async Task ShowLauncherAsync()
    {
        await ViewModel.OpenAsync();
        var previousOpacity = Opacity;
        Opacity = 0;
        Show();
        UpdateLayout();
        PositionWindow();
        Opacity = previousOpacity;
        Activate();
        SearchTextBox.Focus();
        SearchTextBox.SelectAll();
    }

    public void HideLauncher()
    {
        ViewModel.Close();
        Hide();
    }

    public void CloseForShutdown()
    {
        _allowClose = true;
        Close();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (!_manageOwnHotKey)
            return;

        var helper = new System.Windows.Interop.WindowInteropHelper(this);
        _globalHotKeyService.Register(helper.Handle, ModifierKeys.Control, Key.Space, ToggleLauncherAsync);
    }

    private async Task ToggleLauncherAsync()
    {
        if (IsVisible)
        {
            HideLauncher();
            return;
        }

        await ShowLauncherAsync();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            HideLauncher();
            return;
        }

        base.OnClosing(e);
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (HandleNavigationKey(e))
            return;

        switch (e.Key)
        {
            case Key.Escape:
                HideLauncher();
                e.Handled = true;
                break;
        }
    }

    private void SearchTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        HandleNavigationKey(e);
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (IsVisible)
            HideLauncher();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        HideLauncher();
        _windowCoordinator.ShowSettingsWindow();
    }

    private void ResultItem_RightClickSelect(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item)
            item.IsSelected = true;
    }

    private void ResultItem_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem { DataContext: ProcessDisplayItem })
            return;

        ViewModel.ExecuteSelectedCommand.Execute(null);
        HideLauncher();
    }

    private void PositionWindow()
    {
        var workArea = SystemParameters.WorkArea;
        var windowWidth = ActualWidth > 0 ? ActualWidth : Width;
        var windowHeight = ActualHeight > 0 ? ActualHeight : Math.Min(MaxHeight, 360d);
        var centeredLeft = workArea.Left + (workArea.Width - windowWidth) / 2;
        var centeredTop = workArea.Top + (workArea.Height - windowHeight) / 2;
        var targetTop = centeredTop - 40;

        Left = Math.Max(workArea.Left, centeredLeft);
        Top = Math.Clamp(targetTop, workArea.Top + 36, workArea.Bottom - windowHeight - 36);
    }

    private bool HandleNavigationKey(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                ViewModel.SelectNext();
                ScrollSelectedResultIntoView();
                e.Handled = true;
                return true;
            case Key.Up:
                ViewModel.SelectPrevious();
                ScrollSelectedResultIntoView();
                e.Handled = true;
                return true;
            case Key.K when ViewModel.SelectedProcess is not null
                            && Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                if (ViewModel.KillSelectedProcessCommand.CanExecute(null))
                    ViewModel.KillSelectedProcessCommand.Execute(null);

                HideLauncher();
                e.Handled = true;
                return true;
            case Key.Enter when ViewModel.SelectedProcess is not null:
                ViewModel.ExecuteSelectedCommand.Execute(null);
                HideLauncher();
                e.Handled = true;
                return true;
            default:
                return false;
        }
    }

    private void ScrollSelectedResultIntoView()
    {
        if (ViewModel.SelectedProcess is not null)
            ResultsListBox.ScrollIntoView(ViewModel.SelectedProcess);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _globalHotKeyService.Dispose();
    }
}
