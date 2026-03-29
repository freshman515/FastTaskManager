namespace FastTaskManager.App.Models;

public enum ProcessCategory
{
    All,        // 全部
    App,        // 应用（有可见窗口）
    Background, // 后台进程
    Windows,    // Windows 系统进程
    Tray        // 系统托盘进程
}
