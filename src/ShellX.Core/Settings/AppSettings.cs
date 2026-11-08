namespace ShellX.Core.Settings;

public enum AppTheme { Dark, Light }

public enum ProcessSortColumn { Cpu, Memory }

public sealed record AppSettings
{
    public AppTheme Theme { get; init; } = AppTheme.Dark;
    public int Version { get; init; } = 1;
    public double TerminalFontSize { get; init; } = 14;
    public string TerminalFontFamily { get; init; } = "Cascadia Mono,Consolas,monospace";
    public string EditorFontFamily { get; init; } = "Cascadia Mono,Consolas,monospace";
    public double EditorFontSize { get; init; } = 14;
    public string LockPasswordHash { get; init; } = "";
    public bool AutoLockEnabled { get; init; }
    public bool AutoLockOnlyWhenSystemLocked { get; init; } = true;
    public int AutoLockMinutes { get; init; } = 15;
    public bool ShowHiddenFiles { get; init; }
    public bool HighlightTerminalText { get; init; } = true;
    public bool AutoReconnectEnabled { get; init; } = true;
    public int ReconnectIntervalSeconds { get; init; } = 5;
    public ProcessSortColumn ProcessSortColumn { get; init; } = ProcessSortColumn.Cpu;
    public bool ProcessSortDescending { get; init; } = true;
    public double MonitorPaneWidth { get; init; } = 220;
    public double TerminalPaneStar { get; init; } = 1.3;
    public double LocalPaneStar { get; init; } = 1;
    public double RemoteTreeWidth { get; init; } = 150;
    public bool McpEnabled { get; init; } = true;
    public string McpAddress { get; init; } = "http://127.0.0.1:8765";
    public string McpToken { get; init; } = "";
    public string UpdateRepositoryOwner { get; init; } = "coolqingcheng";
    public string UpdateRepositoryName { get; init; } = "shell-x";
    public bool CheckUpdateOnStartup { get; init; } = true;
    /// <summary>点击窗口关闭按钮时最小化到系统托盘，而不是退出程序。</summary>
    public bool CloseToTray { get; init; } = true;
}

public interface ISettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
