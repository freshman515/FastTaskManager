using System.Windows;
using System.Windows.Threading;
using FastTaskManager.App.Views;

namespace FastTaskManager.App.Services;

public sealed class DialogService
{
    public Task<bool> ShowConfirmationAsync(
        string title,
        string message,
        string primaryButtonText = "确定",
        string secondaryButtonText = "取消",
        bool isDanger = false)
    {
        return InvokeAsync(() =>
        {
            var dialog = new CustomDialogWindow(
                title,
                message,
                CustomDialogVariant.Warning,
                primaryButtonText,
                secondaryButtonText,
                isPrimaryDanger: isDanger)
            {
                Owner = GetOwnerWindow()
            };

            return dialog.ShowDialog() == true;
        });
    }

    public Task ShowErrorAsync(string title, string message, string primaryButtonText = "知道了")
    {
        return InvokeAsync(() =>
        {
            var dialog = new CustomDialogWindow(
                title,
                message,
                CustomDialogVariant.Error,
                primaryButtonText,
                secondaryButtonText: null,
                isPrimaryDanger: true)
            {
                Owner = GetOwnerWindow()
            };

            dialog.ShowDialog();
            return true;
        });
    }

    private static Task<T> InvokeAsync<T>(Func<T> action)
    {
        var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        if (dispatcher.CheckAccess())
            return Task.FromResult(action());

        return dispatcher.InvokeAsync(action).Task;
    }

    private static Window? GetOwnerWindow()
    {
        var application = Application.Current;
        if (application is null)
            return null;

        return application.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive)
            ?? application.MainWindow;
    }
}
