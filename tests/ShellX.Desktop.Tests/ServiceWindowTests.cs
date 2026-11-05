using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using ShellX.Core.Services;
using ShellX.Desktop.Features.Services;
using ShellX.Desktop.Features.Workspace;

namespace ShellX.Desktop.Tests;

public sealed class ServiceWindowTests
{
    private sealed class FakeServices : IServiceCenter
    {
        public List<RemoteService> Items =
        [
            new("nginx.service", "loaded", "active", "running", "A high performance web server", "enabled"),
            new("sshd.service", "loaded", "active", "running", "OpenBSD Secure Shell server", "enabled"),
            new("demo.service", "loaded", "inactive", "dead", "", "disabled"),
            new("broken.service", "loaded", "failed", "failed", "Unit failed to start", "enabled")
        ];
        public readonly List<(string Unit, ServiceAction Action)> Executed = [];
        public Task<IReadOnlyList<RemoteService>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RemoteService>>(Items);
        public Task<string> StatusAsync(string unit, CancellationToken cancellationToken = default)
            => Task.FromResult($"● {unit}\n   Loaded: loaded\n   Active: active");
        public Task<ServiceActionResult> ExecuteAsync(string unit, ServiceAction action, CancellationToken cancellationToken = default)
        {
            Executed.Add((unit, action));
            return Task.FromResult(new ServiceActionResult(true, $"{action} executed", 0));
        }
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 250 && !condition(); attempt++) await Task.Delay(20);
        Assert.True(condition());
    }

    [Fact]
    public async Task SearchFiltersByUnitAndDescription()
    {
        var vm = new ServiceWindowViewModel(new FakeServices(), "测试 · 127.0.0.1:22", _ => Task.FromResult(true));
        await vm.RefreshAsync();
        Assert.Equal(4, vm.VisibleServices.Count);
        Assert.Contains("共 4 个服务", vm.Summary);
        Assert.False(vm.HasEmptyText);

        vm.Search = "nginx";
        Assert.Equal("nginx.service", Assert.Single(vm.VisibleServices).Unit);

        vm.Search = "web server";                       // 命中描述
        Assert.Equal("nginx.service", Assert.Single(vm.VisibleServices).Unit);

        vm.Search = "没有任何服务";
        Assert.Empty(vm.VisibleServices);
        Assert.True(vm.HasEmptyText);
        Assert.Equal("没有匹配的服务。", vm.EmptyText);
    }

    [Fact]
    public async Task ActionsOnlyRunAfterConfirmationAndRefreshTheList()
    {
        var fake = new FakeServices();
        var confirmed = false;
        var vm = new ServiceWindowViewModel(fake, "测试 · 127.0.0.1:22", _ => Task.FromResult(confirmed));
        await vm.RefreshAsync();
        vm.Selected = vm.VisibleServices.Single(service => service.Unit == "sshd.service");

        await vm.RestartCommand.ExecuteAsync(null);     // 取消确认
        Assert.Empty(fake.Executed);

        confirmed = true;
        await vm.RestartCommand.ExecuteAsync(null);
        Assert.Equal(("sshd.service", ServiceAction.Restart), Assert.Single(fake.Executed));
        Assert.Contains("重启完成", vm.Status);
        Assert.Equal("#73C991", vm.StatusColor);
        Assert.NotNull(vm.Selected);                    // 刷新后保持选中同一服务
        Assert.Equal("sshd.service", vm.Selected!.Unit);
        await UntilAsync(() => vm.Detail.Contains("sshd.service"));
    }

    [Fact]
    public async Task FailedActionReportsExitCodeAndErrorColor()
    {
        var fake = new FakeServices();
        var vm = new ServiceWindowViewModel(fake, "测试", _ => Task.FromResult(true));
        fake.Executed.Clear();
        await vm.RefreshAsync();
        vm.Selected = vm.VisibleServices[0];
        // 直接验证失败路径的展示规则
        var failing = new ServiceWindowViewModel(new FailingServices(), "测试", _ => Task.FromResult(true));
        await failing.RefreshAsync();
        failing.Selected = failing.VisibleServices[0];
        await failing.StopCommand.ExecuteAsync(null);
        Assert.Contains("失败（exit=1）", failing.Status);
        Assert.Equal("#F48771", failing.StatusColor);
        Assert.Empty(fake.Executed);
    }

    private sealed class FailingServices : IServiceCenter
    {
        public Task<IReadOnlyList<RemoteService>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RemoteService>>([new("x.service", "loaded", "active", "running", "", "enabled")]);
        public Task<string> StatusAsync(string unit, CancellationToken cancellationToken = default) => Task.FromResult("");
        public Task<ServiceActionResult> ExecuteAsync(string unit, ServiceAction action, CancellationToken cancellationToken = default)
            => Task.FromResult(new ServiceActionResult(false, "Access denied", 1));
    }

    [AvaloniaFact]
    public async Task TunnelPanelAndToolbarContainServicesEntry()
    {
        var view = new WorkspaceView();
        var window = new Window { Content = view, Width = 1380, Height = 900 };
        window.Show();
        try
        {
            Assert.NotNull(view.FindControl<Button>("OpenServicesButton"));      // 端口转发面板标题栏
            Assert.NotNull(view.FindControl<Button>("ToolbarServicesButton"));   // 终端工具栏 ⇄ 旁边
            Assert.Contains(view.GetVisualDescendants().OfType<Controls.StrokeIcon>(), icon => icon.Symbol == "service");
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "服务");
            if (Environment.GetEnvironmentVariable("SHELLX_PREVIEW_PATH") is { Length: > 0 } preview)
            {
                await Task.Delay(120);
                using var bitmap = new RenderTargetBitmap(new PixelSize(1380, 900), new Vector(96, 96));
                bitmap.Render(window);
                bitmap.Save(Path.Combine(Path.GetDirectoryName(preview)!, "tunnel-services-entry.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ServiceWindowShowsStatusAndRendersPreview()
    {
        var vm = new ServiceWindowViewModel(new FakeServices(), "生产服务器 · 10.0.0.8:22", _ => Task.FromResult(true));
        await vm.RefreshAsync();
        vm.Selected = vm.VisibleServices.Single(service => service.Unit == "nginx.service");
        await UntilAsync(() => vm.Detail.Contains("nginx.service"));
        var window = new ServiceWindow(vm);
        try
        {
            Assert.StartsWith("服务状态 · 生产服务器", window.Title);
            Assert.Equal(4, vm.VisibleServices.Count);
            if (Environment.GetEnvironmentVariable("SHELLX_PREVIEW_PATH") is not { Length: > 0 } preview) return;
            window.Show();
            await Task.Delay(150);
            using var bitmap = new RenderTargetBitmap(new PixelSize(960, 640), new Vector(96, 96));
            bitmap.Render(window);
            bitmap.Save(Path.Combine(Path.GetDirectoryName(preview)!, "service-status.png"), PngBitmapEncoderOptions.Default);
            window.Close();
        }
        finally
        {
            if (window.IsVisible) window.Close();
        }
    }
}
