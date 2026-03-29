using System.IO;
using System.Text.Json;
using FastTaskManager.App.Models;

namespace FastTaskManager.App.Services;

public sealed class AppSettingsService
{
    private const int MinMainWindowWidth = 1100;
    private const int MinMainWindowHeight = 720;
    private const int MinWindowCoordinate = -10000;
    private const int MaxWindowCoordinate = 10000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsFilePath;

    public AppSettingsService()
    {
        var appDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FastTaskManager");
        _settingsFilePath = Path.Combine(appDirectory, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
                return Sanitize(new AppSettings());

            var json = File.ReadAllText(_settingsFilePath);
            return Sanitize(JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings());
        }
        catch
        {
            return Sanitize(new AppSettings());
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsFilePath)!);
        var sanitizedSettings = Sanitize(settings);
        var json = JsonSerializer.Serialize(sanitizedSettings, JsonOptions);
        File.WriteAllText(_settingsFilePath, json);
    }

    private static AppSettings Sanitize(AppSettings settings)
    {
        settings.MainWindowWidth = Math.Max(MinMainWindowWidth, settings.MainWindowWidth);
        settings.MainWindowHeight = Math.Max(MinMainWindowHeight, settings.MainWindowHeight);
        settings.MainWindowLeft = Math.Clamp(settings.MainWindowLeft, MinWindowCoordinate, MaxWindowCoordinate);
        settings.MainWindowTop = Math.Clamp(settings.MainWindowTop, MinWindowCoordinate, MaxWindowCoordinate);
        return settings;
    }
}
