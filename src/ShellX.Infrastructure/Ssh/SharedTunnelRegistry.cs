using Renci.SshNet;
using Renci.SshNet.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShellX.Core.Connections;
using ShellX.Core.Tunnels;

namespace ShellX.Infrastructure.Ssh;

// 终端和 SFTP 属于标签；隧道传输连接由同一 SSH 地址的所有标签共同持有。
public sealed class SharedTunnelRegistry
{
    private readonly ILoggerFactory _logs;
    private readonly object _sync = new();
    private readonly Dictionary<ConnectionEndpoint, Group> _groups = [];
    public SharedTunnelRegistry(ILoggerFactory? logs = null) { _logs = logs ?? NullLoggerFactory.Instance; }

    internal Lease Acquire(ConnectionProfile profile, SshClient ssh, PrivateKeyFile? key)
    {
        var endpoint = ConnectionEndpoint.From(profile);
        lock (_sync)
        {
            if (!_groups.TryGetValue(endpoint, out var group))
            {
                group = new Group(ssh, key, _logs); _groups.Add(endpoint, group);
            }
            else if (!group.IsConnected)
            {
                group.ReplaceTransport(ssh, key);
            }
            else group.AddSession(ssh, key);
            group.Users++;
            return new Lease(group, () =>
            {
                lock (_sync)
                {
                    try { group.ReleaseSession(ssh); }
                    finally
                    {
                        if (--group.Users == 0) { _groups.Remove(endpoint); group.Dispose(); }
                    }
                }
            });
        }
    }

    internal sealed class Lease : ITunnelService, IDisposable
    {
        private readonly Group _group;
        private readonly Action _release;
        private int _disposed;
        public event EventHandler<TunnelStatus>? StatusChanged;
        internal Lease(Group group, Action release)
        { _group = group; _release = release; group.StatusChanged += Forward; }
        private void Forward(object? sender, TunnelStatus state) => StatusChanged?.Invoke(this, state);
        public bool IsRunning(Guid id) => _group.Service.IsRunning(id);
        public TunnelStatus GetStatus(Guid id) => _group.Service.GetStatus(id);
        public Task StartAsync(TunnelDefinition definition, CancellationToken cancellationToken = default)
            => _group.StartAsync(definition, false, cancellationToken);
        public Task AutoStartAsync(TunnelDefinition definition, CancellationToken cancellationToken = default)
            => _group.StartAsync(definition, true, cancellationToken);
        public Task StopAsync(Guid id, CancellationToken cancellationToken = default)
            => _group.StopAsync(id, cancellationToken);
        public Task CheckAsync(Guid id, CancellationToken cancellationToken = default)
            => _group.RunAsync(service => service.CheckAsync(id, cancellationToken), cancellationToken);
        public Task UpdateAsync(TunnelDefinition definition, CancellationToken cancellationToken = default)
            => _group.UpdateAsync(definition, cancellationToken);
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _group.StatusChanged -= Forward; _release();
        }
    }

    internal sealed class Group : IDisposable
    {
        private readonly SemaphoreSlim _operations = new(1, 1);
        private readonly HashSet<Guid> _initialized = [];
        private readonly Dictionary<Guid, TunnelDefinition> _enabled = [];
        private readonly Dictionary<SshClient, PrivateKeyFile?> _sessions = [];
        private readonly ILoggerFactory _logs;
        private Task _restoring = Task.CompletedTask;
        private SshClient _ssh;
        private PrivateKeyFile? _key;
        private SshTunnelService _service;
        private bool _disposed;
        public int Users;
        public bool IsConnected => _ssh.IsConnected;
        public SshTunnelService Service => Volatile.Read(ref _service);
        public event EventHandler<TunnelStatus>? StatusChanged;
        public Group(SshClient ssh, PrivateKeyFile? key, ILoggerFactory logs)
        {
            _logs = logs; _ssh = ssh; _key = key; _service = new(ssh, logs.CreateLogger<SshTunnelService>());
            _sessions.Add(ssh, key);
            _service.StatusChanged += Forward; ssh.ErrorOccurred += OnError;
        }
        private void Forward(object? sender, TunnelStatus state)
        {
            StatusChanged?.Invoke(this, state);
            if (state.State == TunnelState.Failed && !state.IsRunning && !_ssh.IsConnected)
            {
                var failed = _ssh;
                _ = Task.Run(() => RecoverTransportAsync(failed));
            }
        }
        private void OnError(object? sender, ExceptionEventArgs error)
        {
            if (sender is not SshClient failed || !ReferenceEquals(failed, _ssh)) return;
            Service.NotifyDisconnected(error.Exception.Message);
            // 原传输所属标签可能已关闭；借用仍连通标签的 SSH 恢复，无需重建其终端。
            _ = Task.Run(() => RecoverTransportAsync(failed));
        }
        public void AddSession(SshClient ssh, PrivateKeyFile? key)
        {
            _operations.Wait();
            try { _sessions.Add(ssh, key); }
            finally { _operations.Release(); }
        }
        public void ReleaseSession(SshClient ssh)
        {
            _operations.Wait();
            try
            {
                if (_sessions.Remove(ssh, out var key) && !ReferenceEquals(ssh, _ssh))
                    try { ssh.Dispose(); } finally { key?.Dispose(); }
            }
            finally { _operations.Release(); }
        }
        private async Task RecoverTransportAsync(SshClient failed)
        {
            await _operations.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed || !ReferenceEquals(failed, _ssh)) return;
                var candidate = _sessions.FirstOrDefault(pair => !ReferenceEquals(pair.Key, failed) && pair.Key.IsConnected);
                if (candidate.Key != null) ReplaceTransportCore(candidate.Key, candidate.Value);
            }
            catch (Exception ex) { _logs.CreateLogger<SharedTunnelRegistry>().LogWarning(ex, "共享隧道传输恢复失败"); }
            finally { _operations.Release(); }
        }

        public async Task StartAsync(TunnelDefinition definition, bool automatic, CancellationToken token)
        {
            await _restoring.WaitAsync(token).ConfigureAwait(false);
            await _operations.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                // 新标签保留共享的停止/失败状态，不重新占用端口或撤销其他标签的停止操作。
                if (automatic && _initialized.Contains(definition.Id)) return;
                _initialized.Add(definition.Id);
                try { await Service.StartAsync(definition, token).ConfigureAwait(false); }
                finally
                {
                    if (Service.IsRunning(definition.Id)) _enabled[definition.Id] = definition;
                    else _enabled.Remove(definition.Id);
                }
            }
            finally { _operations.Release(); }
        }
        public async Task RunAsync(Func<SshTunnelService, Task> operation, CancellationToken token)
        {
            await _restoring.WaitAsync(token).ConfigureAwait(false);
            await _operations.WaitAsync(token).ConfigureAwait(false);
            try { ObjectDisposedException.ThrowIf(_disposed, this); await operation(Service).ConfigureAwait(false); }
            finally { _operations.Release(); }
        }
        public Task StopAsync(Guid id, CancellationToken token)
            => RunAsync(async service =>
            {
                _enabled.Remove(id);
                await service.StopAsync(id, token).ConfigureAwait(false);
            }, token);
        public Task UpdateAsync(TunnelDefinition definition, CancellationToken token)
            => RunAsync(async service =>
            {
                definition.Validate(); _initialized.Add(definition.Id);
                var running = service.IsRunning(definition.Id);
                await service.StopAsync(definition.Id, token).ConfigureAwait(false);
                _enabled.Remove(definition.Id);
                if (running)
                {
                    try { await service.StartAsync(definition, token).ConfigureAwait(false); }
                    finally { if (service.IsRunning(definition.Id)) _enabled[definition.Id] = definition; }
                }
            }, token);
        public void ReplaceTransport(SshClient ssh, PrivateKeyFile? key)
        {
            _operations.Wait();
            try
            {
                _sessions.TryAdd(ssh, key);
                ReplaceTransportCore(ssh, key);
            }
            finally { _operations.Release(); }
        }
        private void ReplaceTransportCore(SshClient ssh, PrivateKeyFile? key)
        {
            ReleaseTransport();
            _ssh = ssh; _key = key; _service = new(ssh, _logs.CreateLogger<SshTunnelService>());
            _service.StatusChanged += Forward; ssh.ErrorOccurred += OnError;
            // 恢复整个共享组的运行规则，并保留手动停止记录。
            _restoring = RestoreAsync(_service);
        }
        private async Task RestoreAsync(SshTunnelService service)
        {
            await _operations.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed || !ReferenceEquals(service, Service)) return;
                foreach (var definition in _enabled.Values.ToArray())
                {
                    try { await service.StartAsync(definition).ConfigureAwait(false); }
                    catch (Exception ex)
                    { _logs.CreateLogger<SharedTunnelRegistry>().LogWarning(ex, "重连后恢复隧道失败 {TunnelId}", definition.Id); }
                }
            }
            finally { _operations.Release(); }
        }
        private void ReleaseTransport()
        {
            _ssh.ErrorOccurred -= OnError; _service.StatusChanged -= Forward;
            try { _service.Dispose(); }
            finally
            {
                if (!_sessions.ContainsKey(_ssh))
                    try { _ssh.Dispose(); } finally { _key?.Dispose(); }
            }
        }
        public void Dispose()
        {
            _operations.Wait();
            try
            {
                _disposed = true;
                try { ReleaseTransport(); }
                finally
                {
                    foreach (var pair in _sessions)
                        try { pair.Key.Dispose(); } finally { pair.Value?.Dispose(); }
                    _sessions.Clear();
                }
            }
            finally { _operations.Release(); }
        }
    }
}
