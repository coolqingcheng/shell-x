using System.Net;
using System.Net.Sockets;
using Renci.SshNet;
using Renci.SshNet.Common;
using ShellX.Core.Tunnels;

namespace ShellX.Infrastructure.Ssh;

internal sealed class SshTunnelService : ITunnelService, IDisposable
{
    private sealed class Runtime(TunnelDefinition definition, ForwardedPort port)
    {
        public TunnelDefinition Definition { get; } = definition;
        public ForwardedPort Port { get; } = port;
        public long ErrorVersion;
        public string LastError = "";
        public long LastRequestCheck;
    }
    private readonly SshClient _ssh;
    private readonly Dictionary<Guid, Runtime> _ports = [];
    private readonly Dictionary<Guid, TunnelStatus> _statuses = [];
    private readonly object _sync = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _lifetimeToken;
    private readonly Task _monitor;
    private bool _disposed;
    private string? _disconnectedReason;
    public event EventHandler<TunnelStatus>? StatusChanged;

    public SshTunnelService(SshClient ssh)
    { _ssh = ssh; _lifetimeToken = _lifetime.Token; _monitor = MonitorAsync(); }

    public bool IsRunning(Guid id)
    { lock (_sync) return _disconnectedReason == null && _ports.TryGetValue(id, out var runtime) && runtime.Port.IsStarted; }
    public TunnelStatus GetStatus(Guid id)
    { lock (_sync) return _statuses.GetValueOrDefault(id) ?? new(id, TunnelState.Stopped, false); }
    private void Publish(TunnelStatus status, Runtime? runtime = null, long? expectedErrorVersion = null)
    {
        lock (_sync)
        {
            if (_disposed) return;
            if (runtime != null && (!_ports.TryGetValue(status.TunnelId, out var current) || !ReferenceEquals(current, runtime))) return;
            if (runtime != null && expectedErrorVersion.HasValue && Interlocked.Read(ref runtime.ErrorVersion) != expectedErrorVersion.Value)
                status = status with { State = TunnelState.Failed, IsRunning = runtime.Port.IsStarted, Reason = runtime.LastError, CheckedAt = DateTimeOffset.Now };
            if (_disconnectedReason != null && status.State != TunnelState.Stopped)
                status = status with { State = TunnelState.Failed, IsRunning = false, Reason = _disconnectedReason };
            _statuses[status.TunnelId] = status;
        }
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
            if (previous != null) await Task.Run(() => Release(previous.Port)).ConfigureAwait(false);
            await Task.Run(() =>
            {
                ForwardedPort port = definition.Direction == TunnelDirection.Local
                    ? new ForwardedPortLocal(definition.BindHost, (uint)definition.BindPort, definition.TargetHost, (uint)definition.TargetPort)
                    : new ForwardedPortRemote(definition.BindHost, (uint)definition.BindPort, definition.TargetHost, (uint)definition.TargetPort);
                var runtime = new Runtime(definition, port);
                port.Exception += (_, e) => OnPortError(runtime, e.Exception);
                port.RequestReceived += (_, _) => RequestCheck(runtime);
                try
                {
                    _ssh.AddForwardedPort(port); port.Start(); linked.Token.ThrowIfCancellationRequested();
                    lock (_sync) _ports[definition.Id] = runtime;
                }
                catch { Release(port); throw; }
            }, linked.Token).ConfigureAwait(false);
            Publish(new(definition.Id, TunnelState.Checking, true, "监听已启动，正在检测目标 TCP 连接。"));
        }
        catch (OperationCanceledException)
        { Publish(new(definition.Id, _disconnectedReason == null ? TunnelState.Stopped : TunnelState.Failed, false, _disconnectedReason ?? "启动已取消。")); throw; }
        catch (Exception ex)
        { Publish(new(definition.Id, TunnelState.Failed, false, Describe(ex, definition, starting: true))); throw; }
        finally { _gate.Release(); }
        try { await CheckAsync(definition.Id, cancellationToken).ConfigureAwait(false); }
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

    private void RequestCheck(Runtime runtime)
    {
        lock (_sync)
        {
            if (_disposed || _lifetimeToken.IsCancellationRequested || !_ports.TryGetValue(runtime.Definition.Id, out var current) ||
                !ReferenceEquals(current, runtime) || GetStatus(runtime.Definition.Id).State == TunnelState.Checking) return;
            var now = Environment.TickCount64;
            if (now - runtime.LastRequestCheck < 2000) return;
            runtime.LastRequestCheck = now;
        }
        // SSH.NET 不会把所有 channel-open 拒绝转为 Exception 事件，实际连接到达时再检查目标。
        _ = Task.Run(async () =>
        {
            try { await CheckAsync(runtime.Definition.Id, _lifetimeToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Publish(new(runtime.Definition.Id, TunnelState.Failed, IsRunning(runtime.Definition.Id), ex.Message)); }
        });
    }

    public async Task CheckAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            Runtime? runtime; lock (_sync) _ports.TryGetValue(id, out runtime);
            if (runtime == null) return;
            var definition = runtime.Definition;
            if (!_ssh.IsConnected || !runtime.Port.IsStarted)
            { Publish(new(id, TunnelState.Failed, false, "SSH 已断开或监听已停止，请重新启动隧道。", DateTimeOffset.Now)); return; }
            var errorVersion = Interlocked.Read(ref runtime.ErrorVersion);
            Publish(new(id, TunnelState.Checking, true));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                if (definition.Direction == TunnelDirection.Local)
                    await TunnelTcpProbe.ThroughSshAsync(_ssh, definition.TargetHost, definition.TargetPort, timeout.Token).ConfigureAwait(false);
                else
                {
                    var remote = (ForwardedPortRemote)runtime.Port;
                    await TunnelTcpProbe.LocalAsync(remote.Host, definition.TargetPort, timeout.Token).ConfigureAwait(false);
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
            if (runtime != null) await Task.Run(() => Release(runtime.Port)).ConfigureAwait(false);
            Publish(new(id, TunnelState.Stopped, false));
        }
        finally { _gate.Release(); }
    }
    private void Release(ForwardedPort port)
    {
        try { port.Stop(); }
        finally { try { _ssh.RemoveForwardedPort(port); } finally { port.Dispose(); } }
    }
    private async Task MonitorAsync()
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), _lifetimeToken).ConfigureAwait(false);
                Guid[] ids; lock (_sync) ids = _ports.Keys.ToArray();
                foreach (var id in ids) await CheckAsync(id, _lifetimeToken).ConfigureAwait(false);
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
        _lifetime.Cancel(); _monitor.GetAwaiter().GetResult(); _gate.Wait();
        try
        {
            _disposed = true;
            Runtime[] ports; lock (_sync) { ports = _ports.Values.ToArray(); _ports.Clear(); }
            foreach (var runtime in ports) { try { Release(runtime.Port); } catch (Exception) { } }
        }
        finally { _gate.Release(); _lifetime.Dispose(); }
    }
}
