using System.Security.Cryptography;
using System.Text.Json;
using ShellX.Core.Connections;
using ShellX.Infrastructure.Persistence;

namespace ShellX.Infrastructure.Security;

public sealed class WindowsCredentialStore(AppPaths paths) : ICredentialStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string PathFor(Guid id) => Path.Combine(paths.Root, "credentials", id.ToString("N") + ".bin");

    public async Task<ConnectionSecret?> ReadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) return null;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(PathFor(id))) return null;
            var plain = ProtectedData.Unprotect(await File.ReadAllBytesAsync(PathFor(id), cancellationToken), null, DataProtectionScope.CurrentUser);
            try { return JsonSerializer.Deserialize(plain, ShellXJsonContext.Default.ConnectionSecret); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(Guid id, ConnectionSecret secret, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("当前平台暂不支持保存凭据，请使用仅本次连接。");
        await _gate.WaitAsync(cancellationToken);
        var plain = JsonSerializer.SerializeToUtf8Bytes(secret, ShellXJsonContext.Default.ConnectionSecret);
        var path = PathFor(id);
        try
        {
            var encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path + ".tmp", encrypted, cancellationToken);
            File.Move(path + ".tmp", path, true);
        }
        finally { CryptographicOperations.ZeroMemory(plain); _gate.Release(); }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { File.Delete(PathFor(id)); }
        finally { _gate.Release(); }
    }
}
