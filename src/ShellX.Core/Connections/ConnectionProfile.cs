namespace ShellX.Core.Connections;

public enum AuthenticationKind { Password, PrivateKey }

public sealed record ConnectionProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "新连接";
    public string Group { get; init; } = "默认分组";
    public string AccentColor { get; init; } = "";
    public string Host { get; init; } = "";
    public int Port { get; init; } = 22;
    public string Username { get; init; } = "root";
    public AuthenticationKind Authentication { get; init; }
    public string PrivateKeyPath { get; init; } = "";
    public bool RememberSecret { get; init; }
    public bool McpEnabled { get; init; }
    public string InitialDirectory { get; init; } = ".";
    public List<Tunnels.TunnelDefinition> Tunnels { get; init; } = [];
    /// <summary>隧道启动失败或连通检测失败后，按固定间隔自动重试。</summary>
    public bool TunnelAutoRetry { get; init; }
    /// <summary>自动重试的间隔秒数，取值范围 3–600。</summary>
    public int TunnelRetrySeconds { get; init; } = 10;
    public List<Commands.SavedCommand> QuickCommands { get; init; } = [];
    public string Address => $"{Username}@{Host}:{Port}";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new ArgumentException("请填写连接名称。");
        if (string.IsNullOrWhiteSpace(Host)) throw new ArgumentException("请填写主机地址。");
        if (string.IsNullOrWhiteSpace(Username)) throw new ArgumentException("请填写用户名。");
        if (AccentColor.Length != 0 && (AccentColor.Length != 7 || AccentColor[0] != '#' || !AccentColor.AsSpan(1).ToString().All(Uri.IsHexDigit)))
            throw new ArgumentException("连接标识色请使用 #RRGGBB，例如 #BF6969；留空表示不标记。");
        if (Port is < 1 or > 65535) throw new ArgumentException("端口必须为 1–65535。");
        if (Authentication == AuthenticationKind.PrivateKey && string.IsNullOrWhiteSpace(PrivateKeyPath))
            throw new ArgumentException("请选择私钥文件。");
    }
}

// 凭据不属于可序列化的连接配置，也不得写入日志。
public sealed record ConnectionSecret(string Password = "", string Passphrase = "");

public interface IConnectionRepository
{
    Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IReadOnlyList<ConnectionProfile> connections, CancellationToken cancellationToken = default);
    async Task<ConnectionCatalog> LoadCatalogAsync(CancellationToken cancellationToken = default)
        => new(await LoadAsync(cancellationToken), []);
    Task SaveCatalogAsync(ConnectionCatalog catalog, CancellationToken cancellationToken = default)
        => SaveAsync(catalog.Connections, cancellationToken);
}

public interface ICredentialStore
{
    Task<ConnectionSecret?> ReadAsync(Guid id, CancellationToken cancellationToken = default);
    Task SaveAsync(Guid id, ConnectionSecret secret, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
