using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using ShellX.Core.Services;
using ShellX.Desktop.Features.Ports;
using ShellX.Desktop.Features.Workspace;

namespace ShellX.Desktop.Tests;

public sealed class PortUsageTests
{
    private sealed class FakeReader : IPortUsageReader
    {
        public List<ListeningPort> Rows { get; } =
        [
            new("TCP", "LISTEN", "0.0.0.0", 22, "sshd", "866", "LISTEN 0 128 0.0.0.0:22 …"),
            new("TCP", "LISTEN", "127.0.0.1", 3306, "mysqld", "1234", "LISTEN 0 128 127.0.0.1:3306 …"),
            new("UDP", "UNCONN", "0.0.0.0", 68, "dhclient", "700", "UNCONN 0 0 0.0.0.0:68 …")
        ];
        public Task<IReadOnlyList<ListeningPort>> PortsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ListeningPort>>(Rows);
    }

    [Fact]
    public async Task SearchMatchesPortProcessIdProcessNameAndAddress()
    {
        var vm = new PortUsageWindowViewModel(new FakeReader(), "测试 · 127.0.0.1:22");
        await vm.RefreshAsync();
        Assert.Equal(3, vm.VisiblePorts.Count);
        Assert.Contains("共 3 个监听端口", vm.Summary);

        vm.Search = "3306";                                  // 按端口
        Assert.Equal(3306, Assert.Single(vm.VisiblePorts).Port);

        vm.Search = "866";                                   // 按进程 ID
        Assert.Equal("sshd", Assert.Single(vm.VisiblePorts).ProcessName);

        vm.Search = "mysql";                                 // 按进程名
        Assert.Equal(3306, Assert.Single(vm.VisiblePorts).Port);

        vm.Search = "127.0.0.1";                             // 按地址
        Assert.Equal(3306, Assert.Single(vm.VisiblePorts).Port);

        vm.Search = "9999";
        Assert.Empty(vm.VisiblePorts);
        Assert.True(vm.HasEmptyText);
        Assert.Equal("没有匹配的端口或进程 ID。", vm.EmptyText);
    }

    [Fact]
    public async Task WithoutReaderWindowExplainsUnsupported()
    {
        var vm = new PortUsageWindowViewModel(null, "测试");
        await vm.RefreshAsync();
        Assert.Empty(vm.VisiblePorts);
        Assert.Contains("不支持端口查看", vm.EmptyText);
        Assert.False(vm.CanRefresh);
        Assert.Equal("#F48771", vm.StatusColor);
    }

    [AvaloniaFact]
    public async Task ToolbarHasPortEntryAndWindowRendersPreview()
    {
        var view = new WorkspaceView();
        var window = new Window { Content = view, Width = 1380, Height = 900 };
        window.Show();
        try
        {
            var button = view.FindControl<Button>("ToolbarPortsButton");
            Assert.NotNull(button);
            Assert.Contains(view.GetVisualDescendants().OfType<Controls.StrokeIcon>(), icon => icon.Symbol == "ports");

            var vm = new PortUsageWindowViewModel(new FakeReader(), "生产服务器 · 10.0.0.8:22");
            await vm.RefreshAsync();
            var portsWindow = new PortUsageWindow(vm);
            try
            {
                Assert.StartsWith("端口占用 · 生产服务器", portsWindow.Title);
                if (Environment.GetEnvironmentVariable("SHELLX_PREVIEW_PATH") is { Length: > 0 } preview)
                {
                    portsWindow.Show();
                    await Task.Delay(150);
                    using var bitmap = new RenderTargetBitmap(new PixelSize(860, 560), new Vector(96, 96));
                    bitmap.Render(portsWindow);
                    bitmap.Save(Path.Combine(Path.GetDirectoryName(preview)!, "port-usage.png"), PngBitmapEncoderOptions.Default);
                    portsWindow.Close();
                }
            }
            finally { if (portsWindow.IsVisible) portsWindow.Close(); }
        }
        finally { window.Close(); }
    }
}