using ShellX.Core.Commands;
using ShellX.Core.Connections;
using ShellX.Core.Settings;
using ShellX.Core.Tunnels;

namespace ShellX.Infrastructure.Persistence;

// .NET 10 的 JSON 源生成对缺失的 init 属性不保留初始化值。
// 持久化 DTO 使用普通 setter，保证旧版配置仍沿用领域模型的默认值。
internal sealed class StoredSettings
{
    private static readonly AppSettings Defaults = new();
    public AppTheme Theme { get; set; } = Defaults.Theme;
    public int Version { get; set; } = Defaults.Version;
    public double TerminalFontSize { get; set; } = Defaults.TerminalFontSize;
    public string TerminalFontFamily { get; set; } = Defaults.TerminalFontFamily;
    public string EditorFontFamily { get; set; } = Defaults.EditorFontFamily;
    public double EditorFontSize { get; set; } = Defaults.EditorFontSize;
    public string LockPasswordHash { get; set; } = Defaults.LockPasswordHash;
    public bool AutoLockEnabled { get; set; } = Defaults.AutoLockEnabled;
    public int AutoLockMinutes { get; set; } = Defaults.AutoLockMinutes;
    public bool ShowHiddenFiles { get; set; } = Defaults.ShowHiddenFiles;
    public bool HighlightTerminalText { get; set; } = Defaults.HighlightTerminalText;
    public bool AutoReconnectEnabled { get; set; } = Defaults.AutoReconnectEnabled;
    public int ReconnectIntervalSeconds { get; set; } = Defaults.ReconnectIntervalSeconds;
    public ProcessSortColumn ProcessSortColumn { get; set; } = Defaults.ProcessSortColumn;
    public bool ProcessSortDescending { get; set; } = Defaults.ProcessSortDescending;
    public AppSettings ToModel() => new()
    {
        Theme = Theme, Version = Version, TerminalFontSize = TerminalFontSize, ShowHiddenFiles = ShowHiddenFiles,
        TerminalFontFamily = TerminalFontFamily, EditorFontFamily = EditorFontFamily, EditorFontSize = EditorFontSize,
        LockPasswordHash = LockPasswordHash, AutoLockEnabled = AutoLockEnabled, AutoLockMinutes = AutoLockMinutes,
        HighlightTerminalText = HighlightTerminalText, AutoReconnectEnabled = AutoReconnectEnabled, ReconnectIntervalSeconds = ReconnectIntervalSeconds,
        ProcessSortColumn = ProcessSortColumn, ProcessSortDescending = ProcessSortDescending
    };
    public static StoredSettings From(AppSettings model) => new()
    {
        Theme = model.Theme, Version = model.Version, TerminalFontSize = model.TerminalFontSize, ShowHiddenFiles = model.ShowHiddenFiles,
        TerminalFontFamily = model.TerminalFontFamily, EditorFontFamily = model.EditorFontFamily, EditorFontSize = model.EditorFontSize,
        LockPasswordHash = model.LockPasswordHash, AutoLockEnabled = model.AutoLockEnabled, AutoLockMinutes = model.AutoLockMinutes,
        HighlightTerminalText = model.HighlightTerminalText, AutoReconnectEnabled = model.AutoReconnectEnabled, ReconnectIntervalSeconds = model.ReconnectIntervalSeconds,
        ProcessSortColumn = model.ProcessSortColumn, ProcessSortDescending = model.ProcessSortDescending
    };
}

internal sealed class StoredConnection
{
    private readonly ConnectionProfile _defaults = new();
    public StoredConnection()
    {
        Id = _defaults.Id; Name = _defaults.Name; Group = _defaults.Group; Host = _defaults.Host; Port = _defaults.Port;
        Username = _defaults.Username; Authentication = _defaults.Authentication; PrivateKeyPath = _defaults.PrivateKeyPath;
        RememberSecret = _defaults.RememberSecret; InitialDirectory = _defaults.InitialDirectory;
    }
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Group { get; set; }
    public string AccentColor { get; set; } = "";
    public string Host { get; set; }
    public int Port { get; set; }
    public string Username { get; set; }
    public AuthenticationKind Authentication { get; set; }
    public string PrivateKeyPath { get; set; }
    public bool RememberSecret { get; set; }
    public string InitialDirectory { get; set; }
    public List<StoredTunnel> Tunnels { get; set; } = [];
    public List<StoredCommand> QuickCommands { get; set; } = [];
    public ConnectionProfile ToModel() => new()
    {
        Id = Id, Name = Name, Group = Group, AccentColor = AccentColor ?? "", Host = Host, Port = Port, Username = Username, Authentication = Authentication,
        PrivateKeyPath = PrivateKeyPath, RememberSecret = RememberSecret, InitialDirectory = InitialDirectory,
        Tunnels = Tunnels.Select(t => t.ToModel()).ToList(), QuickCommands = QuickCommands.Select(c => c.ToModel()).ToList()
    };
    public static StoredConnection From(ConnectionProfile model) => new()
    {
        Id = model.Id, Name = model.Name, Group = model.Group, AccentColor = model.AccentColor, Host = model.Host, Port = model.Port, Username = model.Username,
        Authentication = model.Authentication, PrivateKeyPath = model.PrivateKeyPath, RememberSecret = model.RememberSecret,
        InitialDirectory = model.InitialDirectory, Tunnels = model.Tunnels.Select(StoredTunnel.From).ToList(),
        QuickCommands = model.QuickCommands.Select(StoredCommand.From).ToList()
    };
}

internal sealed class StoredTunnel
{
    public StoredTunnel()
    {
        var defaults = new TunnelDefinition();
        Id = defaults.Id; Name = defaults.Name; Direction = defaults.Direction; BindHost = defaults.BindHost;
        BindPort = defaults.BindPort; TargetHost = defaults.TargetHost; TargetPort = defaults.TargetPort;
    }
    public Guid Id { get; set; }
    public string Name { get; set; }
    public TunnelDirection Direction { get; set; }
    public string BindHost { get; set; }
    public int BindPort { get; set; }
    public string TargetHost { get; set; }
    public int TargetPort { get; set; }
    public TunnelDefinition ToModel() => new() { Id = Id, Name = Name, Direction = Direction, BindHost = BindHost, BindPort = BindPort, TargetHost = TargetHost, TargetPort = TargetPort };
    public static StoredTunnel From(TunnelDefinition model) => new() { Id = model.Id, Name = model.Name, Direction = model.Direction, BindHost = model.BindHost, BindPort = model.BindPort, TargetHost = model.TargetHost, TargetPort = model.TargetPort };
}

internal sealed class StoredCommand
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Text { get; set; } = "";
    public SavedCommand ToModel() => new() { Id = Id, Name = Name, Text = Text };
    public static StoredCommand From(SavedCommand model) => new() { Id = model.Id, Name = model.Name, Text = model.Text };
}
