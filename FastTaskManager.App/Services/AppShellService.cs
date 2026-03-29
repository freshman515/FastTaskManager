namespace FastTaskManager.App.Services;

public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error
}

public sealed record ToastNotification(string Message, ToastKind Kind);

public sealed class AppShellService
{
    public string CurrentStatusMessage { get; private set; } = "准备就绪";

    public event Action<string>? StatusMessageChanged;
    public event Action<ToastNotification>? ToastRequested;
    public event Func<Task>? RealtimeRefreshRequested;

    public void SetStatusMessage(string message)
    {
        CurrentStatusMessage = message;
        StatusMessageChanged?.Invoke(message);
    }

    public void ShowToast(string message, ToastKind kind = ToastKind.Info)
        => ToastRequested?.Invoke(new ToastNotification(message, kind));

    public Task RequestRealtimeRefreshAsync()
    {
        var handlers = RealtimeRefreshRequested;
        if (handlers is null)
            return Task.CompletedTask;

        return Task.WhenAll(handlers.GetInvocationList().Cast<Func<Task>>().Select(handler => handler()));
    }
}
