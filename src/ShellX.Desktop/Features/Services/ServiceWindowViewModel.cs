using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShellX.Core.Services;

namespace ShellX.Desktop.Features.Services;

/// <summary>服务状态窗口：读取目标服务器的 systemd 服务，支持搜索、查看详情与启动/停止/重启/启用/禁用。</summary>
public partial class ServiceWindowViewModel(IServiceCenter? services, string target, Func<string, Task<bool>> confirm) : ObservableObject
{
    private readonly List<RemoteService> _all = [];
    private string _detailUnit = "";
    private bool _refreshing;

    public string Target { get; } = target;
    public ObservableCollection<RemoteService> VisibleServices { get; } = [];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasEmptyText))]
    private string _emptyText = "正在读取服务列表…";
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private RemoteService? _selected;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _status = "选择服务后可执行启动、停止、重启等操作。";
    [ObservableProperty] private string _statusColor = "#858585";
    [ObservableProperty] private string _detail = "选择服务后显示 systemctl status 详情。";
    [ObservableProperty] private bool _busy;

    public bool HasEmptyText => !string.IsNullOrWhiteSpace(EmptyText);
    public bool CanRefresh => services != null && !Busy;
    public bool CanAct => services != null && Selected != null && !Busy;

    partial void OnSearchChanged(string value) => ApplyFilter();
    partial void OnBusyChanged(bool value) => NotifyCommands();
    partial void OnSelectedChanged(RemoteService? value)
    {
        NotifyCommands();
        _ = LoadDetailAsync(value);
    }

    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        RestartCommand.NotifyCanExecuteChanged();
        EnableCommand.NotifyCanExecuteChanged();
        DisableCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task Refresh()
    {
        Busy = true;
        try { await RefreshAsync(); }
        finally { Busy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanAct))] private Task Start() => RunAsync(ServiceAction.Start, "启动");
    [RelayCommand(CanExecute = nameof(CanAct))] private Task Stop() => RunAsync(ServiceAction.Stop, "停止");
    [RelayCommand(CanExecute = nameof(CanAct))] private Task Restart() => RunAsync(ServiceAction.Restart, "重启");
    [RelayCommand(CanExecute = nameof(CanAct))] private Task Enable() => RunAsync(ServiceAction.Enable, "启用");
    [RelayCommand(CanExecute = nameof(CanAct))] private Task Disable() => RunAsync(ServiceAction.Disable, "禁用");

    public async Task RefreshAsync()
    {
        if (services == null)
        {
            EmptyText = "当前会话不支持服务管理。";
            Summary = "";
            Status = "没有可用的 SSH 执行通道，无法读取 systemctl 服务。";
            StatusColor = "#F48771";
            return;
        }
        if (_refreshing) return;   // 操作执行后也会走这里，不能用 Busy 拦截
        var keep = Selected?.Unit;
        _refreshing = true;
        try
        {
            var list = await services.ListAsync();
            _all.Clear();
            _all.AddRange(list);
            ApplyFilter(keep);
        }
        catch (Exception ex)
        {
            EmptyText = "读取服务列表失败。";
            Status = ex.Message;
            StatusColor = "#F48771";
        }
        finally { _refreshing = false; }
    }

    private async Task RunAsync(ServiceAction action, string label)
    {
        if (services == null || Selected is not { } service) return;
        if (!await confirm($"即将对 {service.Unit} 执行{label}操作，是否继续？")) return;
        Busy = true;
        try
        {
            var result = await services.ExecuteAsync(service.Unit, action);
            Status = result.Success
                ? $"{label}完成：{FirstLine(result.Message)}"
                : $"{label}失败（exit={result.ExitCode}）：{FirstLine(result.Message)}";
            StatusColor = result.Success ? "#73C991" : "#F48771";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Status = $"{label}失败：{ex.Message}";
            StatusColor = "#F48771";
        }
        finally { Busy = false; }
    }

    private async Task LoadDetailAsync(RemoteService? service)
    {
        var unit = service?.Unit ?? "";
        if (service == null || services == null)
        {
            _detailUnit = "";
            Detail = "选择服务后显示 systemctl status 详情。";
            return;
        }
        _detailUnit = unit;
        Detail = $"正在读取 {unit} 的状态…";
        try
        {
            var text = await services.StatusAsync(unit);
            if (Equals(_detailUnit, unit)) Detail = text;   // 只保留最后一次选择的结果
        }
        catch (Exception ex)
        {
            if (Equals(_detailUnit, unit)) Detail = $"读取服务状态失败：{ex.Message}";
        }
    }

    private void ApplyFilter(string? keepUnit = null)
    {
        var query = (Search ?? "").Trim();
        VisibleServices.Clear();
        foreach (var service in _all)
        {
            if (query.Length == 0
                || service.Unit.Contains(query, StringComparison.OrdinalIgnoreCase)
                || service.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
                VisibleServices.Add(service);
        }
        if (Selected is { } current && !VisibleServices.Contains(current)) Selected = null;
        if (Selected == null && keepUnit != null) Selected = VisibleServices.FirstOrDefault(service => service.Unit == keepUnit);
        Summary = _all.Count == 0 ? "" : $"共 {_all.Count} 个服务 · 匹配 {VisibleServices.Count} 个";
        EmptyText = VisibleServices.Count > 0
            ? ""
            : _all.Count == 0 ? "没有读取到 systemd 服务，该系统可能没有使用 systemd。" : "没有匹配的服务。";
    }

    private static string FirstLine(string message)
    {
        var text = (message ?? "").Trim();
        var index = text.IndexOf('\n');
        return index < 0 ? text : text[..index];
    }
}