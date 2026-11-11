using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using ShellX.Core.Connections;
using ShellX.Core.Settings;
using ShellX.Core.Tunnels;
using ShellX.Desktop.Features.Tunnels;
using ShellX.Desktop.Features.Workspace;
using ShellX.Desktop.Services;
using ShellX.Infrastructure.Files;
using ShellX.Infrastructure.Persistence;
using ShellX.Infrastructure.Ssh;

namespace ShellX.Desktop.Tests;

public sealed class TunnelEditingTests
{
    [AvaloniaFact(Timeout = 30000)]
    public async Task LiveRuleEditorUpdatesBothTabsAndPersistenceFailureLeavesExistingListenerUntouched()
    {
        var fixturePath = Environment.GetEnvironmentVariable("SHELLX_TEST_FIXTURE");
        if (!File.Exists(fixturePath)) Assert.Skip("需要本机 SSH 测试服务。");
        using var fixture = JsonDocument.Parse(await File.ReadAllTextAsync(fixturePath));
        using var target = new TcpListener(IPAddress.Loopback, 0); target.Start();
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-tunnel-edit-" + Guid.NewGuid().ToString("N"))).FullName;
        var store = new JsonConnectionRepository(new(root));
        var rule = new TunnelDefinition { Name = "原规则", BindPort = FreePort(), TargetPort = ((IPEndPoint)target.LocalEndpoint).Port };
        var profile = new ConnectionProfile { Host = "127.0.0.1", Port = fixture.RootElement.GetProperty("Port").GetInt32(), Username = "shellx", Tunnels = [rule] };
        await store.SaveAsync([profile]);
        var window = new Window { Width = 1200, Height = 750 };
        var dialogs = new DialogService(new TextCodec()) { Owner = window }; var factory = new SshSessionFactory(NullLoggerFactory.Instance);
        WorkspaceTabViewModel? first = null, second = null; var failSave = false;
        async Task Save(ConnectionProfile value)
        {
            if (failSave) throw new IOException("模拟配置写入失败");
            await store.SaveAsync([value]); first?.UpdateProfile(value); second?.UpdateProfile(value);
        }
        WorkspaceTabViewModel Tab() => new(profile, factory, dialogs, NullLoggerFactory.Instance, () => Task.FromResult<ConnectionSecret?>(new("fixture-password")),
            _ => Task.FromResult(true), Save, _ => Task.CompletedTask, new AppSettings(), new LocalFileSystem(root));
        first = Tab(); second = Tab();
        var view = new WorkspaceView { DataContext = first }; window.Content = view; window.Show();
        try
        {
            await first.ConnectWithSecretAsync(new("fixture-password")); await second.ConnectWithSecretAsync(new("fixture-password"));
            // 隧道规则已拆到独立窗口，编辑入口在隧道窗口里。
            view.FindControl<Button>("TunnelButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => window.OwnedWindows.Any(w => w.Title?.StartsWith("端口转发") == true));
            var tunnelWindow = window.OwnedWindows.Single(w => w.Title?.StartsWith("端口转发") == true);
            await Task.Delay(60);
            Click(tunnelWindow, tunnelWindow.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "EditTunnelRule"));
            await Until(() => tunnelWindow.OwnedWindows.Any(w => w.Title == "编辑端口转发"));
            var editor = tunnelWindow.OwnedWindows.Single(); var vm = (TunnelEditorViewModel)editor.DataContext!;
            Assert.Equal(rule, vm.Build());
            var port = FreePort(); vm.Name = "修改后的规则"; vm.BindPort = port.ToString();
            await Task.Delay(40); Click(editor, editor.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "保存规则")));
            await Until(() => first.Tunnels[0].Definition.BindPort == port && first.Tunnels[0].State == TunnelState.Connected);
            Assert.Equal(first.Tunnels[0].Definition, second.Tunnels[0].Definition);
            await Until(() => second.Tunnels[0].State == TunnelState.Connected);
            Assert.Equal(rule.Id, first.Tunnels[0].Definition.Id);
            Assert.Equal(port, Assert.Single((await store.LoadAsync())[0].Tunnels).BindPort);
            using (var freed = new TcpListener(IPAddress.Loopback, rule.BindPort)) freed.Start();
            var cancelled = first.Tunnels[0].EditCommand.ExecuteAsync(null); await Until(() => tunnelWindow.OwnedWindows.Any());
            editor = tunnelWindow.OwnedWindows.Single(); ((TunnelEditorViewModel)editor.DataContext!).Name = "不能保存"; editor.Close(); await cancelled;
            Assert.Equal("修改后的规则", first.Tunnels[0].Name);
            await first.Tunnels[0].ToggleCommand.ExecuteAsync(null);
            var editing = second.Tunnels[0].EditCommand.ExecuteAsync(null); await Until(() => tunnelWindow.OwnedWindows.Any());
            editor = tunnelWindow.OwnedWindows.Single(); vm = (TunnelEditorViewModel)editor.DataContext!;
            vm.Name = "停止中编辑"; await Task.Delay(40); Click(editor, editor.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "保存规则"))); await editing;
            Assert.Equal(TunnelState.Stopped, second.Tunnels[0].State);
            await second.Tunnels[0].ToggleCommand.ExecuteAsync(null); await Until(() => first.Tunnels[0].State == TunnelState.Connected);
            failSave = true; editing = first.Tunnels[0].EditCommand.ExecuteAsync(null); await Until(() => tunnelWindow.OwnedWindows.Any());
            editor = tunnelWindow.OwnedWindows.Single(); vm = (TunnelEditorViewModel)editor.DataContext!; vm.BindPort = FreePort().ToString();
            await Task.Delay(40); Click(editor, editor.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "保存规则")));
            await Until(() => tunnelWindow.OwnedWindows.Any(w => w.Title == "操作失败"));
            var error = tunnelWindow.OwnedWindows.Single(); Assert.Contains(error.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("模拟配置写入失败") == true);
            Assert.Equal(port, first.Tunnels[0].Definition.BindPort); Assert.True(first.Tunnels[0].Running);
            error.Close(); await editing;
        }
        finally { foreach (var child in window.OwnedWindows.ToArray()) child.Close(); await first.DisposeAsync(); await second.DisposeAsync(); window.Close(); Directory.Delete(root, true); }
    }
    private static int FreePort() { using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); return ((IPEndPoint)listener.LocalEndpoint).Port; }
    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
    }
    private static async Task Until(Func<bool> condition)
    { for (var i = 0; i < 500 && !condition(); i++) await Task.Delay(20); Assert.True(condition()); }
}
