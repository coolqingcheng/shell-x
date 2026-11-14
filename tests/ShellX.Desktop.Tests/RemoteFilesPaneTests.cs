using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using ShellX.Core.Connections;
using ShellX.Core.Files;
using ShellX.Core.Security;
using ShellX.Core.Sessions;
using ShellX.Core.Settings;
using ShellX.Core.Tunnels;
using ShellX.Desktop.Features.FileBrowser;
using ShellX.Desktop.Features.Security;
using ShellX.Desktop.Features.Shell;
using ShellX.Desktop.Services;
using ShellX.Infrastructure.Files;
using ShellX.Infrastructure.Persistence;
using ShellX.Infrastructure.Security;
using ShellX.Infrastructure.Ssh;

namespace ShellX.Desktop.Tests;

/// <summary>
/// 主窗口右侧的远程文件面板：按层级浏览，点目录进入下一层，
/// 点下载选本地保存目录，本地文件拖进来就上传。
/// </summary>
public sealed class RemoteFilesPaneTests
{
    [Fact]
    public async Task PaneListsTheCurrentDirectoryOnly()
    {
        var files = new RecordingRemoteFileSystem(new Dictionary<string, IReadOnlyList<RemoteEntry>>
        {
            ["/"] = [Entry("etc", true), Entry("readme.txt", false)],
            ["/etc"] = [Entry("hosts", false)]
        });
        using var pane = new RemoteFilesPaneViewModel(new PaneDialogs(), NullLogger<RemoteFilesPaneViewModel>.Instance, new StubLocalFileSystem());
        pane.Attach(files, CancellationToken.None, "测试服务器");
        await WaitForAsync(() => pane.Entries.Count >= 2);

        Assert.True(pane.IsAvailable);
        Assert.Equal("/", pane.CurrentPath);
        Assert.Equal(2, pane.Entries.Count);
        Assert.Contains(pane.Entries, node => node.Name == "etc");
        Assert.Contains(pane.Entries, node => node.Name == "readme.txt");
        // 按层级浏览，不是一棵树：没有展开箭头，也不缩进。
        Assert.False(pane.Entries.Single(node => node.Name == "etc").ShowChevron);
        Assert.False(pane.Entries.Single(node => node.Name == "etc").CanDownloadItem);
        Assert.True(pane.Entries.Single(node => node.Name == "readme.txt").CanDownloadItem);
    }

    [Fact]
    public async Task ClickingAFolderEntersTheNextLevel()
    {
        var files = new RecordingRemoteFileSystem(new Dictionary<string, IReadOnlyList<RemoteEntry>>
        {
            ["/"] = [Entry("etc", true), Entry("readme.txt", false)],
            ["/etc"] = [Entry("hosts", false)]
        });
        using var pane = new RemoteFilesPaneViewModel(new PaneDialogs(), NullLogger<RemoteFilesPaneViewModel>.Instance, new StubLocalFileSystem());
        pane.Attach(files, CancellationToken.None, "测试服务器");
        await WaitForAsync(() => pane.Entries.Count >= 2);

        var folder = pane.Entries.Single(node => node.Name == "etc");
        pane.Select(folder);
        await WaitForAsync(() => pane.CurrentPath == "/etc");

        Assert.Equal("/etc", pane.CurrentPath);
        Assert.Single(pane.Entries);
        Assert.Equal("hosts", pane.Entries[0].Name);
        Assert.True(pane.CanGoUp);

        // 上一级回到根目录。
        await pane.UpCommand.ExecuteAsync(null);
        await WaitForAsync(() => pane.CurrentPath == "/");
        Assert.Equal(2, pane.Entries.Count);
        Assert.False(pane.CanGoUp);
    }

    [Fact]
    public async Task RefreshingDoesNotDuplicateTheRows()
    {
        var files = new RecordingRemoteFileSystem(new Dictionary<string, IReadOnlyList<RemoteEntry>>
        {
            ["/"] = [Entry("etc", true), Entry("var", true), Entry("readme.txt", false)]
        });
        using var pane = new RemoteFilesPaneViewModel(new PaneDialogs(), NullLogger<RemoteFilesPaneViewModel>.Instance, new StubLocalFileSystem());
        pane.Attach(files, CancellationToken.None, "测试服务器");
        await WaitForAsync(() => pane.Entries.Count >= 3);

        await pane.RefreshCommand.ExecuteAsync(null);
        await WaitForAsync(() => pane.Entries.Count == 3);
        await pane.RefreshCommand.ExecuteAsync(null);
        await Task.Delay(120, TestContext.Current.CancellationToken);

        Assert.Equal(3, pane.Entries.Count);
        Assert.Equal(3, pane.Entries.Select(node => node.Name).Distinct().Count());
    }

    [Fact]
    public async Task DownloadingARemoteFileAsksForTheLocalFolderFirst()
    {
        var local = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-pane-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var files = new RecordingRemoteFileSystem(new Dictionary<string, IReadOnlyList<RemoteEntry>>
            {
                ["/"] = [Entry("readme.txt", false, size: 5)]
            });
            var dialogs = new PaneDialogs(local);
            using var pane = new RemoteFilesPaneViewModel(dialogs, NullLogger<RemoteFilesPaneViewModel>.Instance, new StubLocalFileSystem());
            pane.Attach(files, CancellationToken.None, "测试服务器");
            await WaitForAsync(() => pane.Entries.Count >= 1);

            var file = pane.Entries.Single(node => node.Name == "readme.txt");
            pane.Select(file);
            await pane.DownloadItemAsync(file);

            Assert.Equal(1, dialogs.PickFolderCount);
            Assert.True(File.Exists(Path.Combine(local, "readme.txt")), "文件没有下载到选择的本地目录");
            Assert.Equal("/readme.txt", files.LastDownload?.Remote);
        }
        finally { Directory.Delete(local, true); }
    }

    [Fact]
    public async Task DraggingALocalFileUploadsItToTheTargetDirectory()
    {
        var local = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-pane-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var source = Path.Combine(local, "payload.txt");
            await File.WriteAllTextAsync(source, "hello", TestContext.Current.CancellationToken);
            var files = new RecordingRemoteFileSystem(new Dictionary<string, IReadOnlyList<RemoteEntry>>
            {
                ["/"] = [Entry("etc", true)]
            });
            using var pane = new RemoteFilesPaneViewModel(new PaneDialogs(local), NullLogger<RemoteFilesPaneViewModel>.Instance, new StubLocalFileSystem());
            pane.Attach(files, CancellationToken.None, "测试服务器");
            await WaitForAsync(() => pane.Entries.Count >= 1);

            var folder = pane.Entries.Single(node => node.Name == "etc");
            await pane.DropLocalAsync(folder, [source]);

            Assert.NotNull(files.LastUpload);
            Assert.Equal("/etc/payload.txt", files.LastUpload?.Remote);
        }
        finally { Directory.Delete(local, true); }
    }

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

    /// <summary>主窗口右侧固定挂着远程文件面板，默认展开并显示远程层级。</summary>
    [AvaloniaFact]
    public async Task MainWindowShowsTheRemoteFilePaneOnTheRight()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-pane-view-" + Guid.NewGuid().ToString("N"))).FullName;
        MainWindowViewModel? vm = null;
        Window? window = null;
        try
        {
            var profile = new ConnectionProfile { Name = "测试服务器", Host = "10.0.0.1", Port = 22, Username = "root" };
            vm = new MainWindowViewModel(new Connections(profile), new Credentials(), new Hosts(), new Settings(),
                new SshSessionFactory(NullLoggerFactory.Instance), new PaneDialogs(), NullLoggerFactory.Instance,
                new LocalFileSystem(root), new AppLockService(new LockPasswordHasher()), new JsonConfigurationExporter(new(root)));
            window = new MainWindow { DataContext = vm, Width = 1380, Height = 900 };
            window.Show();
            await Task.Delay(150);

            var files = new RecordingRemoteFileSystem(new Dictionary<string, IReadOnlyList<RemoteEntry>>
            {
                ["/"] = [Entry("etc", true), Entry("var", true), Entry("readme.txt", false)],
                ["/usr"] = [Entry("local", true)],
                ["/usr/local"] = [Entry("share", true), Entry("notes.txt", false)]
            });
            vm.FilePane.Attach(files, CancellationToken.None, "测试服务器 · 10.0.0.1:22");
            await Task.Delay(300);

            var pane = window.FindControl<RemoteFilesPaneView>("RemoteFilePane");
            Assert.NotNull(pane);
            Assert.True(pane!.IsVisible, "右侧远程文件面板没有显示");
            Assert.True(pane.Bounds.Width > 200, $"面板宽度不对：{pane.Bounds.Width}");
            Assert.True(pane.GetVisualDescendants().OfType<FileTreeNodeView>().Any(), "面板没有渲染任何远程文件行");
            // 顶部有面包屑导航栏。
            var pathBar = pane.FindControl<Controls.PathBar>("PathBar");
            Assert.True(pathBar != null, "面板顶部没有面包屑导航栏");

            if (Environment.GetEnvironmentVariable("SHELLX_PREVIEW_PATH") is { Length: > 0 } preview)
            {
                using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(
                    new Avalonia.PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height), new Avalonia.Vector(96, 96));
                bitmap.Render(window);
                bitmap.Save(Path.Combine(Path.GetDirectoryName(preview)!, "remote-file-pane.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);

                // 进到更深的目录，面包屑上会多出几段。
                await vm.FilePane.NavigateAsync("/usr/local");
                await Task.Delay(300);
                using var deep = new Avalonia.Media.Imaging.RenderTargetBitmap(
                    new Avalonia.PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height), new Avalonia.Vector(96, 96));
                deep.Render(window);
                deep.Save(Path.Combine(Path.GetDirectoryName(preview)!, "remote-file-pane-deep.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
        }
        finally { if (vm != null) await vm.ShutdownAsync(); window?.Close(); Directory.Delete(root, true); }
    }

    /// <summary>收起右侧面板：压成窄把手，展开按钮还在，状态写回配置。</summary>
    [AvaloniaFact]
    public async Task CollapsingTheFilePaneKeepsAHandleAndRemembersTheState()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-pane-view-" + Guid.NewGuid().ToString("N"))).FullName;
        MainWindowViewModel? vm = null;
        Window? window = null;
        try
        {
            var profile = new ConnectionProfile { Name = "测试服务器", Host = "10.0.0.1", Port = 22, Username = "root" };
            var store = new Settings();
            vm = new MainWindowViewModel(new Connections(profile), new Credentials(), new Hosts(), store,
                new SshSessionFactory(NullLoggerFactory.Instance), new PaneDialogs(), NullLoggerFactory.Instance,
                new LocalFileSystem(root), new AppLockService(new LockPasswordHasher()), new JsonConfigurationExporter(new(root)));
            window = new MainWindow { DataContext = vm, Width = 1380, Height = 900 };
            window.Show();
            await Task.Delay(150);

            await vm.ToggleFilePaneCommand.ExecuteAsync(null);
            await Task.Delay(150);

            Assert.True(vm.IsFilePaneCollapsed);
            Assert.True(store.Value.FilePaneCollapsed, "收起状态没有保存到配置");
            Assert.False(window.FindControl<Border>("FilePaneHost")!.IsVisible, "收起后面板还在显示");
            Assert.True(window.FindControl<Button>("ExpandFilePane")!.IsVisible, "收起后没有展开把手");

            await vm.ToggleFilePaneCommand.ExecuteAsync(null);
            await Task.Delay(150);
            Assert.False(vm.IsFilePaneCollapsed);
            Assert.True(window.FindControl<Border>("FilePaneHost")!.IsVisible, "展开后面板没有显示");
        }
        finally { if (vm != null) await vm.ShutdownAsync(); window?.Close(); Directory.Delete(root, true); }
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
    /// <summary>远程文件系统替身：记录上传和下载。</summary>
    private sealed class RecordingRemoteFileSystem(IReadOnlyDictionary<string, IReadOnlyList<RemoteEntry>> tree) : IRemoteFileSystem
    {
        public (string Local, string Remote)? LastUpload { get; private set; }
        public (string Remote, string Local)? LastDownload { get; private set; }

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
        { LastUpload = (localPath, remotePath); progress.Report(1); return Task.CompletedTask; }
        public Task DownloadAsync(string remotePath, string localPath, IProgress<long> progress, CancellationToken cancellationToken = default)
        {
            LastDownload = (remotePath, localPath);
            File.WriteAllText(localPath, "hello");
            progress.Report(5);
            return Task.CompletedTask;
        }
        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    /// <summary>对话框替身：下载时返回指定目录。</summary>
    private sealed class PaneDialogs(string? downloadFolder = null) : IDialogService
    {
        public int PickFolderCount { get; private set; }
        public Task<string?> PickDownloadFolderAsync() { PickFolderCount++; return Task.FromResult(downloadFolder); }
        public Task<ConnectionEditResult?> EditConnectionAsync(ConnectionProfile? profile, ConnectionSecret? secret) => Task.FromResult<ConnectionEditResult?>(null);
        public Task<bool> ConfirmAsync(string title, string message, bool danger = false) => Task.FromResult(true);
        public Task<string?> PromptAsync(string title, string label, string value = "", bool secret = false) => Task.FromResult<string?>(null);
        public Task ShowErrorAsync(string message) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> PickUploadAsync() => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> PickDownloadAsync(string name) => Task.FromResult<string?>(null);
        public Task EditFileAsync(TextDocument document, Func<TextDocument, string, Task<TextDocument>> save) => Task.CompletedTask;
        public Task<int?> PermissionsAsync(RemoteEntry entry) => Task.FromResult<int?>(null);
        public Task<Core.Tunnels.TunnelDefinition?> EditTunnelAsync() => Task.FromResult<Core.Tunnels.TunnelDefinition?>(null);
        public Task<Core.Settings.AppSettings?> SettingsAsync(Core.Settings.AppSettings settings) => Task.FromResult<Core.Settings.AppSettings?>(null);
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
}
