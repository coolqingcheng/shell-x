using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShellX.Core.Services;

namespace ShellX.Desktop.Features.Ports;

/// <summary>端口占用窗口：读取服务端监听端口，支持按端口号、进程 PID、进程名搜索。</summary>
public partial class PortUsageWindowViewModel(IPortUsageReader? reader, string target) : ObservableObject
{
    private readonly List<ListeningPort> _all = [];
    private bool _refreshing;

    public string Target { get; } = target;
    public ObservableCollection<ListeningPort> VisiblePorts { get; } = [];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasEmptyText))]
    private string _emptyText = "正在读取监听端口…";
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _status = "支持按端口号、进程 PID 或进程名搜索。";
    [ObservableProperty] private string _statusColor = "#858585";
    [ObservableProperty] private bool _busy;

    public bool HasEmptyText => !string.IsNullOrWhiteSpace(EmptyText);
    public bool CanRefresh => reader != null && !Busy;

    partial void OnSearchChanged(string value) => ApplyFilter();
    partial void OnBusyChanged(bool value) => RefreshCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task Refresh()
    {
        Busy = true;
        try { await RefreshAsync(); }
        finally { Busy = false; }
    }

    public async Task RefreshAsync()
    {
        if (reader == null)
        {
            EmptyText = "当前会话不支持端口查看。";
            Summary = "";
            Status = "没有可用的 SSH 执行通道，无法读取服务端监听端口。";
            StatusColor = "#F48771";
            return;
        }
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var list = await reader.PortsAsync();
            _all.Clear();
            _all.AddRange(list);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            EmptyText = "读取监听端口失败。";
            Status = ex.Message;
            StatusColor = "#F48771";
        }
        finally { _refreshing = false; }
    }

    private void ApplyFilter()
    {
        var query = (Search ?? "").Trim();
        VisiblePorts.Clear();
        foreach (var port in _all)
            if (port.Matches(query)) VisiblePorts.Add(port);
        Summary = _all.Count == 0 ? "" : $"共 {_all.Count} 个监听端口 · 匹配 {VisiblePorts.Count} 个";
        EmptyText = VisiblePorts.Count > 0
            ? ""
            : _all.Count == 0 ? "没有读取到监听端口，该系统可能没有 ss / netstat。" : "没有匹配的端口或进程 ID。";
    }
}