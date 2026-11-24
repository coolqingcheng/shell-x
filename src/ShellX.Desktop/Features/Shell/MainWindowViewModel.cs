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
using ShellX.Desktop.Styles;

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
    /// <summary>右侧远程文件面板当前跟随的标签。</summary>
    private WorkspaceTabViewModel? _filePaneTab;
    public ObservableCollection<ConnectionProfile> VisibleConnections { get; } = [];
    public event Action<ConnectionProfile>? ConnectionPicked;
    [ObservableProperty] private ConnectionGroupViewModel? _selectedGroup;
    public bool CanManageFolder => SelectedGroup is { Path.Length: > 0, IsDirectConnections: false };
    public string FolderCaption => SelectedGroup?.Name ?? "全部连接";
    /// <summary>品牌区展示的版本号，与窗口标题、设置页保持一致。</summary>
    public string VersionText => $"v{Core.AppVersion.Value}";
    private AppSettings _settings = new();
    public AppSettings LayoutSettings => _settings;
    public event Action<AppSettings>? LayoutChanged;
    private bool _configurationLoaded;
    public ObservableCollection<ConnectionGroupViewModel> Groups { get; } = [];
    public ObservableCollection<WorkspaceTabViewModel> Tabs { get; } = [];
    /// <summary>对话框服务：各窗口用它登记层级关系，保证弹窗落在正确的父窗口上。</summary>
    public IDialogService Dialogs => _dialogs;
    /// <summary>右侧远程文件面板：只显示远程目录，行内可下载，本地文件拖进来就上传。</summary>
    public RemoteFilesPaneViewModel FilePane { get; }
    [ObservableProperty] private WorkspaceTabViewModel? _selectedTab;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _status = "就绪";
    /// <summary>左侧服务器状态面板是否收起。</summary>
    [ObservableProperty] private bool _isMonitorPaneCollapsed;
    /// <summary>右侧远程文件面板是否收起。</summary>
    [ObservableProperty] private bool _isFilePaneCollapsed;
    [ObservableProperty] private int _connectionCount;
    public bool HasTabs => Tabs.Count > 0;
    public bool HasNoTabs => !HasTabs;
    public bool HasMcpConnections => _settings.McpEnabled && _profiles.Any(profile => profile.McpEnabled);
    public bool NeedsLeafFolder => SelectedGroup is not { Path.Length: > 0, Children.Count: 0 };
    public bool HasNoConnections => !NeedsLeafFolder && VisibleConnections.Count == 0 && string.IsNullOrWhiteSpace(Search);
    public bool HasNoResults => !NeedsLeafFolder && VisibleConnections.Count == 0 && !string.IsNullOrWhiteSpace(Search);

    public MainWindowViewModel(IConnectionRepository connections, ICredentialStore credentials, IHostKeyStore hosts,
        ISettingsStore settingsStore, IRemoteSessionFactory sessions, IDialogService dialogs, ILoggerFactory logs, ILocalFileSystem localFiles, AppLockService applicationLock, IConfigurationExporter configurationExporter, IConfigurationImporter? configurationImporter = null)
    {
        _connections = connections; _credentials = credentials; _hosts = hosts; _settingsStore = settingsStore; _configurationExporter = configurationExporter; _configurationImporter = configurationImporter; _sessions = sessions; _dialogs = dialogs; _logs = logs; _localFiles = localFiles; _applicationLock = applicationLock; _monitors = new(dialogs, logs);
        FilePane = new(dialogs, logs.CreateLogger<RemoteFilesPaneViewModel>(), localFiles);
        // 面板换了目录就记到当前标签上：切回这个标签时还是这个目录。
        FilePane.PathChanged += path => { if (_filePaneTab != null) _filePaneTab.FilePanePath = path; };
    }
    public async Task InitializeAsync()
    {
        try
        {
            var catalog = await _connections.LoadCatalogAsync(); _profiles = catalog.Connections.ToList(); _folders = catalog.Folders.ToList();
            _configurationLoaded = true;
            _settings = await _settingsStore.LoadAsync(); IsMonitorPaneCollapsed = _settings.MonitorPaneCollapsed; IsFilePaneCollapsed = _settings.FilePaneCollapsed; ThemeService.Apply(_settings.Theme); _applicationLock.Configure(_settings, startup: true); RefreshGroups();
        }
        catch (Exception ex) { _applicationLock.ConfigurationFailed(); Status = "配置加载失败"; await _dialogs.ShowErrorAsync("配置未被覆盖，请检查本地配置文件。\n" + ex.Message); }
    }
    partial void OnSearchChanged(string value) => RefreshVisibleConnections();
    partial void OnSelectedGroupChanged(ConnectionGroupViewModel? value)
    { RefreshVisibleConnections(); OnPropertyChanged(nameof(CanManageFolder)); OnPropertyChanged(nameof(FolderCaption)); }
    partial void OnSelectedTabChanged(WorkspaceTabViewModel? value)
    { foreach (var tab in Tabs) tab.IsSelected = ReferenceEquals(tab, value); AttachFilePane(value); }

    /// <summary>右侧面板跟着当前标签走：切换标签或重连都重新挂上远程文件系统。</summary>
    private void AttachFilePane(WorkspaceTabViewModel? tab)
    {
        if (_filePaneTab != null)
        {
            // 切走前先把旧标签的面板状态存下来：目录和滚动位置都按标签各记各的。
            // 没连上的时候不存，免得把断线前记下的位置覆盖成根目录。
            if (FilePane.IsAvailable)
            {
                _filePaneTab.FilePanePath = FilePane.CurrentPath;
                _filePaneTab.FilePaneScroll = FilePane.ScrollOffset;
            }
            _filePaneTab.SessionChanged -= FilePaneSessionChanged;
        }
        _filePaneTab = tab;
        if (tab == null) { FilePane.OpenTerminalHandler = null; FilePane.Attach(null, CancellationToken.None, "", null); return; }
        tab.SessionChanged += FilePaneSessionChanged;
        ApplyFilePaneSession(tab);
    }

    private void FilePaneSessionChanged(WorkspaceTabViewModel tab, IRemoteSession? session)
    {
        if (!ReferenceEquals(tab, _filePaneTab)) return;
        ApplyFilePaneSession(tab);
    }

    private void ApplyFilePaneSession(WorkspaceTabViewModel tab)
    {
        // 面板跟着当前标签走：会话提供日志读取器，“在终端打开”按这个标签新建连接。
        FilePane.OpenTerminalHandler = path => OpenTerminalAtAsync(tab, path);
        // 这个标签上次待的目录和滚动位置：切回来接着看，不跳回根目录。
        FilePane.Attach(tab.Session?.Files, tab.ConnectionToken, $"{tab.Name} · {tab.Profile.Host}:{tab.Profile.Port}", tab.Session,
            tab.FilePanePath, tab.FilePaneScroll);
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
        foreach (var direct in _profiles.GroupBy(p => ConnectionFolders.Normalize(p.Group)))
        {
            var parent = nodes[direct.Key];
            if (parent.Children.Count > 0)
                parent.Children.Insert(0, new(parent.Path, "（本级连接）", direct.Count(), isDirectConnections: true));
        }
        var selection = nodes.GetValueOrDefault(selected, root);
        if (SelectedGroup?.IsDirectConnections == true)
            selection = selection.Children.FirstOrDefault(n => n.IsDirectConnections) ?? selection;
        Groups.Clear(); Groups.Add(root); SelectedGroup = selection;
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
    private async Task ImportXshellFolderAsync()
    {
        if (_applicationLock.IsLocked || !_configurationLoaded) return;
        try
        {
            var group = SelectedGroup is { Path.Length: > 0 } folder ? folder.Path : XshellSessionImporter.DefaultGroup;
            var path = await _dialogs.PickXshellFolderAsync();
            if (path == null || _applicationLock.IsLocked) return;
            var import = await Task.Run(() => XshellFolderImport.Read(path, group));
            if (_applicationLock.IsLocked) return;
            var added = 0;
            var tunnels = 0;
            var folders = 0;
            await CommitCatalogAsync(catalog =>
            {
                if (_applicationLock.IsLocked) throw new InvalidOperationException("应用已锁定。");
                static (string, string, ConnectionEndpoint) Key(ConnectionProfile p) =>
                    (ConnectionFolders.Normalize(p.Group), p.Name, ConnectionEndpoint.From(p));
                var known = catalog.Connections.Select(Key).ToHashSet();
                var additions = import.Profiles.Where(p => known.Add(Key(p))).ToArray();
                added = additions.Length;
                tunnels = additions.Sum(p => p.Tunnels.Count);
                folders = import.Folders.Except(catalog.Folders).Count();
                return new([.. catalog.Connections, .. additions], [.. catalog.Folders, .. import.Folders]);
            });
            Search = "";
            Status = $"已从 Xshell 导入 {added} 个连接、{tunnels} 条隧道，新增 {folders} 个文件夹";
            var message = $"{Status}。跳过重复连接 {import.Profiles.Count - added} 个。\n导入位置：{import.Folders[0]}。首次连接时请输入密码或私钥口令。";
            if (import.Notices.Count > 0) message += "\n" + string.Join('\n', import.Notices);
            await _dialogs.ShowInformationAsync("Xshell 文件夹导入完成", message);
        }
        catch (Exception ex) { await _dialogs.ShowErrorAsync("Xshell 文件夹导入失败：\n" + ex.Message); }
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
            var tunnelCount = parsed.Where(item => addedIds.Contains(item.Profile.Id)).Sum(item => item.Profile.Tunnels.Count);
            Status = added > 0
                ? $"已从 Xshell 导入 {added} 个连接" + (tunnelCount > 0 ? $"、{tunnelCount} 条隧道" : "")
                : "Xshell 会话均为重复连接";
            var summary = $"新增 {added} 个连接，跳过重复 {duplicates} 个，已解密并保存凭据 {restoredSecrets} 条。";
            if (tunnelCount > 0) summary += $"同时导入 {tunnelCount} 条隧道配置。";
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
        if (_applicationLock.IsLocked || SelectedGroup is not { Path.Length: > 0, IsDirectConnections: false } folder) return;
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
        if (_applicationLock.IsLocked || SelectedGroup is not { Path.Length: > 0, IsDirectConnections: false } folder) return;
        bool InFolder(string path) => ConnectionFolders.Contains(folder.Path, path);
        var folders = FolderPaths.Where(InFolder).ToHashSet(StringComparer.Ordinal);
        var ids = _profiles.Where(p => InFolder(ConnectionFolders.Normalize(p.Group))).Select(p => p.Id).ToHashSet();
        var message = $"删除文件夹“{folder.Path}”？包含 {folders.Count - 1} 个子文件夹、{ids.Count} 个连接。";
        await ConfirmDeletionAsync("删除文件夹", message, ids, catalog =>
        {
            var currentIds = catalog.Connections.Where(p => InFolder(ConnectionFolders.Normalize(p.Group))).Select(p => p.Id);
            if (!ids.SetEquals(currentIds) || !folders.SetEquals(catalog.Folders.Where(InFolder)))
                throw new InvalidOperationException("文件夹内容已改变，请重新确认删除范围。");
            return new(catalog.Connections.Where(p => !ids.Contains(p.Id)).ToArray(),
                catalog.Folders.Where(path => !InFolder(path)).ToArray());
        });
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
    private Task DeleteConnectionAsync(ConnectionProfile? profile) =>
        profile == null ? Task.CompletedTask : DeleteConnectionsAsync([profile]);

    public Task DeleteConnectionsAsync(IReadOnlyList<ConnectionProfile> profiles)
    {
        var ids = profiles.Select(p => p.Id).ToHashSet();
        if (ids.Count == 0) return Task.CompletedTask;
        var message = ids.Count == 1 ? $"删除连接“{profiles[0].Name}”？" : $"删除选中的 {ids.Count} 个连接？";
        return ConfirmDeletionAsync("删除连接", message, ids, catalog => catalog with
        {
            Connections = catalog.Connections.Where(p => !ids.Contains(p.Id)).ToArray()
        });
    }

    private async Task ConfirmDeletionAsync(string title, string message, HashSet<Guid> ids,
        Func<ConnectionCatalog, ConnectionCatalog> update)
    {
        if (_applicationLock.IsLocked) return;
        try
        {
            if (!await _dialogs.ConfirmAsync(title, message + " 已打开的标签不受影响。", true)) return;
            await CommitCatalogAsync(catalog => !_applicationLock.IsLocked ? update(catalog)
                : throw new InvalidOperationException("应用已锁定。"));
            var failures = 0;
            foreach (var id in ids)
            {
                _secrets.Remove(id);
                try { await _credentials.DeleteAsync(id); }
                catch (Exception) { failures++; }
            }
            if (failures > 0) await _dialogs.ShowErrorAsync($"配置已删除，但有 {failures} 个连接的本地凭据未能清除。");
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
            p => CommitCatalogAsync(catalog => catalog.Connections.Any(existing => existing.Id == p.Id)
                ? catalog with { Connections = catalog.Connections.Select(existing => existing.Id == p.Id ? p : existing).ToArray() }
                : throw new InvalidOperationException("此连接配置已删除。")), CloseTabAsync, _settings, _localFiles, SaveProcessSortAsync, _monitors, OpenTerminalAtAsync, SaveMonitorNetworkAsync);
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
    private void NotifyTabs() { OnPropertyChanged(nameof(HasTabs)); OnPropertyChanged(nameof(HasNoTabs)); }
    private async Task CloseTabAsync(WorkspaceTabViewModel tab)
    {
        if (tab.Files.HasActiveTransfers && !await _dialogs.ConfirmAsync("关闭连接", "当前连接仍有文件传输，关闭将取消这些任务。", true)) return;
        await tab.DisposeAsync(); var index = Tabs.IndexOf(tab);
        var wasSelected = ReferenceEquals(SelectedTab, tab); Tabs.Remove(tab);
        if (wasSelected || SelectedTab == null) SelectedTab = Tabs.Count == 0 ? null : Tabs[Math.Clamp(index, 0, Tabs.Count - 1)];
        NotifyTabs();
        // 最后一个连接关掉后把内存还给系统，否则工作集一直停在高位。
        if (Tabs.Count == 0) MemoryTrim.Release();
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
    /// <summary>保存服务器监控里选中的网卡：按连接端点区分，重启后继续沿用。</summary>
    private async Task SaveMonitorNetworkAsync(string endpointKey, string interfaceName)
    {
        if (string.IsNullOrWhiteSpace(endpointKey) || string.IsNullOrWhiteSpace(interfaceName)) return;
        if (_settings.MonitorNetworkInterfaces.Find(endpointKey) == interfaceName) return;
        var settings = _settings with { MonitorNetworkInterfaces = _settings.MonitorNetworkInterfaces.With(endpointKey, interfaceName) };
        try { await _settingsStore.SaveAsync(settings); _settings = settings; }
        catch (Exception ex) { Status = "监控网卡保存失败：" + ex.Message; }
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
    /// <summary>收起 / 展开右侧远程文件面板。</summary>
    [RelayCommand]
    private async Task ToggleFilePaneAsync()
    {
        var collapsed = !IsFilePaneCollapsed;
        IsFilePaneCollapsed = collapsed;
        await SaveLayoutAsync(settings => settings with { FilePaneCollapsed = collapsed });
    }
    public Task SaveFilePaneWidthAsync(double width)
        => SaveLayoutAsync(settings => settings with { FilePaneWidth = Math.Clamp(width, 240, 520) });
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
            FilePaneWidth = defaults.FilePaneWidth,
            FilePaneCollapsed = defaults.FilePaneCollapsed,
            TerminalPaneStar = defaults.TerminalPaneStar,
            LocalPaneStar = defaults.LocalPaneStar,
            RemoteTreeWidth = defaults.RemoteTreeWidth,
            LocalTreeWidth = defaults.LocalTreeWidth
        });
    }

    /// <summary>切换主题：立即生效并保存到配置，下次启动沿用。</summary>
    public async Task ApplyThemeAsync(AppTheme theme)
    {
        if (_settings.Theme == theme) return;
        try
        {
            var settings = _settings with { Theme = theme };
            await _settingsStore.SaveAsync(settings);
            _settings = settings;
            ThemeService.Apply(theme);
            foreach (var tab in Tabs) tab.ApplySettings(settings);
            LayoutChanged?.Invoke(settings);
            Status = "主题已切换：" + ThemeCatalog.DisplayName(theme);
        }
        catch (Exception ex) { Status = "主题切换失败：" + ex.Message; }
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
        await Task.WhenAll(Tabs.Select(t => t.DisposeAsync().AsTask())); return true;
    }
}
