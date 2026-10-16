using System.Text.Json;
using ShellX.Core.Connections;
using ShellX.Core.Settings;

namespace ShellX.Infrastructure.Persistence;

public sealed class JsonConfigurationExporter(AppPaths paths) : IConfigurationExporter
{
    public async Task ExportAsync(string path, ConnectionCatalog catalog, AppSettings settings, CancellationToken cancellationToken = default)
    {
        path = Path.GetFullPath(path);
        if (Directory.Exists(path)) throw new IOException("导出路径是文件夹，请选择 JSON 文件。");
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (new[] { "connections.json", "settings.json", "known-hosts.json" }.Any(name => path.Equals(Path.GetFullPath(paths.File(name)), comparison)) ||
            path.StartsWith(Path.GetFullPath(paths.File("credentials")) + Path.DirectorySeparatorChar, comparison))
            throw new IOException("请选择其他导出位置，不能覆盖软件正在使用的配置或凭据文件。");
        var exported = new ExportedConfiguration
        {
            Connections = catalog.Connections.Select(p => StoredConnection.From(p with { RememberSecret = false })).ToList(),
            Folders = ConnectionFolders.Expand(catalog.Folders.Concat(catalog.Connections.Select(p => p.Group))).ToList(),
            Settings = new ExportedSettings
            {
                Theme = settings.Theme, TerminalFontFamily = settings.TerminalFontFamily, TerminalFontSize = settings.TerminalFontSize,
                EditorFontFamily = settings.EditorFontFamily, EditorFontSize = settings.EditorFontSize,
                ShowHiddenFiles = settings.ShowHiddenFiles, HighlightTerminalText = settings.HighlightTerminalText,
                AutoReconnectEnabled = settings.AutoReconnectEnabled, ReconnectIntervalSeconds = settings.ReconnectIntervalSeconds,
                ProcessSortColumn = settings.ProcessSortColumn, ProcessSortDescending = settings.ProcessSortDescending
            }
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

// Deliberately whitelist portable preferences; security settings and credentials never enter the export.
internal sealed class ExportedConfiguration
{
    public string Format { get; set; } = "ShellX.Configuration";
    public int Version { get; set; } = 1;
    public DateTimeOffset ExportedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<StoredConnection> Connections { get; set; } = [];
    public List<string> Folders { get; set; } = [];
    public ExportedSettings Settings { get; set; } = new();
}

internal sealed class ExportedSettings
{
    public AppTheme Theme { get; set; }
    public string TerminalFontFamily { get; set; } = "";
    public double TerminalFontSize { get; set; }
    public string EditorFontFamily { get; set; } = "";
    public double EditorFontSize { get; set; }
    public bool ShowHiddenFiles { get; set; }
    public bool HighlightTerminalText { get; set; }
    public bool AutoReconnectEnabled { get; set; }
    public int ReconnectIntervalSeconds { get; set; }
    public ProcessSortColumn ProcessSortColumn { get; set; }
    public bool ProcessSortDescending { get; set; }
}
