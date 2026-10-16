namespace ShellX.Core.Tunnels;

public enum TunnelDirection { Local, Remote }
public enum TunnelState { Stopped, Starting, Checking, Connected, Failed, Unverified }

public sealed record TunnelStatus(Guid TunnelId, TunnelState State, bool IsRunning, string Reason = "", DateTimeOffset? CheckedAt = null);

public sealed record TunnelDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "新隧道";
    public TunnelDirection Direction { get; init; }
    public string BindHost { get; init; } = "127.0.0.1";
    public int BindPort { get; init; } = 3307;
    public string TargetHost { get; init; } = "127.0.0.1";
    public int TargetPort { get; init; } = 3306;
    public string Description => $"{(Direction == TunnelDirection.Local ? "本地" : "远程")}  {BindHost}:{BindPort} → {TargetHost}:{TargetPort}";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(BindHost) || string.IsNullOrWhiteSpace(TargetHost))
            throw new ArgumentException("请填写隧道名称、监听地址和目标地址。");
        if (BindPort is < 1 or > 65535 || TargetPort is < 1 or > 65535) throw new ArgumentException("端口必须为 1–65535。");
    }
}

public interface ITunnelService
{
    event EventHandler<TunnelStatus>? StatusChanged;
    bool IsRunning(Guid id);
    TunnelStatus GetStatus(Guid id);
    Task StartAsync(TunnelDefinition definition, CancellationToken cancellationToken = default);
    Task AutoStartAsync(TunnelDefinition definition, CancellationToken cancellationToken = default) => StartAsync(definition, cancellationToken);
    Task StopAsync(Guid id, CancellationToken cancellationToken = default);
    Task CheckAsync(Guid id, CancellationToken cancellationToken = default);
    async Task UpdateAsync(TunnelDefinition definition, CancellationToken cancellationToken = default)
    {
        definition.Validate();
        var running = IsRunning(definition.Id);
        await StopAsync(definition.Id, cancellationToken);
        if (running) await StartAsync(definition, cancellationToken);
    }
}
