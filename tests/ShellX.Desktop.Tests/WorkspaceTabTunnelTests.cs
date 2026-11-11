using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using ShellX.Core.Connections;
using ShellX.Core.Files;
using ShellX.Core.Sessions;
using ShellX.Core.Settings;
using ShellX.Core.Tunnels;
using ShellX.Desktop.Features.Tunnels;
using ShellX.Desktop.Features.Workspace;
using ShellX.Desktop.Services;
using ShellX.Infrastructure.Files;

namespace ShellX.Desktop.Tests;

public sealed class WorkspaceTabTunnelTests
{
    private static ConnectionProfile Profile(int rules = 1) => new()
    {
        Name = "隧道测试", Host = "127.0.0.1", Port = 22, Username = "root",
        Tunnels = Enumerable.Range(0, rules)
            .Select(i => new TunnelDefinition { Name = i == 0 ? "数据库隧道" : "缓存隧道", BindPort = 13306 + i, TargetPort = 3306 + i })
            .ToList()
    };

    private static async Task<WorkspaceTabViewModel> NewTab(ConnectionProfile profile, IDialogService dialogs)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-tunneltests-" + Guid.NewGuid().ToString("N"))).FullName;
        return new WorkspaceTabViewModel(profile, new TestFactory(), dialogs, NullLoggerFactory.Instance,
            () => Task.FromResult<ConnectionSecret?>(null), _ => Task.FromResult(true),
            _ => Task.CompletedTask, _ => Task.CompletedTask, new AppSettings(), new LocalFileSystem(root));
    }

    [Fact]
    public async Task SearchFiltersImmediatelyAndTracksRuleChanges()
    {
        var profile = Profile(2);
        profile.Tunnels[0] = profile.Tunnels[0] with { Name = "MongoDB", TargetHost = "db.example.com", TargetPort = 27017 };
        var tab = await NewTab(profile, new TestDialogs());
        try
        {
            Assert.Equal(2, tab.FilteredTunnels.Count);
            tab.TunnelSearch = " mongodb ";
            Assert.Equal("MongoDB", Assert.Single(tab.FilteredTunnels).Name);
            tab.TunnelSearch = "27017";
            Assert.Equal("MongoDB", Assert.Single(tab.FilteredTunnels).Name);
            tab.TunnelSearch = "DB.EXAMPLE";
            Assert.Equal("MongoDB", Assert.Single(tab.FilteredTunnels).Name);
            tab.TunnelSearch = "不存在";
            Assert.Empty(tab.FilteredTunnels);
            tab.TunnelSearch = "缓存";
            await tab.Tunnels[1].RemoveCommand.ExecuteAsync(null);
            Assert.Empty(tab.FilteredTunnels);
            tab.TunnelSearch = "";
            Assert.Single(tab.FilteredTunnels);
        }
        finally { await tab.DisposeAsync(); }
    }

    [Fact]
    public async Task RemovingTunnelRequiresConfirmation()
    {
        var dialogs = new TestDialogs { Confirmation = false };
        var tab = await NewTab(Profile(), dialogs);
        try
        {
            Assert.Single(tab.Tunnels);
            await tab.Tunnels[0].RemoveCommand.ExecuteAsync(null);
            Assert.Single(tab.Tunnels);                                   // 取消确认 → 不删除
            Assert.Equal("删除隧道规则", dialogs.LastConfirmationTitle);

            dialogs.Confirmation = true;
            await tab.Tunnels[0].RemoveCommand.ExecuteAsync(null);
            Assert.Empty(tab.Tunnels);                                    // 确认后才删除
        }
        finally { await tab.DisposeAsync(); }
    }

    [Fact]
    public async Task StartAllWithoutConnectionMarksEveryRuleFailed()
    {
        var dialogs = new TestDialogs();
        var tab = await NewTab(Profile(2), dialogs);
        try
        {
            Assert.Equal(2, tab.Tunnels.Count);
            await tab.StartAllTunnelsCommand.ExecuteAsync(null);
            Assert.All(tab.Tunnels, tunnel => Assert.Equal(TunnelState.Failed, tunnel.State));
            Assert.All(tab.Tunnels, tunnel => Assert.Contains("请先连接", tunnel.Reason));
        }
        finally { await tab.DisposeAsync(); }
    }

    [Fact]
    public async Task StopAllStopsEveryRunningTunnelAndDisablesBulkButtonState()
    {
        var dialogs = new TestDialogs();
        var tab = await NewTab(Profile(2), dialogs);
        var service = new TestTunnelService(tab.Tunnels.Select(t => t.Definition));
        try
        {
            tab.Session = new TestSession(service);
            tab.IsConnected = true;
            foreach (var tunnel in tab.Tunnels) tunnel.Apply(service.GetStatus(tunnel.Definition.Id));
            Assert.True(tab.CanStopAllTunnels);
            tab.TunnelSearch = "数据库";
            Assert.Single(tab.FilteredTunnels);

            await tab.StopAllTunnelsCommand.ExecuteAsync(null);

            Assert.Equal(2, service.Stopped.Count);
            Assert.All(tab.Tunnels, tunnel =>
            {
                Assert.False(tunnel.Running);
                Assert.Equal(TunnelState.Stopped, tunnel.State);
            });
            Assert.False(tab.CanStopAllTunnels);
        }
        finally
        {
            tab.IsConnected = false;
            tab.Session = null;
            await tab.DisposeAsync();
        }
    }
    [AvaloniaFact]
    public void TunnelWindowHasStartAllAndCornerDeleteButtons()
    {
        var dialogs = new TestDialogs();
        var tab = NewTab(Profile(), dialogs).GetAwaiter().GetResult();
        var window = new TunnelWindow(new TunnelWindowViewModel(tab, "隧道测试 · 127.0.0.1:22"));
        window.Show();
        try
        {
            Assert.NotNull(window.FindControl<Button>("StartAllTunnelsButton"));
            Assert.NotNull(window.FindControl<TextBox>("TunnelSearchBox"));
            var stopAll = window.FindControl<Button>("StopAllTunnelsButton");
            Assert.NotNull(stopAll);
            Assert.False(stopAll!.IsEnabled);                                 // 未连接/无运行隧道时不可点
            var tunnel = Assert.Single(tab.Tunnels);
            // 行右上角是小叉，且不再有“删除规则”文字按钮
            var remove = window.GetVisualDescendants().OfType<Button>()
                .First(b => ReferenceEquals(b.Command, tunnel.RemoveCommand));
            Assert.Contains(remove.GetVisualDescendants().OfType<Controls.StrokeIcon>(), icon => icon.Symbol == "close");
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "删除规则");
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => b.Name == "EditTunnelRule");   // 编辑入口保留（模板内名称走视觉树）
            Assert.NotNull(window.FindControl<CheckBox>("AutoRetryToggle"));   // 失败自动重试开关
        }
        finally { window.Close(); tab.DisposeAsync().GetAwaiter().GetResult(); }
    }

    [AvaloniaFact]
    public async Task AutoRetrySwitchPersistsIntoTheConnectionProfile()
    {
        var dialogs = new TestDialogs();
        var tab = NewTab(Profile(), dialogs).GetAwaiter().GetResult();
        try
        {
            var viewModel = new TunnelWindowViewModel(tab, "隧道测试");
            Assert.False(viewModel.AutoRetry);
            viewModel.AutoRetry = true;
            viewModel.RetrySeconds = 20;
            for (var i = 0; i < 50 && !tab.Profile.TunnelAutoRetry; i++) await Task.Delay(20);
            Assert.True(tab.Profile.TunnelAutoRetry);
            Assert.Equal(20, tab.Profile.TunnelRetrySeconds);
            viewModel.Dispose();
        }
        finally { await tab.DisposeAsync(); }
    }


    private sealed class TestTunnelService : ITunnelService
    {
        private readonly Dictionary<Guid, TunnelStatus> _statuses = new();
        public List<Guid> Stopped { get; } = new();
        public event EventHandler<TunnelStatus>? StatusChanged;

        public TestTunnelService(IEnumerable<TunnelDefinition> definitions)
        {
            foreach (var definition in definitions)
                _statuses[definition.Id] = new TunnelStatus(definition.Id, TunnelState.Connected, true, "已连接");
        }

        public bool IsRunning(Guid id) => _statuses.TryGetValue(id, out var status) && status.IsRunning;

        public TunnelStatus GetStatus(Guid id) => _statuses.TryGetValue(id, out var status)
            ? status
            : new TunnelStatus(id, TunnelState.Stopped, false);

        public Task StartAsync(TunnelDefinition definition, CancellationToken cancellationToken = default)
        {
            Set(definition.Id, new TunnelStatus(definition.Id, TunnelState.Connected, true, "已连接"));
            return Task.CompletedTask;
        }

        public Task StopAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Stopped.Add(id);
            Set(id, new TunnelStatus(id, TunnelState.Stopped, false));
            return Task.CompletedTask;
        }

        public Task CheckAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;

        private void Set(Guid id, TunnelStatus status)
        {
            _statuses[id] = status;
            StatusChanged?.Invoke(this, status);
        }
    }

    [AvaloniaFact]
    public async Task AutoRetryRetriesFailedTunnelOnTheConfiguredInterval()
    {
        var dialogs = new TestDialogs();
        var tab = await NewTab(Profile(), dialogs);
        var service = new FailingTunnelService(tab.Tunnels.Select(t => t.Definition));
        try
        {
            tab.Session = new TestSession(service);
            tab.IsConnected = true;
            var tunnel = tab.Tunnels[0];
            tunnel.Apply(new(tunnel.Definition.Id, TunnelState.Failed, false, "端口被占用"));

            await tab.SetTunnelAutoRetryAsync(true, 3);

            for (var i = 0; i < 50 && tunnel.RetryText.Length == 0; i++) await Task.Delay(100);
            Assert.Contains("秒后自动重试", tunnel.RetryText);          // 界面上给出倒计时提示
            for (var i = 0; i < 120 && service.StartAttempts == 0; i++) await Task.Delay(100);
            Assert.True(service.StartAttempts >= 1);                     // 到点后自动重新拉起
        }
        finally { tab.IsConnected = false; tab.Session = null; await tab.DisposeAsync(); }
    }

    [AvaloniaFact]
    public async Task AutoRetryOffLeavesFailedTunnelAlone()
    {
        var dialogs = new TestDialogs();
        var tab = await NewTab(Profile(), dialogs);
        var service = new FailingTunnelService(tab.Tunnels.Select(t => t.Definition));
        try
        {
            tab.Session = new TestSession(service);
            tab.IsConnected = true;
            var tunnel = tab.Tunnels[0];
            tunnel.Apply(new(tunnel.Definition.Id, TunnelState.Failed, false, "端口被占用"));
            for (var i = 0; i < 25; i++) await Task.Delay(100);
            Assert.Equal(0, service.StartAttempts);                      // 开关关闭时不自动重试
            Assert.Equal("", tunnel.RetryText);
        }
        finally { tab.IsConnected = false; tab.Session = null; await tab.DisposeAsync(); }
    }

    private sealed class FailingTunnelService : ITunnelService
    {
        private readonly Dictionary<Guid, TunnelStatus> _statuses = new();
        public int StartAttempts { get; private set; }
        public event EventHandler<TunnelStatus>? StatusChanged;
        public FailingTunnelService(IEnumerable<TunnelDefinition> definitions)
        {
            foreach (var definition in definitions)
                _statuses[definition.Id] = new(definition.Id, TunnelState.Failed, false, "端口被占用");
        }
        public bool IsRunning(Guid id) => false;
        public TunnelStatus GetStatus(Guid id) => _statuses.GetValueOrDefault(id) ?? new(id, TunnelState.Stopped, false);
        public Task StartAsync(TunnelDefinition definition, CancellationToken cancellationToken = default)
        {
            StartAttempts++;
            var status = new TunnelStatus(definition.Id, TunnelState.Failed, false, "端口被占用");
            _statuses[definition.Id] = status; StatusChanged?.Invoke(this, status);
            return Task.CompletedTask;
        }
        public Task StopAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CheckAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TestSession : IRemoteSession
    {
        public TestSession(ITunnelService tunnels) => Tunnels = tunnels;
        public ITerminalChannel Terminal { get; } = new TestTerminal();
        public IRemoteFileSystem? Files => null;
        public string? FileError => null;
        public ITunnelService Tunnels { get; }
        public event EventHandler<string>? Disconnected;
        public void RaiseDisconnected(string reason) => Disconnected?.Invoke(this, reason);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestTerminal : ITerminalChannel
    {
        public Stream Stream => Stream.Null;
        public void Resize(int columns, int rows) { }
    }
    private sealed class TestFactory : IRemoteSessionFactory
    {
        public Task<IRemoteSession> ConnectAsync(ConnectionProfile profile, ConnectionSecret secret,
            Func<ShellX.Core.Security.HostKeyChallenge, Task<bool>> verifyHost, CancellationToken cancellationToken)
            => throw new InvalidOperationException("该测试不建立连接。");
    }

    private sealed class TestDialogs : IDialogService
    {
        public bool Confirmation { get; set; } = true;
        public string? LastConfirmationTitle { get; private set; }
        public Task<ConnectionEditResult?> EditConnectionAsync(ConnectionProfile? profile, ConnectionSecret? secret)
            => Task.FromResult<ConnectionEditResult?>(null);
        public Task<bool> ConfirmAsync(string title, string message, bool danger = false)
        { LastConfirmationTitle = title; return Task.FromResult(Confirmation); }
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
