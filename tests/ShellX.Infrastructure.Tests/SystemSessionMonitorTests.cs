using Microsoft.Extensions.Logging.Abstractions;
using ShellX.Infrastructure.Security;

namespace ShellX.Infrastructure.Tests;

public sealed class WindowsSessionFactAttribute : FactAttribute
{
    public WindowsSessionFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "需要 Windows 用户会话。";
    }
}

public sealed class SystemSessionMonitorTests
{
    [Fact]
    public void UnsupportedMonitorReportsUnknownState()
    {
        using var monitor = new UnsupportedSystemSessionMonitor();
        monitor.Start(); monitor.Start();
        Assert.False(monitor.IsSupported); Assert.Null(monitor.CurrentState);
    }

    [WindowsSessionFact]
    public void WindowsMonitorRegistersAndReadsCurrentSessionWithoutLockingDesktop()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var monitor = new WindowsSystemSessionMonitor(NullLogger<WindowsSystemSessionMonitor>.Instance);
        monitor.Start(); monitor.Start();
        Assert.True(monitor.IsSupported); Assert.NotNull(monitor.CurrentState);
        monitor.Dispose(); monitor.Dispose();
    }
}
