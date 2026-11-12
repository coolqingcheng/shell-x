using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Iciclecreek.Terminal;
using Microsoft.Extensions.Logging.Abstractions;
using ShellX.Core.Connections;
using ShellX.Core.Files;
using ShellX.Core.Settings;
using ShellX.Core.Tunnels;
using ShellX.Desktop.Features.Connections;
using ShellX.Desktop.Features.Monitoring;
using ShellX.Desktop.Features.Security;
using ShellX.Desktop.Features.Shell;
using ShellX.Desktop.Features.Workspace;
using ShellX.Desktop.Controls;
using ShellX.Desktop.Services;
using ShellX.Infrastructure.Files;
using ShellX.Infrastructure.Persistence;
using ShellX.Infrastructure.Security;
using ShellX.Infrastructure.Ssh;

namespace ShellX.Desktop.Tests;

public sealed class ConnectionManagerTests
{
    [AvaloniaFact]
    public async Task ExportButtonIncludesAllConnectionsRegardlessOfSearchAndReportsErrorsAndCancellation()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-export-ui-" + Guid.NewGuid().ToString("N"))).FullName;
        var store = new JsonConnectionRepository(new(root));
        await store.SaveCatalogAsync(new([new() { Name = "one", Host = "one.test" }, new() { Name = "two", Host = "two.test" }], ["空目录"]));
        var dialogs = new Dialogs { ExportPath = Path.Combine(root, "backup.json") }; var vm = Model(root, store, dialogs);
        var view = new ConnectionManagerView { DataContext = vm }; var window = new Window { Content = view, Width = 900, Height = 620 }; window.Show();
        try
        {
            await vm.InitializeAsync(); vm.Search = "missing"; await Task.Delay(60);
            Assert.Empty(vm.VisibleConnections);
            var export = view.FindControl<Button>("ExportConfiguration")!; Assert.Same(vm.ExportConfigurationCommand, export.Command);
            Assert.Same(vm.ImportConfigurationCommand, view.FindControl<Button>("ImportConfiguration")!.Command);
            Assert.Same(vm.ImportXshellSessionsCommand, view.FindControl<Button>("ImportXshellSessions")!.Command);
            Assert.Same(vm.SettingsCommand, view.FindControl<Button>("ManagerSettings")!.Command);
            Click(window, export); await Until(() => dialogs.Information.Count == 1);
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(dialogs.ExportPath));
            Assert.Equal(2, json.RootElement.GetProperty("Connections").GetArrayLength());
            Assert.Contains("空目录", json.RootElement.GetProperty("Folders").EnumerateArray().Select(x => x.GetString()));
            Assert.Equal("配置导出成功", vm.Status);
            dialogs.ExportPath = null; await vm.ExportConfigurationCommand.ExecuteAsync(null); Assert.Single(dialogs.Information);
            dialogs.ExportPath = root; await vm.ExportConfigurationCommand.ExecuteAsync(null);
            Assert.Contains(dialogs.Errors, e => e.StartsWith("导出失败"));
        }
        finally { await vm.ShutdownAsync(); window.Close(); Directory.Delete(root, true); }
    }
    [AvaloniaFact]
    public async Task ConnectionManagerShowsShellXBrandAndVersion()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-brand-" + Guid.NewGuid().ToString("N"))).FullName;
        var store = new JsonConnectionRepository(new(root));
        await store.SaveCatalogAsync(new([new() { Name = "one", Host = "one.test" }], []));
        var vm = Model(root, store, new Dialogs());
        var view = new ConnectionManagerView { DataContext = vm };
        var window = new Window { Content = view, Width = 900, Height = 620 }; window.Show();
        try
        {
            await vm.InitializeAsync(); await Task.Delay(60);
            var brand = view.FindControl<StackPanel>("BrandBlock");
            Assert.NotNull(brand);
            var brandTexts = brand!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("ShellX", brandTexts);
            Assert.Contains(vm.VersionText, brandTexts);
            // 品牌标识在最左侧，主操作按钮仍排在它右边。
            var create = view.FindControl<Button>("NewManagedConnection")!;
            Assert.True(create.Bounds.X > brand.Bounds.X + brand.Bounds.Width);
        }
        finally { await vm.ShutdownAsync(); window.Close(); Directory.Delete(root, true); }
    }
    [AvaloniaFact]
    public async Task ImportsXshellSessionsOnceSelectsXshellFolderAndDecryptsPassword()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-xsh-ui-" + Guid.NewGuid().ToString("N"))).FullName;
        var store = new JsonConnectionRepository(new(root));
        var sessionPath = Path.Combine(root, "iot99.xiaolab.cn.xsh");
        var content = string.Join('\n',
            "[SessionInfo]", "Version=8.1",
            "[CONNECTION]", "Host=iot99.xiaolab.cn", "Port=22", "Protocol=SSH",
            "[CONNECTION:AUTHENTICATION]", "AuthMethodList=01", "UserKey=", "UserName=root",
            $"Password={XshellPasswordCipher.Encrypt("xshell-direct-password", XshellPasswordCipher.BuildCurrentUserKey())}");
        File.WriteAllText(sessionPath, content, System.Text.Encoding.Unicode);

        var dialogs = new Dialogs { XshellPaths = [sessionPath] };
        var vm = Model(root, store, dialogs);
        var view = new ConnectionManagerView { DataContext = vm };
        var window = new Window { Content = view, Width = 900, Height = 620 };
        window.Show();
        try
        {
            await vm.InitializeAsync();
            await vm.ImportXshellSessionsCommand.ExecuteAsync(null);
            await Until(() => dialogs.Information.Count == 1);

            var imported = Assert.Single(await store.LoadAsync());
            Assert.Equal("iot99.xiaolab.cn", imported.Host);
            Assert.Equal(XshellSessionImporter.DefaultGroup, imported.Group);
            Assert.Equal(AuthenticationKind.Password, imported.Authentication);
            Assert.True(imported.RememberSecret);
            Assert.Equal(new ConnectionSecret("xshell-direct-password", ""), await new WindowsCredentialStore(new(root)).ReadAsync(imported.Id));
            Assert.Equal("Xshell", vm.SelectedGroup!.Name);
            Assert.Equal(imported.Id, Assert.Single(vm.VisibleConnections).Id);
            Assert.Contains("已从 Xshell 导入 1 个连接", vm.Status);
            Capture(window, "connection-manager.png");
            Assert.Contains("已解密并保存凭据 1 条", dialogs.Information.Single());
            Assert.Contains("不会明文写入连接配置", dialogs.Information.Single());

            await vm.ImportXshellSessionsCommand.ExecuteAsync(null);
            await Until(() => dialogs.Information.Count == 2);
            Assert.Single(await store.LoadAsync());   // 相同 endpoint 再次导入只保留一个
            Assert.Contains("跳过重复 1 个", dialogs.Information[1]);
        }
        finally { await vm.ShutdownAsync(); window.Close(); Directory.Delete(root, true); }
    }
    [AvaloniaFact]
    public async Task XshellMasterPasswordPromptDecryptsSessionAndStoresPassword()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-xsh-master-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var path = Path.Combine(root, "master-session.xsh");
            var encoded = XshellPasswordCipher.Encrypt("master-saved-password", "xshell-master-key");
            var content = string.Join('\n',
                "[SessionInfo]", "Version=8.1",
                "[CONNECTION]", "Host=master.test", "Port=22", "Protocol=SSH",
                "[CONNECTION:AUTHENTICATION]", "AuthMethodList=01", "UserKey=",
                "UserName=root", $"Password={encoded}");
            File.WriteAllText(path, content, System.Text.Encoding.Unicode);

            var store = new JsonConnectionRepository(new(root));
            var dialogs = new Dialogs { XshellPaths = [path] };
            dialogs.Prompts.Enqueue("xshell-master-key");
            var vm = Model(root, store, dialogs);
            await vm.InitializeAsync();
            await vm.ImportXshellSessionsCommand.ExecuteAsync(null);

            Assert.Empty(dialogs.Errors);
            Assert.Single(dialogs.Information);
            var profile = Assert.Single(await store.LoadAsync());
            Assert.True(profile.RememberSecret);
            Assert.Equal(new ConnectionSecret("master-saved-password", ""), await new WindowsCredentialStore(new(root)).ReadAsync(profile.Id));
            Assert.Contains("已解密并保存凭据 1 条", dialogs.Information.Single());
            await vm.ShutdownAsync();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [AvaloniaFact]
    public async Task EncryptedConfigurationExportRestoresConnectionPasswordAndPortableSettingsOnFreshInstall()
    {
        var sourceRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-config-src-" + Guid.NewGuid().ToString("N"))).FullName;
        var restoreRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-config-dst-" + Guid.NewGuid().ToString("N"))).FullName;
        var backup = Path.Combine(sourceRoot, "ShellX-backup.json");
        var profile = new ConnectionProfile { Name = "备份连接", Host = "backup.test", Group = "恢复", Username = "deploy" };
        var secret = new ConnectionSecret("Exported-Login-Password", "");
        var backupPassword = "backup-password-123";
        try
        {
            await new JsonConnectionRepository(new(sourceRoot)).SaveAsync([profile]);
            await new JsonSettingsStore(new(sourceRoot)).SaveAsync(new AppSettings { Theme = AppTheme.Light, TerminalFontSize = 19 });
            await new WindowsCredentialStore(new(sourceRoot)).SaveAsync(profile.Id, secret);

            var sourceDialogs = new Dialogs { ExportPath = backup };
            sourceDialogs.Prompts.Enqueue(backupPassword);
            var source = Model(sourceRoot, new JsonConnectionRepository(new(sourceRoot)), sourceDialogs);
            await source.InitializeAsync();
            await source.ExportConfigurationCommand.ExecuteAsync(null);
            Assert.Contains("配置导出成功", source.Status);
            Assert.Contains("直接使用", sourceDialogs.Information.Single());
            var backupJson = await File.ReadAllTextAsync(backup);
            Assert.DoesNotContain(secret.Password, backupJson, StringComparison.Ordinal);
            await source.ShutdownAsync();

            var restoreStore = new JsonConnectionRepository(new(restoreRoot));
            var restoreCredentials = new WindowsCredentialStore(new(restoreRoot));
            var restoreDialogs = new Dialogs { ConfigurationImportPath = backup };
            restoreDialogs.Prompts.Enqueue(backupPassword);
            var restored = Model(restoreRoot, restoreStore, restoreDialogs);
            await restored.InitializeAsync();
            await restored.ImportConfigurationCommand.ExecuteAsync(null);

            Assert.Contains("配置导入成功", restored.Status);
            Assert.Contains("恢复凭据 1 条", restoreDialogs.Information.Single());
            var restoredProfile = Assert.Single(await restoreStore.LoadAsync());
            Assert.Equal(profile.Id, restoredProfile.Id);
            Assert.Equal("backup.test", restoredProfile.Host);
            Assert.True(restoredProfile.RememberSecret);
            Assert.Equal(secret, await restoreCredentials.ReadAsync(profile.Id));
            Assert.Equal(AppTheme.Light, restored.LayoutSettings.Theme);
            Assert.Equal(19, restored.LayoutSettings.TerminalFontSize);
            await restored.ShutdownAsync();
        }
        finally
        {
            if (Directory.Exists(sourceRoot)) Directory.Delete(sourceRoot, true);
            if (Directory.Exists(restoreRoot)) Directory.Delete(restoreRoot, true);
        }
    }
    [AvaloniaFact]
    public async Task EmptyAndNestedFoldersPersistRenameConnectionsAndRejectDeletingNonemptyFolders()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-catalog-" + Guid.NewGuid().ToString("N"))).FullName;
        var store = new JsonConnectionRepository(new(root));
        var profile = new ConnectionProfile { Name = "生产服务器", Group = "生产/华东", Host = "localhost" };
        await store.SaveAsync([profile]);
        var dialogs = new Dialogs(); var vm = Model(root, store, dialogs);
        try
        {
            await vm.InitializeAsync();
            Assert.NotNull(vm.FindFolder("生产")); Assert.NotNull(vm.FindFolder("生产/华东"));
            Assert.Empty(vm.VisibleConnections); Assert.True(vm.NeedsLeafFolder);
            vm.SelectedGroup = vm.FindFolder("生产"); Assert.Empty(vm.VisibleConnections);
            vm.Search = "生产服务器"; Assert.Empty(vm.VisibleConnections); Assert.False(vm.HasNoResults);
            vm.SelectedGroup = vm.FindFolder("生产/华东"); Assert.Equal(profile.Id, Assert.Single(vm.VisibleConnections).Id);
            vm.Search = ""; vm.SelectedGroup = vm.FindFolder("");
            dialogs.Prompts.Enqueue("空目录"); await vm.NewFolderCommand.ExecuteAsync(null);
            Assert.Equal("空目录", vm.SelectedGroup!.Path);
            dialogs.Prompts.Enqueue("子目录"); await vm.NewFolderCommand.ExecuteAsync(null);
            Assert.Equal("空目录/子目录", vm.SelectedGroup!.Path); Assert.Empty(vm.VisibleConnections);
            vm.SelectedGroup = vm.FindFolder("生产"); dialogs.Prompts.Enqueue("线上");
            await vm.RenameFolderCommand.ExecuteAsync(null);
            Assert.Null(vm.FindFolder("生产")); Assert.NotNull(vm.FindFolder("线上/华东"));
            Assert.Empty(vm.VisibleConnections); Assert.True(vm.NeedsLeafFolder);
            await vm.DeleteFolderCommand.ExecuteAsync(null);
            Assert.Contains(dialogs.Errors, error => error.Contains("先移走"));
            vm.SelectedGroup = vm.FindFolder("线上/华东");
            Assert.Equal("线上/华东", Assert.Single(vm.VisibleConnections).Group);
            vm.SelectedGroup = vm.FindFolder("空目录/子目录"); await vm.DeleteFolderCommand.ExecuteAsync(null);
            Assert.Null(vm.FindFolder("空目录/子目录")); Assert.NotNull(vm.FindFolder("空目录"));
            var restored = Model(root, new JsonConnectionRepository(new(root)), dialogs); await restored.InitializeAsync();
            Assert.NotNull(restored.FindFolder("空目录")); Assert.NotNull(restored.FindFolder("线上/华东"));
            Assert.Empty(restored.VisibleConnections); Assert.True(restored.NeedsLeafFolder);
            restored.SelectedGroup = restored.FindFolder("线上/华东");
            Assert.Equal(profile.Id, Assert.Single(restored.VisibleConnections).Id);
            restored.Search = "missing"; Assert.True(restored.HasNoResults);
            await restored.ShutdownAsync();
        }
        finally { await vm.ShutdownAsync(); Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task FolderButtonOpensManagerEachClickCreatesTabAndMonitorFollowsSelectionWithoutRecreatingTerminal()
    {
        var fixturePath = Environment.GetEnvironmentVariable("SHELLX_TEST_FIXTURE");
        if (!File.Exists(fixturePath)) Assert.Skip("需要本机 SSH 测试服务。");
        using var config = JsonDocument.Parse(await File.ReadAllTextAsync(fixturePath));
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-manager-ui-" + Guid.NewGuid().ToString("N"))).FullName;
        var paths = new AppPaths(root); var store = new JsonConnectionRepository(paths);
        var profile = new ConnectionProfile { Name = "生产服务器", Group = "生产/华东", Host = "127.0.0.1", Port = config.RootElement.GetProperty("Port").GetInt32(), Username = "shellx", RememberSecret = true };
        var different = profile with { Id = Guid.NewGuid(), Name = "另一地址", Host = "localhost", Group = "测试" };
        await store.SaveCatalogAsync(new([profile, different], ["空文件夹"]));
        var credentials = new WindowsCredentialStore(paths);
        await credentials.SaveAsync(profile.Id, new("fixture-password")); await credentials.SaveAsync(different.Id, new("fixture-password"));
        var state = new AppLockService(new LockPasswordHasher());
        using var coordinator = new AppLockCoordinator(state);
        var dialogs = new DialogService(new TextCodec(), state, coordinator);
        var vm = Model(root, store, dialogs, state);
        var window = new MainWindow { DataContext = vm }; dialogs.Owner = window; coordinator.Protect(window); window.Show();
        try
        {
            await vm.InitializeAsync(); await Task.Delay(60);
            Assert.Same(vm.ToggleConnectionsCommand, window.FindControl<Button>("OpenConnectionManager")!.Command);
            Assert.True(window.FindControl<Button>("OpenConnectionManager")!.Bounds.Width > 0);
            Click(window, window.FindControl<Button>("OpenConnectionManager")!);
            await Until(() => window.OwnedWindows.Any(w => w.Title == "连接管理器"));
            var manager = window.OwnedWindows.Single(w => w.Title == "连接管理器");
            Assert.Single(window.GetVisualDescendants().OfType<ServerMonitorView>());
            var folders = manager.GetVisualDescendants().OfType<TreeView>().Single();
            await Task.Delay(50);
            Assert.NotNull(folders); Assert.NotNull(vm.FindFolder("空文件夹"));
            vm.SelectedGroup = vm.FindFolder("生产/华东");
            Click(manager, manager.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NewConnectionFolder"));
            await Until(() => manager.OwnedWindows.Any());
            var prompt = manager.OwnedWindows.Single();
            Assert.Equal("新建连接文件夹", prompt.Title);
            prompt.GetVisualDescendants().OfType<TextBox>().Single().Text = "临时目录";
            Click(prompt, prompt.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "确定")));
            await Until(() => vm.FindFolder("生产/华东/临时目录") != null);
            await Task.Delay(40);
            Click(manager, manager.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NewManagedConnection"));
            await Until(() => manager.OwnedWindows.Any(w => w.Title == "新建 SSH 连接"));
            var editor = manager.OwnedWindows.Single(w => w.Title == "新建 SSH 连接");
            Assert.Equal("生产/华东/临时目录", editor.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "ConnectionFolderSelector").SelectedItem);
            editor.Close();
            var moving = vm.EditConnectionCommand.ExecuteAsync(profile);
            await Until(() => manager.OwnedWindows.Any(w => w.Title == "编辑 SSH 连接"));
            editor = manager.OwnedWindows.Single(w => w.Title == "编辑 SSH 连接");
            editor.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "ConnectionFolderSelector").SelectedItem = "空文件夹";
            editor.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "ConnectionAccentSelector").SelectedItem = ConnectionAccentChoice.Find("#BF6969");
            Assert.Equal("#BF6969", editor.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "ConnectionAccentHex").Text);
            await Task.Delay(40);
            Click(editor, editor.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "保存连接")));
            await moving;
            Assert.Equal("空文件夹", (await store.LoadAsync()).Single(p => p.Id == profile.Id).Group);
            Assert.Equal("#BF6969", (await store.LoadAsync()).Single(p => p.Id == profile.Id).AccentColor);
            vm.SelectedGroup = vm.FindFolder("空文件夹"); await Task.Delay(50);
            Capture(manager, "connection-manager.png");
            await Task.Delay(40); Pick(manager, profile);
            await AcceptHostAndConnect(window, vm, 1);
            var first = vm.SelectedTab!; var firstTerminal = window.GetVisualDescendants().OfType<TerminalControl>().Single();
            Assert.Equal("#BF6969", first.AccentColor);
            await Until(() => first.Monitor.Hostname == "fixture-linux");
            await first.Files.NavigateAsync("/folder");
            Click(window, window.FindControl<Button>("OpenConnectionManager")!);
            await Until(() => window.OwnedWindows.Any(w => w.Title!.Contains("连接管理器")));
            manager = window.OwnedWindows.Single(w => w.Title!.Contains("连接管理器")); await Task.Delay(40); Pick(manager, profile);
            await AcceptHostAndConnect(window, vm, 2);
            var second = vm.SelectedTab!;
            Assert.Same(first.Monitor, second.Monitor); Assert.NotSame(first.Session, second.Session);
            Assert.Same(second.Monitor, window.GetVisualDescendants().OfType<ServerMonitorView>().Single().DataContext);
            var header = window.GetVisualDescendants().OfType<ShellTabItemView>().Single(v => ReferenceEquals(v.DataContext, first));
            var headerPoint = header.TranslatePoint(new Point(30, header.Bounds.Height / 2), window)!.Value;
            window.MouseDown(headerPoint, MouseButton.Right); window.MouseUp(headerPoint, MouseButton.Right);
            await Until(() => header.ContextMenu!.IsOpen);
            var rename = header.ContextMenu!.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "重命名标签…"));
            Assert.Same(first.RenameCommand, rename.Command);
            header.ContextMenu.Close(); rename.Command!.Execute(null);
            await Until(() => window.OwnedWindows.Any(w => w.Title == "重命名标签"));
            var renamePrompt = window.OwnedWindows.Single(w => w.Title == "重命名标签");
            var renameInput = renamePrompt.GetVisualDescendants().OfType<TextBox>().Single();
            Assert.Equal(profile.Name, renameInput.Text);
            renameInput.Text = "  线上日志  "; await Task.Delay(40);
            Click(renamePrompt, renamePrompt.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "确定")));
            await Until(() => first.Name == "线上日志");
            Assert.Equal(profile.Name, second.Name);
            Assert.Equal(profile.Name, first.Profile.Name);
            Assert.Equal(profile.Name, (await store.LoadAsync()).Single(p => p.Id == profile.Id).Name);
            var cancelRename = first.RenameCommand.ExecuteAsync(null);
            await Until(() => window.OwnedWindows.Any(w => w.Title == "重命名标签"));
            renamePrompt = window.OwnedWindows.Single(w => w.Title == "重命名标签");
            Assert.Equal("线上日志", renamePrompt.GetVisualDescendants().OfType<TextBox>().Single().Text);
            renamePrompt.Close(); await cancelRename; Assert.Equal("线上日志", first.Name);
            Click(window, window.FindControl<Button>("OpenConnectionManager")!);
            await Until(() => window.OwnedWindows.Any(w => w.Title == "连接管理器"));
            manager = window.OwnedWindows.Single(w => w.Title == "连接管理器");
            vm.SelectedGroup = vm.FindFolder("测试"); await Task.Delay(40); Pick(manager, different);
            await AcceptHostAndConnect(window, vm, 3);
            var third = vm.SelectedTab!; Assert.NotSame(first.Monitor, third.Monitor);
            Assert.Same(third.Monitor, window.GetVisualDescendants().OfType<ServerMonitorView>().Single().DataContext);
            vm.SelectedTab = first; await Task.Delay(40);
            Assert.Same(first.Monitor, window.GetVisualDescendants().OfType<ServerMonitorView>().Single().DataContext);
            Assert.Equal("/folder", first.Files.Path); Assert.Equal("/", third.Files.Path);
            Assert.Contains(firstTerminal, window.GetVisualDescendants().OfType<TerminalControl>());
            Assert.True(firstTerminal.IsLive);
            Assert.True(window.GetVisualDescendants().OfType<WorkspaceView>().Single(v => ReferenceEquals(v.DataContext, first)).IsVisible);
            Assert.False(window.GetVisualDescendants().OfType<WorkspaceView>().Single(v => ReferenceEquals(v.DataContext, third)).IsVisible);
            var frame = window.GetVisualDescendants().OfType<ConnectionAccentBorder>().Single(c => c.Name == "TerminalAccentFrame" && ReferenceEquals(c.DataContext, first));
            Assert.Equal(new Thickness(2), frame.BorderThickness);
            foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light, AppTheme.Dark })
            {
                ThemeService.Apply(theme); await Task.Delay(50);
                Assert.Same(firstTerminal, window.GetVisualDescendants().OfType<WorkspaceView>().Single(v => ReferenceEquals(v.DataContext, first)).FindControl<TerminalHost>("Terminal")!.Child);
                foreach (var control in window.GetVisualDescendants().OfType<Control>()) ToolTip.SetIsOpen(control, false);
                Capture(window, theme == AppTheme.Light ? "connection-color-light.png" : "connection-color-dark.png");
            }
            var recoloring = vm.EditConnectionCommand.ExecuteAsync((await store.LoadAsync()).Single(p => p.Id == profile.Id));
            await Until(() => window.OwnedWindows.Any(w => w.Title == "编辑 SSH 连接"));
            editor = window.OwnedWindows.Single(w => w.Title == "编辑 SSH 连接");
            editor.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "ConnectionAccentHex").Text = "#6b96b8";
            await Task.Delay(40);
            Click(editor, editor.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "保存连接")));
            await recoloring;
            Assert.Equal("#6B96B8", first.AccentColor); Assert.Equal(first.AccentColor, second.AccentColor);
            Assert.Equal("#6B96B8", frame.AccentColor); Assert.True(firstTerminal.IsLive);
            Assert.Equal("线上日志", first.Name); Assert.Equal(profile.Name, second.Name);
            await first.ReconnectCommand.ExecuteAsync(null);
            Assert.True(first.IsConnected); Assert.Equal("线上日志", first.Name);
            await first.CloseCommand.ExecuteAsync(null);
            Assert.Same(second, vm.SelectedTab);
            Assert.Same(second.Monitor, window.GetVisualDescendants().OfType<ServerMonitorView>().Single().DataContext);
            Assert.True(window.GetVisualDescendants().OfType<WorkspaceView>().Single(v => ReferenceEquals(v.DataContext, second)).IsVisible);
            await second.Monitor.RefreshCommand.ExecuteAsync(null);
            Assert.True(second.Monitor.IsMonitoring); Assert.Equal("fixture-linux", second.Monitor.Hostname);
            Assert.False(second.Monitor.Status.StartsWith("采样失败"));
            await Task.Delay(60);
            foreach (var control in window.GetVisualDescendants().OfType<Control>()) ToolTip.SetIsOpen(control, false);
            Capture(window, "shared-monitor-tabs.png");
            await vm.ConnectCommand.ExecuteAsync((await store.LoadAsync()).Single(p => p.Id == profile.Id));
            Assert.Equal(profile.Name, vm.SelectedTab!.Name);
            Assert.Equal(profile.Name, (await store.LoadAsync()).Single(p => p.Id == profile.Id).Name);
        }
        finally
        {
            foreach (var owned in window.OwnedWindows.ToArray()) owned.Close();
            await vm.ShutdownAsync(); window.Close(); Directory.Delete(root, true);
        }
    }

    private static MainWindowViewModel Model(string root, IConnectionRepository store, IDialogService dialogs, AppLockService? state = null)
        => new(store, new WindowsCredentialStore(new(root)), new JsonHostKeyStore(new(root)), new JsonSettingsStore(new(root)), new SshSessionFactory(NullLoggerFactory.Instance), dialogs,
            NullLoggerFactory.Instance, new LocalFileSystem(root), state ?? new AppLockService(new LockPasswordHasher()), new JsonConfigurationExporter(new(root)), new JsonConfigurationImporter());
    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
    }
    private static void Pick(Window manager, ConnectionProfile profile)
        => Click(manager, manager.GetVisualDescendants().OfType<Button>().Single(b => b.CommandParameter is ConnectionProfile p && p.Id == profile.Id));
    private static async Task AcceptHostAndConnect(Window window, MainWindowViewModel vm, int count)
    {
        await Until(() => vm.Tabs.Count == count);
        await Until(() => vm.SelectedTab!.IsConnected || window.OwnedWindows.Any(w => w.Title!.StartsWith("首次连接")));
        if (window.OwnedWindows.FirstOrDefault(w => w.Title!.StartsWith("首次连接")) is { } trust)
            Click(trust, trust.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "确认")));
        await Until(() => vm.SelectedTab!.IsConnected && !vm.SelectedTab.IsConnecting);
    }
    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("SHELLX_PREVIEW_PATH") is not { Length: > 0 } preview) return;
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height), new Vector(96, 96));
        bitmap.Render(window); bitmap.Save(Path.Combine(Path.GetDirectoryName(preview)!, name), PngBitmapEncoderOptions.Default);
    }
    private static async Task Until(Func<bool> condition)
    { for (var i = 0; i < 400 && !condition(); i++) await Task.Delay(20); Assert.True(condition()); }
    private sealed class Dialogs : IDialogService
    {
        public string? ExportPath { get; set; }
        public string? ConfigurationImportPath { get; set; }
        public List<string> XshellPaths { get; set; } = [];
        public List<string> Information { get; } = [];
        public Queue<string> Prompts { get; } = [];
        public List<string> Errors { get; } = [];
        public Task<bool> ConfirmAsync(string title, string message, bool danger = false) => Task.FromResult(true);
        public Task ShowErrorAsync(string message) { Errors.Add(message); return Task.CompletedTask; }
        public Task<ConnectionEditResult?> EditConnectionAsync(ConnectionProfile? profile, ConnectionSecret? secret) => Task.FromResult<ConnectionEditResult?>(null);
        public Task<string?> PromptAsync(string title, string label, string value = "", bool secret = false) => Task.FromResult<string?>(Prompts.Dequeue());
        public Task<IReadOnlyList<string>> PickUploadAsync() => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> PickDownloadAsync(string name) => Task.FromResult(ExportPath);
        public Task<IReadOnlyList<string>> PickXshellSessionsAsync() => Task.FromResult<IReadOnlyList<string>>(XshellPaths);
        public Task<string?> PickConfigurationImportAsync() => Task.FromResult(ConfigurationImportPath);
        public Task ShowInformationAsync(string title, string message) { Information.Add(message); return Task.CompletedTask; }
        public Task EditFileAsync(TextDocument document, Func<TextDocument, string, Task<TextDocument>> save) => Task.CompletedTask;
        public Task<int?> PermissionsAsync(RemoteEntry entry) => Task.FromResult<int?>(null);
        public Task<TunnelDefinition?> EditTunnelAsync() => Task.FromResult<TunnelDefinition?>(null);
        public Task<AppSettings?> SettingsAsync(AppSettings settings) => Task.FromResult<AppSettings?>(null);
    }
}
