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

public sealed class FileManagerWindowTests
{
    [AvaloniaFact]
    public async Task ToolbarOpensFileManagerInItsOwnWindowAndMainViewNoLongerEmbedsIt()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-filewindow-" + Guid.NewGuid().ToString("N"))).FullName;
        var tab = new WorkspaceTabViewModel(
            new ConnectionProfile { Name = "测试连接", Host = "127.0.0.1", Port = 22, Username = "root" },
            new UnusedFactory(), new FakeDialogs(), NullLoggerFactory.Instance,
            () => Task.FromResult<ConnectionSecret?>(null), _ => Task.FromResult(true),
            _ => Task.CompletedTask, _ => Task.CompletedTask, new AppSettings(), new LocalFileSystem(root));
        var view = new WorkspaceView { DataContext = tab };
        var window = new Window { Content = view, Width = 1380, Height = 900 };
        window.Show();
        try
        {
            var button = view.FindControl<Button>("OpenFileManagerButton");
            Assert.NotNull(button);
            Assert.True(button!.IsEnabled);                                   // 没连接也能打开本地目录
            Assert.Empty(view.GetVisualDescendants().OfType<FileBrowserView>());   // 主窗口不再内嵌文件管理器

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(80);
            var fileWindow = Assert.Single(window.OwnedWindows);
            var browser = fileWindow.GetVisualDescendants().OfType<FileBrowserView>().Single();
            Assert.Same(tab.Files, browser.DataContext);                      // 复用同一个 VM，状态不丢
            Assert.StartsWith("文件管理 · 测试连接", fileWindow.Title);
            Assert.NotNull(browser.FindControl<LocalFileBrowserView>("LocalPane"));
            Assert.NotNull(browser.FindControl<RemoteFileBrowserView>("RemotePane"));

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));         // 重复点击只激活已有窗口
            await Task.Delay(40);
            Assert.Single(window.OwnedWindows);

            if (Environment.GetEnvironmentVariable("SHELLX_PREVIEW_PATH") is { Length: > 0 } preview)
            {
                await Task.Delay(60);
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)fileWindow.ClientSize.Width, (int)fileWindow.ClientSize.Height), new Vector(96, 96));
                bitmap.Render(fileWindow);
                bitmap.Save(Path.Combine(Path.GetDirectoryName(preview)!, "file-manager-window.png"), PngBitmapEncoderOptions.Default);
            }

            fileWindow.Close(); await Task.Delay(40);
            Assert.Empty(window.OwnedWindows);
        }
        finally
        {
            foreach (var owned in window.OwnedWindows.ToArray()) owned.Close();
            window.Close();
            await tab.DisposeAsync();
            Directory.Delete(root, true);
        }
    }

    [AvaloniaFact]
    public async Task OneFileManagerWindowSupportsMultipleNumberedTabsPerConnectionAndAAndB()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-filetabs-" + Guid.NewGuid().ToString("N"))).FullName;
        var paths = new AppPaths(root);
        var repository = new JsonConnectionRepository(paths);
        var dialogs = new FakeDialogs();
        var main = new MainWindowViewModel(repository, new WindowsCredentialStore(paths), new JsonHostKeyStore(paths),
            new JsonSettingsStore(paths), new UnusedFactory(), dialogs, NullLoggerFactory.Instance, new LocalFileSystem(root),
            new AppLockService(new LockPasswordHasher()), new JsonConfigurationExporter(paths));
        await main.InitializeAsync();

        static WorkspaceTabViewModel CreateTab(string name, string host, string root) => new(
            new ConnectionProfile { Name = name, Host = host, Port = 22, Username = "root" },
            new UnusedFactory(), new FakeDialogs(), NullLoggerFactory.Instance,
            () => Task.FromResult<ConnectionSecret?>(null), _ => Task.FromResult(true),
            _ => Task.CompletedTask, _ => Task.CompletedTask, new AppSettings(), new LocalFileSystem(root));

        var tabA = CreateTab("连接 A", "a.test", root);
        var tabB = CreateTab("连接 B", "b.test", root);
        var view = new WorkspaceView { DataContext = tabA };
        var owner = new Window { DataContext = main, Content = view, Width = 1200, Height = 760 };
        owner.Show();
        try
        {
            var button = view.FindControl<Button>("OpenFileManagerButton")!;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(80);
            var fileWindow = main.FileManagerWindow!;
            Assert.Null(fileWindow.Owner);                                  // 独立顶层窗口，不是模态/Owner 窗口
            Assert.True(owner.IsEnabled);                                    // 主窗口图标与终端保持可点击
            Assert.Equal("文件管理 · 连接 A (1)", TitleWithoutVersion(fileWindow.Title));
            Assert.Single(main.FileManagerTabs);
            Assert.Equal("连接 A (1)", main.SelectedFileManagerTab!.Title);

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));       // 重复打开 A 只激活已有页签
            await Task.Delay(40);
            Assert.Single(main.FileManagerTabs);

            var add = fileWindow.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "AddFileManagerTab");
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));          // “+”为 A 新建第二个页签
            await Task.Delay(80);
            Assert.Equal(2, main.FileManagerTabs.Count);
            Assert.All(main.FileManagerTabs, item => Assert.Same(tabA, item.Connection));
            Assert.Equal("连接 A (1)", main.FileManagerTabs[0].Title);
            Assert.Equal("连接 A (2)", main.FileManagerTabs[1].Title);
            Assert.Equal("连接 A (2)", main.SelectedFileManagerTab!.Title);
            Assert.NotSame(main.FileManagerTabs[0].Files, main.FileManagerTabs[1].Files);   // 同连接两个页签状态独立

            view.DataContext = tabB;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));       // B 追加到同一个窗口
            await Task.Delay(80);
            Assert.Same(fileWindow, main.FileManagerWindow);
            Assert.Equal(3, main.FileManagerTabs.Count);
            Assert.Equal("连接 B (1)", main.SelectedFileManagerTab!.Title);
            Assert.Equal(3, fileWindow.GetVisualDescendants().OfType<ListBoxItem>().Count());
            var browser = fileWindow.GetVisualDescendants().OfType<FileBrowserView>().Single();
            Assert.Same(main.SelectedFileManagerTab.Files, browser.DataContext);
            Assert.NotSame(tabB.Files, browser.DataContext);               // 文件页签使用独立浏览状态

            if (Environment.GetEnvironmentVariable("SHELLX_PREVIEW_PATH") is { Length: > 0 } tabsPreview)
            {
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)fileWindow.ClientSize.Width, (int)fileWindow.ClientSize.Height), new Vector(96, 96));
                bitmap.Render(fileWindow);
                bitmap.Save(Path.Combine(Path.GetDirectoryName(tabsPreview)!, "file-manager-tabs.png"), PngBitmapEncoderOptions.Default);
            }

            var closeB = fileWindow.GetVisualDescendants().OfType<Button>()
                .Single(b => ReferenceEquals(b.DataContext, main.SelectedFileManagerTab) && Equals(Avalonia.Controls.ToolTip.GetTip(b), "关闭文件标签"));
            closeB.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(40);
            Assert.Equal(2, main.FileManagerTabs.Count);
            Assert.Same(tabA, main.SelectedFileManagerTab!.Connection);
            Assert.Equal("连接 A (2)", main.SelectedFileManagerTab.Title);   // 关闭 B 后剩余 A 页签重新连续编号
            Assert.True(fileWindow.IsVisible);
            Assert.Null(fileWindow.Owner);
        }
        finally
        {
            main.FileManagerWindow?.Close();
            foreach (var owned in owner.OwnedWindows.ToArray()) owned.Close();
            owner.Close();
            await main.ShutdownAsync();
            Directory.Delete(root, true);
        }
    }

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

    /// <summary>文件管理器里弹出的对话框以文件管理器为父窗口：关闭弹窗时只回到文件管理器，
    /// 不会把主窗口提到前面。</summary>
    [AvaloniaFact]
    public async Task DialogFromFileManagerKeepsFileManagerAsParent()
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
            view.FindControl<Button>("OpenFileManagerButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(80);
            var fileWindow = main.FileManagerWindow!;
            Assert.NotNull(fileWindow);

            var entry = new RemoteEntry("test.txt", "/tmp/test.txt", false, false, 1024, DateTime.UtcNow, 420, 1000, 1000);
            var pending = dialogs.PermissionsAsync(entry);
            await Task.Delay(80);
            var permission = fileWindow.OwnedWindows.OfType<Window>().Single(w => w.Title == "属性与权限");
            Assert.Same(fileWindow, permission.Owner);
            Assert.DoesNotContain(owner.OwnedWindows.OfType<Window>(), w => ReferenceEquals(w, permission));
            permission.Close();
            await pending;
        }
        finally
        {
            main.FileManagerWindow?.Close();
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