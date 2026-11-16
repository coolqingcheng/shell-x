using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using ShellX.Core.Connections;
using ShellX.Core.Files;
using ShellX.Core.Security;
using ShellX.Core.Sessions;
using ShellX.Core.Settings;
using ShellX.Core.Tunnels;
using ShellX.Desktop.Features.FileBrowser;
using ShellX.Desktop.Features.Workspace;
using ShellX.Desktop.Services;
using ShellX.Desktop.Features.Shell;
using ShellX.Desktop.Features.Security;
using ShellX.Infrastructure.Persistence;
using ShellX.Infrastructure.Security;
using ShellX.Infrastructure.Files;

namespace ShellX.Desktop.Tests;

public sealed class DialogOwnerTests
{
    [AvaloniaFact]
    public async Task PermissionDialogUsesFileManagerOwnerInsteadOfTerminalWindow()
    {
        var main = new Window();
        var file = new Window();
        main.Show();
        file.Show();
        var dialogs = new DialogService(new TextCodec()) { Owner = main };
        dialogs.TrackOwnerWindow(main);
        dialogs.TrackOwnerWindow(file);
        try
        {
            var entry = new RemoteEntry("test.txt", "/tmp/test.txt", false, false, 1024, DateTime.UtcNow, 420, 1000, 1000);
            var pending = dialogs.PermissionsAsync(entry);
            await Task.Delay(80);

            var permission = file.OwnedWindows.OfType<Window>().Single(w => w.Title == "属性与权限");
            Assert.Same(file, permission.Owner);
            Assert.DoesNotContain(main.OwnedWindows.OfType<Window>(), w => ReferenceEquals(w, permission));
            Assert.True(main.IsEnabled);                                      // 终端主窗口仍可点击

            permission.Close();
            Assert.Null(await pending);
        }
        finally
        {
            dialogs.UntrackOwnerWindow(file);
            foreach (var owned in file.OwnedWindows.ToArray()) owned.Close();
            file.Close();
            main.Close();
        }
    }

    /// <summary>右侧远程文件面板里的弹窗以主窗口为父窗口：关闭弹窗时不会把别的窗口提到前面。</summary>
    [AvaloniaFact]
    public async Task DialogFromRemoteFilePaneUsesMainWindowAsParent()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-filedialog-" + Guid.NewGuid().ToString("N"))).FullName;
        var paths = new AppPaths(root);
        var repository = new JsonConnectionRepository(paths);
        var dialogs = new DialogService(new TextCodec());
        var main = new MainWindowViewModel(repository, new WindowsCredentialStore(paths), new JsonHostKeyStore(paths),
            new JsonSettingsStore(paths), new UnusedFactory(), dialogs, NullLoggerFactory.Instance, new LocalFileSystem(root),
            new AppLockService(new LockPasswordHasher()), new JsonConfigurationExporter(paths));
        await main.InitializeAsync();
        var tab = new WorkspaceTabViewModel(
            new ConnectionProfile { Name = "连接 A", Host = "a.test", Port = 22, Username = "root" },
            new UnusedFactory(), dialogs, NullLoggerFactory.Instance,
            () => Task.FromResult<ConnectionSecret?>(null), _ => Task.FromResult(true),
            _ => Task.CompletedTask, _ => Task.CompletedTask, new AppSettings(), new LocalFileSystem(root));
        var view = new WorkspaceView { DataContext = tab };
        var owner = new Window { DataContext = main, Content = view, Width = 1200, Height = 760 };
        owner.Show();
        try
        {
            dialogs.Owner = owner;
            dialogs.TrackOwnerWindow(owner);

            var entry = new RemoteEntry("test.txt", "/tmp/test.txt", false, false, 1024, DateTime.UtcNow, 420, 1000, 1000);
            var pending = dialogs.PermissionsAsync(entry);
            await Task.Delay(80);
            var permission = owner.OwnedWindows.OfType<Window>().Single(w => w.Title == "属性与权限");
            Assert.Same(owner, permission.Owner);
            permission.Close();
            await pending;
        }
        finally
        {
            foreach (var owned in owner.OwnedWindows.ToArray()) owned.Close();
            owner.Close();
            await main.ShutdownAsync();
            await tab.DisposeAsync();
            Directory.Delete(root, true);
        }
    }

    /// <summary>模态弹窗的父窗口取“最近激活的窗口”：文件管理器里弹出的对话框不会落到主窗口上，
    /// 关闭时也就不会把主窗口提到前面。</summary>
    [AvaloniaFact]
    public async Task DialogOwnerFollowsMostRecentlyActivatedWindow()
    {
        var main = new Window();
        var file = new Window();
        main.Show();
        file.Show();
        var dialogs = new DialogService(new TextCodec()) { Owner = main };
        dialogs.TrackOwnerWindow(main);
        dialogs.TrackOwnerWindow(file);
        try
        {
            var entry = new RemoteEntry("test.txt", "/tmp/test.txt", false, false, 1024, DateTime.UtcNow, 420, 1000, 1000);

            // 文件管理器是最近激活的窗口：弹窗挂在文件管理器下。
            var first = dialogs.PermissionsAsync(entry);
            await Task.Delay(80);
            var permission = file.OwnedWindows.OfType<Window>().Single(w => w.Title == "属性与权限");
            Assert.Same(file, permission.Owner);
            permission.Close();
            await first;

            // 弹窗关闭后父窗口仍然有效，后续弹窗不会退回到主窗口。
            var second = dialogs.PermissionsAsync(entry);
            await Task.Delay(80);
            var again = file.OwnedWindows.OfType<Window>().Single(w => w.Title == "属性与权限");
            Assert.Same(file, again.Owner);
            Assert.DoesNotContain(main.OwnedWindows.OfType<Window>(), w => ReferenceEquals(w, again));
            again.Close();
            await second;

            // 用户回到终端后，弹窗跟随主窗口。
            dialogs.TrackOwnerWindow(main);
            var third = dialogs.PermissionsAsync(entry);
            await Task.Delay(80);
            var terminal = main.OwnedWindows.OfType<Window>().Single(w => w.Title == "属性与权限");
            Assert.Same(main, terminal.Owner);
            terminal.Close();
            await third;
        }
        finally
        {
            dialogs.UntrackOwnerWindow(file);
            foreach (var owned in file.OwnedWindows.ToArray()) owned.Close();
            foreach (var owned in main.OwnedWindows.ToArray()) owned.Close();
            file.Close();
            main.Close();
        }
    }
    private static string TitleWithoutVersion(string? title)
    {
        var text = title ?? "";
        var index = text.IndexOf(" v", StringComparison.Ordinal);
        Assert.True(index > 0);
        return text[..index];
    }
    /// <summary>文件树里“在终端打开”会交给外壳，由外壳新建一个连接。</summary>
    [AvaloniaFact]
    public async Task OpenInTerminalInFileTreeGoesToShell()
    {
        WorkspaceTabViewModel? source = null;
        string? requested = null;
        var tab = new WorkspaceTabViewModel(
            new ConnectionProfile { Name = "测试连接", Host = "127.0.0.1", Port = 22, Username = "root" },
            new UnusedFactory(), new FakeDialogs(), NullLoggerFactory.Instance,
            () => Task.FromResult<ConnectionSecret?>(null), _ => Task.FromResult(true),
            _ => Task.CompletedTask, _ => Task.CompletedTask, new AppSettings(), new LocalFileSystem(),
            null, null, (owner, path) => { source = owner; requested = path; return Task.CompletedTask; });

        await tab.OpenTerminalAtAsync("/usr/local");
        Assert.Same(tab, source);
        Assert.Equal("/usr/local", requested);
    }

    private sealed class UnusedFactory : IRemoteSessionFactory
    {
        public Task<IRemoteSession> ConnectAsync(ConnectionProfile profile, ConnectionSecret secret,
            Func<HostKeyChallenge, Task<bool>> verifyHost, CancellationToken cancellationToken)
            => throw new InvalidOperationException("该测试只验证界面入口，不应建立连接。");
    }

    private sealed class FakeDialogs : IDialogService
    {
        public Task<ConnectionEditResult?> EditConnectionAsync(ConnectionProfile? profile, ConnectionSecret? secret)
            => Task.FromResult<ConnectionEditResult?>(null);
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
}