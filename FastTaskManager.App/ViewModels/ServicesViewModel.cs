using FastTaskManager.App.Infrastructure;
using FastTaskManager.App.Models;
using FastTaskManager.App.Services;
using System.Windows;

namespace FastTaskManager.App.ViewModels;

public sealed class ServicesViewModel : ObservableObject
{
    private readonly WindowsServicesService _servicesService;
    private readonly DialogService _dialogService;
    private readonly PrivilegeService _privilegeService;
    private readonly RangeObservableCollection<WindowsServiceItem> _services = [];
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private readonly AsyncCommand _startServiceCommand;
    private readonly AsyncCommand _stopServiceCommand;
    private readonly AsyncCommand _restartServiceCommand;
    private readonly AsyncCommand _pauseServiceCommand;
    private readonly AsyncCommand _resumeServiceCommand;
    private readonly AsyncCommand _refreshCommand;

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

    public ServicesViewModel(WindowsServicesService servicesService, DialogService dialogService, PrivilegeService privilegeService)
    {
        _servicesService = servicesService;
        _dialogService = dialogService;
        _privilegeService = privilegeService;
        _startServiceCommand = new AsyncCommand(StartSelectedServiceAsync, () => SelectedService?.CanStart == true);
        _stopServiceCommand = new AsyncCommand(StopSelectedServiceAsync, () => SelectedService?.CanStop == true);
        _restartServiceCommand = new AsyncCommand(RestartSelectedServiceAsync, () => SelectedService?.CanRestart == true);
        _pauseServiceCommand = new AsyncCommand(PauseSelectedServiceAsync, () => SelectedService?.CanPause == true);
        _resumeServiceCommand = new AsyncCommand(ResumeSelectedServiceAsync, () => SelectedService?.CanResume == true);
        _refreshCommand = new AsyncCommand(RefreshAsync);
    }

    public IEnumerable<WindowsServiceItem> Services => _services;
    public AsyncCommand StartServiceCommand => _startServiceCommand;
    public AsyncCommand StopServiceCommand => _stopServiceCommand;
    public AsyncCommand RestartServiceCommand => _restartServiceCommand;
    public AsyncCommand PauseServiceCommand => _pauseServiceCommand;
    public AsyncCommand ResumeServiceCommand => _resumeServiceCommand;
    public AsyncCommand RefreshCommand => _refreshCommand;
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
        if (!await EnsureElevatedForServiceActionAsync()) return;

        var diagnostic = _servicesService.DiagnoseStartFailure(SelectedService.Name);
        if (diagnostic.DisabledDependencies.Count > 0)
        {
            var dependencyText = string.Join("、", diagnostic.DisabledDependencies.Select(item => item.DisplayName));
            await _dialogService.ShowErrorAsync(
                "无法启动服务",
                $"服务 \"{SelectedService.DisplayName}\" 依赖以下已禁用服务：{dependencyText}。请先将依赖服务改为“手动”或“自动”后再试。");
            return;
        }

        if (diagnostic.IsServiceDisabled)
        {
            var confirmed = await _dialogService.ShowConfirmationAsync(
                "服务已禁用",
                $"服务 \"{SelectedService.DisplayName}\" 当前启动类型为“禁用”。是否将其改为“手动”并立即启动？",
                primaryButtonText: "启用并启动",
                secondaryButtonText: "取消");

            if (!confirmed)
                return;

            try
            {
                await _servicesService.SetStartModeAsync(SelectedService.Name, WindowsServicesService.ServiceStartMode.Manual);
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("修改启动类型失败", ex.Message);
                return;
            }
        }

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
        if (!await EnsureElevatedForServiceActionAsync()) return;

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
        if (!await EnsureElevatedForServiceActionAsync()) return;

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

    private async Task PauseSelectedServiceAsync()
    {
        if (SelectedService is null) return;
        if (!await EnsureElevatedForServiceActionAsync()) return;

        try
        {
            await _servicesService.PauseServiceAsync(SelectedService.Name);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("操作失败", $"暂停服务失败：{ex.Message}");
        }
    }

    private async Task ResumeSelectedServiceAsync()
    {
        if (SelectedService is null) return;
        if (!await EnsureElevatedForServiceActionAsync()) return;

        try
        {
            await _servicesService.ResumeServiceAsync(SelectedService.Name);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("操作失败", $"恢复服务失败：{ex.Message}");
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
        _pauseServiceCommand.RaiseCanExecuteChanged();
        _resumeServiceCommand.RaiseCanExecuteChanged();
    }

    private async Task<bool> EnsureElevatedForServiceActionAsync()
    {
        if (_privilegeService.IsAdministrator())
            return true;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            "需要管理员权限",
            "服务的启动、停止和重启需要管理员权限。是否立即以管理员身份重新启动应用？",
            primaryButtonText: "重新启动",
            secondaryButtonText: "取消");

        if (!confirmed)
            return false;

        try
        {
            if (_privilegeService.TryRestartAsAdministrator())
            {
                Application.Current.Shutdown();
                return false;
            }

            await _dialogService.ShowErrorAsync("提权失败", "无法重新启动应用，请手动以管理员身份运行。");
            return false;
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("提权失败", $"无法重新启动应用：{ex.Message}");
            return false;
        }
    }
}
