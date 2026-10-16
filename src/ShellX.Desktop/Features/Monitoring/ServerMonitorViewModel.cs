using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ShellX.Core.Monitoring;
using ShellX.Core.Settings;
using ShellX.Desktop.Services;

namespace ShellX.Desktop.Features.Monitoring;

public sealed record ProcessRow(ServerProcess Value, long TotalMemory)
{
    public int Pid => Value.Pid;
    public string Memory => ServerMetrics.Size(Value.MemoryBytes);
    public string MemoryPercent => TotalMemory <= 0 ? "—" : $"{Value.MemoryBytes * 100d / TotalMemory:0.#}%";
    public string Cpu => $"{Value.CpuPercent:0.#}%";
    public string Command => Value.Command;
    public string Description => $"PID {Pid} · {Value.Command}\nCPU {Cpu} · 内存 {MemoryPercent}（{Memory}）";
}
public sealed record DiskRow(ServerDisk Value)
{
    public string Path => Value.MountPath;
    public string Capacity => $"{ServerMetrics.Size(Value.AvailableBytes)} / {ServerMetrics.Size(Value.TotalBytes)}";
    public double UsedPercent => Value.UsedPercent;
    public string Description => $"{Value.Device} · 已用 {UsedPercent:0.#}%";
}
public partial class NetworkViewModel(string name) : ObservableObject
{
    public string Name => name;
    [ObservableProperty] private string _upload = "—";
    [ObservableProperty] private string _download = "—";
    [ObservableProperty] private IReadOnlyList<double> _uploadHistory = [];
    [ObservableProperty] private IReadOnlyList<double> _downloadHistory = [];
    public void Add(NetworkRate rate)
    {
        Upload = ServerMetrics.Size(rate.UploadBytesPerSecond) + "/s";
        Download = ServerMetrics.Size(rate.DownloadBytesPerSecond) + "/s";
        UploadHistory = UploadHistory.TakeLast(59).Append(rate.UploadBytesPerSecond).ToArray();
        DownloadHistory = DownloadHistory.TakeLast(59).Append(rate.DownloadBytesPerSecond).ToArray();
    }
}

public partial class ServerMonitorViewModel(string host, IDialogService dialogs, ILogger log, Func<string, Task> navigate,
    Func<ProcessSortColumn, bool, Task>? saveSort = null) : ObservableObject
{
    private IServerMonitor? _source;
    private CancellationTokenSource? _cancel;
    private Task _loop = Task.CompletedTask;
    private readonly SemaphoreSlim _sampleGate = new(1, 1);
    private ServerSnapshot? _previous;
    private ServerSnapshot? _latest;
    private int _generation;
    private string? _lastError;
    public ProcessSortColumn SortColumn { get; private set; } = ProcessSortColumn.Cpu;
    public bool SortDescending { get; private set; } = true;
    public string CpuHeader => "CPU" + (SortColumn == ProcessSortColumn.Cpu ? SortDescending ? " ↓" : " ↑" : "");
    public string MemoryHeader => "内存" + (SortColumn == ProcessSortColumn.Memory ? SortDescending ? " ↓" : " ↑" : "");

    public void ApplySort(ProcessSortColumn column, bool descending)
    {
        SortColumn = column; SortDescending = descending;
        OnPropertyChanged(nameof(SortColumn)); OnPropertyChanged(nameof(SortDescending));
        OnPropertyChanged(nameof(CpuHeader)); OnPropertyChanged(nameof(MemoryHeader));
        SortProcesses();
    }
    private void SortProcesses()
    {
        Processes.Clear();
        if (_latest is not { } data) return;
        Func<ServerProcess, double> key = SortColumn == ProcessSortColumn.Memory ? p => p.MemoryBytes : p => p.CpuPercent;
        var sorted = SortDescending ? data.Processes.OrderByDescending(key) : data.Processes.OrderBy(key);
        foreach (var process in sorted.ThenBy(p => p.Pid).Take(8)) Processes.Add(new(process, data.MemoryTotal));
    }
    private async Task ChangeSortAsync(ProcessSortColumn column)
    {
        ApplySort(column, column != SortColumn || !SortDescending);
        if (saveSort != null) await saveSort(SortColumn, SortDescending);
    }
    [RelayCommand] private Task SortCpuAsync() => ChangeSortAsync(ProcessSortColumn.Cpu);
    [RelayCommand] private Task SortMemoryAsync() => ChangeSortAsync(ProcessSortColumn.Memory);
    public ObservableCollection<ProcessRow> Processes { get; } = [];
    public ObservableCollection<DiskRow> Disks { get; } = [];
    public ObservableCollection<NetworkViewModel> Networks { get; } = [];
    [ObservableProperty] private NetworkViewModel? _selectedNetwork;
    [ObservableProperty] private string _address = host;
    [ObservableProperty] private string _hostname = "未连接";
    [ObservableProperty] private string _status = "连接后显示服务器状态";
    [ObservableProperty] private string _uptime = "—";
    [ObservableProperty] private string _load = "—";
    [ObservableProperty] private string _cpuText = "—";
    [ObservableProperty] private double _cpu;
    [ObservableProperty] private string _memoryText = "—";
    [ObservableProperty] private double _memory;
    [ObservableProperty] private string _swapText = "—";
    [ObservableProperty] private double _swap;
    [ObservableProperty] private string _response = "—";
    [ObservableProperty] private IReadOnlyList<double> _responseHistory = [];
    [ObservableProperty] private bool _isMonitoring;

    public void Start(IServerMonitor? source, CancellationToken token)
    {
        _source = source; _previous = null; _latest = null; _lastError = null; ++_generation;
        Networks.Clear(); Processes.Clear(); Disks.Clear(); SelectedNetwork = null; ResponseHistory = [];
        Cpu = Memory = Swap = 0; CpuText = MemoryText = SwapText = Uptime = Load = Response = "—";
        IsMonitoring = source != null;
        if (source == null) { Status = "当前连接不提供系统监控"; return; }
        _cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
        Status = "正在采样…"; _loop = PollAsync(_cancel.Token);
    }

    private async Task PollAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await SampleAsync(token);
                if (!IsMonitoring) break;
                await Task.Delay(_lastError == null ? 2000 : 6000, token);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task SampleAsync(CancellationToken token)
    {
        if (_source == null || !await _sampleGate.WaitAsync(0, token)) return;
        var source = _source; var generation = _generation;
        try
        {
            var data = await source.ReadAsync(token);
            if (token.IsCancellationRequested || generation != _generation) return;
            Apply(data); _lastError = null;
            Status = "已更新 " + DateTime.Now.ToString("HH:mm:ss");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (NotSupportedException ex) { if (generation == _generation) { Status = ex.Message; IsMonitoring = false; } }
        catch (Exception ex)
        {
            if (generation != _generation || token.IsCancellationRequested) return;
            Status = "采样失败：" + ex.Message;
            if (_lastError != ex.Message) log.LogWarning(ex, "服务器监控采样失败");
            _lastError = ex.Message;
        }
        finally { _sampleGate.Release(); }
    }

    private void Apply(ServerSnapshot data)
    {
        _latest = data; Hostname = data.Hostname;
        Address = string.IsNullOrWhiteSpace(data.Addresses) ? Address : data.Addresses;
        Uptime = data.Uptime.TotalDays >= 1 ? $"{(int)data.Uptime.TotalDays}天 {data.Uptime.Hours}时" : $"{data.Uptime.Hours}时 {data.Uptime.Minutes}分";
        Load = data.LoadAverage;
        var cpu = ServerMetrics.CpuUsage(_previous?.Cpu, data.Cpu);
        Cpu = cpu ?? 0; CpuText = cpu.HasValue ? $"{cpu:0.#}%" : "采样中";
        Memory = (data.MemoryTotal - data.MemoryAvailable) * 100d / data.MemoryTotal;
        MemoryText = $"{Memory:0.#}% · {ServerMetrics.Size(data.MemoryTotal - data.MemoryAvailable)} / {ServerMetrics.Size(data.MemoryTotal)}";
        Swap = data.SwapTotal <= 0 ? 0 : Math.Clamp((data.SwapTotal - data.SwapFree) * 100d / data.SwapTotal, 0, 100);
        SwapText = data.SwapTotal <= 0 ? "未启用" : $"{ServerMetrics.Size(data.SwapTotal - data.SwapFree)} / {ServerMetrics.Size(data.SwapTotal)}";
        foreach (var rate in ServerMetrics.NetworkRates(_previous, data))
        {
            var network = Networks.FirstOrDefault(n => n.Name == rate.Name);
            if (network == null) { network = new(rate.Name); Networks.Add(network); }
            network.Add(rate);
        }
        foreach (var old in Networks.Where(n => data.Networks.All(current => current.Name != n.Name)).ToArray()) Networks.Remove(old);
        if (SelectedNetwork == null || !Networks.Contains(SelectedNetwork)) SelectedNetwork = Networks.FirstOrDefault(n => n.Name != "lo") ?? Networks.FirstOrDefault();
        SortProcesses();
        Disks.Clear(); foreach (var disk in data.Disks) Disks.Add(new(disk));
        Response = $"{data.ResponseMilliseconds:0} ms";
        ResponseHistory = ResponseHistory.TakeLast(59).Append(data.ResponseMilliseconds).ToArray();
        _previous = data;
    }

    [RelayCommand] private Task RefreshAsync() => _cancel == null ? Task.CompletedTask : SampleAsync(_cancel.Token);
    [RelayCommand] private Task OpenDiskAsync(DiskRow? disk) => disk == null ? Task.CompletedTask : navigate(disk.Path);
    [RelayCommand]
    private async Task SystemInfoAsync()
    {
        var data = _latest;
        await dialogs.ShowInformationAsync("系统信息", data == null ? "连接 Linux 服务器后显示系统信息。" :
            $"主机名：{data.Hostname}\n系统：{data.OperatingSystem}\n内核：{data.Kernel}\n地址：{Address}\n运行时间：{(int)data.Uptime.TotalDays}天 {data.Uptime.Hours}时 {data.Uptime.Minutes}分\n负载：{Load}\n内存：{MemoryText}\n交换分区：{SwapText}\nSSH 采样响应：{Response}");
    }
    public async Task StopAsync()
    {
        ++_generation; var cancel = _cancel; _cancel = null; _source = null; IsMonitoring = false;
        cancel?.Cancel(); await _loop; cancel?.Dispose(); Status = "监控已断开";
    }
}
