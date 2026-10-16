using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using ShellX.Core.Connections;
using ShellX.Core.Sessions;
using ShellX.Desktop.Features.Connections;
using ShellX.Desktop.Features.Security;
using ShellX.Desktop.Services;
using ShellX.Infrastructure.Files;
using ShellX.Infrastructure.Persistence;
using ShellX.Infrastructure.Ssh;
using ShellX.Infrastructure.Security;

namespace ShellX.Desktop.Tests;

public sealed class ConnectionTestTests
{
    [AvaloniaFact(Timeout = 25000)]
    public async Task NewConnectionCanTestUnsavedFormShowRealAuthenticationErrorsAndCancelHostPrompt()
    {
        var fixturePath = Environment.GetEnvironmentVariable("SHELLX_TEST_FIXTURE");
        if (!File.Exists(fixturePath)) Assert.Skip("需要本机 SSH 测试服务。");
        using var config = JsonDocument.Parse(await File.ReadAllTextAsync(fixturePath));
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-test-connect-" + Guid.NewGuid().ToString("N"))).FullName;
        var paths = new AppPaths(root); var known = new JsonHostKeyStore(paths);
        var state = new AppLockService(new LockPasswordHasher());
        using var coordinator = new AppLockCoordinator(state);
        var owner = new Window { Content = new Grid(), Width = 900, Height = 750 }; coordinator.Protect(owner); owner.Show();
        var dialogs = new DialogService(new TextCodec(), state, coordinator, new SshConnectionTester(NullLoggerFactory.Instance), known) { Owner = owner };
        var editing = dialogs.EditConnectionAsync(null, null);
        var editor = owner.OwnedWindows.Single(); var vm = (ConnectionEditorViewModel)editor.DataContext!;
        try
        {
            vm.Host = "127.0.0.1"; vm.Port = config.RootElement.GetProperty("Port").GetInt32().ToString(); vm.Username = "shellx"; vm.Password = "fixture-password";
            await Task.Delay(60); Click(editor, editor.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "TestConnection"));
            await Until(() => editor.OwnedWindows.Any());
            var trust = editor.OwnedWindows.Single();
            Assert.StartsWith("测试连接", trust.Title);
            Click(trust, trust.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "确认")));
            await Until(() => !vm.IsTesting); Assert.True(vm.TestSucceeded, vm.TestStatus); Assert.Contains("SFTP 可用", vm.TestStatus);
            Assert.False(File.Exists(paths.File("connections.json"))); Assert.False(Directory.Exists(paths.File("credentials")));
            Assert.NotNull(await known.GetAsync(vm.Host, int.Parse(vm.Port)));
            if (Environment.GetEnvironmentVariable("SHELLX_PREVIEW_PATH") is { Length: > 0 } preview)
            {
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)editor.ClientSize.Width, (int)editor.ClientSize.Height), new Vector(96, 96));
                bitmap.Render(editor); bitmap.Save(Path.Combine(Path.GetDirectoryName(preview)!, "test-connection-success.png"), PngBitmapEncoderOptions.Default);
            }
            vm.Password = "incorrect-password"; Assert.Empty(vm.TestStatus); Assert.False(vm.TestSucceeded);
            await vm.TestConnectionCommand.ExecuteAsync(null); Assert.Contains("连接失败", vm.TestStatus); Assert.False(vm.TestSucceeded);
            vm.Password = "fixture-password"; vm.Host = "localhost";
            var testing = vm.TestConnectionCommand.ExecuteAsync(null);
            await Until(() => editor.OwnedWindows.Any());
            vm.CancelTestCommand.Execute(null); await testing;
            await Until(() => !editor.OwnedWindows.Any()); Assert.Contains("取消", vm.TestStatus);
            Assert.Null(await known.GetAsync("localhost", int.Parse(vm.Port)));
            editor.Close(); Assert.Null(await editing); Assert.Empty(vm.Password); Assert.Empty(vm.Passphrase);
        }
        finally { foreach (var child in editor.OwnedWindows.ToArray()) child.Close(); editor.Close(); owner.Close(); Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task ValidationPreventsNetworkCallsAndClosingCancelsPendingTestWithoutStaleSuccess()
    {
        var calls = 0; var cancelled = false;
        var vm = new ConnectionEditorViewModel(null, new("test-only"), async (snapshot, ct) =>
        {
            calls++; Assert.Equal("current.test", snapshot.Profile.Host); Assert.Equal("test-only", snapshot.Secret.Password);
            try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) { cancelled = true; throw; }
            return new ConnectionTestResult(true, null);
        });
        await vm.TestConnectionCommand.ExecuteAsync(null); Assert.Equal(0, calls); Assert.Contains("主机", vm.TestStatus);
        vm.Host = "current.test"; var testing = vm.TestConnectionCommand.ExecuteAsync(null);
        Assert.True(vm.IsTesting); Assert.False(vm.CanEdit); Assert.False(vm.TestConnectionCommand.CanExecute(null));
        vm.Close(); await testing;
        Assert.True(cancelled); Assert.False(vm.TestSucceeded); Assert.Empty(vm.Password); Assert.False(vm.TestConnectionCommand.CanExecute(null));
    }
    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
    }
    private static async Task Until(Func<bool> condition)
    { for (var i = 0; i < 500 && !condition(); i++) await Task.Delay(20); Assert.True(condition()); }
}
