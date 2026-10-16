using ShellX.Core.Connections;
using ShellX.Core.Files;
using ShellX.Core.Security;
using ShellX.Core.Tunnels;

namespace ShellX.Core.Sessions;

public interface ITerminalChannel
{
    Stream Stream { get; }
    void Resize(int columns, int rows);
}

public interface IRemoteSession : IAsyncDisposable
{
    ITerminalChannel Terminal { get; }
    IRemoteFileSystem? Files { get; }
    string? FileError { get; }
    ITunnelService Tunnels { get; }
    Monitoring.IServerMonitor? Monitor => null;
    event EventHandler<string>? Disconnected;
}

public interface IRemoteSessionFactory
{
    Task<IRemoteSession> ConnectAsync(ConnectionProfile profile, ConnectionSecret secret,
        Func<HostKeyChallenge, Task<bool>> verifyHost, CancellationToken cancellationToken);
}
