using System.Collections.ObjectModel;
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
    public ObservableCollection<ConnectionProfile> VisibleConnections { get; } = [];
    public event Action<ConnectionProfile>? ConnectionPicked;
    [ObservableProperty] private ConnectionGroupViewModel? _selectedGroup;
    public bool CanManageFolder => SelectedGroup is { Path.Length: > 0 };
    public string FolderCaption => SelectedGroup?.Name ?? "全部连接";
    private AppSettings _settings = new();
    public AppSettings LayoutSettings => _settings;
    public event Action<AppSettings>? LayoutChanged;
    private bool _configurationLoaded;
    public ObservableCollection<ConnectionGroupViewModel> Groups { get; } = [];
    public ObservableCollection<WorkspaceTabViewModel> Tabs { get; } = [];
    [ObservableProperty] private WorkspaceTabViewModel? _selectedTab;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _status = "就绪";
    [ObservableProperty] private int _connectionCount;
    public bool HasTabs => Tabs.Count > 0;
    public bool HasNoTabs => !HasTabs;
    public bool HasNoConnections => VisibleConnections.Count == 0 && string.IsNullOrEmpty(Search);
    public bool HasNoResults => VisibleConnections.Count == 0 && !string.IsNullOrEmpty(Search);

    public MainWindowViewModel(IConnectionRepository connections, ICredentialStore credentials, IHostKeyStore hosts,
        ISettingsStore settingsStore, IRemoteSessionFactory sessions, IDialogService dialogs, ILoggerFactory logs, ILocalFileSystem localFiles, AppLockService applicationLock, IConfigurationExporter configurationExporter)
    { _connections = connections; _credentials = credentials; _hosts = hosts; _settingsStore = settingsStore; _configurationExporter = configurationExporter; _sessions = sessions; _dialogs = dialogs; _logs = logs; _localFiles = localFiles; _applicationLock = applicationLock; _monitors = new(dialogs, logs); }
    public async Task InitializeAsync()
    {
        try
        {
            var catalog = await _connections.LoadCatalogAsync(); _profiles = catalog.Connections.ToList(); _folders = catalog.Folders.ToList();
            _configurationLoaded = true;
            _settings = await _settingsStore.LoadAsync(); ThemeService.Apply(_settings.Theme); _applicationLock.Configure(_settings, startup: true); RefreshGroups();
        }
        catch (Exception ex) { _applicationLock.ConfigurationFailed(); Status = "配置加载失败"; await _dialogs.ShowErrorAsync("配置未被覆盖，请检查本地配置文件。\n" + ex.Message); }
    }
    partial void OnSearchChanged(string value) => RefreshVisibleConnections();
    partial void OnSelectedGroupChanged(ConnectionGroupViewModel? value)
    { RefreshVisibleConnections(); OnPropertyChanged(nameof(CanManageFolder)); OnPropertyChanged(nameof(FolderCaption)); }
    partial void OnSelectedTabChanged(WorkspaceTabViewModel? value)
    { foreach (var tab in Tabs) tab.IsSelected = ReferenceEquals(tab, value); }
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
        RefreshVisibleConnections(); OnPropertyChanged(nameof(FolderPaths));
    }
    private void RefreshVisibleConnections()
    {
        VisibleConnections.Clear();
        var folder = SelectedGroup?.Path ?? "";
        foreach (var profile in _profiles.Where(p => (folder.Length == 0 || ConnectionFolders.Contains(folder, ConnectionFolders.Normalize(p.Group))) &&
            (string.IsNullOrWhiteSpace(Search) || $"{p.Name} {p.Host} {p.Username} {p.Group}".Contains(Search, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)) VisibleConnections.Add(profile);
        OnPropertyChanged(nameof(HasNoConnections)); OnPropertyChanged(nameof(HasNoResults));
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
            await _configurationExporter.ExportAsync(path, catalog, _settings);
            Status = "配置导出成功";
            await _dialogs.ShowInformationAsync("导出成功", $"已导出 {catalog.Connections.Count} 个连接及文件夹、隧道和常规设置。\n{path}\n\n不包含登录密码、锁定密码和私钥内容；私钥路径仍按原配置保留。");
        }
        catch (Exception ex) { await _dialogs.ShowErrorAsync("导出失败：\n" + ex.Message); }
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
            WorkspaceTabViewModel? tab = null;
            tab = new WorkspaceTabViewModel(profile, _sessions, _dialogs, _logs, () => SecretAsync(tab!.Profile), VerifyHostAsync,
                SaveProfileAsync, CloseTabAsync, _settings, _localFiles, SaveProcessSortAsync, _monitors);
            Tabs.Add(tab); SelectedTab = tab; NotifyTabs();
            await tab.ConnectWithSecretAsync(secret);
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
    public Task ShowMonitorPanelExistsAsync(string name) => _dialogs.ShowInformationAsync("监控面板", $"“{name}”的监控面板已经打开过了。\n\n每个地址只能打开一个监控面板。");
    public Task SaveWorkspaceLayoutAsync(double terminalStar, double lowerStar)
        => SaveLayoutAsync(settings => settings with { TerminalPaneStar = Math.Clamp(lowerStar <= 0 ? terminalStar : terminalStar / lowerStar, 0.3, 8) });
    public Task SaveFilePaneLayoutAsync(double localStar, double remoteStar)
        => SaveLayoutAsync(settings => settings with { LocalPaneStar = Math.Clamp(remoteStar <= 0 ? localStar : localStar / remoteStar, 0.3, 8) });
    public Task SaveRemoteTreeWidthAsync(double width)
        => SaveLayoutAsync(settings => settings with { RemoteTreeWidth = Math.Clamp(width, 100, 320) });
    [RelayCommand]
    private Task ResetLayoutAsync()
    {
        var defaults = new AppSettings();
        return SaveLayoutAsync(settings => settings with
        {
            TerminalPaneStar = defaults.TerminalPaneStar,
            LocalPaneStar = defaults.LocalPaneStar,
            RemoteTreeWidth = defaults.RemoteTreeWidth
        });
    }

    [RelayCommand]
    private async Task SettingsAsync()
    {
        if (_applicationLock.IsLocked) return;
        if (await _dialogs.SettingsAsync(_settings) is not { } settings) return;
        try { await _settingsStore.SaveAsync(settings); _settings = settings; ThemeService.Apply(settings.Theme); _applicationLock.Configure(settings); foreach (var tab in Tabs) tab.ApplySettings(settings); }
        catch (Exception ex) { await _dialogs.ShowErrorAsync(ex.Message); }
    }
    public async Task<bool> ShutdownAsync()
    {
        if (_applicationLock.IsReady && _applicationLock.IsLocked) return false;
        if (Tabs.Any(t => t.Files.HasActiveTransfers) && !await _dialogs.ConfirmAsync("退出 ShellX", "仍有文件正在传输，退出将取消传输并断开所有连接。", true)) return false;
        await Task.WhenAll(Tabs.Select(t => t.DisposeAsync().AsTask())); return true;
    }
}
