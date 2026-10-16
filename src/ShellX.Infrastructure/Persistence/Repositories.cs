using ShellX.Core.Connections;
using ShellX.Core.Security;
using ShellX.Core.Settings;

namespace ShellX.Infrastructure.Persistence;

public sealed class JsonConnectionRepository(AppPaths paths) : IConnectionRepository
{
    private readonly JsonStore<ConnectionConfiguration> _store = new(paths.File("connections.json"), () => new(1, []), ShellXJsonContext.Default.ConnectionConfiguration);
    public async Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var data = await _store.ReadAsync(cancellationToken);
        if (data.Version != 1) throw new InvalidDataException("连接配置版本不受支持。");
        return data.Connections.Select(c => c.ToModel()).ToArray();
    }
    public Task SaveAsync(IReadOnlyList<ConnectionProfile> connections, CancellationToken cancellationToken = default)
        => _store.UpdateAsync(data => data.Version == 1 ? data with { Connections = connections.Select(StoredConnection.From).ToList() }
            : throw new InvalidDataException("连接配置版本不受支持。"), cancellationToken);
    public async Task<ConnectionCatalog> LoadCatalogAsync(CancellationToken cancellationToken = default)
    {
        var data = await _store.ReadAsync(cancellationToken);
        if (data.Version != 1) throw new InvalidDataException("连接配置版本不受支持。");
        var connections = data.Connections.Select(c => c.ToModel()).ToArray();
        return new(connections, ConnectionFolders.Expand((data.Folders ?? []).Concat(connections.Select(c => c.Group))));
    }
    public Task SaveCatalogAsync(ConnectionCatalog catalog, CancellationToken cancellationToken = default)
        => _store.WriteAsync(new(1, catalog.Connections.Select(StoredConnection.From).ToList())
        { Folders = ConnectionFolders.Expand(catalog.Folders.Concat(catalog.Connections.Select(c => c.Group))).ToList() }, cancellationToken);
}

public sealed class JsonSettingsStore(AppPaths paths) : ISettingsStore
{
    private readonly JsonStore<StoredSettings> _store = new(paths.File("settings.json"), () => new(), ShellXJsonContext.Default.StoredSettings);
    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => (await _store.ReadAsync(cancellationToken)).ToModel();
    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => _store.WriteAsync(StoredSettings.From(settings), cancellationToken);
}

public sealed class JsonHostKeyStore(AppPaths paths) : IHostKeyStore
{
    private readonly JsonStore<List<KnownHost>> _store = new(paths.File("known-hosts.json"), () => [], ShellXJsonContext.Default.ListKnownHost);
    public async Task<string?> GetAsync(string host, int port, CancellationToken cancellationToken = default)
        => (await _store.ReadAsync(cancellationToken)).FirstOrDefault(x => x.Host.Equals(host, StringComparison.OrdinalIgnoreCase) && x.Port == port)?.Fingerprint;

    public Task TrustAsync(HostKeyChallenge host, CancellationToken cancellationToken = default)
        => _store.UpdateAsync(items =>
        {
            items.RemoveAll(x => x.Host.Equals(host.Host, StringComparison.OrdinalIgnoreCase) && x.Port == host.Port);
            items.Add(new(host.Host, host.Port, host.Fingerprint));
            return items;
        }, cancellationToken);
}
