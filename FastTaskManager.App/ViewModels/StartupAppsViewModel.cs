using FastTaskManager.App.Infrastructure;
using FastTaskManager.App.Models;
using FastTaskManager.App.Services;
using System.Diagnostics;
using System.Windows;

namespace FastTaskManager.App.ViewModels;

public sealed class StartupAppsViewModel : ObservableObject
{
    private readonly StartupAppsService _startupAppsService;
    private readonly DialogService _dialogService;
    private readonly RangeObservableCollection<StartupAppItem> _startupApps = [];
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly RelayCommand<StartupAppItem> _toggleStartupStateCommand;
    private readonly RelayCommand<StartupAppItem> _openStartupLocationCommand;
    private readonly RelayCommand<StartupAppItem> _copyStartupCommandCommand;

    private int _startupEnabledCount;
    private int _startupDisabledCount;
    private int _startupSourceCount;
    private string _startupLastUpdatedText = "-";
    private string _searchText = string.Empty;
    private bool _hasLoaded;
    private IReadOnlyList<StartupAppItem> _allStartupApps = [];

    public StartupAppsViewModel(StartupAppsService startupAppsService, DialogService dialogService)
    {
        _startupAppsService = startupAppsService;
        _dialogService = dialogService;
        _toggleStartupStateCommand = new RelayCommand<StartupAppItem>(item => _ = ToggleStartupStateAsync(item), item => item is not null);
        _openStartupLocationCommand = new RelayCommand<StartupAppItem>(item => _ = OpenStartupLocationAsync(item), item => item?.CanOpenLocation == true);
        _copyStartupCommandCommand = new RelayCommand<StartupAppItem>(item => SafeCopy(item?.CommandText), item => !string.IsNullOrWhiteSpace(item?.CommandText));
    }

    public IEnumerable<StartupAppItem> StartupApps => _startupApps;
    public bool HasLoaded => _hasLoaded;
    public RelayCommand<StartupAppItem> ToggleStartupStateCommand => _toggleStartupStateCommand;
    public RelayCommand<StartupAppItem> OpenStartupLocationCommand => _openStartupLocationCommand;
    public RelayCommand<StartupAppItem> CopyStartupCommandCommand => _copyStartupCommandCommand;

    public int StartupEnabledCount
    {
        get => _startupEnabledCount;
        private set => SetProperty(ref _startupEnabledCount, value);
    }

    public int StartupDisabledCount
    {
        get => _startupDisabledCount;
        private set => SetProperty(ref _startupDisabledCount, value);
    }

    public int StartupSourceCount
    {
        get => _startupSourceCount;
        private set => SetProperty(ref _startupSourceCount, value);
    }

    public string StartupLastUpdatedText
    {
        get => _startupLastUpdatedText;
        private set => SetProperty(ref _startupLastUpdatedText, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value)) return;
            ApplyFilter();
        }
    }

    public async Task RefreshAsync()
    {
        if (!await _refreshLock.WaitAsync(0)) return;

        try
        {
            var items = await Task.Run(() => _startupAppsService.GetStartupApps());
            foreach (var item in items)
                item.Icon = ProcessIconService.GetIcon(item.LocationPath);

            _allStartupApps = items;
            ApplyFilter();
            StartupEnabledCount = items.Count(item => item.IsEnabled);
            StartupDisabledCount = items.Count(item => !item.IsEnabled);
            StartupSourceCount = items.Select(item => item.SourceText).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            StartupLastUpdatedText = DateTime.Now.ToString("HH:mm:ss");
            _hasLoaded = true;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task ToggleStartupStateAsync(StartupAppItem? item)
    {
        if (item is null)
            return;

        try
        {
            await Task.Run(() => _startupAppsService.SetStartupAppEnabled(item, !item.IsEnabled));
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("操作失败", $"更新启动项状态失败：{ex.Message}");
        }
    }

    private async Task OpenStartupLocationAsync(StartupAppItem? item)
    {
        if (item?.CanOpenLocation != true)
            return;

        try
        {
            Process.Start("explorer.exe", $"/select,\"{item.LocationPath}\"");
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("操作失败", $"打开位置失败：{ex.Message}");
        }
    }

    private static void SafeCopy(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, copy: true);
                return;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Thread.Sleep(20);
            }
        }
    }

    private void ApplyFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(_searchText)
            ? _allStartupApps
            : _allStartupApps.Where(MatchesSearch).ToList();

        _startupApps.ReplaceAll(filtered);
    }

    private bool MatchesSearch(StartupAppItem item)
    {
        return item.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
               || item.Publisher.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
               || item.SourceText.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
               || item.CommandText.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
               || item.StatusText.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
    }
}
