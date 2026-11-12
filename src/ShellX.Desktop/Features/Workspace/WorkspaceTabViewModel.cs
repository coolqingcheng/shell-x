using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ShellX.Core.Connections;
using ShellX.Core.Files;
using ShellX.Core.Security;
using ShellX.Core.Sessions;
using ShellX.Core.Settings;
using ShellX.Core.Tunnels;
using ShellX.Desktop.Features.FileBrowser;
using ShellX.Desktop.Features.Commands;
using ShellX.Desktop.Features.Monitoring;
using ShellX.Desktop.Features.Tunnels;
using ShellX.Desktop.Services;

namespace ShellX.Desktop.Features.Workspace;

public partial class WorkspaceTabViewModel : ObservableObject, IAsyncDisposable
{
    private readonly SessionManager _sessions;
    private readonly IDialogService _dialogs;
    private readonly Func<Task<ConnectionSecret?>> _getSecret;
    private readonly Func<HostKeyChallenge, Task<bool>> _verify;
    private readonly Func<ConnectionProfile, Task> _save;
    private readonly Func<WorkspaceTabViewModel, Task> _close;
    private readonly ILogger _log;
    private readonly SharedMonitorRegistry _monitors;
    private SharedMonitorRegistry.Lease _monitorLease;
    private ServerMonitorViewModel? _observedMonitor;
    private ConnectionEndpoint _monitorEndpoint;
    private readonly Func<ProcessSortColumn, bool, Task>? _saveProcessSort;
    private CancellationTokenSource? _connection;
    private CancellationTokenSource? _reconnectCancellation;
    private Task _reconnectTask = Task.CompletedTask;
    private CancellationTokenSource? _tunnelRetryCancellation;
    // 每条规则的下一次重试时间，避免失败后高频反复拉起。
    private readonly Dictionary<Guid, DateTimeOffset> _tunnelRetryAt = [];
    private ConnectionSecret? _reconnectSecret;
    private bool _autoReconnectEnabled;
    private int _reconnectIntervalSeconds = 5;
    private bool _hasConnected;
    private int _connectionVersion;
    private bool _disposed;
    private string? _temporaryName;
    private readonly SemaphoreSlim _terminalWriteGate = new(1, 1);
    public ConnectionProfile Profile { get; private set; }
    public string Name => _temporaryName ?? Profile.Name;
    public string Address => Profile.Address;
    public string AccentColor => Profile.AccentColor;
    public IDialogService Dialogs => _dialogs;
    /// <summary>标签被关闭（会话已释放）时触发，用于同步关闭该连接的附属窗口。</summary>
    public event EventHandler? Disposed;
    public FileBrowserViewModel Files { get; }
    /// <summary>当前会话用的密钥，只在内存里，用于再开一个同主机的连接。</summary>
    public ConnectionSecret? CurrentSecret => _reconnectSecret;
    public SessionTerminalChannel Terminal { get; } = new();
    public ServerMonitorViewModel Monitor => _monitorLease.Monitor;
    public CommandPanelViewModel Commands { get; }
    public ObservableCollection<TunnelViewModel> Tunnels { get; } = [];
    public IReadOnlyList<TunnelViewModel> FilteredTunnels => string.IsNullOrWhiteSpace(TunnelSearch)
        ? Tunnels.ToArray()
        : Tunnels.Where(t => t.Name.Contains(TunnelSearch.Trim(), StringComparison.OrdinalIgnoreCase)
            || t.Description.Contains(TunnelSearch.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
    [ObservableProperty] private string _tunnelSearch = "";
    partial void OnTunnelSearchChanged(string value) => OnPropertyChanged(nameof(FilteredTunnels));
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isConnecting;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isReconnecting;
    [ObservableProperty] private bool _highlightTerminalText = true;
    [ObservableProperty] private string _status = "未连接";
    /// <summary>SSH 往返延迟，取自监控采样的响应耗时。</summary>
    [ObservableProperty] private string _latency = "";
    public bool HasLatency => !string.IsNullOrEmpty(Latency);
    partial void OnLatencyChanged(string value) => OnPropertyChanged(nameof(HasLatency));
    [ObservableProperty] private double _fontSize = 14;
    [ObservableProperty] private string _fontFamily = "Cascadia Mono,Consolas,monospace";
    [ObservableProperty] private IRemoteSession? _session;
    public event Action<WorkspaceTabViewModel, IRemoteSession?>? SessionChanged;
    public CancellationToken ConnectionToken => _connection?.Token ?? CancellationToken.None;
    partial void OnSessionChanged(IRemoteSession? value) => SessionChanged?.Invoke(this, value);
    public bool ShowOverlay => !IsConnected && !_hasConnected;
    public string StatusColor => IsConnected ? "#73c991" : IsConnecting || IsReconnecting ? "#d7ba7d" : "#858585";
    public bool CanStopAllTunnels => IsConnected && Tunnels.Any(tunnel => tunnel.Running || tunnel.IsBusy);
    public string TunnelStatusColor => Tunnels.Any(t => t.State == TunnelState.Failed) ? "#F48771"
        : Tunnels.Any(t => t.State is TunnelState.Starting or TunnelState.Checking or TunnelState.Unverified) ? "#D7BA7D"
        : Tunnels.Any(t => t.State == TunnelState.Connected) ? "#73C991" : "#858585";
    public string TunnelTooltip => Tunnels.Count == 0 ? "端口转发 · 点击打开隧道窗口"
        : "端口转发 · 点击打开隧道窗口\n" + string.Join("\n", Tunnels.Select(t => $"{t.Name}：{t.Status}" + (t.State == TunnelState.Failed && t.HasReason ? " · " + t.Reason : "")));
    public WorkspaceTabViewModel(ConnectionProfile profile, IRemoteSessionFactory factory, IDialogService dialogs,
        ILoggerFactory logs, Func<Task<ConnectionSecret?>> getSecret, Func<HostKeyChallenge, Task<bool>> verify,
        Func<ConnectionProfile, Task> save, Func<WorkspaceTabViewModel, Task> close, AppSettings settings, ILocalFileSystem localFiles,
        Func<ProcessSortColumn, bool, Task>? saveProcessSort = null, SharedMonitorRegistry? monitors = null,
        Func<WorkspaceTabViewModel, string, Task>? openTerminal = null)
    {
        Profile = profile; _sessions = new SessionManager(factory); _dialogs = dialogs; _getSecret = getSecret; _verify = verify; _save = save; _close = close;
        _log = logs.CreateLogger<WorkspaceTabViewModel>(); Files = new(dialogs, logs.CreateLogger<FileBrowserViewModel>(), localFiles);
        // 文件树里“在终端打开”交给外壳处理：它要新建连接，标签自己做不到。
        Files.OpenTerminalHandler = path => openTerminal == null ? Task.CompletedTask : openTerminal(this, path);
        _monitors = monitors ?? new SharedMonitorRegistry(dialogs, logs); _saveProcessSort = saveProcessSort;
        _monitorEndpoint = ConnectionEndpoint.From(profile);
        _monitorLease = AcquireMonitor();
        ObserveMonitor();
        Commands = new(dialogs, SendRawAsync, async commands =>
        {
            var updated = Profile with { QuickCommands = commands.ToList() };
            await _save(updated); UpdateProfile(updated);
        });
        Commands.Load(profile.QuickCommands);
        Terminal.Closed += (source, reason) => Dispatcher.UIThread.Post(() =>
        { if (Session is { } session && ReferenceEquals(source, session.Terminal)) OnDisconnected(session, reason); });
        Tunnels.CollectionChanged += (_, e) =>
        {
            if (e.OldItems != null) foreach (TunnelViewModel tunnel in e.OldItems) tunnel.PropertyChanged -= TunnelChanged;
            if (e.NewItems != null) foreach (TunnelViewModel tunnel in e.NewItems) tunnel.PropertyChanged += TunnelChanged;
            OnPropertyChanged(nameof(FilteredTunnels));
            NotifyTunnelStatus();
        };
        ApplySettings(settings); RebuildTunnels();
        EnsureTunnelRetryLoop();
    }
    private SharedMonitorRegistry.Lease AcquireMonitor() => _monitors.Acquire(Profile, () => IsSelected,
        async path => await Files.NavigateAsync(path), _saveProcessSort);
    /// <summary>监控面板与工作区共享同一个采样器，这里跟随它更新连接延迟显示。</summary>
    private void ObserveMonitor()
    {
        var monitor = _monitorLease.Monitor;
        if (ReferenceEquals(_observedMonitor, monitor)) return;
        if (_observedMonitor != null) _observedMonitor.PropertyChanged -= MonitorPropertyChanged;
        _observedMonitor = monitor;
        if (_observedMonitor != null) _observedMonitor.PropertyChanged += MonitorPropertyChanged;
        UpdateLatency();
    }
    private void MonitorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ServerMonitorViewModel.Response)) UpdateLatency();
    }
    private void UpdateLatency()
    {
        var response = _observedMonitor?.Response ?? "";
        Latency = string.IsNullOrWhiteSpace(response) || response == "—" ? "" : response;
    }
    private void TunnelChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName is nameof(TunnelViewModel.State) or nameof(TunnelViewModel.Running) or nameof(TunnelViewModel.Reason)) NotifyTunnelStatus(); }
    private void NotifyTunnelStatus()
    {
        OnPropertyChanged(nameof(TunnelStatusColor));
        OnPropertyChanged(nameof(TunnelTooltip));
        OnPropertyChanged(nameof(CanStopAllTunnels));
    }
    public void UpdateProfile(ConnectionProfile profile) { Profile = profile; Commands.Load(profile.QuickCommands); RebuildTunnels(); OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(Address)); OnPropertyChanged(nameof(AccentColor)); }
    public void ApplySettings(AppSettings settings)
    {
        FontSize = settings.TerminalFontSize; FontFamily = settings.TerminalFontFamily; Files.ShowHidden = settings.ShowHiddenFiles;
        Files.Local.ShowHidden = settings.ShowHiddenFiles; HighlightTerminalText = settings.HighlightTerminalText;
        Monitor.ApplySort(settings.ProcessSortColumn, settings.ProcessSortDescending);
        var changed = _autoReconnectEnabled != settings.AutoReconnectEnabled || _reconnectIntervalSeconds != settings.ReconnectIntervalSeconds;
        _autoReconnectEnabled = settings.AutoReconnectEnabled; _reconnectIntervalSeconds = Math.Clamp(settings.ReconnectIntervalSeconds, 1, 86400);
        if (changed && _reconnectCancellation != null)
        {
            var stopping = StopAutoReconnect();
            _ = RestartRetryAsync(stopping);
        }
        else if (changed && _hasConnected && !IsConnected && !IsConnecting && _reconnectSecret != null) StartAutoReconnect();
    }
    private async Task RestartRetryAsync(Task stopping)
    {
        await stopping;
        if (_disposed || IsConnected || _reconnectSecret == null) return;
        if (_autoReconnectEnabled) StartAutoReconnect();
        else { Status = "自动重连已关闭"; await Terminal.LogAsync(Status); }
    }
    partial void OnIsConnectedChanged(bool value) { Commands.IsAvailable = value; OnPropertyChanged(nameof(ShowOverlay)); OnPropertyChanged(nameof(StatusColor)); OnPropertyChanged(nameof(CanStopAllTunnels)); }
    partial void OnIsConnectingChanged(bool value) => OnPropertyChanged(nameof(StatusColor));
    partial void OnIsReconnectingChanged(bool value) => OnPropertyChanged(nameof(StatusColor));

    public async Task ConnectWithSecretAsync(ConnectionSecret secret)
    {
        if (_disposed || IsConnecting) return;
        var version = ++_connectionVersion;
        await StopAutoReconnect();
        if (_disposed || version != _connectionVersion) return;
        _reconnectSecret = secret;
        await ConnectCoreAsync(secret, CancellationToken.None);
    }
    private async Task ConnectCoreAsync(ConnectionSecret secret, CancellationToken cancellationToken)
    {
        if (_disposed || IsConnecting || cancellationToken.IsCancellationRequested) return;
        var version = _connectionVersion;
        IsConnecting = true;
        IRemoteSession? created = null;
        try
        {
            await DisconnectSessionAsync();
            if (_disposed || version != _connectionVersion || cancellationToken.IsCancellationRequested) return;
            if (_monitorEndpoint != ConnectionEndpoint.From(Profile))
            {
                var column = Monitor.SortColumn; var descending = Monitor.SortDescending;
                await _monitorLease.DisposeAsync();
                if (_disposed || version != _connectionVersion || cancellationToken.IsCancellationRequested) return;
                _monitorEndpoint = ConnectionEndpoint.From(Profile); _monitorLease = AcquireMonitor();
                ObserveMonitor();
                Monitor.ApplySort(column, descending); OnPropertyChanged(nameof(Monitor));
            }
            Status = "正在连接…";
            _connection = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); var ct = _connection.Token;
            created = await _sessions.ConnectAsync(Profile, secret, async challenge =>
            {
                var trusted = await _verify(challenge);
                if (!trusted && IsReconnecting) _autoReconnectEnabled = false;
                return trusted;
            }, ct);
            if (ct.IsCancellationRequested || _disposed) { await created.DisposeAsync(); return; }
            Session = created; _hasConnected = true; IsConnected = true; Status = "已连接";
            Terminal.Attach(created.Terminal);
            created.Disconnected += (_, error) => Dispatcher.UIThread.Post(() => OnDisconnected(created, error));
            created.Tunnels.StatusChanged += (_, state) => Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(Session, created)) Tunnels.FirstOrDefault(t => t.Definition.Id == state.TunnelId)?.Apply(created.Tunnels.GetStatus(state.TunnelId));
            });
            foreach (var tunnel in Tunnels) tunnel.Apply(created.Tunnels.GetStatus(tunnel.Definition.Id));
            _log.LogInformation("已建立会话 {ConnectionId}", Profile.Id);
            Files.Attach(created.Files, ct, created, $"{Name} · {Profile.Host}:{Profile.Port}");
            await _monitorLease.SetSourceAsync(created.Monitor);
            foreach (var tunnel in Tunnels.ToArray())
            {
                ct.ThrowIfCancellationRequested();
                try { await created.Tunnels.AutoStartAsync(tunnel.Definition, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogWarning(ex, "自动启动隧道失败 {TunnelId}", tunnel.Definition.Id); }
                if (ReferenceEquals(Session, created)) tunnel.Apply(created.Tunnels.GetStatus(tunnel.Definition.Id));
            }
            if (created.Files != null) await Files.NavigateAsync(Profile.InitialDirectory);
            else Files.Error = "SFTP 连接失败：" + created.FileError + "。可重连后重试。";
        }
        catch (OperationCanceledException) { if (!IsReconnecting) Status = "连接已取消"; }
        catch (Exception ex)
        {
            Status = "连接失败：" + ex.Message; _log.LogWarning(ex, "连接失败 {ConnectionId}", Profile.Id);
            if (created != null && ReferenceEquals(Session, created)) await DisconnectSessionAsync();
        }
        finally { IsConnecting = false; }
    }
    [RelayCommand]
    private async Task ReconnectAsync()
    { if (IsConnecting) return; try { if (await _getSecret() is { } secret) await ConnectWithSecretAsync(secret); } catch (Exception ex) { Status = ex.Message; } }
    [RelayCommand]
    private async Task DisconnectAsync()
    {
        ++_connectionVersion;
        var wasActive = IsConnected || IsConnecting || IsReconnecting;
        var retry = StopAutoReconnect(); _reconnectSecret = null;
        await DisconnectSessionAsync(); await retry;
        if (!_disposed)
        {
            Status = "已断开";
            if (wasActive) await Terminal.LogAsync("已手动断开，自动重连已停止。");
        }
    }
    private async Task DisconnectSessionAsync()
    {
        _connection?.Cancel(); Files.CancelTransfers();
        var monitoring = _monitorLease.SetSourceAsync(null);
        var terminal = Terminal.DetachAsync();
        Session = null; IsConnected = false;
        await monitoring;
        await _sessions.DisconnectAsync();
        await terminal;
        await Files.DrainTransfersAsync(); Files.Attach(null, CancellationToken.None);
        _connection?.Dispose(); _connection = null;
        foreach (var tunnel in Tunnels) tunnel.Apply(new(tunnel.Definition.Id, TunnelState.Stopped, false));
    }
    private void OnDisconnected(IRemoteSession source, string reason)
    {
        if (_disposed || !ReferenceEquals(Session, source) || !IsConnected) return;
        IsConnected = false; Status = "连接断开：" + reason;
        _connection?.Cancel(); Files.CancelTransfers(); Files.Attach(null, CancellationToken.None);
        _ = Terminal.DetachAsync();
        _ = _monitorLease.SetSourceAsync(null);
        foreach (var tunnel in Tunnels) tunnel.Apply(source.Tunnels.GetStatus(tunnel.Definition.Id));
        _ = Terminal.LogAsync(Status, error: true);
        StartAutoReconnect();
    }
    private void StartAutoReconnect()
    {
        if (_disposed || !_autoReconnectEnabled || _reconnectSecret == null || _reconnectCancellation != null) return;
        var cancellation = _reconnectCancellation = new CancellationTokenSource();
        _reconnectTask = AutoReconnectAsync(_reconnectSecret, cancellation);
    }
    private Task StopAutoReconnect()
    {
        _reconnectCancellation?.Cancel();
        return _reconnectTask;
    }
    private async Task AutoReconnectAsync(ConnectionSecret secret, CancellationTokenSource cancellation)
    {
        var ct = cancellation.Token; IsReconnecting = true;
        try
        {
            for (var attempt = 1; !_disposed && _autoReconnectEnabled && !IsConnected; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                Status = $"已断线 · {_reconnectIntervalSeconds} 秒后重连";
                await Terminal.LogAsync($"{_reconnectIntervalSeconds} 秒后自动重连。", cancellationToken: ct);
                await Task.Delay(TimeSpan.FromSeconds(_reconnectIntervalSeconds), ct);
                while (IsConnecting) await Task.Delay(50, ct);
                await Terminal.LogAsync($"开始第 {attempt} 次自动重连…", cancellationToken: ct);
                await ConnectCoreAsync(secret, ct); ct.ThrowIfCancellationRequested();
                await Terminal.LogAsync(IsConnected ? "自动重连成功。" : $"第 {attempt} 次重连失败：{Status}", error: !IsConnected, cancellationToken: ct);
                if (!_autoReconnectEnabled && !IsConnected) await Terminal.LogAsync("服务器身份验证未通过，已停止自动重连。", error: true, cancellationToken: ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { Status = "自动重连停止：" + ex.Message; _log.LogWarning(ex, "自动重连失败 {ConnectionId}", Profile.Id); }
        finally
        {
            if (ReferenceEquals(_reconnectCancellation, cancellation)) { _reconnectCancellation = null; IsReconnecting = false; }
            cancellation.Dispose();
        }
    }
    [RelayCommand]
    private async Task RenameAsync()
    {
        var name = await _dialogs.PromptAsync("重命名标签", "标签名称（仅当前标签有效，关闭后不保留）", Name);
        if (_disposed || string.IsNullOrWhiteSpace(name)) return;
        _temporaryName = name.Trim();
        OnPropertyChanged(nameof(Name));
    }
    [RelayCommand] private Task CloseAsync() => _close(this);
    [RelayCommand] private void IncreaseFont() => FontSize = Math.Min(28, FontSize + 1);
    [RelayCommand] private void DecreaseFont() => FontSize = Math.Max(10, FontSize - 1);
    [RelayCommand]
    private async Task TerminalDirectoryAsync()
    {
        if (!IsConnected || !Files.IsAvailable) return;
        await ChangeDirectoryAsync(Files.Path);
    }

    /// <summary>把终端切到指定目录。</summary>
    public async Task ChangeDirectoryAsync(string path)
    {
        if (!IsConnected || string.IsNullOrWhiteSpace(path)) return;
        Commands.Input = "cd -- '" + path.Replace("'", "'\"'\"'") + "'";
        await Commands.SendCommand.ExecuteAsync(null);
    }

    /// <summary>在终端打开某个目录：交给外壳新建一个连接并切到该目录。</summary>
    public Task OpenTerminalAtAsync(string path)
        => Files.OpenTerminalHandler == null ? Task.CompletedTask : Files.OpenTerminalHandler(path);

    /// <summary>给标签临时改名，只影响当前标签，不写回连接配置。</summary>
    public void SetTemporaryName(string? name)
    {
        _temporaryName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        OnPropertyChanged(nameof(Name));
    }
    private async Task SendRawAsync(string command)
    {
        var session = Session;
        if (session == null || !IsConnected) throw new InvalidOperationException("请先连接 SSH。");
        var token = _connection!.Token;
        await _terminalWriteGate.WaitAsync(token);
        try
        {
            await session.Terminal.Stream.WriteAsync(Encoding.UTF8.GetBytes(command + "\r"), token);
            await session.Terminal.Stream.FlushAsync(token);
        }
        finally { _terminalWriteGate.Release(); }
    }
    private void RebuildTunnels()
    {
        foreach (var old in Tunnels.Where(t => !Profile.Tunnels.Contains(t.Definition)).ToArray()) Tunnels.Remove(old);
        foreach (var definition in Profile.Tunnels)
        {
            if (Tunnels.Any(t => t.Definition == definition)) continue;
            var tunnel = new TunnelViewModel(definition, ToggleTunnelAsync, RemoveTunnelAsync, CheckTunnelAsync, EditTunnelAsync);
            if (Session != null) tunnel.Apply(Session.Tunnels.GetStatus(definition.Id));
            Tunnels.Insert(Profile.Tunnels.IndexOf(definition), tunnel);
        }
    }
    [RelayCommand]
    private async Task AddTunnelAsync()
    {
        if (await _dialogs.EditTunnelAsync() is not { } definition) return;
        try { var updated = Profile with { Tunnels = [.. Profile.Tunnels, definition] }; await _save(updated); UpdateProfile(updated); }
        catch (Exception ex) { await _dialogs.ShowErrorAsync(ex.Message); }
    }
    private async Task ToggleTunnelAsync(TunnelViewModel tunnel)
    {
        var session = Session;
        if (session == null || !IsConnected) { tunnel.Apply(new(tunnel.Definition.Id, TunnelState.Failed, false, "请先连接 SSH，再启动隧道。")); return; }
        try
        {
            if (tunnel.Running) await session.Tunnels.StopAsync(tunnel.Definition.Id);
            else await session.Tunnels.StartAsync(tunnel.Definition, _connection!.Token);
            if (ReferenceEquals(Session, session)) tunnel.Apply(session.Tunnels.GetStatus(tunnel.Definition.Id));
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(Session, session))
            {
                var state = session.Tunnels.GetStatus(tunnel.Definition.Id);
                tunnel.Apply(state.State == TunnelState.Failed ? state : new(tunnel.Definition.Id, TunnelState.Failed, session.Tunnels.IsRunning(tunnel.Definition.Id), ex.Message));
            }
        }
    }
    private async Task CheckTunnelAsync(TunnelViewModel tunnel)
    {
        var session = Session; if (session == null || !IsConnected) return;
        try { await session.Tunnels.CheckAsync(tunnel.Definition.Id, _connection!.Token); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (ReferenceEquals(Session, session)) tunnel.Apply(new(tunnel.Definition.Id, TunnelState.Failed, session.Tunnels.IsRunning(tunnel.Definition.Id), ex.Message)); }
        if (ReferenceEquals(Session, session)) tunnel.Apply(session.Tunnels.GetStatus(tunnel.Definition.Id));
    }
    /// <summary>一键启动本连接所有未运行的转发规则；单条失败不影响其余规则。</summary>
    [RelayCommand]
    private async Task StartAllTunnelsAsync()
    {
        var session = Session;
        if (session == null || !IsConnected)
        {
            foreach (var tunnel in Tunnels) tunnel.Apply(new(tunnel.Definition.Id, TunnelState.Failed, false, "请先连接 SSH，再启动隧道。"));
            return;
        }
        foreach (var tunnel in Tunnels.ToArray())
        {
            if (tunnel.Running || tunnel.IsBusy) continue;
            try
            {
                await session.Tunnels.StartAsync(tunnel.Definition, _connection?.Token ?? CancellationToken.None);
                if (ReferenceEquals(Session, session)) tunnel.Apply(session.Tunnels.GetStatus(tunnel.Definition.Id));
            }
            catch (Exception ex)
            {
                if (!ReferenceEquals(Session, session)) continue;
                var state = session.Tunnels.GetStatus(tunnel.Definition.Id);
                tunnel.Apply(state.State == TunnelState.Failed ? state : new(tunnel.Definition.Id, TunnelState.Failed, session.Tunnels.IsRunning(tunnel.Definition.Id), ex.Message));
            }
        }
    }

    /// <summary>一键停止本连接所有正在运行或正在启动/检测的转发规则；单条失败不影响其余规则。</summary>
    [RelayCommand]
    private async Task StopAllTunnelsAsync()
    {
        var session = Session;
        if (session == null || !IsConnected) return;
        foreach (var tunnel in Tunnels.ToArray())
        {
            if (!tunnel.Running && !tunnel.IsBusy) continue;
            try
            {
                await session.Tunnels.StopAsync(tunnel.Definition.Id, _connection?.Token ?? CancellationToken.None);
                if (ReferenceEquals(Session, session)) tunnel.Apply(session.Tunnels.GetStatus(tunnel.Definition.Id));
            }
            catch (Exception ex)
            {
                if (!ReferenceEquals(Session, session)) continue;
                var status = session.Tunnels.GetStatus(tunnel.Definition.Id);
                tunnel.Apply(status.IsRunning
                    ? status with { State = TunnelState.Failed, Reason = ex.Message }
                    : status);
            }
        }
    }
    private async Task RemoveTunnelAsync(TunnelViewModel tunnel)
    {
        if (!await _dialogs.ConfirmAsync("删除隧道规则", $"确认删除规则“{tunnel.Name}”？删除后该转发不再自动启动，正在运行的监听会立即停止。", true)) return;
        try
        {
            if (Session != null) await Session.Tunnels.StopAsync(tunnel.Definition.Id);
            var updated = Profile with { Tunnels = Profile.Tunnels.Where(t => t.Id != tunnel.Definition.Id).ToList() };
            await _save(updated); UpdateProfile(updated);
        }
        catch (Exception ex) { await _dialogs.ShowErrorAsync(ex.Message); }
    }
    private async Task EditTunnelAsync(TunnelViewModel tunnel)
    {
        var saved = false;
        try
        {
            if (await _dialogs.EditTunnelAsync(tunnel.Definition) is not { } definition) return;
            definition = definition with { Id = tunnel.Definition.Id }; definition.Validate();
            if (definition == tunnel.Definition) return;
            if (!Profile.Tunnels.Contains(tunnel.Definition)) throw new InvalidOperationException("此规则已发生变化，请重新打开编辑。");
            var updated = Profile with { Tunnels = Profile.Tunnels.Select(t => t.Id == definition.Id ? definition : t).ToList() };
            await _save(updated); UpdateProfile(updated); saved = true;
            var session = Session;
            if (session != null && IsConnected)
            {
                await session.Tunnels.UpdateAsync(definition, _connection!.Token);
                if (ReferenceEquals(Session, session)) Tunnels.FirstOrDefault(t => t.Definition.Id == definition.Id)?.Apply(session.Tunnels.GetStatus(definition.Id));
            }
        }
        catch (OperationCanceledException) when (_disposed || !IsConnected) { }
        catch (Exception ex) { await _dialogs.ShowErrorAsync((saved ? "规则已保存，但应用失败：\n" : "规则未能保存：\n") + ex.Message); }
    }
    /// <summary>隧道窗口的“失败自动重试”开关：写回连接配置并持久化。</summary>
    public async Task SetTunnelAutoRetryAsync(bool enabled, int seconds)
    {
        var updated = Profile with { TunnelAutoRetry = enabled, TunnelRetrySeconds = Math.Clamp(seconds, 3, 600) };
        if (updated.TunnelAutoRetry == Profile.TunnelAutoRetry && updated.TunnelRetrySeconds == Profile.TunnelRetrySeconds) return;
        await _save(updated); UpdateProfile(updated);
    }
    private void EnsureTunnelRetryLoop()
    {
        if (_disposed || _tunnelRetryCancellation != null) return;
        var cancellation = _tunnelRetryCancellation = new CancellationTokenSource();
        _ = TunnelRetryLoopAsync(cancellation.Token);   // 后台循环：关闭标签时只取消，不等待它结束
    }
    /// <summary>定时重试：监听没建立的重新拉起，监听仍在的复检目标连通性。</summary>
    private async Task TunnelRetryLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
                if (_disposed) break;
                try { await RetryFailedTunnelsAsync(ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex) { _log.LogWarning(ex, "隧道自动重试异常 {ConnectionId}", Profile.Id); }
            }
        }
        catch (OperationCanceledException) { }
    }
    private async Task RetryFailedTunnelsAsync(CancellationToken ct)
    {
        var session = Session;
        var now = DateTimeOffset.Now;
        if (session == null || !IsConnected || !Profile.TunnelAutoRetry)
        {
            foreach (var tunnel in Tunnels) { _tunnelRetryAt.Remove(tunnel.Definition.Id); tunnel.SetRetry(null); }
            return;
        }
        var interval = Math.Clamp(Profile.TunnelRetrySeconds, 3, 600);
        foreach (var tunnel in Tunnels.ToArray())
        {
            if (ct.IsCancellationRequested) return;
            if (tunnel.State != TunnelState.Failed || tunnel.IsBusy)
            {
                _tunnelRetryAt.Remove(tunnel.Definition.Id); tunnel.SetRetry(null); continue;
            }
            // 监听仍在说明只是目标不通，复检连通性即可；否则重新建立监听。
            var probe = tunnel.Running;
            if (!_tunnelRetryAt.TryGetValue(tunnel.Definition.Id, out var next))
            {
                next = now.AddSeconds(interval); _tunnelRetryAt[tunnel.Definition.Id] = next;
                var reason = tunnel.HasReason ? tunnel.Reason : "未知原因";
                Terminal.TryLog($"隧道“{tunnel.Name}”{(probe ? "连通检测失败" : "启动失败")}，{interval} 秒后自动重试：{reason}", error: true);
            }
            if (next > now) { tunnel.SetRetry(next, probe); continue; }
            _tunnelRetryAt[tunnel.Definition.Id] = now.AddSeconds(interval); tunnel.SetRetry(null);
            try
            {
                if (probe) await session.Tunnels.CheckAsync(tunnel.Definition.Id, ct);
                else await session.Tunnels.StartAsync(tunnel.Definition, ct);
            }
            catch (Exception ex) { _log.LogWarning(ex, "隧道自动重试失败 {TunnelId}", tunnel.Definition.Id); }
            if (!ReferenceEquals(Session, session)) return;
            var status = session.Tunnels.GetStatus(tunnel.Definition.Id);
            tunnel.Apply(status);
            if (status.State == TunnelState.Failed)
                Terminal.TryLog($"隧道“{tunnel.Name}”重试仍然失败：{status.Reason}", error: true);
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        // 只取消不等待：重试循环的续体排在界面线程上，在这里等待会与界面线程互相等待。
        _tunnelRetryCancellation?.Cancel();
        await DisconnectAsync(); await _monitorLease.DisposeAsync(); await _sessions.DisposeAsync();
        await Terminal.DisposeAsync(); Files.Dispose();
        Disposed?.Invoke(this, EventArgs.Empty);
    }
}
