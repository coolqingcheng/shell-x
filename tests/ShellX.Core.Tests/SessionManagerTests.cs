using ShellX.Core.Connections;
using ShellX.Core.Files;
using ShellX.Core.Security;
using ShellX.Core.Sessions;
using ShellX.Core.Tunnels;

namespace ShellX.Core.Tests;

public class SessionManagerTests
{
    [Fact]
    public async Task ClosingWhileConnectionCompletesStillDisposesNewResources()
    {
        var factory = new DelayedFactory(); var manager = new SessionManager(factory);
        var connecting = manager.ConnectAsync(new(), new(), _ => Task.FromResult(true), CancellationToken.None);
        await factory.Started.Task;
        var closing = manager.DisposeAsync().AsTask(); var session = new Session(); factory.Ready.SetResult(session);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connecting); await closing;
        Assert.Equal(1, session.DisposeCount);
    }
    [Fact]
    public async Task ReplacingAndRepeatedlyClosingDoesNotLeakOrDisposeTwice()
    {
        var factory = new ImmediateFactory(); await using var manager = new SessionManager(factory);
        var first = (Session)await manager.ConnectAsync(new(), new(), _ => Task.FromResult(true), CancellationToken.None);
        var second = (Session)await manager.ConnectAsync(new(), new(), _ => Task.FromResult(true), CancellationToken.None);
        Assert.Equal(1, first.DisposeCount); Assert.Equal(0, second.DisposeCount);
        await manager.DisconnectAsync(); await manager.DisconnectAsync(); Assert.Equal(1, second.DisposeCount);
    }
    private sealed class DelayedFactory : IRemoteSessionFactory
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<IRemoteSession> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IRemoteSession> ConnectAsync(ConnectionProfile profile, ConnectionSecret secret, Func<HostKeyChallenge, Task<bool>> verifyHost, CancellationToken cancellationToken)
        { Started.SetResult(); return Ready.Task; }
    }
    private sealed class ImmediateFactory : IRemoteSessionFactory
    {
        public Task<IRemoteSession> ConnectAsync(ConnectionProfile profile, ConnectionSecret secret, Func<HostKeyChallenge, Task<bool>> verifyHost, CancellationToken cancellationToken) => Task.FromResult<IRemoteSession>(new Session());
    }
    private sealed class Session : IRemoteSession
    {
        public int DisposeCount { get; private set; }
        public ITerminalChannel Terminal => throw new NotSupportedException();
        public IRemoteFileSystem? Files => null;
        public string? FileError => null;
        public ITunnelService Tunnels => throw new NotSupportedException();
        public event EventHandler<string>? Disconnected { add { } remove { } }
        public ValueTask DisposeAsync() { DisposeCount++; return ValueTask.CompletedTask; }
    }
}
