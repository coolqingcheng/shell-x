using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Iciclecreek.Terminal;
using Microsoft.Extensions.Logging.Abstractions;
using ShellX.Core.Connections;
using ShellX.Core.Security;
using ShellX.Core.Sessions;
using ShellX.Core.Settings;
using ShellX.Desktop.Controls;
using ShellX.Desktop.Features.Settings;
using ShellX.Desktop.Features.Workspace;
using ShellX.Desktop.Services;
using ShellX.Infrastructure.Files;
using ShellX.Infrastructure.Security;
using ShellX.Infrastructure.Ssh;

namespace ShellX.Desktop.Tests;

public sealed class ReconnectTests
{
    [AvaloniaFact(Timeout = 30000)]
    public async Task UnexpectedDisconnectRetriesAtConfiguredIntervalAndKeepsTerminalHistoryWithLocalLogs()
    {
        var profile = Fixture(); var factory = new Factory();
        await using var tab = Tab(profile, factory);
        var view = new WorkspaceView { DataContext = tab };
        var window = new Window { Content = view, Width = 1200, Height = 750 }; window.Show();
        try
        {
            await tab.ConnectWithSecretAsync(new("fixture-password"));
            var terminal = view.GetVisualDescendants().OfType<TerminalControl>().Single();
            await Until(() => terminal.IsLive && Screen(terminal).Contains("本地测试服务器"));
            await terminal.SendInputAsync("before-disconnect\r");
            await Until(() => Screen(terminal).Contains("before-disconnect"));
            var old = tab.Session; factory.FailNext = true;
            await terminal.SendInputAsync("__SX_DISCONNECT_TRANSPORT__\r");
            await Until(() => tab.IsReconnecting);
            Assert.Single(factory.Attempts);
            Assert.False(tab.ShowOverlay);
            try { await Until(() => factory.Attempts.Count >= 3 && tab.IsConnected && !tab.IsConnecting && !tab.IsReconnecting); }
            catch (Exception ex) { throw new Exception($"{tab.Status}; connected={tab.IsConnected}; connecting={tab.IsConnecting}; retry={tab.IsReconnecting}; attempts={factory.Attempts.Count}; {Screen(terminal)}", ex); }
            Assert.NotSame(old, tab.Session);
            Assert.True(factory.Attempts[1] - factory.DisconnectedAt >= TimeSpan.FromMilliseconds(900));
            Assert.True(factory.Attempts[2] - factory.Attempts[1] >= TimeSpan.FromMilliseconds(900));
            try { await Until(() => Screen(terminal).Contains("自动重连成功")); }
            catch (Exception ex) { throw new Exception("重连后终端内容：" + Screen(terminal), ex); }
            var screen = Screen(terminal);
            Assert.Contains("连接断开", screen); Assert.Contains("1 秒后自动重连", screen);
            Assert.Contains("第 1 次重连失败", screen); Assert.Contains("simulated connection refused", screen);
            Assert.Contains("before-disconnect", screen);
            Assert.Same(terminal, view.FindControl<TerminalHost>("Terminal")!.Child);
            Assert.True(tab.Files.IsAvailable);
            await Until(() => tab.Monitor.Hostname == "fixture-linux");
            await terminal.SendInputAsync("after-reconnect\r");
            await Until(() => Screen(terminal).Contains("received: after-reconnect"));
            if (Environment.GetEnvironmentVariable("SHELLX_PREVIEW_PATH") is { Length: > 0 } preview)
            {
                using var bitmap = new RenderTargetBitmap(new PixelSize(1200, 750), new Vector(96, 96));
                bitmap.Render(window); bitmap.Save(Path.Combine(Path.GetDirectoryName(preview)!, "auto-reconnect.png"), PngBitmapEncoderOptions.Default);
            }
            await tab.DisconnectCommand.ExecuteAsync(null);
            var attempts = factory.Attempts.Count; await Task.Delay(1200);
            Assert.Equal(attempts, factory.Attempts.Count); Assert.False(tab.IsConnected); Assert.False(tab.IsReconnecting);
        }
        finally { await tab.DisposeAsync(); window.Close(); }
    }

    [AvaloniaTheory(Timeout = 20000)]
    [InlineData("disconnect")]
    [InlineData("disable")]
    [InlineData("close")]
    public async Task ManualActionsCancelAnInFlightRetryAndPreventMoreAttempts(string action)
    {
        var factory = new Factory(); await using var tab = Tab(Fixture(), factory);
        await tab.ConnectWithSecretAsync(new("fixture-password"));
        factory.HoldNext = true;
        await tab.Session!.Terminal.Stream.WriteAsync(Encoding.UTF8.GetBytes("__SX_DISCONNECT_TRANSPORT__\r"));
        await tab.Session.Terminal.Stream.FlushAsync();
        try { await Until(() => factory.Holding); }
        catch (Exception ex) { throw new Exception($"{tab.Status}; connected={tab.IsConnected}; connecting={tab.IsConnecting}; retry={tab.IsReconnecting}; attempts={factory.Attempts.Count}; disconnected={factory.DisconnectedAt:o}", ex); }
        if (action == "disconnect") await tab.DisconnectCommand.ExecuteAsync(null);
        else if (action == "close") await tab.DisposeAsync();
        else tab.ApplySettings(new AppSettings { AutoReconnectEnabled = false, ReconnectIntervalSeconds = 1 });
        await Until(() => !tab.IsReconnecting && !tab.IsConnecting);
        Assert.True(factory.Cancelled);
        var attempts = factory.Attempts.Count; await Task.Delay(1200);
        Assert.Equal(attempts, factory.Attempts.Count); Assert.False(tab.IsConnected);
    }

    [AvaloniaFact]
    public async Task ReconnectSettingsRejectInvalidIntervalsAndSaveIndependentOfOtherSettings()
    {
        var vm = new SettingsViewModel(new AppSettings { Theme = AppTheme.Light, EditorFontSize = 19 }, new LockPasswordHasher(), ["Consolas"]);
        AppSettings? saved = null; vm.Saved += value => saved = value;
        foreach (var invalid in new decimal[] { 0, 1.5m, 86401 })
        { vm.ReconnectIntervalSeconds = invalid; await vm.SaveCommand.ExecuteAsync(null); Assert.Null(saved); Assert.Contains("重连间隔", vm.Error); }
        vm.ReconnectIntervalSeconds = 30; vm.AutoReconnect = false; await vm.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(saved); Assert.Equal(30, saved.ReconnectIntervalSeconds); Assert.False(saved.AutoReconnectEnabled);
        Assert.Equal(AppTheme.Light, saved.Theme); Assert.Equal(19, saved.EditorFontSize);
    }

    private static WorkspaceTabViewModel Tab(ConnectionProfile profile, Factory factory) => new(profile, factory,
        new DialogService(new TextCodec()), NullLoggerFactory.Instance,
        () => throw new InvalidOperationException("自动重连不应重新提示凭据。"), _ => Task.FromResult(true), _ => Task.CompletedTask,
        _ => Task.CompletedTask, new AppSettings { ReconnectIntervalSeconds = 1 }, new LocalFileSystem(Path.GetTempPath()));
    private static ConnectionProfile Fixture()
    {
        var path = Environment.GetEnvironmentVariable("SHELLX_TEST_FIXTURE");
        if (!File.Exists(path)) Assert.Skip("需要本机 SSH 测试服务。");
        using var config = JsonDocument.Parse(File.ReadAllText(path));
        return new() { Host = "127.0.0.1", Port = config.RootElement.GetProperty("Port").GetInt32(), Username = "shellx" };
    }
    private static string Screen(TerminalControl terminal)
    {
        var buffer = terminal.Terminal.Buffer; var result = new StringBuilder();
        for (var i = 0; i < buffer.Length; i++) result.Append(buffer.GetLine(i)?.TranslateToString(true));
        return result.ToString();
    }
    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++) await Task.Delay(20);
        Assert.True(condition(), "等待重连状态超时。");
    }
    private sealed class Factory : IRemoteSessionFactory
    {
        private readonly SshSessionFactory _inner = new(NullLoggerFactory.Instance);
        public bool FailNext, HoldNext, Holding, Cancelled;
        public DateTime DisconnectedAt;
        public List<DateTime> Attempts { get; } = [];
        public async Task<IRemoteSession> ConnectAsync(ConnectionProfile profile, ConnectionSecret secret, Func<HostKeyChallenge, Task<bool>> verifyHost, CancellationToken cancellationToken)
        {
            Attempts.Add(DateTime.UtcNow);
            if (FailNext) { FailNext = false; throw new IOException("simulated connection refused"); }
            if (HoldNext)
            {
                HoldNext = false; Holding = true;
                try { await Task.Delay(Timeout.Infinite, cancellationToken); }
                catch (OperationCanceledException) { Cancelled = true; throw; }
            }
            var session = await _inner.ConnectAsync(profile, secret, verifyHost, cancellationToken);
            session.Disconnected += (_, _) => DisconnectedAt = DateTime.UtcNow;
            return session;
        }
    }
}
