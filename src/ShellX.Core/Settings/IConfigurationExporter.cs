using ShellX.Core.Connections;

namespace ShellX.Core.Settings;

public interface IConfigurationExporter
{
    Task ExportAsync(string path, ConnectionCatalog catalog, AppSettings settings, CancellationToken cancellationToken = default);
}
