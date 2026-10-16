using Microsoft.Extensions.Logging;
using ShellX.Core.Connections;
using ShellX.Core.Monitoring;
using ShellX.Core.Settings;
using ShellX.Desktop.Services;

namespace ShellX.Desktop.Features.Monitoring;

// 只共享采样和展示状态，终端和 SFTP 仍属于各标签。所有入口在 UI 线程调用。
public sealed class SharedMonitorRegistry(IDialogService dialogs, ILoggerFactory logs)
{
    private readonly Dictionary<ConnectionEndpoint, Group> _groups = [];
    public Lease Acquire(ConnectionProfile profile, Func<bool> selected, Func<string, Task> navigate,
        Func<ProcessSortColumn, bool, Task>? saveSort)
    {
        var endpoint = ConnectionEndpoint.From(profile);
        if (!_groups.TryGetValue(endpoint, out var group))
        {
            group = new(profile.Host, dialogs, logs.CreateLogger<ServerMonitorViewModel>(), saveSort);
            _groups.Add(endpoint, group);
        }
        var lease = new Lease(group, selected, navigate, () => _groups.Remove(endpoint));
        group.Leases.Add(lease); return lease;
    }

    public sealed class Lease : IAsyncDisposable
    {
        private readonly Group _group;
        private readonly Action _removeGroup;
        private bool _disposed;
        internal bool IsDisposed => _disposed;
        internal SourceSlot? Slot;
        internal Func<bool> Selected { get; }
        internal Func<string, Task> Navigate { get; }
        public ServerMonitorViewModel Monitor => _group.Monitor;
        internal Lease(Group group, Func<bool> selected, Func<string, Task> navigate, Action removeGroup)
        { _group = group; Selected = selected; Navigate = navigate; _removeGroup = removeGroup; }
        public Task SetSourceAsync(IServerMonitor? source) => _group.SetSourceAsync(this, source);
        public async ValueTask DisposeAsync()
        {
            if (_disposed) return; _disposed = true;
            await SetSourceAsync(null); _group.Leases.Remove(this);
            if (_group.Leases.Count == 0) _removeGroup();
        }
    }
    internal sealed class SourceSlot(IServerMonitor source) : IDisposable
    {
        public IServerMonitor Source { get; } = source;
        public CancellationTokenSource Lifetime { get; } = new();
        public Task<ServerSnapshot>? Pending;
        public void Dispose() => Lifetime.Dispose();
    }
    internal sealed class Group : IServerMonitor
    {
        public List<Lease> Leases { get; } = [];
        public ServerMonitorViewModel Monitor { get; }
        private readonly SemaphoreSlim _updates = new(1, 1);
        private bool _running;
        public Group(string host, IDialogService dialogs, ILogger log, Func<ProcessSortColumn, bool, Task>? saveSort)
        {
            Monitor = new(host, dialogs, log, path =>
                (Leases.FirstOrDefault(lease => lease.Selected()) ?? Leases.FirstOrDefault())?.Navigate(path) ?? Task.CompletedTask, saveSort);
        }
        public async Task SetSourceAsync(Lease lease, IServerMonitor? source)
        {
            await _updates.WaitAsync();
            try
            {
                if (source != null && lease.IsDisposed) return;
                var old = lease.Slot;
                if (old != null && ReferenceEquals(old.Source, source)) return;
                lease.Slot = null;
                if (old != null)
                {
                    old.Lifetime.Cancel();
                    if (old.Pending is { } pending) try { await pending; } catch (Exception) { /* 原会话即将释放，采样循环会切换来源。 */ }
                    old.Dispose();
                }
                if (source != null) lease.Slot = new(source);
                if (Leases.Any(user => user.Slot != null))
                {
                    if (!_running) { _running = true; Monitor.Start(this, CancellationToken.None); }
                }
                else if (_running) { _running = false; await Monitor.StopAsync(); }
            }
            finally { _updates.Release(); }
        }
        public async Task<ServerSnapshot> ReadAsync(CancellationToken token)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var lease = Leases.FirstOrDefault(user => user.Slot != null);
                if (lease?.Slot is not { } slot)
                { await Task.Delay(Timeout.Infinite, token); throw new OperationCanceledException(token); }
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, slot.Lifetime.Token);
                try
                {
                    var pending = slot.Source.ReadAsync(linked.Token); slot.Pending = pending;
                    var result = await pending;
                    token.ThrowIfCancellationRequested();
                    if (ReferenceEquals(lease.Slot, slot)) return result;
                }
                catch (Exception) when (!token.IsCancellationRequested && !ReferenceEquals(lease.Slot, slot)) { }
                finally { slot.Pending = null; }
            }
        }
    }
}
