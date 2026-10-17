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
            dialogs.Prompts.Enqueue("空目录"); await vm.NewFolderCommand.ExecuteAsync(null);
            Assert.Equal("空目录", vm.SelectedGroup!.Path);
            dialogs.Prompts.Enqueue("子目录"); await vm.NewFolderCommand.ExecuteAsync(null);
            Assert.Equal("空目录/子目录", vm.SelectedGroup!.Path); Assert.Empty(vm.VisibleConnections);
            vm.SelectedGroup = vm.FindFolder("生产"); dialogs.Prompts.Enqueue("线上");
            await vm.RenameFolderCommand.ExecuteAsync(null);
            Assert.Null(vm.FindFolder("生产")); Assert.NotNull(vm.FindFolder("线上/华东"));
            Assert.Equal("线上/华东", Assert.Single(vm.VisibleConnections).Group);
            await vm.DeleteFolderCommand.ExecuteAsync(null);
            Assert.Contains(dialogs.Errors, error => error.Contains("先移走"));
            vm.SelectedGroup = vm.FindFolder("空目录/子目录"); await vm.DeleteFolderCommand.ExecuteAsync(null);
            Assert.Null(vm.FindFolder("空目录/子目录")); Assert.NotNull(vm.FindFolder("空目录"));
            var restored = Model(root, new JsonConnectionRepository(new(root)), dialogs); await restored.InitializeAsync();
            Assert.NotNull(restored.FindFolder("空目录")); Assert.NotNull(restored.FindFolder("线上/华东"));
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
            vm.SelectedGroup = vm.FindFolder(""); await Task.Delay(50);
            Capture(manager, "connection-manager.png");
            await Task.Delay(40); Pick(manager, profile);
            await AcceptHostAndConnect(window, vm, 1);
            var first = vm.SelectedTab!; var firstTerminal = window.GetVisualDescendants().OfType<TerminalControl>().Single();
            Assert.Equal("#BF6969", first.AccentColor);
            await Until(() => first.Monitor.Hostname == "fixture-linux");
            await first.Files.NavigateAsync("/folder");
            Click(window, window.FindControl<Button>("OpenConnectionManager")!);
            await Until(() => window.OwnedWindows.Any(w => w.Title == "连接管理器"));
            manager = window.OwnedWindows.Single(w => w.Title == "连接管理器"); await Task.Delay(40); Pick(manager, profile);
            await AcceptHostAndConnect(window, vm, 2);
            var second = vm.SelectedTab!;
            Assert.Same(first.Monitor, second.Monitor); Assert.NotSame(first.Session, second.Session);
            Assert.Same(second.Monitor, window.GetVisualDescendants().OfType<ServerMonitorView>().Single().DataContext);
            var header = window.GetVisualDescendants().OfType<ConnectionAccentBorder>().Single(c => c.Name == "ConnectionTabBackground" && ReferenceEquals(c.DataContext, first));
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
            manager = window.OwnedWindows.Single(w => w.Title == "连接管理器"); await Task.Delay(40); Pick(manager, different);
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
            NullLoggerFactory.Instance, new LocalFileSystem(root), state ?? new AppLockService(new LockPasswordHasher()), new JsonConfigurationExporter(new(root)));
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
        public List<string> Information { get; } = [];
        public Queue<string> Prompts { get; } = [];
        public List<string> Errors { get; } = [];
        public Task<bool> ConfirmAsync(string title, string message, bool danger = false) => Task.FromResult(true);
        public Task ShowErrorAsync(string message) { Errors.Add(message); return Task.CompletedTask; }
        public Task<ConnectionEditResult?> EditConnectionAsync(ConnectionProfile? profile, ConnectionSecret? secret) => Task.FromResult<ConnectionEditResult?>(null);
        public Task<string?> PromptAsync(string title, string label, string value = "", bool secret = false) => Task.FromResult<string?>(Prompts.Dequeue());
        public Task<IReadOnlyList<string>> PickUploadAsync() => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> PickDownloadAsync(string name) => Task.FromResult(ExportPath);
        public Task ShowInformationAsync(string title, string message) { Information.Add(message); return Task.CompletedTask; }
        public Task EditFileAsync(TextDocument document, Func<TextDocument, string, Task<TextDocument>> save) => Task.CompletedTask;
        public Task<int?> PermissionsAsync(RemoteEntry entry) => Task.FromResult<int?>(null);
        public Task<TunnelDefinition?> EditTunnelAsync() => Task.FromResult<TunnelDefinition?>(null);
        public Task<AppSettings?> SettingsAsync(AppSettings settings) => Task.FromResult<AppSettings?>(null);
    }
}
