using Microsoft.Extensions.Logging;
using Renci.SshNet;
using ShellX.Core.Files;
using ShellX.Core.Sessions;
using ShellX.Core.Tunnels;

namespace ShellX.Infrastructure.Ssh;

internal sealed class SshRemoteSession : IRemoteSession, ITerminalChannel
{
    private readonly SshClient _ssh;
    private readonly ShellStream _shell;
    private readonly SftpClient? _sftp;
    private readonly PrivateKeyFile? _key;
    private readonly ILogger _log;
    private readonly SharedTunnelRegistry.Lease _tunnelLease;
    private readonly EventHandler<Renci.SshNet.Common.ExceptionEventArgs> _sshError;
    private readonly EventHandler<EventArgs> _shellClosed;
    private int _disposed;
    public ITerminalChannel Terminal => this;
    public Stream Stream => _shell;
    public IRemoteFileSystem? Files { get; }
    public string? FileError { get; }
    public ITunnelService Tunnels { get; }
    public Core.Monitoring.IServerMonitor Monitor { get; }
    public event EventHandler<string>? Disconnected;

    internal SshRemoteSession(SshClient ssh, ShellStream shell, SftpClient? sftp, PrivateKeyFile? key, SharedTunnelRegistry.Lease tunnels, string? fileError, ILogger log)
    {
        _ssh = ssh; _shell = shell; _sftp = sftp; _key = key; _log = log;
        FileError = fileError;
        Files = sftp == null ? null : new SftpFileSystem(sftp);
        _tunnelLease = tunnels; Tunnels = tunnels;
        Monitor = new Monitoring.LinuxServerMonitor(ssh);
        _sshError = (_, e) => Report(e.Exception.Message); _shellClosed = (_, _) => Report("远程终端已关闭。");
        ssh.ErrorOccurred += _sshError; shell.Closed += _shellClosed;
    }
    private void Report(string error)
    { if (_disposed == 0) { _log.LogInformation("SSH 会话断开：{Reason}", error); Disconnected?.Invoke(this, error); } }
    public void Resize(int columns, int rows)
    { if (_disposed == 0 && _ssh.IsConnected) _shell.ChangeWindowSize((uint)Math.Max(columns, 2), (uint)Math.Max(rows, 2), 0, 0); }
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
        return new(Task.Run(() =>
        {
            _ssh.ErrorOccurred -= _sshError; _shell.Closed -= _shellClosed;
            try { _shell.Dispose(); }
            finally
            {
                try { _sftp?.Dispose(); }
                finally
                {
                    try { if (!_tunnelLease.OwnsTransport) { try { _ssh.Dispose(); } finally { _key?.Dispose(); } } }
                    finally { _tunnelLease.Dispose(); }
                }
            }
        }));
    }
}
