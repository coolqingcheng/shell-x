using System.Text.Json;
using ShellX.Core.Connections;
using ShellX.Core.Settings;

namespace ShellX.Infrastructure.Persistence;

public sealed class JsonConfigurationExporter(AppPaths paths) : IConfigurationExporter
{
    public Task ExportAsync(string path, ConnectionCatalog catalog, AppSettings settings, CancellationToken cancellationToken = default)
        => ExportAsync(path, catalog, settings, null, null, cancellationToken);

    public async Task ExportAsync(string path, ConnectionCatalog catalog, AppSettings settings,
        IReadOnlyDictionary<Guid, ConnectionSecret>? secrets, string? encryptionPassword, CancellationToken cancellationToken = default)
    {
        path = Path.GetFullPath(path);
        if (Directory.Exists(path)) throw new IOException("导出路径是文件夹，请选择 JSON 文件。");
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (new[] { "connections.json", "settings.json", "known-hosts.json" }.Any(name => path.Equals(Path.GetFullPath(paths.File(name)), comparison)) ||
            path.StartsWith(Path.GetFullPath(paths.File("credentials")) + Path.DirectorySeparatorChar, comparison))
            throw new IOException("请选择其他导出位置，不能覆盖软件正在使用的配置或凭据文件。");

        var includedSecrets = catalog.Connections
            .Select(profile => (Profile: profile, Secret: secrets?.GetValueOrDefault(profile.Id)))
            .Where(item => item.Secret is { } secret && (secret.Password.Length > 0 || secret.Passphrase.Length > 0))
            .ToDictionary(item => item.Profile.Id, item => item.Secret!);
        if (includedSecrets.Count > 0)
        {
            if (string.IsNullOrEmpty(encryptionPassword))
                throw new ArgumentException("导出已保存的密码必须设置备份加密密码。", nameof(encryptionPassword));
            if (encryptionPassword.Length < 8)
                throw new ArgumentException("备份加密密码至少需要 8 个字符。", nameof(encryptionPassword));
        }

        var exported = new ExportedConfiguration
        {
            Connections = catalog.Connections.Select(profile => StoredConnection.From(profile with
            {
                RememberSecret = includedSecrets.ContainsKey(profile.Id)
            })).ToList(),
            Folders = ConnectionFolders.Expand(catalog.Folders.Concat(catalog.Connections.Select(profile => profile.Group))).ToList(),
            Settings = new PortableAppSettings
            {
                Theme = settings.Theme, TerminalFontFamily = settings.TerminalFontFamily, TerminalFontSize = settings.TerminalFontSize,
                EditorFontFamily = settings.EditorFontFamily, EditorFontSize = settings.EditorFontSize, ShowHiddenFiles = settings.ShowHiddenFiles,
                HighlightTerminalText = settings.HighlightTerminalText, AutoReconnectEnabled = settings.AutoReconnectEnabled,
                ReconnectIntervalSeconds = settings.ReconnectIntervalSeconds, ProcessSortColumn = settings.ProcessSortColumn,
                ProcessSortDescending = settings.ProcessSortDescending
            },
            CredentialVault = includedSecrets.Count == 0 || encryptionPassword == null
                ? null
                : ConfigurationCredentialCrypto.Encrypt(encryptionPassword, includedSecrets)
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, exported, ShellXJsonContext.Default.ExportedConfiguration, cancellationToken);
                await stream.FlushAsync(cancellationToken); stream.Flush(true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

// 连接、文件夹和可移植偏好可恢复；锁定、MCP、更新源等本机安全设置不进入导出。
internal sealed class ExportedConfiguration
{
    public string Format { get; set; } = "ShellX.Configuration";
    public int Version { get; set; } = 2;
    public DateTimeOffset ExportedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<StoredConnection> Connections { get; set; } = [];
    public List<string> Folders { get; set; } = [];
    public PortableAppSettings Settings { get; set; } = new();
    public ExportedCredentialVault? CredentialVault { get; set; }
}