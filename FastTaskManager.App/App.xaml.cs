using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using FastTaskManager.App.Models;
using FastTaskManager.App.Services;
using FastTaskManager.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FastTaskManager.App;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private MainWindow? _mainWindow;
    private QuickLauncherWindow? _quickLauncherWindow;
    private static readonly string CrashLogDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
    private static readonly string CrashLogPath = Path.Combine(CrashLogDirectory, "crash.log");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RegisterGlobalExceptionHandlers();

        _serviceProvider = ConfigureServices();

        var settings = _serviceProvider.GetRequiredService<AppSettings>();
        var themeService = _serviceProvider.GetRequiredService<ThemeService>();
        themeService.ApplyTheme(settings.Theme);

        _quickLauncherWindow = _serviceProvider.GetRequiredService<QuickLauncherWindow>();
        _mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        MainWindow = _mainWindow;

        if (settings.StartupMode == AppStartupMode.QuickLauncherOnly)
        {
            await _quickLauncherWindow.ShowLauncherAsync();
            return;
        }

        _mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    public void ExitFromTray()
    {
        _quickLauncherWindow?.CloseForShutdown();
        _mainWindow?.CloseForShutdown();
        Shutdown();
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<AppSettingsService>();
        services.AddSingleton(sp => sp.GetRequiredService<AppSettingsService>().Load());
        services.AddSingleton<ThemeService>();
        services.AddSingleton<AppShellService>();
        services.AddSingleton<WindowCoordinator>();
        services.AddSingleton<TrayService>();
        services.AddSingleton<DialogService>();
        services.AddSingleton<PrivilegeService>();
        services.AddSingleton<StartupLaunchService>();

        services.AddSingleton<ProcessMonitorService>();
        services.AddSingleton<ProcessCategoryService>();
        services.AddSingleton<WindowSearchService>();
        services.AddSingleton<StartupAppsService>();
        services.AddSingleton<SystemTrayService>();
        services.AddSingleton<WindowsServicesService>();

        services.AddSingleton<ProcessesViewModel>();
        services.AddSingleton<PerformanceViewModel>();
        services.AddSingleton<StartupAppsViewModel>();
        services.AddSingleton<ServicesViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<QuickLauncherViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        services.AddTransient<GlobalHotKeyService>();

        services.AddSingleton<QuickLauncherWindow>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }
    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            WriteCrashLog("DispatcherUnhandledException", args.Exception);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            WriteCrashLog("AppDomainUnhandledException", args.ExceptionObject as Exception, args.IsTerminating);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteCrashLog("TaskSchedulerUnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }

    private static void WriteCrashLog(string source, Exception? exception, bool isTerminating = false)
    {
        try
        {
            Directory.CreateDirectory(CrashLogDirectory);

            var builder = new StringBuilder();
            builder.AppendLine(new string('=', 80));
            builder.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            builder.AppendLine($"Source: {source}");
            builder.AppendLine($"IsTerminating: {isTerminating}");
            builder.AppendLine($"OS: {Environment.OSVersion}");
            builder.AppendLine($"Process: {Environment.ProcessPath}");
            builder.AppendLine($"ThreadId: {Environment.CurrentManagedThreadId}");

            if (exception is not null)
            {
                builder.AppendLine($"Exception: {exception.GetType().FullName}");
                builder.AppendLine($"Message: {exception.Message}");
                builder.AppendLine("StackTrace:");
                builder.AppendLine(exception.ToString());

                var inner = exception.InnerException;
                var depth = 1;
                while (inner is not null)
                {
                    builder.AppendLine($"InnerException[{depth}]: {inner.GetType().FullName}: {inner.Message}");
                    builder.AppendLine(inner.StackTrace ?? string.Empty);
                    inner = inner.InnerException;
                    depth++;
                }
            }
            else
            {
                builder.AppendLine("Exception: <null>");
            }

            File.AppendAllText(CrashLogPath, builder.ToString(), Encoding.UTF8);
        }
        catch
        {
        }
    }
}
