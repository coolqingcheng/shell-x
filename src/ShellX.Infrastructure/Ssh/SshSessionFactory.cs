using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Renci.SshNet.Common;
using ShellX.Core.Connections;
using ShellX.Core.Security;
using ShellX.Core.Sessions;

namespace ShellX.Infrastructure.Ssh;

public sealed class SshSessionFactory(ILoggerFactory logs, SharedTunnelRegistry tunnels) : IRemoteSessionFactory
{
    public SshSessionFactory(ILoggerFactory logs) : this(logs, new SharedTunnelRegistry()) { }
    public Task<IRemoteSession> ConnectAsync(ConnectionProfile profile, ConnectionSecret secret,
        Func<HostKeyChallenge, Task<bool>> verifyHost, CancellationToken cancellationToken)
        => Task.Run<IRemoteSession>(() => Connect(profile, secret, verifyHost, cancellationToken), cancellationToken);

    private IRemoteSession Connect(ConnectionProfile profile, ConnectionSecret secret,
        Func<HostKeyChallenge, Task<bool>> verify, CancellationToken ct)
    {
        profile.Validate();
        PrivateKeyFile? key = null;
        SshClient? ssh = null;
        SftpClient? sftp = null;
        try
        {
            if (profile.Authentication == AuthenticationKind.PrivateKey)
                key = string.IsNullOrEmpty(secret.Passphrase) ? new(profile.PrivateKeyPath) : new(profile.PrivateKeyPath, secret.Passphrase);
            ConnectionInfo Info() => new(profile.Host, profile.Port, profile.Username,
                profile.Authentication == AuthenticationKind.Password
                    ? [new PasswordAuthenticationMethod(profile.Username, secret.Password)]
                    : [new PrivateKeyAuthenticationMethod(profile.Username, new PreferredKeySource(key!))])
            { Timeout = TimeSpan.FromSeconds(20), Encoding = System.Text.Encoding.UTF8 };
            void Verify(object? sender, HostKeyEventArgs args)
            {
                ct.ThrowIfCancellationRequested();
                var fingerprint = "SHA256:" + Convert.ToBase64String(SHA256.HashData(args.HostKey)).TrimEnd('=');
                args.CanTrust = verify(new(profile.Host, profile.Port, args.HostKeyName, fingerprint)).GetAwaiter().GetResult();
            }
            ssh = new SshClient(Info()) { KeepAliveInterval = TimeSpan.FromSeconds(20) };
            ssh.HostKeyReceived += Verify;
            using var cancellation = ct.Register(() => { try { ssh.Dispose(); sftp?.Dispose(); } catch (ObjectDisposedException) { } });
            ssh.Connect();
            ct.ThrowIfCancellationRequested();
            var shell = ssh.CreateShellStream("xterm-256color", 100, 30, 0, 0, 64 * 1024);
            string? fileError = null;
            try
            {
                sftp = new SftpClient(Info()) { KeepAliveInterval = TimeSpan.FromSeconds(20), OperationTimeout = TimeSpan.FromSeconds(30) };
                sftp.HostKeyReceived += Verify;
                sftp.Connect();
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            { fileError = ex.Message; sftp?.Dispose(); sftp = null; }
            ct.ThrowIfCancellationRequested();
            var lease = tunnels.Acquire(profile, ssh, key);
            try { return new SshRemoteSession(ssh, shell, sftp, lease, fileError, logs.CreateLogger<SshRemoteSession>()); }
            catch { lease.Dispose(); throw; }
        }
        catch { sftp?.Dispose(); ssh?.Dispose(); key?.Dispose(); throw; }
    }

    // 优先使用 RSA SHA-2，避免现代服务器拒绝旧 ssh-rsa 探测后直接断开认证。
    private sealed class PreferredKeySource(PrivateKeyFile key) : IPrivateKeySource
    {
        public IReadOnlyCollection<Renci.SshNet.Security.HostAlgorithm> HostKeyAlgorithms { get; } =
            key.HostKeyAlgorithms.OrderBy(a => a.Name == "ssh-rsa" ? 1 : 0).ToArray();
    }
}
