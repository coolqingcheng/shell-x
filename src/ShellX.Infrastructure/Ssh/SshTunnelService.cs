using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Renci.SshNet.Common;
using ShellX.Core.Tunnels;

namespace ShellX.Infrastructure.Ssh;

internal sealed class SshTunnelService : ITunnelService, IDisposable
{
    private sealed class Runtime(TunnelDefinition definition, ForwardedPort port, TunnelRelay relay)
    {
        public TunnelDefinition Definition { get; } = definition;
        public ForwardedPort Port { get; } = port;
        public TunnelRelay Relay { get; } = relay;
        public long ErrorVersion;
        public string LastError = "";
        // 流量采样基线：上一秒的累计字节与是否有流量，避免空闲时每秒打扰界面。
        public long LastSent;
        public long LastReceived;
        public bool TrafficActive;
    }
    private readonly SshClient _ssh;
    private readonly ILogger<SshTunnelService> _log;
    private readonly Dictionary<Guid, Runtime> _ports = [];
    private readonly Dictionary<Guid, TunnelStatus> _statuses = [];
    private readonly object _sync = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _lifetimeToken;
    private readonly Task _monitor;
    private readonly Task _traffic;
    private bool _disposed;
    private string? _disconnectedReason;
    public event EventHandler<TunnelStatus>? StatusChanged;

    public SshTunnelService(SshClient ssh, ILogger<SshTunnelService> log)
    { _ssh = ssh; _log = log; _lifetimeToken = _lifetime.Token; _monitor = MonitorAsync(); _traffic = TrafficLoopAsync(); }

    public bool IsRunning(Guid id)
    { lock (_sync) return _disconnectedReason == null && _ports.TryGetValue(id, out var runtime) && runtime.Port.IsStarted && runtime.Relay.IsListening; }
    public TunnelStatus GetStatus(Guid id)
    { lock (_sync) return _statuses.GetValueOrDefault(id) ?? new(id, TunnelState.Stopped, false); }
    private void Publish(TunnelStatus status, Runtime? runtime = null, long? expectedErrorVersion = null)
    {
        bool changed;
        lock (_sync)
        {
            if (_disposed) return;
            if (runtime != null && (!_ports.TryGetValue(status.TunnelId, out var current) || !ReferenceEquals(current, runtime))) return;
            if (runtime != null && expectedErrorVersion.HasValue && Interlocked.Read(ref runtime.ErrorVersion) != expectedErrorVersion.Value)
                status = status with { State = TunnelState.Failed, IsRunning = runtime.Port.IsStarted, Reason = runtime.LastError, CheckedAt = DateTimeOffset.Now };
            if (_disconnectedReason != null && status.State != TunnelState.Stopped)
                status = status with { State = TunnelState.Failed, IsRunning = false, Reason = _disconnectedReason };
            changed = !_statuses.TryGetValue(status.TunnelId, out var previous) || previous.State != status.State ||
                previous.IsRunning != status.IsRunning || previous.Reason != status.Reason;
            _statuses[status.TunnelId] = status;
        }
        if (changed && status.State == TunnelState.Failed)
            _log.LogWarning("隧道 {TunnelId} 异常（监听运行：{IsRunning}）：{Reason}", status.TunnelId, status.IsRunning, status.Reason);
        StatusChanged?.Invoke(this, status);
    }

    public async Task StartAsync(TunnelDefinition definition, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this); definition.Validate();
            if (IsRunning(definition.Id)) return;
            Publish(new(definition.Id, TunnelState.Starting, false));
            if (!_ssh.IsConnected || _disconnectedReason != null) throw new SshConnectionException("SSH 会话已断开，请重新连接。");
            Runtime? conflict;
            lock (_sync) conflict = _ports.Values.FirstOrDefault(r => r.Port.IsStarted && r.Definition.Id != definition.Id &&
                r.Definition.Direction == definition.Direction && r.Definition.BindPort == definition.BindPort && Overlaps(r.Definition.BindHost, definition.BindHost));
            if (conflict != null) throw new IOException($"监听冲突：{definition.BindHost}:{definition.BindPort} 已由隧道“{conflict.Definition.Name}”使用，请修改监听地址或端口。");
            Runtime? previous; lock (_sync) _ports.Remove(definition.Id, out previous);
            if (previous != null) await Task.Run(() => Release(previous)).ConfigureAwait(false);
            var (port, relay) = await Task.Run(() => CreateTransport(definition), linked.Token).ConfigureAwait(false);
            var runtime = new Runtime(definition, port, relay);
            port.Exception += (_, e) => OnPortError(runtime, e.Exception);
            if (linked.Token.IsCancellationRequested)
            {
                await Task.Run(() => Release(runtime)).ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested();
            }
            lock (_sync) _ports[definition.Id] = runtime;
            Publish(new(definition.Id, TunnelState.Checking, true, "监听已启动，正在检测目标 TCP 连接。"));
        }
        catch (OperationCanceledException)
        { Publish(new(definition.Id, _disconnectedReason == null ? TunnelState.Stopped : TunnelState.Failed, false, _disconnectedReason ?? "启动已取消。")); throw; }
        catch (Exception ex)
        { Publish(new(definition.Id, TunnelState.Failed, false, Describe(ex, definition, starting: true))); throw; }
        finally { _gate.Release(); }
        try { await CheckCoreAsync(definition.Id, cancellationToken, false).ConfigureAwait(false); }
        catch (OperationCanceledException)
        { Publish(new(definition.Id, TunnelState.Unverified, IsRunning(definition.Id), "检测已取消，监听状态以当前会话为准。")); throw; }
    }

    private static bool Overlaps(string first, string second)
    {
        if (string.Equals(first, second, StringComparison.OrdinalIgnoreCase)) return true;
        // 仅判断确定重叠的地址；主机名别名与跨会话冲突交由实际绑定结果识别。
        if (first == "localhost") first = "127.0.0.1";
        if (second == "localhost") second = "127.0.0.1";
        if (!IPAddress.TryParse(first, out var a) || !IPAddress.TryParse(second, out var b)) return first == second;
        return a.Equals(b) || a.AddressFamily == b.AddressFamily && (a.Equals(IPAddress.Any) || b.Equals(IPAddress.Any) || a.Equals(IPAddress.IPv6Any) || b.Equals(IPAddress.IPv6Any));
    }

    private void OnPortError(Runtime runtime, Exception error)
    {
        lock (_sync)
        {
            if (!_ports.TryGetValue(runtime.Definition.Id, out var current) || !ReferenceEquals(current, runtime)) return;
            Interlocked.Increment(ref runtime.ErrorVersion);
            runtime.LastError = Describe(error, runtime.Definition);
        }
        Publish(new(runtime.Definition.Id, TunnelState.Failed, runtime.Port.IsStarted, runtime.LastError, DateTimeOffset.Now), runtime);
    }

    public Task CheckAsync(Guid id, CancellationToken cancellationToken = default)
        => CheckCoreAsync(id, cancellationToken, true);

    private async Task CheckCoreAsync(Guid id, CancellationToken cancellationToken, bool publishProgress)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            Runtime? runtime; lock (_sync) _ports.TryGetValue(id, out runtime);
            if (runtime == null) return;
            var definition = runtime.Definition;
            if (!_ssh.IsConnected || !runtime.Port.IsStarted || !runtime.Relay.IsListening)
            { Publish(new(id, TunnelState.Failed, false, "SSH 已断开或监听已停止，请重新启动隧道。", DateTimeOffset.Now)); return; }
            var errorVersion = Interlocked.Read(ref runtime.ErrorVersion);
            if (publishProgress) Publish(new(id, TunnelState.Checking, true));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                if (definition.Direction == TunnelDirection.Local)
                    await TunnelTcpProbe.ThroughSshAsync(_ssh, definition.TargetHost, definition.TargetPort, timeout.Token).ConfigureAwait(false);
                else
                {
                    var remote = (ForwardedPortRemote)runtime.Port;
                    // 中转接管后 Port.Host 指向本机中转地址，本机目标可达性按规则里的目标直接检测。
                    await TunnelTcpProbe.LocalAsync(definition.TargetHost, definition.TargetPort, timeout.Token).ConfigureAwait(false);
                    var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    void Incoming(object? sender, PortForwardEventArgs args) => received.TrySetResult();
                    runtime.Port.RequestReceived += Incoming;
                    try
                    {
                        var bind = remote.BoundHostAddress;
                        var address = bind.Equals(IPAddress.Any) ? "127.0.0.1" : bind.Equals(IPAddress.IPv6Any) ? "::1" : remote.BoundHost;
                        await TunnelTcpProbe.ThroughSshAsync(_ssh, address, definition.BindPort, timeout.Token).ConfigureAwait(false);
                        await received.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                    catch (TunnelTcpProbe.ProbeException ex)
                    {
                        Publish(new(id, TunnelState.Unverified, true, "远程监听已建立，本机目标 TCP 可达；检测连接被拒绝或入口不可达，尚未确认远程入口。\n" + ex.Message, DateTimeOffset.Now), runtime, errorVersion);
                        return;
                    }
                    finally { runtime.Port.RequestReceived -= Incoming; }
                }
                linked.Token.ThrowIfCancellationRequested();
                Publish(new(id, TunnelState.Connected, true, definition.Direction == TunnelDirection.Local
                        ? $"本地监听已启动，经 SSH 可连接目标 {definition.TargetHost}:{definition.TargetPort}。"
                        : $"远程监听可访问，反向转发请求已到达，本机目标 {definition.TargetHost}:{definition.TargetPort} TCP 可达。", DateTimeOffset.Now), runtime, errorVersion);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            { if (_disconnectedReason == null) Publish(new(id, TunnelState.Unverified, runtime.Port.IsStarted, "检测已取消，尚未确认目标连通。")); }
            catch (OperationCanceledException)
            { Publish(new(id, TunnelState.Failed, runtime.Port.IsStarted, "连通检测超时（5 秒），目标或 SSH 转发请求未响应。", DateTimeOffset.Now)); }
            catch (Exception ex)
            { Publish(new(id, TunnelState.Failed, runtime.Port.IsStarted, Describe(ex, definition), DateTimeOffset.Now)); }
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Runtime? runtime; lock (_sync) _ports.Remove(id, out runtime);
            if (runtime != null) await Task.Run(() => Release(runtime)).ConfigureAwait(false);
            Publish(new(id, TunnelState.Stopped, false));
        }
        finally { _gate.Release(); }
    }
    private void Release(Runtime runtime)
    {
        try { runtime.Relay.Dispose(); }
        finally
        {
            try { runtime.Port.Stop(); }
            finally { try { _ssh.RemoveForwardedPort(runtime.Port); } finally { runtime.Port.Dispose(); } }
        }
    }

    // 每条规则都由一个中转持有对外监听以便统计流量；SSH 侧只建立到下一跳的转发，目标连接只在真正有客户端接入时才建立。
    private (ForwardedPort Port, TunnelRelay Relay) CreateTransport(TunnelDefinition definition)
    {
        if (definition.Direction == TunnelDirection.Local)
        {
            // SSH 侧只开一条仅本机可见的临时转发，对外端口由中转监听。
            var inner = new ForwardedPortLocal("127.0.0.1", (uint)FreeLoopbackPort(), definition.TargetHost, (uint)definition.TargetPort);
            try { _ssh.AddForwardedPort(inner); inner.Start(); }
            catch { StopForward(inner); throw; }
            var relay = new TunnelRelay(definition.BindHost, definition.BindPort, token => ConnectAsync("127.0.0.1", (int)inner.BoundPort, token));
            try { relay.Start(); }
            catch { StopForward(inner); throw; }
            return (inner, relay);
        }
        else
        {
            // 远程转发由 SSH 服务端回连本机中转，中转再连规则里的目标。
            var relay = new TunnelRelay("127.0.0.1", 0, token => ConnectAsync(definition.TargetHost, definition.TargetPort, token));
            int relayPort;
            try { relayPort = relay.Start(); }
            catch { relay.Dispose(); throw; }
            var remote = new ForwardedPortRemote(definition.BindHost, (uint)definition.BindPort, "127.0.0.1", (uint)relayPort);
            try { _ssh.AddForwardedPort(remote); remote.Start(); }
            catch { StopForward(remote); relay.Dispose(); throw; }
            return (remote, relay);
        }
    }

    private void StopForward(ForwardedPort port)
    {
        try { port.Stop(); }
        finally { try { _ssh.RemoveForwardedPort(port); } finally { port.Dispose(); } }
    }

    private static int FreeLoopbackPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try { return ((IPEndPoint)probe.LocalEndpoint).Port; }
        finally { probe.Stop(); }
    }

    private static async Task<Socket> ConnectAsync(string host, int port, CancellationToken token)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try { await socket.ConnectAsync(host, port, token).ConfigureAwait(false); return socket; }
        catch { socket.Dispose(); throw; }
    }
    private async Task MonitorAsync()
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), _lifetimeToken).ConfigureAwait(false);
                // 后台只检查 SSH 和监听器，不向数据库等目标反复建立无协议握手的连接。
                // 多个监听端口可指向同一目标，不能为每条规则额外制造连接负载。
                if (!_ssh.IsConnected) { NotifyDisconnected("SSH 传输连接已关闭。"); return; }
                Runtime[] ports; lock (_sync) ports = _ports.Values.ToArray();
                foreach (var runtime in ports)
                    if (!runtime.Port.IsStarted || !runtime.Relay.IsListening)
                        Publish(new(runtime.Definition.Id, TunnelState.Failed, false, "监听已停止，请重新启动隧道。", DateTimeOffset.Now), runtime);
            }
        }
        catch (OperationCanceledException) { }
    }


    // 每秒采样一次上下行字节；只有出现流量或刚停止流量时才发布状态，空闲时不打扰界面。
    private async Task TrafficLoopAsync()
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), _lifetimeToken).ConfigureAwait(false);
                Runtime[] ports; lock (_sync) ports = _ports.Values.ToArray();
                foreach (var runtime in ports)
                {
                    var sent = runtime.Relay.BytesSent;
                    var received = runtime.Relay.BytesReceived;
                    TunnelStatus? current;
                    long lastSent, lastReceived;
                    bool wasActive;
                    lock (_sync)
                    {
                        lastSent = runtime.LastSent; lastReceived = runtime.LastReceived; wasActive = runtime.TrafficActive;
                        runtime.LastSent = sent; runtime.LastReceived = received;
                        current = _statuses.GetValueOrDefault(runtime.Definition.Id);
                    }
                    var sentRate = Math.Max(0, sent - lastSent);
                    var receivedRate = Math.Max(0, received - lastReceived);
                    var active = sentRate > 0 || receivedRate > 0;
                    if (!active && !wasActive) continue;
                    lock (_sync) runtime.TrafficActive = active;
                    if (current == null || !current.IsRunning) continue;
                    Publish(current with { BytesSent = sent, BytesReceived = received, SentRate = sentRate, ReceivedRate = receivedRate }, runtime);
                }
            }
        }
        catch (OperationCanceledException) { }
    }
    internal void NotifyDisconnected(string reason)
    {
        Guid[] ids; lock (_sync) { if (_disposed) return; _disconnectedReason = "SSH 会话已断开：" + reason; ids = _ports.Keys.ToArray(); }
        try { _lifetime.Cancel(); } catch (ObjectDisposedException) { }
        foreach (var id in ids) Publish(new(id, TunnelState.Failed, false, _disconnectedReason, DateTimeOffset.Now));
    }

    private static string Describe(Exception error, TunnelDefinition definition, bool starting = false)
    {
        var socket = error as SocketException ?? error.InnerException as SocketException;
        if (socket != null)
        {
            var endpoint = starting ? $"{definition.BindHost}:{definition.BindPort}" : $"{definition.TargetHost}:{definition.TargetPort}";
            return socket.SocketErrorCode switch
            {
                SocketError.AddressAlreadyInUse => $"端口冲突：监听地址 {endpoint} 已被其他程序或隧道占用。",
                SocketError.AddressNotAvailable when starting => $"无法绑定监听地址 {endpoint}，该地址不属于当前可用的本机接口。",
                SocketError.AccessDenied => $"无权使用地址 {endpoint}，端口可能被系统保留或当前用户没有绑定权限。\n{socket.Message}",
                SocketError.ConnectionRefused => $"目标 {endpoint} 拒绝 TCP 连接，请确认目标服务已启动。",
                SocketError.HostNotFound or SocketError.NoData => $"无法解析地址 {endpoint}：{socket.Message}",
                SocketError.TimedOut => $"连接 {endpoint} 超时。",
                SocketError.HostUnreachable or SocketError.NetworkUnreachable => $"目标 {endpoint} 网络不可达。",
                _ => $"连接 {endpoint} 失败：{socket.Message}"
            };
        }
        if (starting && definition.Direction == TunnelDirection.Remote && error is SshException && error.Message.Contains("failed to start", StringComparison.OrdinalIgnoreCase))
            return $"SSH 服务端拒绝监听 {definition.BindHost}:{definition.BindPort}。服务端未返回细分原因，可能是远程端口占用或转发策略限制。\n原始错误：{error.Message}";
        var detail = error.Message;
        if (detail.Contains("refused", StringComparison.OrdinalIgnoreCase)) return $"目标 {definition.TargetHost}:{definition.TargetPort} 拒绝 TCP 连接。\n{detail}";
        if (detail.Contains("administratively", StringComparison.OrdinalIgnoreCase) || detail.Contains("prohibited", StringComparison.OrdinalIgnoreCase)) return "SSH 服务端禁止此转发连接，请检查转发权限。\n" + detail;
        if (error is TunnelTcpProbe.ProbeException) return $"目标 {definition.TargetHost}:{definition.TargetPort} 未通过连通检测：{detail}";
        return detail;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _lifetime.Cancel(); _monitor.GetAwaiter().GetResult(); _traffic.GetAwaiter().GetResult(); _gate.Wait();
        try
        {
            _disposed = true;
            Runtime[] ports; lock (_sync) { ports = _ports.Values.ToArray(); _ports.Clear(); }
            foreach (var runtime in ports) { try { Release(runtime); } catch (Exception) { } }
        }
        finally { _gate.Release(); _lifetime.Dispose(); }
    }
}
