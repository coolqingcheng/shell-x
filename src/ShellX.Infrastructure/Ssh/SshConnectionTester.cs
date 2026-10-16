using Microsoft.Extensions.Logging;
using ShellX.Core.Connections;
using ShellX.Core.Security;
using ShellX.Core.Sessions;

namespace ShellX.Infrastructure.Ssh;

public sealed class SshConnectionTester(ILoggerFactory logs) : IConnectionTester
{
    public async Task<ConnectionTestResult> TestAsync(ConnectionProfile profile, ConnectionSecret secret,
        Func<HostKeyChallenge, Task<bool>> verifyHost, CancellationToken cancellationToken)
    {
        // A separate registry prevents a test from taking over any live workspace tunnel transport.
        await using var session = await new SshSessionFactory(logs).ConnectAsync(profile, secret, verifyHost, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new(session.Files != null, session.FileError);
    }
}
