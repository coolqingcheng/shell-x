using ShellX.Core.Connections;
using ShellX.Core.Files;
using ShellX.Core.Settings;
using ShellX.Core.Tunnels;

namespace ShellX.Desktop.Services;

public sealed record ConnectionEditResult(ConnectionProfile Profile, ConnectionSecret Secret);

public interface IDialogService
{
    Task<ConnectionEditResult?> EditConnectionAsync(ConnectionProfile? profile, ConnectionSecret? secret);
    Task<ConnectionEditResult?> EditConnectionAsync(ConnectionProfile? profile, ConnectionSecret? secret, IReadOnlyList<string> folders, string defaultFolder)
        => EditConnectionAsync(profile, secret);
    Task<ConnectionProfile?> ManageConnectionsAsync(Features.Shell.MainWindowViewModel model) => Task.FromResult<ConnectionProfile?>(null);
    Task<bool> ConfirmAsync(string title, string message, bool danger = false);
    Task<string?> PromptAsync(string title, string label, string value = "", bool secret = false);
    Task ShowErrorAsync(string message);
    Task ShowInformationAsync(string title, string message) => ConfirmAsync(title, message);
    Task<IReadOnlyList<string>> PickUploadAsync();
    Task<string?> PickDownloadAsync(string name);
    Task<string?> PickConfigurationExportAsync() => PickDownloadAsync("ShellX-config.json");
    Task<string?> PickConfigurationImportAsync() => Task.FromResult<string?>(null);
    Task<IReadOnlyList<string>> PickXshellSessionsAsync() => PickUploadAsync();
    Task<string?> PickDownloadFolderAsync() => Task.FromResult<string?>(null);
    Task EditFileAsync(TextDocument document, Func<TextDocument, string, Task<TextDocument>> save);
    Task<int?> PermissionsAsync(RemoteEntry entry);
    Task<TunnelDefinition?> EditTunnelAsync();
    Task<TunnelDefinition?> EditTunnelAsync(TunnelDefinition definition) => EditTunnelAsync();
    Task<AppSettings?> SettingsAsync(AppSettings settings);
}
