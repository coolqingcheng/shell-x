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

    [AvaloniaFact]
    public void TunnelPanelHasStartAllAndCornerDeleteButtons()
    {
        var dialogs = new TestDialogs();
        var tab = NewTab(Profile(), dialogs).GetAwaiter().GetResult();
        var view = new WorkspaceView { DataContext = tab };
        var window = new Window { Content = view, Width = 1380, Height = 900 };
        tab.ShowTunnels = true;                    // 面板展开后才能看到规则行
        window.Show();
        try
        {
            Assert.NotNull(view.FindControl<Button>("StartAllTunnelsButton"));
            var tunnel = Assert.Single(tab.Tunnels);
            // 行右上角是小叉，且不再有“删除规则”文字按钮
            var remove = view.GetVisualDescendants().OfType<Button>()
                .First(b => ReferenceEquals(b.Command, tunnel.RemoveCommand));
            Assert.Contains(remove.GetVisualDescendants().OfType<Controls.StrokeIcon>(), icon => icon.Symbol == "close");
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "删除规则");
            Assert.Contains(view.GetVisualDescendants().OfType<Button>(), b => b.Name == "EditTunnelRule");   // 编辑入口保留（模板内名称走视觉树）
        }
        finally { window.Close(); tab.DisposeAsync().GetAwaiter().GetResult(); }
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