using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Interactivity;
using Iciclecreek.Terminal;
using Microsoft.Extensions.Logging.Abstractions;
using ShellX.Core.Connections;
using ShellX.Core.Files;
using ShellX.Core.Monitoring;
using ShellX.Core.Security;
using ShellX.Core.Settings;
using ShellX.Core.Tunnels;
using ShellX.Desktop;
using ShellX.Desktop.Features.FileBrowser;
using ShellX.Desktop.Features.Shell;
using ShellX.Desktop.Features.Monitoring;
using ShellX.Desktop.Features.Workspace;
using ShellX.Desktop.Services;
using ShellX.Infrastructure.Ssh;
using ShellX.Infrastructure.Files;
using ShellX.Desktop.Features.Security;
using ShellX.Infrastructure.Security;
using ShellX.Infrastructure.Persistence;

[assembly: AvaloniaTestApplication(typeof(ShellX.Desktop.Tests.TestApplication))]

namespace ShellX.Desktop.Tests;

public static class TestApplication
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var render = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SHELLX_PREVIEW_PATH"));
        var builder = AppBuilder.Configure<App>();
        if (render) builder = builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !render });
    }
}

public sealed class WorkspaceTests
{
    [AvaloniaFact]
    public async Task RightClickEditsTheClickedLocalAndRemoteFile()
    {
        var fixturePath = Environment.GetEnvironmentVariable("SHELLX_TEST_FIXTURE");
        if (!File.Exists(fixturePath)) Assert.Skip("需要本机 SSH 测试服务。");
        using var config = JsonDocument.Parse(await File.ReadAllTextAsync(fixturePath));
        var localRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-context-" + Guid.NewGuid().ToString("N"))).FullName;
        var remotePath = "/context-" + Guid.NewGuid().ToString("N");
        var profile = new ConnectionProfile { Host = "127.0.0.1", Port = config.RootElement.GetProperty("Port").GetInt32(), Username = "shellx" };
        await using var session = await new SshSessionFactory(NullLoggerFactory.Instance).ConnectAsync(profile, new("fixture-password"), _ => Task.FromResult(true), CancellationToken.None);
        await session.Files!.CreateAsync(remotePath, true);
        foreach (var name in new[] { "first.txt", "target.txt" })
        { await File.WriteAllTextAsync(Path.Combine(localRoot, name), "original"); await session.Files.CreateAsync(remotePath + "/" + name, false); }
        var dialogs = new Dialogs { EditedContent = "remote edited\n" };
        using var browser = new FileBrowserViewModel(dialogs, NullLogger<FileBrowserViewModel>.Instance, new LocalFileSystem(localRoot));
        browser.Attach(session.Files, CancellationToken.None);
        var view = new FileBrowserView { DataContext = browser };
        var window = new Window { Content = view, Width = 1280, Height = 620 }; window.Show();
        try
        {
            await browser.NavigateAsync(remotePath); await Until(() => browser.Local.Entries.Count == 2);
            var remoteGrid = view.FindControl<RemoteFileBrowserView>("RemotePane")!.FindControl<DataGrid>("FileGrid")!;
            browser.SelectedEntry = browser.Entries.Single(e => e.Name == "first.txt");
            await Until(() => remoteGrid.GetVisualDescendants().OfType<DataGridRow>().Count() >= 2);
            var row = remoteGrid.GetVisualDescendants().OfType<DataGridRow>().Single(r => r.DataContext is RemoteEntry { Name: "target.txt" });
            var point = row.TranslatePoint(new Point(32, row.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Right); window.MouseUp(point, MouseButton.Right); await Task.Delay(40);
            Assert.Equal("target.txt", browser.SelectedEntry?.Name);
            var menu = remoteGrid.ContextMenu!.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "编辑文本"));
            Assert.Same(browser.EditTextCommand, menu.Command); Assert.True(menu.IsEnabled);
            remoteGrid.ContextMenu.Close(); await browser.EditTextCommand.ExecuteAsync(null);
            Assert.Equal("remote edited\n", (await session.Files.ReadTextAsync(remotePath + "/target.txt")).Content);
            Assert.Equal("", (await session.Files.ReadTextAsync(remotePath + "/first.txt")).Content);
            dialogs.EditedContent = "local edited\n";
            var localGrid = view.FindControl<LocalFileBrowserView>("LocalPane")!.FindControl<DataGrid>("LocalGrid")!;
            browser.Local.SelectedEntry = browser.Local.Entries.Single(e => e.Name == "first.txt");
            row = localGrid.GetVisualDescendants().OfType<DataGridRow>().Single(r => r.DataContext is LocalEntry { Name: "target.txt" });
            point = row.TranslatePoint(new Point(32, row.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Right); window.MouseUp(point, MouseButton.Right); await Task.Delay(40);
            Assert.Equal("target.txt", browser.Local.SelectedEntry?.Name);
            menu = localGrid.ContextMenu!.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "编辑文本"));
            Assert.Same(browser.Local.EditTextCommand, menu.Command); Assert.True(menu.IsEnabled);
            menu = localGrid.ContextMenu!.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "删除    Delete"));
            Assert.Same(browser.Local.DeleteCommand, menu.Command);
            localGrid.ContextMenu.Close(); await browser.Local.EditTextCommand.ExecuteAsync(null);
            Assert.Equal("local edited\n", await File.ReadAllTextAsync(Path.Combine(localRoot, "target.txt")));
            Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(localRoot, "first.txt")));
            localGrid.SelectedItems.Add(browser.Local.Entries.Single(e => e.Name == "first.txt"));
            localGrid.SelectedItems.Add(browser.Local.Entries.Single(e => e.Name == "target.txt"));
            Assert.False(browser.Local.EditTextCommand.CanExecute(null));
        }
        finally { window.Close(); await session.Files.DeleteAsync(remotePath); Directory.Delete(localRoot, true); }
    }

    [AvaloniaFact(Timeout = 20000)]
    public async Task BuiltInEditorRendersBoundedTextAndKeepsUiResponsive()
    {
        var owner = new Window(); owner.Show();
        var content = string.Concat(Enumerable.Repeat("server_name example.local; # configuration line\r\n", 5000));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var dialog = new DialogService(new TextCodec()) { Owner = owner };
        var editing = dialog.EditFileAsync(new("/etc/nginx/nginx.conf", content, System.Text.Encoding.UTF8.GetByteCount(content), DateTime.UtcNow), (original, _) => Task.FromResult(original));
        Window? editor = null;
        try
        {
            await Until(() => owner.OwnedWindows.Any()); editor = owner.OwnedWindows.Single();
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { });
            Assert.True(editor.IsVisible);
            if (Environment.GetEnvironmentVariable("SHELLX_PREVIEW_PATH") is { Length: > 0 } preview)
            {
                using var bitmap = new RenderTargetBitmap(new PixelSize(900, 650), new Vector(96, 96));
                bitmap.Render(editor); bitmap.Save(Path.Combine(Path.GetDirectoryName(preview)!, "text-editor-preview.png"), PngBitmapEncoderOptions.Default);
                await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(preview)!, "text-editor-render.json"), JsonSerializer.Serialize(new { Characters = content.Length, Lines = 5000, OpenAndRenderMilliseconds = watch.ElapsedMilliseconds }));
            }
        }
        finally { editor?.Close(); await editing; owner.Close(); }
    }
    [AvaloniaFact]
    public async Task ProcessSortUsesNumericValuesAcrossAllProcessesAndSurvivesRefresh()
    {
        var saved = new AppSettings();
        var monitor = new ServerMonitorViewModel("fixture", new Dialogs(), NullLogger.Instance, _ => Task.CompletedTask,
            (column, descending) => { saved = saved with { ProcessSortColumn = column, ProcessSortDescending = descending }; return Task.CompletedTask; });
        monitor.Start(new ProcessSource(), CancellationToken.None);
        try
        {
            Assert.Equal(8, monitor.Processes.Count); Assert.Equal(10, monitor.Processes[0].Pid);
            await monitor.SortMemoryCommand.ExecuteAsync(null);
            Assert.Equal(1, monitor.Processes[0].Pid); Assert.Equal("内存 ↓", monitor.MemoryHeader);
            await monitor.SortMemoryCommand.ExecuteAsync(null);
            Assert.Equal(10, monitor.Processes[0].Pid); Assert.Equal("内存 ↑", monitor.MemoryHeader);
            await monitor.RefreshCommand.ExecuteAsync(null);
            Assert.Equal(10, monitor.Processes[0].Pid);
            Assert.Equal(ProcessSortColumn.Memory, saved.ProcessSortColumn); Assert.False(saved.ProcessSortDescending);
            await monitor.SortCpuCommand.ExecuteAsync(null); Assert.Equal("CPU ↓", monitor.CpuHeader);
            await monitor.SortCpuCommand.ExecuteAsync(null); Assert.Equal(1, monitor.Processes[0].Pid);
        }
        finally { await monitor.StopAsync(); }
    }
    [AvaloniaFact]
    public async Task ProcessListShowsMemoryPercentAndAbsoluteUsage()
    {
        var monitor = new ServerMonitorViewModel("fixture", new Dialogs(), NullLogger.Instance, _ => Task.CompletedTask);
        monitor.Start(new ProcessSource(), CancellationToken.None);
        var view = new ServerMonitorView { DataContext = monitor };
        var window = new Window { Content = view, Width = 260, Height = 760 };
        window.Show();
        try
        {
            await Until(() => monitor.Processes.Count > 0);
            var row = monitor.Processes[0];                       // 默认按 CPU 降序，首行是 pid 10
            Assert.Equal(10, row.Pid);
            Assert.Equal("0.1%", row.MemoryPercent);
            Assert.Equal("1 KiB", row.Memory);
            var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains(row.MemoryPercent, texts);   // 内存占比
            Assert.Contains(row.Memory, texts);           // 绝对占用
        }
        finally { window.Close(); await monitor.StopAsync(); }
    }
    private sealed class ProcessSource : IServerMonitor
    {
        public Task<ServerSnapshot> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(new ServerSnapshot(
            DateTimeOffset.UtcNow, "fixture", "Linux", "test", "127.0.0.1", TimeSpan.Zero, "0", new(100, 50),
            1024 * 1024, 512 * 1024, 0, 0, [], Enumerable.Range(1, 10).Select(i => new ServerProcess(i, i * 11, (11 - i) * 1024, "process" + i)).ToArray(), [], 1));
    }
    [AvaloniaFact]
    public async Task DualPaneTransfersUseVisibleDirectoriesAndRespectOverwrite()
    {
        var fixturePath = Environment.GetEnvironmentVariable("SHELLX_TEST_FIXTURE");
        if (!File.Exists(fixturePath)) Assert.Skip("需要本机 SSH 测试服务。");
        using var config = JsonDocument.Parse(await File.ReadAllTextAsync(fixturePath));
        var localRoot = Path.Combine(Path.GetTempPath(), "shellx-dual-" + Guid.NewGuid().ToString("N"));
        var downloadDirectory = Directory.CreateDirectory(Path.Combine(localRoot, "downloads")).FullName;
        var name = "传输测试-" + Guid.NewGuid().ToString("N")[..6] + ".txt";
        var source = Path.Combine(localRoot, name);
        await File.WriteAllTextAsync(source, "双栏传输内容\n");
        var profile = new ConnectionProfile { Name = "本地 / 远程双栏测试", Host = "127.0.0.1", Port = config.RootElement.GetProperty("Port").GetInt32(), Username = "shellx", RememberSecret = true };
        var dialogs = new Dialogs();
        var vm = new MainWindowViewModel(new Connections(profile), new Credentials(), new Hosts(), new Settings(), new SshSessionFactory(NullLoggerFactory.Instance), dialogs, NullLoggerFactory.Instance, new LocalFileSystem(localRoot), new AppLockService(new LockPasswordHasher()), new JsonConfigurationExporter(new(localRoot)));
        var window = new MainWindow { DataContext = vm, Width = 1380, Height = 900 };
        window.Show();
        try
        {
            await vm.InitializeAsync(); await vm.ConnectCommand.ExecuteAsync(profile);
            var selected = vm.SelectedTab!; var files = selected.Files;
            await Until(() => window.GetVisualDescendants().OfType<TerminalControl>().Any());
            var terminal = Assert.Single(window.GetVisualDescendants().OfType<TerminalControl>());
            await Until(() => terminal.IsLive);
            await Until(() => Screen(terminal).Contains("本地测试服务器"));
            selected.Commands.Input = "pwd"; await selected.Commands.SendCommand.ExecuteAsync(null);
            await Until(() => Screen(terminal).Contains("received: pwd"));
            await files.Local.InitializeAsync(); await Until(() => files.Local.Path == localRoot);
            await files.NavigateAsync("/folder");
            files.Local.SelectedEntry = files.Local.Entries.Single(e => e.Name == name);
            await files.Local.UploadCommand.ExecuteAsync(null); await files.DrainTransfersAsync();
            Assert.Equal("已完成", Assert.Single(files.Transfers).Status);
            var uploaded = files.Entries.Single(e => e.Name == name);
            Assert.Equal("/folder/" + name, uploaded.FullPath);
            await files.Local.NavigateAsync(downloadDirectory); files.SelectedEntry = uploaded;
            Assert.Null(files.Local.SelectedEntry); Assert.Empty(files.Local.SelectedEntries);
            await files.DownloadCommand.ExecuteAsync(null); await files.DrainTransfersAsync();
            var target = Path.Combine(downloadDirectory, name);
            Assert.Equal(await File.ReadAllTextAsync(source), await File.ReadAllTextAsync(target));
            Assert.Contains(files.Local.Entries, e => e.FullPath == target);
            Assert.Equal(2, files.Transfers.Count);
            await File.WriteAllTextAsync(target, "保留现有文件"); dialogs.Confirmation = false;
            await files.DownloadCommand.ExecuteAsync(null); await files.DrainTransfersAsync();
            Assert.Equal("保留现有文件", await File.ReadAllTextAsync(target)); Assert.Equal(2, files.Transfers.Count);
            Assert.Equal("覆盖本地文件", dialogs.LastConfirmationTitle);
            await Until(() => selected.Monitor.Processes.Count > 0);
            Assert.StartsWith("25%", selected.Monitor.MemoryText);
            var process = selected.Monitor.Processes.Single(p => p.Pid == 123);
            Assert.Equal("0.1%", process.MemoryPercent); Assert.Contains("10 MiB", process.Description);
            var filesWorkspace = window.GetVisualDescendants().OfType<WorkspaceView>().Single(view => ReferenceEquals(view.DataContext, selected));
            filesWorkspace.FindControl<Button>("OpenFileManagerButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(80);
            var fileWindow = vm.FileManagerWindow!;
            Assert.Null(fileWindow.Owner);
            var browser = fileWindow.GetVisualDescendants().OfType<FileBrowserView>().Single();
            foreach (var width in new[] { 1050d, 1380d })
            {
                fileWindow.Width = width; await Task.Delay(80);
                var left = browser.FindControl<LocalFileBrowserView>("LocalPane")!;
                var right = browser.FindControl<RemoteFileBrowserView>("RemotePane")!;
                Assert.Same(files.Local, left.DataContext); Assert.Same(files, right.DataContext);
                var leftOrigin = left.TranslatePoint(default, browser)!.Value;
                var rightOrigin = right.TranslatePoint(default, browser)!.Value;
                Assert.True(left.Bounds.Width > 300 && right.Bounds.Width > 300);
                Assert.True(leftOrigin.X + left.Bounds.Width <= rightOrigin.X);
                Assert.True(rightOrigin.X + right.Bounds.Width <= browser.Bounds.Width + 1);
            }
            if (Environment.GetEnvironmentVariable("SHELLX_PREVIEW_PATH") is { Length: > 0 } preview)
            {
                files.ClearTransfersCommand.Execute(null);
                files.ToggleTransfersCommand.Execute(null);
                await files.Local.NavigateAsync(localRoot); await files.NavigateAsync("/");
                await Until(() => selected.Monitor.CpuText.EndsWith('%'));
                await Task.Delay(60);
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)fileWindow.ClientSize.Width, (int)fileWindow.ClientSize.Height), new Vector(96, 96));
                bitmap.Render(fileWindow); bitmap.Save(Path.Combine(Path.GetDirectoryName(preview)!, "dual-pane-layout-preview.png"), PngBitmapEncoderOptions.Default);
                foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
                {
                    ThemeService.Apply(theme); await Task.Delay(80);
                    Assert.Same(terminal, window.GetVisualDescendants().OfType<TerminalControl>().Single());
                    Assert.True(terminal.IsLive); Assert.Contains("received: pwd", Screen(terminal));
                    using var themed = new RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height), new Vector(96, 96));
                    themed.Render(window); themed.Save(Path.Combine(Path.GetDirectoryName(preview)!, theme == AppTheme.Light ? "workspace-light.png" : "workspace-dark.png"), PngBitmapEncoderOptions.Default);
                }
            }
            await selected.Session!.Files!.DeleteAsync(uploaded.FullPath);
        }
        finally
        {
            foreach (var owned in window.OwnedWindows.ToArray()) owned.Close();
            foreach (var owned in window.OwnedWindows.ToArray()) owned.Close();
            await vm.ShutdownAsync(); window.Close(); Directory.Delete(localRoot, true);
        }
    }

    [AvaloniaFact]
    public async Task DirectoryUploadPreservesTreeMergesWithConfirmationAndCanBeCanceled()
    {
        var fixturePath = Environment.GetEnvironmentVariable("SHELLX_TEST_FIXTURE");
        if (!File.Exists(fixturePath)) Assert.Skip("需要本机 SSH 测试服务。");
        using var config = JsonDocument.Parse(await File.ReadAllTextAsync(fixturePath));
        var localRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-recursive-" + Guid.NewGuid().ToString("N"))).FullName;
        var name = "中文文件夹-" + Guid.NewGuid().ToString("N")[..6];
        var folder = Directory.CreateDirectory(Path.Combine(localRoot, name)).FullName;
        Directory.CreateDirectory(Path.Combine(folder, "层一", "空目录"));
        var nested = Path.Combine(folder, "层一", "内容.txt");
        await File.WriteAllTextAsync(nested, "递归上传内容\n");
        await File.WriteAllTextAsync(Path.Combine(folder, ".hidden"), "hidden");
        await File.WriteAllTextAsync(Path.Combine(folder, "root.txt"), "root");
        var profile = new ConnectionProfile { Host = "127.0.0.1", Port = config.RootElement.GetProperty("Port").GetInt32(), Username = "shellx" };
        await using var session = await new SshSessionFactory(NullLoggerFactory.Instance).ConnectAsync(profile, new("fixture-password"), _ => Task.FromResult(true), CancellationToken.None);
        var dialogs = new Dialogs();
        using var browser = new FileBrowserViewModel(dialogs, NullLogger<FileBrowserViewModel>.Instance, new LocalFileSystem(localRoot));
        browser.Attach(session.Files, CancellationToken.None);
        var remote = "/folder/" + name;
        try
        {
            await browser.NavigateAsync("/folder"); await browser.Local.InitializeAsync();
            browser.Local.SelectedEntry = browser.Local.Entries.Single(e => e.Name == name);
            await browser.Local.UploadCommand.ExecuteAsync(null); await browser.DrainTransfersAsync();
            var transfer = Assert.Single(browser.Transfers);
            Assert.Equal("已完成", transfer.Status); Assert.Equal(100, transfer.Percent);
            Assert.Equal("递归上传内容\n", (await session.Files!.ReadTextAsync(remote + "/层一/内容.txt")).Content);
            Assert.Empty((await session.Files.ListAsync(remote + "/层一/空目录")).Entries);
            Assert.True(await session.Files.ExistsAsync(remote + "/.hidden"));
            await session.Files.CreateAsync(remote + "/keep.txt", false);
            await File.WriteAllTextAsync(nested, "本地修改\n"); dialogs.Confirmation = false;
            await browser.UploadPathsAsync([folder]); await browser.DrainTransfersAsync();
            Assert.Equal("已跳过", browser.Transfers.Last().Status);
            Assert.Equal("合并远程文件夹", dialogs.LastConfirmationTitle);
            Assert.Equal("递归上传内容\n", (await session.Files.ReadTextAsync(remote + "/层一/内容.txt")).Content);
            dialogs.Confirmation = true;
            await browser.UploadPathsAsync([folder]); await browser.DrainTransfersAsync();
            Assert.Equal("已完成", browser.Transfers.Last().Status);
            Assert.Equal("本地修改\n", (await session.Files.ReadTextAsync(remote + "/层一/内容.txt")).Content);
            Assert.True(await session.Files.ExistsAsync(remote + "/keep.txt"));
            var canceled = Directory.CreateDirectory(Path.Combine(localRoot, "canceled")).FullName;
            await browser.UploadPathsAsync([canceled]); browser.Transfers.Last().CancelCommand.Execute(null);
            await browser.DrainTransfersAsync(); Assert.Equal("已取消", browser.Transfers.Last().Status);
            Assert.False(await session.Files.ExistsAsync("/folder/canceled"));
            await session.Files.DeleteAsync(remote + "/层一"); await session.Files.CreateAsync(remote + "/层一", false);
            await browser.UploadPathsAsync([folder]); await browser.DrainTransfersAsync();
            Assert.Equal("失败", browser.Transfers.Last().Status);
            Assert.Contains(remote + "/层一", browser.Transfers.Last().Details);
            Assert.False((await session.Files.ListAsync(remote)).Entries.Single(e => e.Name == "层一").IsDirectory);
        }
        finally { await browser.DrainTransfersAsync(); await session.Files!.DeleteAsync(remote); Directory.Delete(localRoot, true); }
    }

    [AvaloniaFact]
    public void PermissionCheckboxesAndOctalStayInSync()
    {
        var vm = new PermissionsViewModel(Convert.ToInt32("4750", 8));
        Assert.True(vm.Bits.Single(b => b.Label == "Set UID").IsSet);
        vm.Bits.Single(b => b.Label == "其他 · 读").IsSet = true; Assert.Equal("4754", vm.Octal);
        vm.Octal = "0644"; Assert.False(vm.Bits.Single(b => b.Label == "Set UID").IsSet);
        Assert.True(vm.Bits.Single(b => b.Label == "所有者 · 写").IsSet);
        vm.Octal = "999"; Assert.Throws<ArgumentException>(() => vm.Build());
    }

    [AvaloniaFact]
    public async Task NativeTerminalRendersAnsiAndChineseAndSurvivesTabSwitch()
    {
        var path = Environment.GetEnvironmentVariable("SHELLX_TEST_FIXTURE");
        if (!File.Exists(path)) Assert.Skip("需要本机 SSH 测试服务。");
        var config = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        using var target = new TcpListener(IPAddress.Loopback, 0); target.Start();
        using var portReservation = new TcpListener(IPAddress.Loopback, 0); portReservation.Start();
        var tunnelPort = ((IPEndPoint)portReservation.LocalEndpoint).Port; portReservation.Stop();
        var firstRule = new TunnelDefinition { Name = "数据库隧道", BindPort = tunnelPort, TargetPort = ((IPEndPoint)target.LocalEndpoint).Port };
        var conflictRule = firstRule with { Id = Guid.NewGuid(), Name = "重复端口测试" };
        var profile = new ConnectionProfile { Name = "本机 SSH 测试", Host = "127.0.0.1", Port = config.RootElement.GetProperty("Port").GetInt32(), Username = "shellx", RememberSecret = true, Tunnels = [firstRule, conflictRule] };
        var localDirectory = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(path)!, "desktop-local")).FullName;
        await File.WriteAllTextAsync(Path.Combine(localDirectory, "本地文件.txt"), "ShellX 本机测试文件。\n");
        var settings = new Settings();
        var applicationLock = new AppLockService(new LockPasswordHasher());
        using var lockCoordinator = new AppLockCoordinator(applicationLock);
        var vm = new MainWindowViewModel(new Connections(profile), new Credentials(), new Hosts(), settings, new SshSessionFactory(NullLoggerFactory.Instance), new Dialogs(), NullLoggerFactory.Instance, new LocalFileSystem(localDirectory), applicationLock, new JsonConfigurationExporter(new(localDirectory)));
        var window = new MainWindow { DataContext = vm, Width = 1380, Height = 900 };
        lockCoordinator.Protect(window);
        window.Show();
        try
        {
            await vm.InitializeAsync(); await vm.ConnectCommand.ExecuteAsync(profile);
            await Until(() => window.GetVisualDescendants().OfType<TerminalControl>().Any());
            var first = Assert.Single(window.GetVisualDescendants().OfType<TerminalControl>());
            await Until(() => Screen(first).Contains("本地测试服务器"));
            Assert.DoesNotContain("[32m", Screen(first));
            Assert.True(first.IsLive); Assert.Equal("", first.Process);
            await first.SendInputAsync("中文输入\r"); await Until(() => Screen(first).Contains("received: 中文输入"));
            var selected = vm.SelectedTab!;
            selected.ApplySettings(settings.Value with { TerminalFontFamily = "Consolas", TerminalFontSize = 18 });
            Assert.Equal("Consolas", first.FontFamily.Name); Assert.Equal(18, first.FontSize);
            selected.ApplySettings(settings.Value);
            applicationLock.Configure(applicationLock.Settings with { LockPasswordHash = applicationLock.Passwords.Hash("test-live-lock") });
            first.Focus(); window.KeyPress(Key.L, RawInputModifiers.Control, PhysicalKey.None, null);
            Assert.True(applicationLock.IsLocked); Assert.False(first.IsEffectivelyVisible); Assert.True(first.IsLive);
            await first.SendInputAsync("background-locked\r"); await Until(() => Screen(first).Contains("received: background-locked"));
            applicationLock.Password = "test-live-lock"; await applicationLock.UnlockCommand.ExecuteAsync(null);
            Assert.False(applicationLock.IsLocked); Assert.True(first.IsEffectivelyVisible); Assert.True(first.IsLive);
            await Until(() => selected.Monitor.Hostname == "fixture-linux");

            await Until(() => selected.Tunnels[0].State == TunnelState.Connected && selected.Tunnels[1].State == TunnelState.Failed);
            Assert.Equal("#73C991", selected.Tunnels[0].StatusColor);
            Assert.Equal("#F48771", selected.Tunnels[1].StatusColor);
            Assert.Contains("数据库隧道", selected.Tunnels[1].Reason);
            Assert.Equal("#F48771", selected.TunnelStatusColor); Assert.Contains("重复端口测试", selected.TunnelTooltip);
            var workspace = window.GetVisualDescendants().OfType<WorkspaceView>().Single(view => ReferenceEquals(view.DataContext, selected));
            var terminalPoint = first.TranslatePoint(new Point(100, 50), window)!.Value;
            window.MouseWheel(terminalPoint, new Vector(0, 1));
            Assert.Equal(14, selected.FontSize);
            window.MouseWheel(terminalPoint, new Vector(0, 1), RawInputModifiers.Control);
            Assert.Equal(15, selected.FontSize); Assert.Equal(15, first.FontSize);
            window.MouseWheel(terminalPoint, new Vector(0, -1), RawInputModifiers.Control);
            Assert.Equal(14, selected.FontSize); Assert.Equal(14, first.FontSize);
            selected.FontSize = 27;
            window.MouseWheel(terminalPoint, new Vector(0, 1), RawInputModifiers.Control);
            window.MouseWheel(terminalPoint, new Vector(0, 1), RawInputModifiers.Control);
            Assert.Equal(28, first.FontSize);
            selected.FontSize = 11;
            window.MouseWheel(terminalPoint, new Vector(0, -1), RawInputModifiers.Control);
            window.MouseWheel(terminalPoint, new Vector(0, -1), RawInputModifiers.Control);
            Assert.Equal(10, first.FontSize);
            selected.FontSize = 15;
            workspace.FindControl<Button>("OpenFileManagerButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(80);
            var fileWindow = vm.FileManagerWindow!;
            Assert.Null(fileWindow.Owner);
            var localPane = fileWindow.GetVisualDescendants().OfType<LocalFileBrowserView>().Single();
            fileWindow.MouseWheel(localPane.TranslatePoint(new Point(100, 100), fileWindow)!.Value, new Vector(0, 1), RawInputModifiers.Control);
            Assert.Equal(15, first.FontSize);
            Assert.Null(workspace.FindControl<Border>("TunnelPanel"));            // 隧道已拆成独立窗口
            Assert.Equal(Color.Parse("#F48771"), Assert.IsAssignableFrom<ISolidColorBrush>(workspace.FindControl<Button>("TunnelButton")!.Foreground).Color);
            workspace.FindControl<Button>("TunnelButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(120);
            var tunnelWindow = window.OwnedWindows.Single(w => w.Title?.StartsWith("端口转发") == true);
            Assert.NotNull(tunnelWindow);
            Assert.Equal("已连接", selected.Status);
            selected.Commands.Input = "pwd"; await selected.Commands.SendCommand.ExecuteAsync(null);
            await Until(() => Screen(first).Contains("received: pwd")); Assert.Equal("pwd", selected.Commands.History[0].Text);
            await selected.Files.TreeRoots[0].ToggleCommand.ExecuteAsync(null);
            Assert.Contains(selected.Files.TreeRoots[0].Children, node => node.Name == "folder");
            await vm.ConnectCommand.ExecuteAsync(profile); Assert.Equal(2, vm.Tabs.Count);
            var secondTab = vm.SelectedTab!;
            Assert.Equal(14, secondTab.FontSize);

            await Until(() => secondTab.Tunnels[0].State == TunnelState.Connected);
            Assert.Equal("#73C991", secondTab.Tunnels[0].StatusColor);
            Assert.Equal(selected.Tunnels[1].Reason, secondTab.Tunnels[1].Reason);
            await selected.Monitor.SortMemoryCommand.ExecuteAsync(null);
            Assert.Equal(ProcessSortColumn.Memory, secondTab.Monitor.SortColumn);
            Assert.Equal(ProcessSortColumn.Memory, settings.Value.ProcessSortColumn);
            vm.SelectedTab = selected; await Task.Delay(50);
            Assert.Same(selected.Monitor.SortMemoryCommand, window.GetVisualDescendants().OfType<ServerMonitorView>().Single().FindControl<Button>("SortMemory")!.Command);
            Assert.Contains(first, window.GetVisualDescendants().OfType<TerminalControl>());
            Assert.Equal(15, selected.FontSize); Assert.Equal(15, first.FontSize);
            selected.DecreaseFontCommand.Execute(null); Assert.Equal(14, first.FontSize);
            selected.IncreaseFontCommand.Execute(null); Assert.Equal(15, first.FontSize);
            Assert.Contains("中文输入", Screen(first)); Assert.True(first.IsLive);
            Assert.Equal("fixture-linux", selected.Monitor.Hostname);
            if (Environment.GetEnvironmentVariable("SHELLX_PREVIEW_PATH") is { Length: > 0 } preview)
            {
                await Until(() => selected.Monitor.CpuText.EndsWith('%'));
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height), new Vector(96, 96));
                bitmap.Render(window); bitmap.Save(preview, PngBitmapEncoderOptions.Default);
            }
            window.Width = 1100; window.Height = 700; await Task.Delay(100);
            await first.SendInputAsync("after-resize\r"); await Until(() => Screen(first).Contains("after-resize"));
            await secondTab.Tunnels[0].ToggleCommand.ExecuteAsync(null);
            await Until(() => selected.Tunnels[0].State == TunnelState.Stopped);
            await vm.ConnectCommand.ExecuteAsync(profile); var thirdTab = vm.SelectedTab!;
            Assert.Equal(TunnelState.Stopped, thirdTab.Tunnels[0].State);
            Assert.Equal(ProcessSortColumn.Memory, thirdTab.Monitor.SortColumn);
            await thirdTab.CloseCommand.ExecuteAsync(null);
            await secondTab.Tunnels[0].ToggleCommand.ExecuteAsync(null);
            await Until(() => selected.Tunnels[0].State == TunnelState.Connected);
            await selected.CloseCommand.ExecuteAsync(null); Assert.Single(vm.Tabs);
            Assert.Equal(TunnelState.Connected, secondTab.Tunnels[0].State);
            await vm.Tabs[0].CloseCommand.ExecuteAsync(null); Assert.Empty(vm.Tabs);
        }
        finally
        {
            if (applicationLock.IsLocked) { applicationLock.Password = "test-live-lock"; await applicationLock.UnlockCommand.ExecuteAsync(null); }
            foreach (var owned in window.OwnedWindows.ToArray()) owned.Close();
            await vm.ShutdownAsync(); window.Close();
        }
    }
    private static string Screen(TerminalControl terminal)
    {
        var buffer = terminal.Terminal.Buffer; var text = new StringBuilder();
        for (var i = 0; i < buffer.Length; i++) text.Append(buffer.GetLine(i)?.TranslateToString(true));
        return text.ToString();
    }
    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!condition()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("等待终端状态超时。"); await Task.Delay(30); }
    }
    [AvaloniaFact]
    public async Task WorkspaceHeaderShowsShellXBrandNextToPanelTitle()
    {
        var profile = new ConnectionProfile { Name = "测试", Host = "127.0.0.1", Port = 22, Username = "root" };
        var root = Path.Combine(Path.GetTempPath(), "shellx-brand-ws-" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new Connections(profile), new Credentials(), new Hosts(), new Settings(),
            new SshSessionFactory(NullLoggerFactory.Instance), new Dialogs(), NullLoggerFactory.Instance,
            new LocalFileSystem(root), new AppLockService(new LockPasswordHasher()), new JsonConfigurationExporter(new(root)));
        var window = new MainWindow { DataContext = vm, Width = 1380, Height = 900 };
        window.Show();
        try
        {
            await Task.Delay(80);
            var brand = window.FindControl<TextBlock>("BrandName");
            Assert.NotNull(brand);
            Assert.Equal("ShellX", brand!.Text);
            // 品牌右侧显示版本号
            var version = window.FindControl<TextBlock>("BrandVersion");
            Assert.NotNull(version);
            Assert.Equal(vm.VersionText, version!.Text);
            Assert.StartsWith("v", version.Text);
            // 分区标题仍在品牌标识右侧
            Assert.Contains("服务器状态", window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            Capture(window, "workspace-header.png");
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task MonitorPaneCollapsesIntoAHandleAndRestoresItsWidth()
    {
        var localDirectory = Path.Combine(Path.GetTempPath(), "shellx-collapse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(localDirectory);
        var settings = new Settings();
        var profile = new ConnectionProfile { Name = "折叠测试", Host = "127.0.0.1", Port = 22, Username = "root" };
        var vm = new MainWindowViewModel(new Connections(profile), new Credentials(), new Hosts(), settings, new SshSessionFactory(NullLoggerFactory.Instance), new Dialogs(), NullLoggerFactory.Instance, new LocalFileSystem(localDirectory), new AppLockService(new LockPasswordHasher()), new JsonConfigurationExporter(new(localDirectory)));
        var window = new MainWindow { DataContext = vm };
        window.Show();
        try
        {
            await vm.InitializeAsync();
            var layout = window.FindControl<Grid>("RootLayout")!;
            Assert.Equal(220, layout.ColumnDefinitions[0].Width.Value);

            var collapse = window.FindControl<Button>("CollapseMonitorPane")!;
            Assert.True(collapse.IsEffectivelyVisible);
            Assert.Same(vm.ToggleMonitorPaneCommand, collapse.Command);       // 标题栏折叠入口已接上命令
            collapse.Command!.Execute(null);
            await Task.Delay(60);
            Assert.True(vm.IsMonitorPaneCollapsed);
            Assert.Equal(26, layout.ColumnDefinitions[0].Width.Value);          // 收起后只剩窄把手
            Assert.True(settings.Value.MonitorPaneCollapsed);                 // 折叠状态被记住
            Assert.False(collapse.IsEffectivelyVisible);

            var expand = window.FindControl<Button>("ExpandMonitorPane")!;
            Assert.True(expand.IsEffectivelyVisible);
            expand.Command!.Execute(null);
            await Task.Delay(60);
            Assert.False(vm.IsMonitorPaneCollapsed);
            Assert.Equal(220, layout.ColumnDefinitions[0].Width.Value);         // 展开后恢复原宽度
            Assert.False(settings.Value.MonitorPaneCollapsed);

            await vm.ResetLayoutCommand.ExecuteAsync(null);
            Assert.False(vm.IsMonitorPaneCollapsed);                            // 恢复默认布局会展开面板
        }
        finally { window.Close(); }
    }

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("SHELLX_PREVIEW_PATH") is not { Length: > 0 } preview) return;
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height), new Vector(96, 96));
        bitmap.Render(window); bitmap.Save(Path.Combine(Path.GetDirectoryName(preview)!, name), PngBitmapEncoderOptions.Default);
    }
    private sealed class Connections(ConnectionProfile profile) : IConnectionRepository
    {
        public Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ConnectionProfile>>([profile]);
        public Task SaveAsync(IReadOnlyList<ConnectionProfile> connections, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class Credentials : ICredentialStore
    {
        public Task<ConnectionSecret?> ReadAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<ConnectionSecret?>(new("fixture-password"));
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
        public string? EditedContent { get; set; }
        public bool Confirmation { get; set; } = true;
        public string? LastConfirmationTitle { get; private set; }
        public Task<bool> ConfirmAsync(string title, string message, bool danger = false) { LastConfirmationTitle = title; return Task.FromResult(Confirmation); }
        public Task ShowErrorAsync(string message) => throw new Exception(message);
        public Task<ConnectionEditResult?> EditConnectionAsync(ConnectionProfile? profile, ConnectionSecret? secret) => Task.FromResult<ConnectionEditResult?>(null);
        public Task<string?> PromptAsync(string title, string label, string value = "", bool secret = false) => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<string>> PickUploadAsync() => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> PickDownloadAsync(string name) => Task.FromResult<string?>(null);
        public async Task EditFileAsync(TextDocument document, Func<TextDocument, string, Task<TextDocument>> save)
        { if (EditedContent != null) await save(document, EditedContent); }
        public Task<int?> PermissionsAsync(RemoteEntry entry) => Task.FromResult<int?>(null);
        public Task<TunnelDefinition?> EditTunnelAsync() => Task.FromResult<TunnelDefinition?>(null);
        public Task<AppSettings?> SettingsAsync(AppSettings settings) => Task.FromResult<AppSettings?>(null);
    }
}
