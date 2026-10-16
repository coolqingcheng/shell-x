namespace ShellX.Core.Security;

public sealed record HostKeyChallenge(string Host, int Port, string Algorithm, string Fingerprint);
public sealed record KnownHost(string Host, int Port, string Fingerprint);

public interface IHostKeyStore
{
    Task<string?> GetAsync(string host, int port, CancellationToken cancellationToken = default);
    Task TrustAsync(HostKeyChallenge host, CancellationToken cancellationToken = default);
}
