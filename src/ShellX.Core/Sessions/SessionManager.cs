using ShellX.Core.Connections;
using ShellX.Core.Security;

namespace ShellX.Core.Sessions;

/// <summary>单个标签的会话所有者，串行创建和释放资源，并在关闭时取消尚未完成的连接。</summary>
public sealed class SessionManager(IRemoteSessionFactory factory) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cancellation;
    private IRemoteSession? _current;
    private volatile bool _disposed;

    public async Task<IRemoteSession> ConnectAsync(ConnectionProfile profile, ConnectionSecret secret,
        Func<HostKeyChallenge, Task<bool>> verify, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await CloseCurrentAsync().ConfigureAwait(false);
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = _cancellation.Token;
            var created = await factory.ConnectAsync(profile, secret, verify, token).ConfigureAwait(false);
            if (token.IsCancellationRequested || _disposed)
            { await created.DisposeAsync().ConfigureAwait(false); throw new OperationCanceledException(token); }
            return _current = created;
        }
        finally { _gate.Release(); }
    }

    private void CancelPending()
    { try { _cancellation?.Cancel(); } catch (ObjectDisposedException) { } }
    private async Task CloseCurrentAsync()
    {
        CancelPending();
        if (_current != null) { var previous = _current; _current = null; await previous.DisposeAsync().ConfigureAwait(false); }
        _cancellation?.Dispose(); _cancellation = null;
    }
    public async Task DisconnectAsync()
    {
        CancelPending(); await _gate.WaitAsync().ConfigureAwait(false);
        try { await CloseCurrentAsync().ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
    public async ValueTask DisposeAsync()
    { _disposed = true; await DisconnectAsync().ConfigureAwait(false); }
}
