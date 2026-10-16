using Renci.SshNet;
using Renci.SshNet.Common;
using ShellX.Core.Connections;
using ShellX.Core.Tunnels;

namespace ShellX.Infrastructure.Ssh;

// 终端和 SFTP 属于标签；隧道传输连接由同一 SSH 地址的所有标签共同持有。
public sealed class SharedTunnelRegistry
{
    private readonly object _sync = new();
    private readonly Dictionary<ConnectionEndpoint, Group> _groups = [];

    internal Lease Acquire(ConnectionProfile profile, SshClient ssh, PrivateKeyFile? key)
    {
        var endpoint = ConnectionEndpoint.From(profile);
        lock (_sync)
        {
            var ownsTransport = false;
            if (!_groups.TryGetValue(endpoint, out var group))
            {
                group = new Group(ssh, key); _groups.Add(endpoint, group); ownsTransport = true;
            }
            else if (!group.IsConnected)
            {
                group.ReplaceTransport(ssh, key); ownsTransport = true;
            }
            group.Users++;
            return new Lease(group, ownsTransport, () =>
            {
                lock (_sync)
                {
                    if (--group.Users != 0) return;
                    _groups.Remove(endpoint); group.Dispose();
                }
            });
        }
    }

    internal sealed class Lease : ITunnelService, IDisposable
    {
        private readonly Group _group;
        private readonly Action _release;
        private int _disposed;
        public bool OwnsTransport { get; }
        public event EventHandler<TunnelStatus>? StatusChanged;
        internal Lease(Group group, bool ownsTransport, Action release)
        { _group = group; OwnsTransport = ownsTransport; _release = release; group.StatusChanged += Forward; }
        private void Forward(object? sender, TunnelStatus state) => StatusChanged?.Invoke(this, state);
        public bool IsRunning(Guid id) => _group.Service.IsRunning(id);
        public TunnelStatus GetStatus(Guid id) => _group.Service.GetStatus(id);
        public Task StartAsync(TunnelDefinition definition, CancellationToken cancellationToken = default)
            => _group.StartAsync(definition, false, cancellationToken);
        public Task AutoStartAsync(TunnelDefinition definition, CancellationToken cancellationToken = default)
            => _group.StartAsync(definition, true, cancellationToken);
        public Task StopAsync(Guid id, CancellationToken cancellationToken = default)
            => _group.RunAsync(service => service.StopAsync(id, cancellationToken), cancellationToken);
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
        private SshClient _ssh;
        private PrivateKeyFile? _key;
        private SshTunnelService _service;
        private bool _disposed;
        public int Users;
        public bool IsConnected => _ssh.IsConnected;
        public SshTunnelService Service => Volatile.Read(ref _service);
        public event EventHandler<TunnelStatus>? StatusChanged;
        public Group(SshClient ssh, PrivateKeyFile? key)
        {
            _ssh = ssh; _key = key; _service = new(ssh);
            _service.StatusChanged += Forward; ssh.ErrorOccurred += OnError;
        }
        private void Forward(object? sender, TunnelStatus state) => StatusChanged?.Invoke(this, state);
        private void OnError(object? sender, ExceptionEventArgs error)
        { if (ReferenceEquals(sender, _ssh)) Service.NotifyDisconnected(error.Exception.Message); }

        public async Task StartAsync(TunnelDefinition definition, bool automatic, CancellationToken token)
        {
            await _operations.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                // 新标签保留共享的停止/失败状态，不重新占用端口或撤销其他标签的停止操作。
                if (automatic && _initialized.Contains(definition.Id)) return;
                _initialized.Add(definition.Id);
                await Service.StartAsync(definition, token).ConfigureAwait(false);
            }
            finally { _operations.Release(); }
        }
        public async Task RunAsync(Func<SshTunnelService, Task> operation, CancellationToken token)
        {
            await _operations.WaitAsync(token).ConfigureAwait(false);
            try { ObjectDisposedException.ThrowIf(_disposed, this); await operation(Service).ConfigureAwait(false); }
            finally { _operations.Release(); }
        }
        public Task UpdateAsync(TunnelDefinition definition, CancellationToken token)
            => RunAsync(async service =>
            {
                definition.Validate(); _initialized.Add(definition.Id);
                var running = service.IsRunning(definition.Id);
                await service.StopAsync(definition.Id, token).ConfigureAwait(false);
                if (running) await service.StartAsync(definition, token).ConfigureAwait(false);
            }, token);
        public void ReplaceTransport(SshClient ssh, PrivateKeyFile? key)
        {
            _operations.Wait();
            try
            {
                var ids = _initialized.ToArray();
                ReleaseTransport();
                _ssh = ssh; _key = key; _service = new(ssh); _initialized.Clear();
                _service.StatusChanged += Forward; ssh.ErrorOccurred += OnError;
                foreach (var id in ids) Forward(this, new(id, TunnelState.Stopped, false));
            }
            finally { _operations.Release(); }
        }
        private void ReleaseTransport()
        {
            _ssh.ErrorOccurred -= OnError; _service.StatusChanged -= Forward;
            try { _service.Dispose(); }
            finally { try { _ssh.Dispose(); } finally { _key?.Dispose(); } }
        }
        public void Dispose()
        {
            _operations.Wait();
            try { _disposed = true; ReleaseTransport(); }
            finally { _operations.Release(); }
        }
    }
}
