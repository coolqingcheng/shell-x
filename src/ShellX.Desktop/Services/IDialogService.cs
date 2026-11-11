using ShellX.Core.Connections;
using ShellX.Core.Files;
using ShellX.Core.Settings;
using ShellX.Core.Tunnels;

namespace ShellX.Desktop.Services;

public sealed record ConnectionEditResult(ConnectionProfile Profile, ConnectionSecret Secret);

/// <summary>确认框结果：Confirmed 表示是否确认；Remember 表示勾选了“保持选择”（本次批量操作不再询问）。</summary>
public sealed record ConfirmOutcome(bool Confirmed, bool Remember);

public interface IDialogService
{
    Task<ConnectionEditResult?> EditConnectionAsync(ConnectionProfile? profile, ConnectionSecret? secret);
    Task<ConnectionEditResult?> EditConnectionAsync(ConnectionProfile? profile, ConnectionSecret? secret, IReadOnlyList<string> folders, string defaultFolder)
        => EditConnectionAsync(profile, secret);
    Task<ConnectionProfile?> ManageConnectionsAsync(Features.Shell.MainWindowViewModel model) => Task.FromResult<ConnectionProfile?>(null);
    Task<bool> ConfirmAsync(string title, string message, bool danger = false);
    /// <summary>带“保持选择”复选框的确认框，用于批量操作只确认一次。</summary>
    async Task<ConfirmOutcome> ConfirmWithRememberAsync(string title, string message, bool danger = false)
        => new(await ConfirmAsync(title, message, danger), false);
    Task<string?> PromptAsync(string title, string label, string value = "", bool secret = false);
    Task ShowErrorAsync(string message);
    Task ShowInformationAsync(string title, string message) => ConfirmAsync(title, message);
    Task<IReadOnlyList<string>> PickUploadAsync();
    Task<string?> PickDownloadAsync(string name);
    Task<string?> PickConfigurationExportAsync() => PickDownloadAsync("ShellX-config.json");
    /// <summary>登记窗口为模态对话框的候选父窗口；最近激活的窗口优先。</summary>
    void TrackOwnerWindow(Avalonia.Controls.Window window) { }
    /// <summary>注销窗口。</summary>
    void UntrackOwnerWindow(Avalonia.Controls.Window window) { }
    Task<string?> PickConfigurationImportAsync() => Task.FromResult<string?>(null);
    Task<IReadOnlyList<string>> PickXshellSessionsAsync() => PickUploadAsync();
    Task<string?> PickDownloadFolderAsync() => Task.FromResult<string?>(null);
    Task EditFileAsync(TextDocument document, Func<TextDocument, string, Task<TextDocument>> save);
    Task<int?> PermissionsAsync(RemoteEntry entry);
    Task<TunnelDefinition?> EditTunnelAsync();
    Task<TunnelDefinition?> EditTunnelAsync(TunnelDefinition definition) => EditTunnelAsync();
    Task<AppSettings?> SettingsAsync(AppSettings settings);
}
