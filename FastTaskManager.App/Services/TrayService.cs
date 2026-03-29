using Hardcodet.Wpf.TaskbarNotification;

namespace FastTaskManager.App.Services;

public sealed class TrayService
{
    public TaskbarIcon? Tray { get; set; }

    public void ShowBalloon(
        string title,
        string message,
        BalloonIcon icon = BalloonIcon.Info)
    {
        Tray?.ShowBalloonTip(title, message, icon);
    }
}
