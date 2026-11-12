using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using ShellX.Core.Connections;
using ShellX.Core.Files;
using ShellX.Core.Security;
using ShellX.Core.Settings;
using ShellX.Core.Sessions;
using ShellX.Core.Tunnels;
using ShellX.Desktop.Controls;
using ShellX.Desktop.Features.FileBrowser;
using ShellX.Desktop.Features.Security;
using ShellX.Desktop.Features.Shell;
using ShellX.Desktop.Features.Workspace;
using ShellX.Desktop.Services;
using ShellX.Infrastructure.Files;
using ShellX.Infrastructure.Persistence;
using ShellX.Infrastructure.Security;

namespace ShellX.Desktop.Tests;

/// <summary>
/// 终端、文件管理器、文本编辑器三处页签必须是同一个控件，外观才能始终一致。
/// </summary>
public sealed class ShellTabStyleTests
{
    [AvaloniaFact]
    public async Task TerminalFileManagerAndEditorTabsShareTheSameTabControl()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-sharedtabs-" + Guid.NewGuid().ToString("N"))).FullName;
        var paths = new AppPaths(root);
        var dialogs = new FakeDialogs();
        var vm = new MainWindowViewModel(new JsonConnectionRepository(paths), new WindowsCredentialStore(paths), new JsonHostKeyStore(paths),
            new JsonSettingsStore(paths), new UnusedFactory(), dialogs, NullLoggerFactory.Instance, new LocalFileSystem(root),
            new AppLockService(new LockPasswordHasher()), new JsonConfigurationExporter(new(root)));
        await vm.InitializeAsync();
        var profile = new ConnectionProfile { Name = "终端连接", Host = "127.0.0.1", Port = 22, Username = "root" };
        var window = new MainWindow { DataContext = vm, Width = 1380, Height = 900 };
        window.Show();
        var tab = new WorkspaceTabViewModel(profile, new UnusedFactory(), dialogs, NullLoggerFactory.Instance,
            () => Task.FromResult<ConnectionSecret?>(null), _ => Task.FromResult(true), _ => Task.CompletedTask, _ => Task.CompletedTask,
            new AppSettings(), new LocalFileSystem(root));
        var owner = new Window();
        owner.Show();
        try
        {
            vm.Tabs.Add(tab);
            vm.SelectedTab = tab;
            await Task.Delay(120);
            var terminal = window.FindControl<ListBox>("ConnectionTabs")!.GetVisualDescendants().OfType<ShellTabItemView>().Single();
            Assert.Equal("终端连接", terminal.Title);
            Assert.True(terminal.IsActive);

            // 文件管理器页签用同一个控件，标题带连接名与编号。
            var fileTab = new FileManagerTabViewModel(tab, 1, dialogs, NullLoggerFactory.Instance, new LocalFileSystem(root), _ => { });
            vm.FileManagerTabs.Add(fileTab);
            vm.SelectedFileManagerTab = fileTab;
            var fileView = new FileManagerWindowView { DataContext = vm };
            var fileWindow = new Window { Content = fileView, Width = 1200, Height = 700 };
            fileWindow.Show();
            await Task.Delay(120);
            var file = fileView.FindControl<ListBox>("FileTabs")!.GetVisualDescendants().OfType<ShellTabItemView>().Single();
            Assert.Equal("终端连接 (1)", file.Title);
            Assert.True(file.IsActive);

            // 文本编辑器页签同样是这个控件，文件名作为标题。
            var editor = new DialogService(new TextCodec()) { Owner = owner };
            await editor.EditFileAsync(new("/etc/app.conf", "one", 3, DateTime.UtcNow), (document, _) => Task.FromResult(document));
            var editorWindow = owner.OwnedWindows.Single();
            await Task.Delay(80);
            var editorStrip = editorWindow.GetVisualDescendants().OfType<ListBox>().Single(list => list.Name == "EditorTabs");
            var editorTab = editorStrip.GetVisualDescendants().OfType<ShellTabItemView>().Single();
            Assert.Equal("app.conf", editorTab.Title);
            Assert.True(editorTab.IsActive);

            // 三处页签都带强调线与关闭按钮，结构完全一致。
            foreach (var item in new[] { terminal, file, editorTab })
            {
                Assert.Contains(item.GetVisualDescendants().OfType<ConnectionAccentBorder>(), border => border.Appearance == ConnectionAccentAppearance.Tab);
                Assert.Contains(item.GetVisualDescendants().OfType<ConnectionAccentBorder>(), border => border.Appearance == ConnectionAccentAppearance.Indicator);
                Assert.Single(item.GetVisualDescendants().OfType<Button>(), button => button.Name == "CloseButton");
            }

            fileWindow.Close();
            editorWindow.Close();
            await Task.Delay(40);
        }
        finally
        {
            foreach (var owned in owner.OwnedWindows.ToArray()) owned.Close();
            owner.Close();
            window.Close();
            await tab.DisposeAsync();
            Directory.Delete(root, true);
        }
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
