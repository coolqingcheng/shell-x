using System.Text.Json;
using ShellX.Core.Connections;
using ShellX.Core.Settings;

namespace ShellX.Infrastructure.Persistence;

public sealed class JsonConfigurationImporter : IConfigurationImporter
{
    private const long MaximumFileBytes = 10 * 1024 * 1024;

    public async Task<bool> HasCredentialsAsync(string path, CancellationToken cancellationToken = default)
        => (await ReadAsync(path, null, cancellationToken)).CredentialVault != null;

    public async Task<ConfigurationRestoreData> ImportAsync(string path, string? encryptionPassword, CancellationToken cancellationToken = default)
    {
        var exported = await ReadAsync(path, encryptionPassword, cancellationToken);
        var connections = exported.Connections.Select(stored => stored.ToModel()).ToArray();
        foreach (var profile in connections) profile.Validate();
        var folders = ConnectionFolders.Expand((exported.Folders ?? []).Concat(connections.Select(profile => profile.Group)));
        var secrets = exported.CredentialVault == null
            ? new Dictionary<Guid, ConnectionSecret>()
            : ConfigurationCredentialCrypto.Decrypt(exported.CredentialVault, encryptionPassword ?? "")
                .Where(pair => connections.Any(profile => profile.Id == pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
        return new ConfigurationRestoreData(new(connections, folders), exported.Settings ?? new PortableAppSettings(), secrets);
    }

    private static async Task<ExportedConfiguration> ReadAsync(string path, string? _, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("请选择 ShellX 配置文件。", nameof(path));
        var file = new FileInfo(Path.GetFullPath(path));
        if (!file.Exists) throw new FileNotFoundException("配置文件不存在。", file.FullName);
        if (Directory.Exists(file.FullName)) throw new IOException("所选路径是文件夹，请选择 JSON 配置文件。");
        if (file.Length > MaximumFileBytes) throw new InvalidDataException("配置文件超过 10 MiB，已拒绝读取。");

        ExportedConfiguration? exported;
        await using (var stream = File.OpenRead(file.FullName))
            exported = await JsonSerializer.DeserializeAsync(stream, ShellXJsonContext.Default.ExportedConfiguration, cancellationToken);
        if (exported == null) throw new InvalidDataException("配置文件内容为空。");
        if (!exported.Format.Equals("ShellX.Configuration", StringComparison.Ordinal))
            throw new InvalidDataException("这不是 ShellX 配置文件。");
        if (exported.Version is < 1 or > 2)
            throw new InvalidDataException($"配置文件版本 {exported.Version} 不受支持。");
        if (exported.Connections == null || exported.Folders == null)
            throw new InvalidDataException("配置文件缺少连接或文件夹数据。");
        return exported;
    }
}