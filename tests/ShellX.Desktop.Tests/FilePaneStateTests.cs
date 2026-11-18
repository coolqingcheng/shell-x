using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using ShellX.Core.Connections;
using ShellX.Core.Files;
using ShellX.Core.Logs;
using ShellX.Core.Security;
using ShellX.Core.Sessions;
using ShellX.Core.Settings;
using ShellX.Core.Tunnels;
using ShellX.Desktop.Features.FileBrowser;
using ShellX.Desktop.Features.Security;
using ShellX.Desktop.Features.Shell;
using ShellX.Desktop.Features.Workspace;
using ShellX.Desktop.Services;
using ShellX.Infrastructure.Files;
using ShellX.Infrastructure.Persistence;
using ShellX.Infrastructure.Security;
using ShellX.Infrastructure.Ssh;

namespace ShellX.Desktop.Tests;

/// <summary>
/// 切换终端标签时，右侧的远程文件面板要记住每个标签自己的目录和滚动位置：
/// 切走时存下来，切回来时接着看。
/// </summary>
public sealed class FilePaneStateTests
{
    [Fact]
    public async Task AttachingWithASavedDirectoryOpensThatDirectory()
    {
        var files = new RecordingRemoteFileSystem(new Dictionary<string, IReadOnlyList<RemoteEntry>>
        {
            ["/"] = [Entry("etc", true)],
            ["/etc"] = [Entry("hosts", false)]
        });
        using var pane = new RemoteFilesPaneViewModel(new PaneDialogs(), NullLogger<RemoteFilesPaneViewModel>.Instance, new StubLocalFileSystem());
        // 模拟切回这个标签：带上上次待的目录。
        pane.Attach(files, CancellationToken.None, "测试服务器", null, "/etc");
        await WaitForAsync(() => pane.CurrentPath == "/etc");

        Assert.Equal("/etc", pane.CurrentPath);
        Assert.Single(pane.Entries);
        Assert.Equal("hosts", pane.Entries[0].Name);
    }

    [Fact]
    public async Task ASavedDirectoryThatIsGoneFallsBackToTheRoot()
    {
        var files = new StrictRemoteFileSystem(new Dictionary<string, IReadOnlyList<RemoteEntry>>
        {
            ["/"] = [Entry("etc", true)],
            ["/etc"] = [Entry("hosts", false)]
        });
        using var pane = new RemoteFilesPaneViewModel(new PaneDialogs(), NullLogger<RemoteFilesPaneViewModel>.Instance, new StubLocalFileSystem());
        // 上次的目录已经被删了：退回根目录，不报错。
        pane.Attach(files, CancellationToken.None, "测试服务器", null, "/gone");
        await WaitForAsync(() => pane.CurrentPath == "/");

        Assert.Equal("/", pane.CurrentPath);
        Assert.Equal("", pane.Error);
        Assert.Single(pane.Entries);
    }

    [Fact]
    public async Task EachTabRemembersItsOwnDirectory()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-pane-state-" + Guid.NewGuid().ToString("N"))).FullName;
        MainWindowViewModel? vm = null;
        try
        {
            var profile = new ConnectionProfile { Name = "测试", Host = "127.0.0.1", Port = 22, Username = "root" };
            var dialogs = new Dialogs();
            vm = new MainWindowViewModel(new Connections(profile), new Credentials(), new Hosts(), new Settings(), new SshSessionFactory(NullLoggerFactory.Instance),
                dialogs, NullLoggerFactory.Instance, new LocalFileSystem(root), new AppLockService(new LockPasswordHasher()), new JsonConfigurationExporter(new(root)));
            var first = NewTab(profile, dialogs, root);
            var second = NewTab(profile with { Name = "第二个" }, dialogs, root);
            vm.Tabs.Add(first); vm.Tabs.Add(second);

            vm.SelectedTab = first;
            first.Session = new PaneSession(new StrictRemoteFileSystem(new Dictionary<string, IReadOnlyList<RemoteEntry>>
            {
                ["/"] = [Entry("etc", true)],
                ["/etc"] = [Entry("hosts", false)]
            }));
            await WaitForAsync(() => vm.FilePane.CurrentPath == "/");
            await vm.FilePane.NavigateAsync("/etc");
            await WaitForAsync(() => vm.FilePane.CurrentPath == "/etc");

            // 切到第二个标签：面板换成第二个连接的目录。
            vm.SelectedTab = second;
            second.Session = new PaneSession(new StrictRemoteFileSystem(new Dictionary<string, IReadOnlyList<RemoteEntry>>
            {
                ["/"] = [Entry("var", true)],
                ["/var"] = [Entry("log", false)]
            }));
            await WaitForAsync(() => vm.FilePane.CurrentPath == "/");

            // 切回第一个标签：还是上次看的目录。
            vm.SelectedTab = first;
            await WaitForAsync(() => vm.FilePane.CurrentPath == "/etc");
            Assert.Equal("/etc", vm.FilePane.CurrentPath);
            Assert.Equal("hosts", vm.FilePane.Entries.Single().Name);

            // 再切到第二个标签：它也记得自己的目录。
            vm.SelectedTab = second;
            await WaitForAsync(() => vm.FilePane.CurrentPath == "/");
            Assert.Equal("/", vm.FilePane.CurrentPath);
        }
        finally
        {
            if (vm?.Tabs.Count > 0) foreach (var tab in vm.Tabs.ToArray()) await tab.DisposeAsync();
            Directory.Delete(root, true);
        }
    }

    [AvaloniaFact]
    public async Task SwitchingBackRestoresTheScrollPosition()
    {
        var files = new RecordingRemoteFileSystem(new Dictionary<string, IReadOnlyList<RemoteEntry>>
        {
            ["/"] = [Entry("etc", true)],
            ["/etc"] = Enumerable.Range(0, 200).Select(i => Entry($"file{i:000}.txt", false)).ToArray()
        });
        using var pane = new RemoteFilesPaneViewModel(new PaneDialogs(), NullLogger<RemoteFilesPaneViewModel>.Instance, new StubLocalFileSystem());
        pane.Attach(files, CancellationToken.None, "测试服务器");
        var view = new RemoteFilesPaneView { DataContext = pane };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        window.Show();
        try
        {
            await WaitForAsync(() => pane.Entries.Count >= 1);
            await pane.NavigateAsync("/etc");
            await WaitForAsync(() => pane.CurrentPath == "/etc" && pane.Entries.Count >= 200);

            var list = ListOf(view);
            await WaitForAsync(() => list.Extent.Height > list.Viewport.Height);
            list.Offset = new Vector(0, 300);
            await WaitForAsync(() => pane.ScrollOffset >= 300);
            var saved = pane.ScrollOffset;
            Assert.True(saved > 0, "滚动位置没有被记录");

            // 模拟切换标签再切回来：重新挂上同一个目录，并带上刚才的滚动位置。
            pane.Attach(files, CancellationToken.None, "测试服务器", null, "/etc", saved);
            await WaitForAsync(() => pane.CurrentPath == "/etc");
            await WaitForAsync(() => list.Offset.Y >= saved - 1);

            Assert.Equal("/etc", pane.CurrentPath);
            Assert.True(list.Offset.Y >= saved - 1, $"滚动位置没有恢复：{list.Offset.Y}，期望 {saved}");
        }
        finally { window.Close(); }
    }


    private static ScrollViewer ListOf(RemoteFilesPaneView view)
        => view.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "TreeList");

    private static WorkspaceTabViewModel NewTab(ConnectionProfile profile, IDialogService dialogs, string root)
        => new(profile, new SshSessionFactory(NullLoggerFactory.Instance), dialogs, NullLoggerFactory.Instance,
            () => Task.FromResult<ConnectionSecret?>(null), _ => Task.FromResult(true), _ => Task.CompletedTask, _ => Task.CompletedTask,
            new AppSettings(), new LocalFileSystem(root));

    private static RemoteEntry Entry(string name, bool directory, long size = 0)
        => new(name, "/" + name, directory, false, size, DateTime.UtcNow, 493, 0, 0);

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 80; i++)
        {
            if (condition()) return;
            await Task.Delay(25);
        }
        Assert.True(condition(), "等待超时");
    }

    /// <summary>远程文件系统替身：记录上传和下载。</summary>
    private sealed class RecordingRemoteFileSystem(IReadOnlyDictionary<string, IReadOnlyList<RemoteEntry>> tree) : IRemoteFileSystem
    {
        public Task<RemoteDirectory> ListAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(new RemoteDirectory(path, tree.TryGetValue(path, out var entries) ? entries : []));
        public Task CreateAsync(string path, bool directory, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RenameAsync(string path, string newPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetPermissionsAsync(string path, int permissions, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<TextDocument> ReadTextAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(new TextDocument(path, "", 0, DateTime.UtcNow));
        public Task<TextDocument> SaveTextAsync(TextDocument original, string content, CancellationToken cancellationToken = default)
            => Task.FromResult(original);
        public Task UploadAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken cancellationToken = default)
        { progress.Report(1); return Task.CompletedTask; }
        public Task DownloadAsync(string remotePath, string localPath, IProgress<long> progress, CancellationToken cancellationToken = default)
        { progress.Report(5); return Task.CompletedTask; }
        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    /// <summary>远程文件系统替身：目录不存在时直接报错。</summary>
    private sealed class StrictRemoteFileSystem(IReadOnlyDictionary<string, IReadOnlyList<RemoteEntry>> tree) : IRemoteFileSystem
    {
        public Task<RemoteDirectory> ListAsync(string path, CancellationToken cancellationToken = default)
            => tree.TryGetValue(path, out var entries)
                ? Task.FromResult(new RemoteDirectory(path, entries))
                : throw new DirectoryNotFoundException(path);
        public Task CreateAsync(string path, bool directory, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RenameAsync(string path, string newPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetPermissionsAsync(string path, int permissions, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<TextDocument> ReadTextAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(new TextDocument(path, "", 0, DateTime.UtcNow));
        public Task<TextDocument> SaveTextAsync(TextDocument original, string content, CancellationToken cancellationToken = default)
            => Task.FromResult(original);
        public Task UploadAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken cancellationToken = default)
        { progress.Report(1); return Task.CompletedTask; }
        public Task DownloadAsync(string remotePath, string localPath, IProgress<long> progress, CancellationToken cancellationToken = default)
        { progress.Report(5); return Task.CompletedTask; }
        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    /// <summary>会话替身：只提供远程文件系统。</summary>
    private sealed class PaneSession(IRemoteFileSystem files) : IRemoteSession
    {
        public ITerminalChannel Terminal => throw new NotSupportedException();
        public IRemoteFileSystem? Files => files;
        public string? FileError => null;
        public ITunnelService Tunnels => throw new NotSupportedException();
        public IRemoteLogReader? Logs => null;
        public event EventHandler<string>? Disconnected { add { } remove { } }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>对话框替身：面板测试里用不到确认。</summary>
    private sealed class PaneDialogs : IDialogService
    {
        public Task<string?> PickDownloadFolderAsync() => Task.FromResult<string?>(null);
        public Task<ConnectionEditResult?> EditConnectionAsync(ConnectionProfile? profile, ConnectionSecret? secret) => Task.FromResult<ConnectionEditResult?>(null);
        public Task<bool> ConfirmAsync(string title, string message, bool danger = false) => Task.FromResult(true);
        public Task<string?> PromptAsync(string title, string label, string value = "", bool secret = false) => Task.FromResult<string?>(null);
        public Task ShowErrorAsync(string message) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> PickUploadAsync() => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> PickDownloadAsync(string name) => Task.FromResult<string?>(null);
        public Task EditFileAsync(TextDocument document, Func<TextDocument, string, Task<TextDocument>> save) => Task.CompletedTask;
        public Task<int?> PermissionsAsync(RemoteEntry entry) => Task.FromResult<int?>(null);
        public Task<TunnelDefinition?> EditTunnelAsync() => Task.FromResult<TunnelDefinition?>(null);
        public Task<AppSettings?> SettingsAsync(AppSettings settings) => Task.FromResult<AppSettings?>(null);
    }

    /// <summary>本地文件系统替身：本面板不读本地目录。</summary>
    private sealed class StubLocalFileSystem : ILocalFileSystem
    {
        public string HomeDirectory => Path.GetTempPath();
        public IReadOnlyList<string> Roots => [Path.GetTempPath()];
        public Task<LocalDirectory> ListAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(new LocalDirectory(path, []));
        public Task<DirectoryUploadPlan> PlanDirectoryUploadAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(new DirectoryUploadPlan(Path.GetFileName(path), [], [], 0));
        public Task<TextDocument> ReadTextAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(new TextDocument(path, "", 0, DateTime.UtcNow));
        public Task<TextDocument> SaveTextAsync(TextDocument original, string content, CancellationToken cancellationToken = default)
            => Task.FromResult(original);
        public Task CreateAsync(string directory, string name, bool folder) => Task.CompletedTask;
        public Task RenameAsync(string path, string name) => Task.CompletedTask;
        public Task DeleteAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task OpenAsync(string path) => Task.CompletedTask;
    }

    private sealed class Connections(ConnectionProfile profile) : IConnectionRepository
    {
        public Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ConnectionProfile>>([profile]);
        public Task SaveAsync(IReadOnlyList<ConnectionProfile> connections, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class Credentials : ICredentialStore
    {
        public Task<ConnectionSecret?> ReadAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<ConnectionSecret?>(null);
        public Task SaveAsync(Guid id, ConnectionSecret secret, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class Hosts : IHostKeyStore
    {
        public Task<string?> GetAsync(string host, int port, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task TrustAsync(HostKeyChallenge host, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class Settings : ISettingsStore
    {
        public AppSettings Value { get; private set; } = new();
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Value);
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) { Value = settings; return Task.CompletedTask; }
    }
    private sealed class Dialogs : IDialogService
    {
        public Task<bool> ConfirmAsync(string title, string message, bool danger = false) => Task.FromResult(true);
        public Task ShowErrorAsync(string message) => Task.CompletedTask;
        public Task<ConnectionEditResult?> EditConnectionAsync(ConnectionProfile? profile, ConnectionSecret? secret) => Task.FromResult<ConnectionEditResult?>(null);
        public Task<string?> PromptAsync(string title, string label, string value = "", bool secret = false) => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<string>> PickUploadAsync() => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> PickDownloadAsync(string name) => Task.FromResult<string?>(null);
        public Task EditFileAsync(TextDocument document, Func<TextDocument, string, Task<TextDocument>> save) => Task.CompletedTask;
        public Task<int?> PermissionsAsync(RemoteEntry entry) => Task.FromResult<int?>(null);
        public Task<TunnelDefinition?> EditTunnelAsync() => Task.FromResult<TunnelDefinition?>(null);
        public Task<AppSettings?> SettingsAsync(AppSettings settings) => Task.FromResult<AppSettings?>(null);
    }
}
