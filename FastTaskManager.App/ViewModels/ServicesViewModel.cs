using FastTaskManager.App.Infrastructure;
using FastTaskManager.App.Models;
using FastTaskManager.App.Services;

namespace FastTaskManager.App.ViewModels;

public sealed class ServicesViewModel : ObservableObject
{
    private readonly WindowsServicesService _servicesService;
    private readonly DialogService _dialogService;
    private readonly RangeObservableCollection<WindowsServiceItem> _services = [];
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private readonly AsyncCommand _startServiceCommand;
    private readonly AsyncCommand _stopServiceCommand;
    private readonly AsyncCommand _restartServiceCommand;

    private WindowsServiceItem? _selectedService;
    private string _searchText = string.Empty;
    private string _servicesLastUpdatedText = "-";
    private bool _hasLoaded;
    private bool _isLoading;
    private string _loadErrorText = string.Empty;
    private IReadOnlyList<WindowsServiceItem> _allServices = [];
    private int _runningCount;
    private int _stoppedCount;
    private int _groupedCount;

    public ServicesViewModel(WindowsServicesService servicesService, DialogService dialogService)
    {
        _servicesService = servicesService;
        _dialogService = dialogService;
        _startServiceCommand = new AsyncCommand(StartSelectedServiceAsync, () => SelectedService?.CanStart == true);
        _stopServiceCommand = new AsyncCommand(StopSelectedServiceAsync, () => SelectedService?.CanStop == true);
        _restartServiceCommand = new AsyncCommand(RestartSelectedServiceAsync, () => SelectedService?.CanRestart == true);
    }

    public IEnumerable<WindowsServiceItem> Services => _services;
    public AsyncCommand StartServiceCommand => _startServiceCommand;
    public AsyncCommand StopServiceCommand => _stopServiceCommand;
    public AsyncCommand RestartServiceCommand => _restartServiceCommand;
    public bool HasLoaded => _hasLoaded;
    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string LoadErrorText
    {
        get => _loadErrorText;
        private set => SetProperty(ref _loadErrorText, value);
    }

    public WindowsServiceItem? SelectedService
    {
        get => _selectedService;
        set
        {
            if (!SetProperty(ref _selectedService, value)) return;
            RaiseSelectionCommands();
        }
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

    public string ServicesLastUpdatedText
    {
        get => _servicesLastUpdatedText;
        private set => SetProperty(ref _servicesLastUpdatedText, value);
    }

    public int RunningCount
    {
        get => _runningCount;
        private set => SetProperty(ref _runningCount, value);
    }

    public int StoppedCount
    {
        get => _stoppedCount;
        private set => SetProperty(ref _stoppedCount, value);
    }

    public int GroupedCount
    {
        get => _groupedCount;
        private set => SetProperty(ref _groupedCount, value);
    }

    public Task EnsureLoadedAsync()
    {
        if (_hasLoaded || IsLoading)
            return Task.CompletedTask;

        return RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (!await _refreshLock.WaitAsync(0)) return;

        try
        {
            IsLoading = true;
            LoadErrorText = string.Empty;
            var items = await Task.Run(() => _servicesService.GetServices());
            _allServices = items;
            RunningCount = items.Count(item => item.IsRunning);
            StoppedCount = items.Count(item => !item.IsRunning);
            GroupedCount = items.Count(item => !string.IsNullOrWhiteSpace(item.GroupText));
            ServicesLastUpdatedText = DateTime.Now.ToString("HH:mm:ss");
            _hasLoaded = true;
            ApplyFilter();
        }
        catch (Exception ex)
        {
            LoadErrorText = $"读取服务列表失败：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
            _refreshLock.Release();
        }
    }

    private async Task StartSelectedServiceAsync()
    {
        if (SelectedService is null) return;

        try
        {
            await _servicesService.StartServiceAsync(SelectedService.Name);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("操作失败", $"启动服务失败：{ex.Message}");
        }
    }

    private async Task StopSelectedServiceAsync()
    {
        if (SelectedService is null) return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            "停止服务",
            $"确定要停止服务 \"{SelectedService.DisplayName}\" 吗？",
            primaryButtonText: "停止服务",
            secondaryButtonText: "取消",
            isDanger: true);

        if (!confirmed) return;

        try
        {
            await _servicesService.StopServiceAsync(SelectedService.Name);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("操作失败", $"停止服务失败：{ex.Message}");
        }
    }

    private async Task RestartSelectedServiceAsync()
    {
        if (SelectedService is null) return;

        try
        {
            await _servicesService.RestartServiceAsync(SelectedService.Name);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("操作失败", $"重启服务失败：{ex.Message}");
        }
    }

    private void ApplyFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(_searchText)
            ? _allServices
            : _allServices.Where(MatchesSearch).ToList();

        var selectedName = _selectedService?.Name;
        _services.ReplaceAll(filtered);
        SelectedService = _services.FirstOrDefault(item => item.Name == selectedName) ?? _services.FirstOrDefault();
    }

    private bool MatchesSearch(WindowsServiceItem item)
    {
        return item.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
               || item.DisplayName.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
               || item.Description.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
               || item.GroupText.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
               || item.ProcessIdText.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
    }

    private void RaiseSelectionCommands()
    {
        _startServiceCommand.RaiseCanExecuteChanged();
        _stopServiceCommand.RaiseCanExecuteChanged();
        _restartServiceCommand.RaiseCanExecuteChanged();
    }
}
