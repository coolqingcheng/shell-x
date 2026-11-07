using ShellX.Core.Connections;

namespace ShellX.Core.Settings;

public sealed record PortableAppSettings
{
    public AppTheme Theme { get; init; } = AppTheme.Dark;
    public string TerminalFontFamily { get; init; } = "Cascadia Mono,Consolas,monospace";
    public double TerminalFontSize { get; init; } = 14;
    public string EditorFontFamily { get; init; } = "Cascadia Mono,Consolas,monospace";
    public double EditorFontSize { get; init; } = 14;
    public bool ShowHiddenFiles { get; init; }
    public bool HighlightTerminalText { get; init; } = true;
    public bool AutoReconnectEnabled { get; init; } = true;
    public int ReconnectIntervalSeconds { get; init; } = 5;
    public ProcessSortColumn ProcessSortColumn { get; init; } = ProcessSortColumn.Cpu;
    public bool ProcessSortDescending { get; init; } = true;
}

public interface IConfigurationExporter
{
    Task ExportAsync(string path, ConnectionCatalog catalog, AppSettings settings, CancellationToken cancellationToken = default);

    /// <summary>导出可恢复的凭据；secrets 非空时必须提供 encryptionPassword，文件内只保存加密后的凭据。</summary>
    Task ExportAsync(string path, ConnectionCatalog catalog, AppSettings settings,
        IReadOnlyDictionary<Guid, ConnectionSecret>? secrets, string? encryptionPassword, CancellationToken cancellationToken = default)
        => ExportAsync(path, catalog, settings, cancellationToken);
}