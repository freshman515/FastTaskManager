using System.Windows;
using FastTaskManager.App.Models;

namespace FastTaskManager.App.Services;

public sealed class ThemeService
{
    public AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;

    public event EventHandler<AppTheme>? ThemeChanged;

    public void ApplyTheme(AppTheme theme)
    {
        var resources = Application.Current?.Resources;
        if (resources is null)
            return;

        var mergedDictionaries = resources.MergedDictionaries;
        var existingThemeDictionaries = mergedDictionaries
            .Where(IsThemeDictionary)
            .ToList();

        foreach (var dictionary in existingThemeDictionaries)
            mergedDictionaries.Remove(dictionary);

        var themeDictionary = new ResourceDictionary
        {
            Source = new Uri(theme == AppTheme.Light ? "Themes/LightTheme.xaml" : "Themes/DarkTheme.xaml", UriKind.Relative)
        };
        mergedDictionaries.Add(themeDictionary);

        CurrentTheme = theme;
        ThemeChanged?.Invoke(this, theme);
    }

    private static bool IsThemeDictionary(ResourceDictionary dictionary)
    {
        var source = dictionary.Source?.OriginalString;
        return string.Equals(source, "Themes/DarkTheme.xaml", StringComparison.OrdinalIgnoreCase)
               || string.Equals(source, "Themes/LightTheme.xaml", StringComparison.OrdinalIgnoreCase);
    }
}
