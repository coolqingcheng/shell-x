using ShellX.Core.Connections;
using ShellX.Core.Security;

namespace ShellX.Core.Sessions;

public sealed record ConnectionTestResult(bool SftpAvailable, string? FileError);

public interface IConnectionTester
{
    Task<ConnectionTestResult> TestAsync(ConnectionProfile profile, ConnectionSecret secret,
        Func<HostKeyChallenge, Task<bool>> verifyHost, CancellationToken cancellationToken);
}
