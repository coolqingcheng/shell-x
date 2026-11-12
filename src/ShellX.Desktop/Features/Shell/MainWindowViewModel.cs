using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ShellX.Core.Connections;
using ShellX.Core.Files;
using ShellX.Core.Security;
using ShellX.Core.Sessions;
using ShellX.Core.Settings;
using ShellX.Desktop.Features.Connections;
using ShellX.Desktop.Features.FileBrowser;
using ShellX.Desktop.Features.Workspace;
using ShellX.Desktop.Features.Monitoring;
using ShellX.Desktop.Features.Security;
using ShellX.Desktop.Services;

namespace ShellX.Desktop.Features.Shell;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IConnectionRepository _connections;
    private readonly ICredentialStore _credentials;
    private readonly IHostKeyStore _hosts;
    private readonly ISettingsStore _settingsStore;
    private readonly IConfigurationExporter _configurationExporter;
    private readonly IConfigurationImporter? _configurationImporter;
    private readonly IRemoteSessionFactory _sessions;
    private readonly IDialogService _dialogs;
    private readonly ILoggerFactory _logs;
    private readonly ILocalFileSystem _localFiles;
    private readonly AppLockService _applicationLock;
    private readonly Dictionary<Guid, ConnectionSecret> _secrets = [];
    private readonly SemaphoreSlim _hostGate = new(1, 1);
    private List<ConnectionProfile> _profiles = [];
    private List<string> _folders = [];
    private readonly SemaphoreSlim _catalogGate = new(1, 1);
    private readonly SharedMonitorRegistry _monitors;
    private Window? _fileWindow;
    private readonly HashSet<WorkspaceTabViewModel> _fileManagerTabSubscriptions = [];
    private bool _disposingFileManagerTabs;
    public ObservableCollection<ConnectionProfile> VisibleConnections { get; } = [];
    public event Action<ConnectionProfile>? ConnectionPicked;
    [ObservableProperty] private ConnectionGroupViewModel? _selectedGroup;
    public bool CanManageFolder => SelectedGroup is { Path.Length: > 0 };
    public string FolderCaption => SelectedGroup?.Name ?? "全部连接";
    /// <summary>品牌区展示的版本号，与窗口标题、设置页保持一致。</summary>
    public string VersionText => $"v{Core.AppVersion.Value}";
    private AppSettings _settings = new();
    public AppSettings LayoutSettings => _settings;
    public event Action<AppSettings>? LayoutChanged;
    private bool _configurationLoaded;
    public ObservableCollection<ConnectionGroupViewModel> Groups { get; } = [];
    public ObservableCollection<WorkspaceTabViewModel> Tabs { get; } = [];
    public ObservableCollection<FileManagerTabViewModel> FileManagerTabs { get; } = [];
    [ObservableProperty] private FileManagerTabViewModel? _selectedFileManagerTab;
    public string FileManagerWindowTitle => SelectedFileManagerTab is null ? "文件管理" : $"文件管理 · {SelectedFileManagerTab.Title}";
    public bool HasSelectedFileManagerTab => SelectedFileManagerTab is not null;
    public Window? FileManagerWindow => _fileWindow;
    /// <summary>对话框服务：各窗口用它登记层级关系，保证弹窗落在正确的父窗口上。</summary>
    public IDialogService Dialogs => _dialogs;
    [ObservableProperty] private WorkspaceTabViewModel? _selectedTab;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _status = "就绪";
    /// <summary>左侧服务器状态面板是否收起。</summary>
    [ObservableProperty] private bool _isMonitorPaneCollapsed;
    [ObservableProperty] private int _connectionCount;
    public bool HasTabs => Tabs.Count > 0;
    public bool HasNoTabs => !HasTabs;
    public bool HasMcpConnections => _settings.McpEnabled && _profiles.Any(profile => profile.McpEnabled);
    public bool NeedsLeafFolder => SelectedGroup is not { Path.Length: > 0, Children.Count: 0 };
    public bool HasNoConnections => !NeedsLeafFolder && VisibleConnections.Count == 0 && string.IsNullOrWhiteSpace(Search);
    public bool HasNoResults => !NeedsLeafFolder && VisibleConnections.Count == 0 && !string.IsNullOrWhiteSpace(Search);

    public MainWindowViewModel(IConnectionRepository connections, ICredentialStore credentials, IHostKeyStore hosts,
        ISettingsStore settingsStore, IRemoteSessionFactory sessions, IDialogService dialogs, ILoggerFactory logs, ILocalFileSystem localFiles, AppLockService applicationLock, IConfigurationExporter configurationExporter, IConfigurationImporter? configurationImporter = null)
    { _connections = connections; _credentials = credentials; _hosts = hosts; _settingsStore = settingsStore; _configurationExporter = configurationExporter; _configurationImporter = configurationImporter; _sessions = sessions; _dialogs = dialogs; _logs = logs; _localFiles = localFiles; _applicationLock = applicationLock; _monitors = new(dialogs, logs); }
    public async Task InitializeAsync()
    {
        try
        {
            var catalog = await _connections.LoadCatalogAsync(); _profiles = catalog.Connections.ToList(); _folders = catalog.Folders.ToList();
            _configurationLoaded = true;
            _settings = await _settingsStore.LoadAsync(); IsMonitorPaneCollapsed = _settings.MonitorPaneCollapsed; ThemeService.Apply(_settings.Theme); _applicationLock.Configure(_settings, startup: true); RefreshGroups();
        }
        catch (Exception ex) { _applicationLock.ConfigurationFailed(); Status = "配置加载失败"; await _dialogs.ShowErrorAsync("配置未被覆盖，请检查本地配置文件。\n" + ex.Message); }
    }
    partial void OnSearchChanged(string value) => RefreshVisibleConnections();
    partial void OnSelectedGroupChanged(ConnectionGroupViewModel? value)
    { RefreshVisibleConnections(); OnPropertyChanged(nameof(CanManageFolder)); OnPropertyChanged(nameof(FolderCaption)); }
    partial void OnSelectedTabChanged(WorkspaceTabViewModel? value)
    { foreach (var tab in Tabs) tab.IsSelected = ReferenceEquals(tab, value); }
    partial void OnSelectedFileManagerTabChanged(FileManagerTabViewModel? value)
    {
        // 页签外观与终端标签一致，选中状态由这里同步给视图模型。
        foreach (var tab in FileManagerTabs) tab.IsActive = ReferenceEquals(tab, value);
        // 移除当前页签时 ListBox 会先回写 null；剩余页签存在时立即恢复选择，避免内容区变空。
        if (value is null && !_disposingFileManagerTabs && FileManagerTabs.Count > 0)
        {
            SelectedFileManagerTab = FileManagerTabs[0];
            return;
        }
        OnPropertyChanged(nameof(FileManagerWindowTitle));
        OnPropertyChanged(nameof(HasSelectedFileManagerTab));
        if (_fileWindow is not null) _fileWindow.Title = WindowTitles.ApplyVersionToTitle(FileManagerWindowTitle);
    }
    public IReadOnlyList<string> FolderPaths => ConnectionFolders.Expand(_folders.Concat(_profiles.Select(p => p.Group)));
    private void RefreshGroups()
    {
        var selected = SelectedGroup?.Path ?? "";
        ConnectionCount = _profiles.Count;
        var root = new ConnectionGroupViewModel("", "全部连接", _profiles.Count);
        var nodes = new Dictionary<string, ConnectionGroupViewModel>(StringComparer.Ordinal) { [""] = root };
        foreach (var path in FolderPaths.OrderBy(p => p.Count(c => c == '/')).ThenBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var separator = path.LastIndexOf('/');
            var parent = separator < 0 ? "" : path[..separator];
            var node = new ConnectionGroupViewModel(path, separator < 0 ? path : path[(separator + 1)..],
                _profiles.Count(p => ConnectionFolders.Contains(path, ConnectionFolders.Normalize(p.Group))));
            nodes.Add(path, node); nodes[parent].Children.Add(node);
        }
        Groups.Clear(); Groups.Add(root); SelectedGroup = nodes.GetValueOrDefault(selected, root);
        RefreshVisibleConnections(); OnPropertyChanged(nameof(FolderPaths)); OnPropertyChanged(nameof(HasMcpConnections));
    }
    private void RefreshVisibleConnections()
    {
        VisibleConnections.Clear();
        var folder = SelectedGroup?.Path ?? "";
        foreach (var profile in _profiles.Where(p => !NeedsLeafFolder && folder == ConnectionFolders.Normalize(p.Group) &&
            (string.IsNullOrWhiteSpace(Search) || $"{p.Name} {p.Host} {p.Username} {p.Group}".Contains(Search, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)) VisibleConnections.Add(profile);
        OnPropertyChanged(nameof(NeedsLeafFolder)); OnPropertyChanged(nameof(HasNoConnections)); OnPropertyChanged(nameof(HasNoResults));
    }
    private async Task CommitCatalogAsync(Func<ConnectionCatalog, ConnectionCatalog> update)
    {
        await _catalogGate.WaitAsync();
        try
        {
            if (!_configurationLoaded) throw new IOException("连接配置尚未成功加载，不能覆盖现有配置。");
            var next = update(new(_profiles, _folders));
            var folders = ConnectionFolders.Expand(next.Folders.Concat(next.Connections.Select(p => p.Group)));
            await _connections.SaveCatalogAsync(new(next.Connections, folders));
            _profiles = next.Connections.ToList(); _folders = folders.ToList(); RefreshGroups();
            foreach (var tab in Tabs)
                if (_profiles.FirstOrDefault(p => p.Id == tab.Profile.Id) is { } profile) tab.UpdateProfile(profile);
        }
        finally { _catalogGate.Release(); }
    }
    private Task SaveProfileAsync(ConnectionProfile profile)
    {
        profile = profile with { Group = ConnectionFolders.Normalize(profile.Group) };
        return CommitCatalogAsync(catalog => catalog with { Connections = catalog.Connections.Where(p => p.Id != profile.Id).Append(profile).ToArray() });
    }
    [RelayCommand]
    private async Task NewFolderAsync()
    {
        if (_applicationLock.IsLocked) return;
        var parent = SelectedGroup?.Path ?? "";
        var name = await _dialogs.PromptAsync("新建连接文件夹", "文件夹名称"); if (name == null) return;
        try
        {
            name = name.Trim(); ConnectionFolders.ValidateName(name);
            var path = parent.Length == 0 ? name : parent + "/" + name;
            await CommitCatalogAsync(catalog =>
            {
                if (ConnectionFolders.Expand(catalog.Folders.Concat(catalog.Connections.Select(p => p.Group))).Contains(path))
                    throw new ArgumentException("同名连接文件夹已存在。");
                return catalog with { Folders = [.. catalog.Folders, path] };
            });
            SelectedGroup = FindFolder(path); Status = "已创建连接文件夹";
        }
        catch (Exception ex) { await _dialogs.ShowErrorAsync(ex.Message); }
    }
    [RelayCommand]
    private async Task ExportConfigurationAsync()
    {
        if (_applicationLock.IsLocked || !_configurationLoaded) return;
        try
        {
            var path = await _dialogs.PickConfigurationExportAsync();
            if (path == null || _applicationLock.IsLocked) return;
            ConnectionCatalog catalog;
            await _catalogGate.WaitAsync();
            try { catalog = new(_profiles.ToArray(), FolderPaths.ToArray()); }
            finally { _catalogGate.Release(); }

            var secrets = new Dictionary<Guid, ConnectionSecret>();
            foreach (var profile in catalog.Connections)
            {
                var secret = _secrets.GetValueOrDefault(profile.Id) ?? await _credentials.ReadAsync(profile.Id);
                if (secret is { Password.Length: > 0 } or { Passphrase.Length: > 0 }) secrets[profile.Id] = secret;
            }

            string? encryptionPassword = null;
            if (secrets.Count > 0)
            {
                encryptionPassword = await _dialogs.PromptAsync("导出配置（含已保存凭据）",
                    "设置备份加密密码（至少 8 个字符，导入时输入）", "", true);
                if (encryptionPassword == null || _applicationLock.IsLocked) return;
                if (encryptionPassword.Length < 8)
                {
                    await _dialogs.ShowErrorAsync("备份加密密码至少需要 8 个字符。");
                    return;
                }
            }

            await _configurationExporter.ExportAsync(path, catalog, _settings, secrets, encryptionPassword);
            Status = "配置导出成功";
            var credentialText = secrets.Count > 0
                ? $"包含 {secrets.Count} 条已保存的登录密码或私钥口令，使用本次设置的备份加密密码保护；导入时输入该密码即可直接使用。"
                : "当前没有可导出的已保存登录密码或私钥口令。";
            await _dialogs.ShowInformationAsync("导出成功",
                $"已导出 {catalog.Connections.Count} 个连接及文件夹、隧道和常规设置。\n{path}\n\n{credentialText}\n锁定密码、私钥文件内容和应用本机安全设置不会导出。");
        }
        catch (Exception ex) { await _dialogs.ShowErrorAsync("导出失败：\n" + ex.Message); }
    }
    [RelayCommand]
    private async Task ImportConfigurationAsync()
    {
        if (_applicationLock.IsLocked || !_configurationLoaded) return;
        try
        {
            if (_configurationImporter == null)
            {
                await _dialogs.ShowErrorAsync("当前版本不支持导入 ShellX 配置。");
                return;
            }
            var path = await _dialogs.PickConfigurationImportAsync();
            if (path == null || _applicationLock.IsLocked) return;

            string? encryptionPassword = null;
            if (await _configurationImporter.HasCredentialsAsync(path))
            {
                encryptionPassword = await _dialogs.PromptAsync("导入配置（含已保存凭据）",
                    "输入导出时设置的备份密码", "", true);
                if (encryptionPassword == null || _applicationLock.IsLocked) return;
                if (encryptionPassword.Length == 0)
                {
                    await _dialogs.ShowErrorAsync("该配置包含加密凭据，必须输入备份密码。");
                    return;
                }
            }

            var restore = await _configurationImporter.ImportAsync(path, encryptionPassword);
            if (_applicationLock.IsLocked) return;

            var added = 0;
            var updated = 0;
            var skipped = 0;
            await CommitCatalogAsync(catalog =>
            {
                var profiles = catalog.Connections.ToList();
                var endpoints = profiles.Select(ConnectionEndpoint.From).ToHashSet();
                foreach (var imported in restore.Catalog.Connections)
                {
                    var index = profiles.FindIndex(profile => profile.Id == imported.Id);
                    if (index >= 0)
                    {
                        endpoints.Remove(ConnectionEndpoint.From(profiles[index]));
                        profiles[index] = imported;
                        endpoints.Add(ConnectionEndpoint.From(imported));
                        updated++;
                    }
                    else if (endpoints.Add(ConnectionEndpoint.From(imported)))
                    {
                        profiles.Add(imported);
                        added++;
                    }
                    else skipped++;
                }
                var folders = ConnectionFolders.Expand(catalog.Folders.Concat(restore.Catalog.Folders).Concat(profiles.Select(profile => profile.Group)));
                return new(profiles, folders);
            });

            var restoredSecrets = 0;
            var secretFailures = new List<string>();
            foreach (var pair in restore.Secrets)
            {
                if (pair.Value.Password.Length == 0 && pair.Value.Passphrase.Length == 0) continue;
                if (!_profiles.Any(profile => profile.Id == pair.Key)) continue;
                try { await _credentials.SaveAsync(pair.Key, pair.Value); restoredSecrets++; }
                catch (Exception ex) { secretFailures.Add(ex.Message); }
            }

            var portable = restore.Settings;
            _settings = _settings with
            {
                Theme = portable.Theme, TerminalFontFamily = portable.TerminalFontFamily, TerminalFontSize = portable.TerminalFontSize,
                EditorFontFamily = portable.EditorFontFamily, EditorFontSize = portable.EditorFontSize, ShowHiddenFiles = portable.ShowHiddenFiles,
                HighlightTerminalText = portable.HighlightTerminalText, AutoReconnectEnabled = portable.AutoReconnectEnabled,
                ReconnectIntervalSeconds = portable.ReconnectIntervalSeconds, ProcessSortColumn = portable.ProcessSortColumn,
                ProcessSortDescending = portable.ProcessSortDescending
            };
            await _settingsStore.SaveAsync(_settings);
            ThemeService.Apply(_settings.Theme);
            LayoutChanged?.Invoke(_settings);

            Status = "ShellX 配置导入成功";
            var summary = $"新增 {added} 个连接，更新 {updated} 个，跳过重复 {skipped} 个，恢复凭据 {restoredSecrets} 条。";
            if (secretFailures.Count > 0)
                summary += "\n部分凭据未能写入本机安全存储：\n" + string.Join('\n', secretFailures);
            summary += "\n锁定密码、私钥文件内容和本机 MCP / 更新设置不会被导入覆盖。";
            await _dialogs.ShowInformationAsync("导入成功", summary);
        }
        catch (Exception ex) { await _dialogs.ShowErrorAsync("配置导入失败：\n" + ex.Message); }
    }
    [RelayCommand]
    private async Task ImportXshellSessionsAsync()
    {
        if (_applicationLock.IsLocked || !_configurationLoaded) return;
        try
        {
            var paths = await _dialogs.PickXshellSessionsAsync();
            if (paths.Count == 0 || _applicationLock.IsLocked) return;

            // 选中了最底层文件夹就导入到那里，否则统一放进 Xshell 文件夹，避免混入现有分组。
            var group = SelectedGroup is { Path.Length: > 0, Children.Count: 0 } folder
                ? folder.Path
                : XshellSessionImporter.DefaultGroup;
            var parsed = new List<XshellImportResult>();
            var failures = new List<string>();
            var warnings = new List<string>();
            var selectedDuplicates = 0;
            var seen = new HashSet<ConnectionEndpoint>();
            string? credentialKey = null;
            var promptedCredentialKey = false;

            void AddImported(XshellImportResult imported)
            {
                var profile = imported.Profile with { Group = ConnectionFolders.Normalize(group) };
                profile.Validate();
                imported = imported with { Profile = profile };
                if (seen.Add(ConnectionEndpoint.From(profile))) parsed.Add(imported);
                else selectedDuplicates++;
            }

            foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try { AddImported(XshellSessionImporter.ParseFile(path, credentialKey)); }
                catch (XshellCredentialDecryptException ex) when (!promptedCredentialKey)
                {
                    promptedCredentialKey = true;
                    var masterKey = await _dialogs.PromptAsync("Xshell 主密码",
                        "默认使用当前 Windows 账户解密；检测到会话使用了 Xshell 主密码，请输入主密码", "", true);
                    if (masterKey == null || _applicationLock.IsLocked)
                    {
                        failures.Add($"{Path.GetFileName(path)}：{ex.Message}");
                        continue;
                    }
                    if (masterKey.Length == 0)
                    {
                        failures.Add($"{Path.GetFileName(path)}：未输入 Xshell 主密码。");
                        continue;
                    }
                    credentialKey = masterKey;
                    try { AddImported(XshellSessionImporter.ParseFile(path, credentialKey)); }
                    catch (Exception retryEx) { failures.Add($"{Path.GetFileName(path)}：{retryEx.Message}"); }
                }
                catch (Exception ex) { failures.Add($"{Path.GetFileName(path)}：{ex.Message}"); }
            }
            if (parsed.Count == 0)
            {
                Status = "Xshell 导入失败";
                var detail = failures.Count == 0 ? "所选会话与现有连接重复。" : string.Join('\n', failures);
                await _dialogs.ShowErrorAsync("没有导入任何 Xshell 连接。\n" + detail);
                return;
            }

            var added = 0;
            var duplicates = selectedDuplicates;
            var addedIds = new HashSet<Guid>();
            await CommitCatalogAsync(catalog =>
            {
                var known = catalog.Connections.Select(ConnectionEndpoint.From).ToHashSet();
                var additions = parsed.Where(item => known.Add(ConnectionEndpoint.From(item.Profile))).ToArray();
                added = additions.Length;
                duplicates += parsed.Count - additions.Length;
                foreach (var item in additions) addedIds.Add(item.Profile.Id);
                if (added == 0) return catalog;
                return catalog with
                {
                    Connections = [.. catalog.Connections, .. additions.Select(item => item.Profile)],
                    Folders = [.. catalog.Folders, ConnectionFolders.Normalize(group)]
                };
            });

            var restoredSecrets = 0;
            var credentialFailures = new List<string>();
            foreach (var item in parsed.Where(item => addedIds.Contains(item.Profile.Id)))
            {
                if (item.Warning != null) warnings.Add($"{item.Profile.Name}：{item.Warning}");
                if (item.Secret.Password.Length == 0 && item.Secret.Passphrase.Length == 0) continue;
                try { await _credentials.SaveAsync(item.Profile.Id, item.Secret); restoredSecrets++; }
                catch (Exception ex) { credentialFailures.Add($"{item.Profile.Name}：{ex.Message}"); }
            }

            SelectedGroup = FindFolder(ConnectionFolders.Normalize(group));
            Search = "";
            Status = added > 0 ? $"已从 Xshell 导入 {added} 个连接" : "Xshell 会话均为重复连接";
            var summary = $"新增 {added} 个连接，跳过重复 {duplicates} 个，已解密并保存凭据 {restoredSecrets} 条。";
            if (warnings.Count > 0) summary += "\n注意：\n" + string.Join('\n', warnings);
            if (credentialFailures.Count > 0) summary += "\n凭据未能写入本机安全存储：\n" + string.Join('\n', credentialFailures);
            if (failures.Count > 0) summary += $"\n未导入 {failures.Count} 个文件：\n" + string.Join('\n', failures);
            summary += "\n\nXshell 密码只在解密后写入本机安全存储，不会明文写入连接配置。";
            await _dialogs.ShowInformationAsync(added > 0 ? "Xshell 导入完成" : "Xshell 导入完成", summary);
        }
        catch (Exception ex) { await _dialogs.ShowErrorAsync("Xshell 导入失败：\n" + ex.Message); }
    }
    public ConnectionGroupViewModel? FindFolder(string path)
    {
        ConnectionGroupViewModel? Find(IEnumerable<ConnectionGroupViewModel> nodes)
        {
            foreach (var node in nodes) { if (node.Path == path) return node; if (Find(node.Children) is { } found) return found; }
            return null;
        }
        return Find(Groups);
    }
    [RelayCommand]
    private async Task RenameFolderAsync()
    {
        if (_applicationLock.IsLocked || SelectedGroup is not { Path.Length: > 0 } folder) return;
        var name = await _dialogs.PromptAsync("重命名连接文件夹", "新名称", folder.Name); if (name == null) return;
        try
        {
            name = name.Trim(); ConnectionFolders.ValidateName(name);
            if (name == folder.Name) return;
            var index = folder.Path.LastIndexOf('/');
            var path = index < 0 ? name : folder.Path[..(index + 1)] + name;
            await CommitCatalogAsync(catalog =>
            {
                if (ConnectionFolders.Expand(catalog.Folders.Concat(catalog.Connections.Select(p => p.Group))).Contains(path))
                    throw new ArgumentException("同名连接文件夹已存在。");
                string Rename(string old) => ConnectionFolders.Contains(folder.Path, old) ? path + old[folder.Path.Length..] : old;
                return new(catalog.Connections.Select(p => p with { Group = Rename(ConnectionFolders.Normalize(p.Group)) }).ToArray(),
                    catalog.Folders.Select(Rename).ToArray());
            });
            SelectedGroup = FindFolder(path); Status = "已重命名连接文件夹";
        }
        catch (Exception ex) { await _dialogs.ShowErrorAsync(ex.Message); }
    }
    [RelayCommand]
    private async Task DeleteFolderAsync()
    {
        if (_applicationLock.IsLocked || SelectedGroup is not { Path.Length: > 0 } folder) return;
        try
        {
            await CommitCatalogAsync(catalog =>
            {
                if (catalog.Connections.Any(p => ConnectionFolders.Contains(folder.Path, ConnectionFolders.Normalize(p.Group))) ||
                    catalog.Folders.Any(p => p != folder.Path && ConnectionFolders.Contains(folder.Path, p)))
                    throw new ArgumentException("请先移走连接和子文件夹，再删除这个文件夹。");
                return catalog with { Folders = catalog.Folders.Where(p => p != folder.Path).ToArray() };
            });
        }
        catch (Exception ex) { await _dialogs.ShowErrorAsync(ex.Message); }
    }
    [RelayCommand] private void PickConnection(ConnectionProfile? profile)
    { if (profile != null && !_applicationLock.IsLocked) ConnectionPicked?.Invoke(profile); }
    private async Task<ConnectionSecret?> SecretAsync(ConnectionProfile profile)
    {
        if (_secrets.TryGetValue(profile.Id, out var cached)) return cached;
        if (profile.RememberSecret && await _credentials.ReadAsync(profile.Id) is { } saved) { _secrets[profile.Id] = saved; return saved; }
        var value = await _dialogs.PromptAsync("连接 · " + profile.Name,
            profile.Authentication == AuthenticationKind.Password ? "请输入密码" : "请输入私钥口令（未加密私钥可留空）", secret: true);
        if (value == null) return null;
        return profile.Authentication == AuthenticationKind.Password ? new(value) : new("", value);
    }
    private async Task EditAsync(ConnectionProfile? profile)
    {
        if (_applicationLock.IsLocked) return;
        try
        {
            ConnectionSecret? secret = null;
            if (profile != null) { _secrets.TryGetValue(profile.Id, out secret); if (secret == null && profile.RememberSecret) secret = await _credentials.ReadAsync(profile.Id); }
            if (await _dialogs.EditConnectionAsync(profile, secret, FolderPaths, SelectedGroup?.Path is { Length: > 0 } folder ? folder : ConnectionFolders.Default) is not { } result) return;
            if (result.Profile.RememberSecret) await _credentials.SaveAsync(result.Profile.Id, result.Secret);
            else await _credentials.DeleteAsync(result.Profile.Id);
            await SaveProfileAsync(result.Profile); _secrets[result.Profile.Id] = result.Secret;
            Status = "已保存连接 · " + result.Profile.Name;
        }
        catch (Exception ex) { await _dialogs.ShowErrorAsync(ex.Message); }
    }
    [RelayCommand] private Task NewConnectionAsync() => EditAsync(null);
    [RelayCommand] private async Task ToggleConnectionsAsync()
    {
        if (_applicationLock.IsLocked) return;
        if (await _dialogs.ManageConnectionsAsync(this) is { } profile) _ = ConnectCommand.ExecuteAsync(profile);
    }
    [RelayCommand] private Task EditConnectionAsync(ConnectionProfile? profile) => profile == null ? Task.CompletedTask : EditAsync(profile);
    [RelayCommand]
    private async Task DeleteConnectionAsync(ConnectionProfile? profile)
    {
        if (profile == null || !await _dialogs.ConfirmAsync("删除连接", $"删除“{profile.Name}”的连接配置？已打开的标签不受影响。", true)) return;
        try
        {
            await CommitCatalogAsync(catalog => catalog with { Connections = catalog.Connections.Where(p => p.Id != profile.Id).ToArray() });
            await _credentials.DeleteAsync(profile.Id); _secrets.Remove(profile.Id); RefreshGroups();
        }
        catch (Exception ex) { await _dialogs.ShowErrorAsync(ex.Message); }
    }
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ConnectAsync(ConnectionProfile? profile)
    {
        if (profile == null || _applicationLock.IsLocked) return;
        try
        {
            var secret = await SecretAsync(profile); if (secret == null) return;
            var tab = CreateWorkspaceTab(profile);
            Tabs.Add(tab); SelectedTab = tab; NotifyTabs();
            await tab.ConnectWithSecretAsync(secret);
        }
        catch (Exception ex) { await _dialogs.ShowErrorAsync(ex.Message); }
    }

    /// <summary>新建一个工作区标签，参数都接到外壳上。</summary>
    private WorkspaceTabViewModel CreateWorkspaceTab(ConnectionProfile profile)
    {
        WorkspaceTabViewModel? tab = null;
        tab = new WorkspaceTabViewModel(profile, _sessions, _dialogs, _logs, () => SecretAsync(tab!.Profile), VerifyHostAsync,
            SaveProfileAsync, CloseTabAsync, _settings, _localFiles, SaveProcessSortAsync, _monitors, OpenTerminalAtAsync);
        return tab;
    }

    /// <summary>文件树里“在终端打开”：新建一个到同一主机的连接，并把终端切到该目录。</summary>
    private async Task OpenTerminalAtAsync(WorkspaceTabViewModel source, string path)
    {
        if (_applicationLock.IsLocked || string.IsNullOrWhiteSpace(path)) return;
        var profile = source.Profile;
        try
        {
            // 优先复用当前会话的密钥，必要时才重新要一次。
            var secret = source.CurrentSecret ?? await SecretAsync(profile);
            if (secret == null) return;
            var tab = CreateWorkspaceTab(profile);
            tab.SetTemporaryName(profile.Name + " · " + path);
            Tabs.Add(tab); SelectedTab = tab; NotifyTabs();
            await tab.ConnectWithSecretAsync(secret);
            if (!tab.IsConnected) return;
            await tab.ChangeDirectoryAsync(path);
            // 文件面板也跟着切到同一个目录，新标签打开就是那个地方。
            try { await tab.Files.NavigateAsync(path); } catch (Exception) { }
        }
        catch (Exception ex) { await _dialogs.ShowErrorAsync(ex.Message); }
    }
    private async Task<bool> VerifyHostAsync(HostKeyChallenge challenge)
    {
        await _hostGate.WaitAsync();
        try
        {
            var known = await _hosts.GetAsync(challenge.Host, challenge.Port);
            if (known == challenge.Fingerprint) return true;
            if (known != null)
            {
                await Dispatcher.UIThread.InvokeAsync(async () => await _dialogs.ShowErrorAsync(
                    $"主机密钥发生变化，已拒绝连接。\n{challenge.Host}:{challenge.Port}\n已保存：{known}\n当前：{challenge.Fingerprint}\n请核实服务器身份后再更新 known-hosts.json。"));
                return false;
            }
            var trusted = await Dispatcher.UIThread.InvokeAsync(async () => await _dialogs.ConfirmAsync("首次连接 · 确认服务器身份",
                $"主机：{challenge.Host}:{challenge.Port}\n算法：{challenge.Algorithm}\n指纹：{challenge.Fingerprint}\n\n请与服务器指纹核对，确认后将保存此主机身份。"));
            if (trusted) await _hosts.TrustAsync(challenge);
            return trusted;
        }
        finally { _hostGate.Release(); }
    }
    /// <summary>打开统一的文件管理器窗口；同一连接默认激活已有页签，可通过“+”创建多个独立页签。</summary>
    public async Task OpenFileManagerAsync(WorkspaceTabViewModel tab, Window? owner = null)
    {
        if (_applicationLock.IsLocked || tab is null) return;
        SubscribeFileManagerTab(tab);
        ShowFileManagerWindow(owner);

        var existing = FileManagerTabs.LastOrDefault(item => ReferenceEquals(item.Connection, tab));
        if (existing != null)
        {
            SelectedFileManagerTab = existing;
            _fileWindow?.Activate();
            await existing.InitializeAsync();
            return;
        }

        var fileTab = CreateFileManagerTab(tab);
        await fileTab.InitializeAsync();
    }

    /// <summary>为当前选中的文件页签所属连接再开一个独立文件页签。</summary>
    public async Task AddFileManagerTabAsync()
    {
        if (_applicationLock.IsLocked || SelectedFileManagerTab is not { } current) return;
        SubscribeFileManagerTab(current.Connection);
        var fileTab = CreateFileManagerTab(current.Connection);
        ShowFileManagerWindow(null);
        await fileTab.InitializeAsync();
    }

    private FileManagerTabViewModel CreateFileManagerTab(WorkspaceTabViewModel connection)
    {
        var number = FileManagerTabs.Count(item => ReferenceEquals(item.Connection, connection)) + 1;
        var fileTab = new FileManagerTabViewModel(connection, number, _dialogs, _logs, _localFiles, CloseFileManagerTab);
        FileManagerTabs.Add(fileTab);
        SelectedFileManagerTab = fileTab;
        return fileTab;
    }

    private void ShowFileManagerWindow(Window? owner)
    {
        if (_fileWindow is { IsVisible: true })
        {
            _fileWindow.Activate();
            return;
        }

        var window = new Window
        {
            Title = WindowTitles.ApplyVersionToTitle(FileManagerWindowTitle),
            Width = 1180, Height = 760, MinWidth = 780, MinHeight = 460,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Icon = owner?.Icon,
            Content = new FileManagerWindowView { DataContext = this }
        };
        _fileWindow = window;
        // 登记为模态对话框的候选父窗口：文件管理器里的权限、删除、覆盖等弹窗都以它为父窗口，
        // 关闭弹窗时只会回到文件管理器，不会把主窗口提到前面。窗口失焦不注销，避免弹窗层级错乱。
        _dialogs.TrackOwnerWindow(window);
        window.Closed += (_, _) =>
        {
            _dialogs.UntrackOwnerWindow(window);
            if (ReferenceEquals(_fileWindow, window)) _fileWindow = null;
        };
        // 文件管理器是独立顶层窗口：不设置 Owner、不使用 ShowDialog，主窗口图标和终端始终可点击。
        window.Show();
        window.Activate();
    }

    public void CloseFileManagerTab(FileManagerTabViewModel tab) => RemoveFileManagerTab(tab);

    private void SubscribeFileManagerTab(WorkspaceTabViewModel tab)
    {
        if (!_fileManagerTabSubscriptions.Add(tab)) return;
        tab.Disposed += FileManagerTabDisposed;
        tab.PropertyChanged += FileManagerTabChanged;
    }

    private void UnsubscribeFileManagerTab(WorkspaceTabViewModel tab)
    {
        if (!_fileManagerTabSubscriptions.Remove(tab)) return;
        tab.Disposed -= FileManagerTabDisposed;
        tab.PropertyChanged -= FileManagerTabChanged;
    }

    private void FileManagerTabDisposed(object? sender, EventArgs e)
    {
        if (sender is WorkspaceTabViewModel tab) RemoveFileManagerTabsForConnection(tab);
    }

    private void FileManagerTabChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceTabViewModel.Name) &&
            sender is WorkspaceTabViewModel connection &&
            ReferenceEquals(SelectedFileManagerTab?.Connection, connection) &&
            _fileWindow is not null)
            _fileWindow.Title = WindowTitles.ApplyVersionToTitle(FileManagerWindowTitle);
    }

    private void RemoveFileManagerTabsForConnection(WorkspaceTabViewModel connection)
    {
        foreach (var fileTab in FileManagerTabs.Where(item => ReferenceEquals(item.Connection, connection)).ToArray())
            RemoveFileManagerTab(fileTab);
        UnsubscribeFileManagerTab(connection);
    }

    private void RemoveFileManagerTab(FileManagerTabViewModel fileTab)
    {
        var index = FileManagerTabs.IndexOf(fileTab);
        if (index < 0) return;
        var wasSelected = ReferenceEquals(SelectedFileManagerTab, fileTab);
        FileManagerTabs.Remove(fileTab);
        fileTab.Dispose();
        RenumberFileManagerTabs();

        if (FileManagerTabs.Count == 0)
        {
            SelectedFileManagerTab = null;
            CloseFileManagerWindow();
            return;
        }
        if (wasSelected || SelectedFileManagerTab is null)
            SelectedFileManagerTab = FileManagerTabs[Math.Clamp(index, 0, FileManagerTabs.Count - 1)];
        else if (_fileWindow is not null)
            _fileWindow.Title = WindowTitles.ApplyVersionToTitle(FileManagerWindowTitle);

        if (!FileManagerTabs.Any(item => ReferenceEquals(item.Connection, fileTab.Connection)))
            UnsubscribeFileManagerTab(fileTab.Connection);
    }

    private void RenumberFileManagerTabs()
    {
        foreach (var connection in FileManagerTabs.Select(item => item.Connection).Distinct())
        {
            var number = 1;
            foreach (var fileTab in FileManagerTabs.Where(item => ReferenceEquals(item.Connection, connection)))
                fileTab.SetNumber(number++);
        }
    }

    private void CloseFileManagerWindow()
    {
        var window = _fileWindow;
        _fileWindow = null;
        window?.Close();
    }

    private void DisposeFileManagerTabs()
    {
        _disposingFileManagerTabs = true;
        CloseFileManagerWindow();
        foreach (var fileTab in FileManagerTabs.ToArray())
        {
            fileTab.Dispose();
            UnsubscribeFileManagerTab(fileTab.Connection);
        }
        FileManagerTabs.Clear();
        SelectedFileManagerTab = null;
        _disposingFileManagerTabs = false;
    }
    private void NotifyTabs() { OnPropertyChanged(nameof(HasTabs)); OnPropertyChanged(nameof(HasNoTabs)); }
    private async Task CloseTabAsync(WorkspaceTabViewModel tab)
    {
        if (tab.Files.HasActiveTransfers && !await _dialogs.ConfirmAsync("关闭连接", "当前连接仍有文件传输，关闭将取消这些任务。", true)) return;
        RemoveFileManagerTabsForConnection(tab);
        await tab.DisposeAsync(); var index = Tabs.IndexOf(tab);
        var wasSelected = ReferenceEquals(SelectedTab, tab); Tabs.Remove(tab);
        if (wasSelected || SelectedTab == null) SelectedTab = Tabs.Count == 0 ? null : Tabs[Math.Clamp(index, 0, Tabs.Count - 1)];
        NotifyTabs();
    }
    [RelayCommand] private void SelectTab(WorkspaceTabViewModel? tab) { if (tab != null) SelectedTab = tab; }
    private async Task SaveProcessSortAsync(ProcessSortColumn column, bool descending)
    {
        var settings = _settings with { ProcessSortColumn = column, ProcessSortDescending = descending };
        try
        {
            await _settingsStore.SaveAsync(settings); _settings = settings;
            foreach (var tab in Tabs) tab.Monitor.ApplySort(column, descending);
        }
        catch (Exception ex) { Status = "进程排序保存失败：" + ex.Message; }
    }
    private async Task SaveLayoutAsync(Func<AppSettings, AppSettings> update)
    {
        var settings = update(_settings);
        if (settings == _settings) return;
        try
        {
            await _settingsStore.SaveAsync(settings);
            _settings = settings;
            LayoutChanged?.Invoke(settings);
            Status = "布局已保存";
        }
        catch (Exception ex) { Status = "布局保存失败：" + ex.Message; }
    }
    partial void OnIsMonitorPaneCollapsedChanged(bool value)
    {
        // 立即同步布局，避免等设置落盘才生效。
        LayoutChanged?.Invoke(_settings with { MonitorPaneCollapsed = value });
    }

    /// <summary>收起 / 展开左侧服务器状态面板。</summary>
    [RelayCommand]
    private async Task ToggleMonitorPaneAsync()
    {
        var collapsed = !IsMonitorPaneCollapsed;
        IsMonitorPaneCollapsed = collapsed;
        await SaveLayoutAsync(settings => settings with { MonitorPaneCollapsed = collapsed });
    }
    public Task SaveMonitorPaneWidthAsync(double width)
        => SaveLayoutAsync(settings => settings with { MonitorPaneWidth = Math.Clamp(width, 160, 420) });
    public Task SaveWorkspaceLayoutAsync(double terminalStar, double lowerStar)
        => SaveLayoutAsync(settings => settings with { TerminalPaneStar = Math.Clamp(lowerStar <= 0 ? terminalStar : terminalStar / lowerStar, 0.3, 8) });
    public Task SaveFilePaneLayoutAsync(double localStar, double remoteStar)
        => SaveLayoutAsync(settings => settings with { LocalPaneStar = Math.Clamp(remoteStar <= 0 ? localStar : localStar / remoteStar, 0.3, 8) });
    public Task SaveRemoteTreeWidthAsync(double width)
        => SaveLayoutAsync(settings => settings with { RemoteTreeWidth = Math.Clamp(width, 100, 320) });
    public Task SaveLocalTreeWidthAsync(double width)
        => SaveLayoutAsync(settings => settings with { LocalTreeWidth = Math.Clamp(width, 100, 320) });
    [RelayCommand]
    private Task ResetLayoutAsync()
    {
        var defaults = new AppSettings();
        return SaveLayoutAsync(settings => settings with
        {
            MonitorPaneWidth = defaults.MonitorPaneWidth,
            MonitorPaneCollapsed = defaults.MonitorPaneCollapsed,
            TerminalPaneStar = defaults.TerminalPaneStar,
            LocalPaneStar = defaults.LocalPaneStar,
            RemoteTreeWidth = defaults.RemoteTreeWidth,
            LocalTreeWidth = defaults.LocalTreeWidth
        });
    }

    [RelayCommand]
    private async Task SettingsAsync()
    {
        if (_applicationLock.IsLocked) return;
        if (await _dialogs.SettingsAsync(_settings) is not { } settings) return;
        try { await _settingsStore.SaveAsync(settings); _settings = settings; OnPropertyChanged(nameof(HasMcpConnections)); ThemeService.Apply(settings.Theme); _applicationLock.Configure(settings); foreach (var tab in Tabs) tab.ApplySettings(settings); }
        catch (Exception ex) { await _dialogs.ShowErrorAsync(ex.Message); }
    }
    public async Task<bool> ShutdownAsync()
    {
        if (_applicationLock.IsReady && _applicationLock.IsLocked) return false;
        if (Tabs.Any(t => t.Files.HasActiveTransfers) && !await _dialogs.ConfirmAsync("退出 ShellX", "仍有文件正在传输，退出将取消传输并断开所有连接。", true)) return false;
        DisposeFileManagerTabs();
        await Task.WhenAll(Tabs.Select(t => t.DisposeAsync().AsTask())); return true;
    }
}
