using ShellX.Core.Connections;

namespace ShellX.Core.Settings;

public sealed record ConfigurationRestoreData(
    ConnectionCatalog Catalog,
    PortableAppSettings Settings,
    IReadOnlyDictionary<Guid, ConnectionSecret> Secrets);

public interface IConfigurationImporter
{
    Task<bool> HasCredentialsAsync(string path, CancellationToken cancellationToken = default);
    Task<ConfigurationRestoreData> ImportAsync(string path, string? encryptionPassword, CancellationToken cancellationToken = default);
}