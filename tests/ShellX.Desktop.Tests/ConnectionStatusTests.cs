using Microsoft.Extensions.Logging.Abstractions;
using ShellX.Core.Connections;
using ShellX.Core.Files;
using ShellX.Core.Security;
using ShellX.Core.Sessions;
using ShellX.Core.Settings;
using ShellX.Core.Tunnels;
using ShellX.Desktop.Features.Workspace;
using ShellX.Desktop.Services;
using ShellX.Infrastructure.Files;

namespace ShellX.Desktop.Tests;

/// <summary>连接状态栏上的延迟显示。</summary>
public sealed class ConnectionStatusTests
{
    [Fact]
    public async Task ConnectionStatusShowsLatencyFromMonitor()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-latency-" + Guid.NewGuid().ToString("N"))).FullName;
        var tab = new WorkspaceTabViewModel(
            new ConnectionProfile { Name = "延迟测试", Host = "127.0.0.1", Port = 22, Username = "root" },
            new TestFactory(), new TestDialogs(), NullLoggerFactory.Instance,
            () => Task.FromResult<ConnectionSecret?>(null), _ => Task.FromResult(true),
            _ => Task.CompletedTask, _ => Task.CompletedTask, new AppSettings(), new LocalFileSystem(root));
        try
        {
            Assert.Equal("", tab.Latency);
            Assert.False(tab.HasLatency);

            tab.Monitor.Response = "42 ms";
            await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Equal("42 ms", tab.Latency);
            Assert.True(tab.HasLatency);

            tab.Monitor.Response = "—";
            await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Equal("", tab.Latency);
            Assert.False(tab.HasLatency);
        }
        finally
        {
            await tab.DisposeAsync();
            Directory.Delete(root, true);
        }
    }

    private sealed class TestFactory : IRemoteSessionFactory
    {
        public Task<IRemoteSession> ConnectAsync(ConnectionProfile profile, ConnectionSecret secret,
            Func<HostKeyChallenge, Task<bool>> verifyHost, CancellationToken cancellationToken)
            => throw new InvalidOperationException("该测试不建立连接。");
    }

    private sealed class TestDialogs : IDialogService
    {
        public Task<ConnectionEditResult?> EditConnectionAsync(ConnectionProfile? profile, ConnectionSecret? secret) => Task.FromResult<ConnectionEditResult?>(null);
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