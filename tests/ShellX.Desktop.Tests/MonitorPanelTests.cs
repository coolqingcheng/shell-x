using Microsoft.Extensions.Logging.Abstractions;
using ShellX.Core.Connections;
using ShellX.Desktop.Features.Monitoring;

namespace ShellX.Desktop.Tests;

public sealed class MonitorPanelTests
{
    [Fact]
    public void SameEndpointCanOnlyOpenOnePanelAndDifferentAddressesCanCoexist()
    {
        var panels = new MonitorPanelViewModel();
        var first = new ServerMonitorViewModel("one", null!, NullLogger<ServerMonitorViewModel>.Instance, _ => Task.CompletedTask);
        var second = new ServerMonitorViewModel("two", null!, NullLogger<ServerMonitorViewModel>.Instance, _ => Task.CompletedTask);
        var endpointA = new ConnectionProfile { Name = "生产", Host = "EXAMPLE.com.", Port = 22, Username = "root" };
        var endpointAAgain = endpointA with { Name = "生产副本", Host = "example.com" };
        var endpointB = endpointA with { Name = "测试", Host = "127.0.0.1" };

        Assert.True(panels.TryAdd(endpointA, "生产", "root@example.com:22", first));
        Assert.False(panels.TryAdd(endpointAAgain, "生产副本", "root@example.com:22", first));
        Assert.True(panels.TryAdd(endpointB, "测试", "root@127.0.0.1:22", second));
        Assert.Equal(2, panels.Panels.Count);

        panels.Panels[0].CloseCommand.Execute(null);
        Assert.Single(panels.Panels);
    }
}