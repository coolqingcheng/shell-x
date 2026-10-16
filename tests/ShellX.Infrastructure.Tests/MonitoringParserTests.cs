using ShellX.Core.Monitoring;
using ShellX.Infrastructure.Monitoring;

namespace ShellX.Infrastructure.Tests;
public class MonitoringParserTests
{
    private const string Sample = """
        __SX_SYSTEM__
        linux-host
        Linux 6.1.0 x86_64
        Debian GNU/Linux 12
        192.0.2.10
        __SX_CPU__
        cpu 100 20 50 800 30 0 0 0 40 5
        __SX_UPTIME__
        90061.50 2000.00
        __SX_LOAD__
        0.10 0.20 0.30 1/100 1234
        __SX_MEMORY__
        MemTotal: 1000000 kB
        MemAvailable: 750000 kB
        MemFree: 100000 kB
        Buffers: 50000 kB
        Cached: 550000 kB
        SwapTotal: 200000 kB
        SwapFree: 150000 kB
        __SX_NETWORK__
        Inter-| Receive | Transmit
        eth0: 1000 0 0 0 0 0 0 0 2000 0 0 0 0 0 0 0
        lo: 50 0 0 0 0 0 0 0 50 0 0 0 0 0 0 0
        __SX_PROCESS__
        123 12.5 10240 nginx
        456 0.2 2048 sshd
        __SX_DISK__
        Filesystem 1024-blocks Used Available Capacity Mounted on
        /dev/sda1 1000000 250000 750000 25% /
        /dev/sdb1 2000000 500000 1500000 25% /mnt/my disk
        """;

    [Fact]
    public void LinuxSnapshotUsesAvailableMemoryAndExcludesDoubleCountedGuestCpu()
    {
        var result = LinuxMonitorParser.Parse(Sample, DateTimeOffset.UtcNow, 12);
        Assert.Equal(1000UL, result.Cpu.Total); Assert.Equal(830UL, result.Cpu.Idle);
        Assert.Equal(750000 * 1024L, result.MemoryAvailable); Assert.Equal(90061.5, result.Uptime.TotalSeconds);
        Assert.Equal("Debian GNU/Linux 12", result.OperatingSystem); Assert.Equal("192.0.2.10", result.Addresses);
        Assert.Equal(10240 * 1024L, result.Processes[0].MemoryBytes);
        Assert.Equal("/mnt/my disk", result.Disks[1].MountPath); Assert.Equal(750000 * 1024L, result.Disks[0].AvailableBytes);
    }
    [Fact]
    public void CpuAndNetworkUseDifferencesBetweenSamplesAndHandleCounterReset()
    {
        var first = LinuxMonitorParser.Parse(Sample, DateTimeOffset.UtcNow, 12);
        var second = first with { SampleTime = first.SampleTime.AddSeconds(2), Cpu = new(1100, 900), Networks = [new("eth0", 3000, 3000)] };
        Assert.Null(ServerMetrics.CpuUsage(null, first.Cpu)); Assert.Equal(30, ServerMetrics.CpuUsage(first.Cpu, second.Cpu));
        var network = Assert.Single(ServerMetrics.NetworkRates(first, second));
        Assert.Equal(1000, network.DownloadBytesPerSecond); Assert.Equal(500, network.UploadBytesPerSecond);
        var reset = second with { SampleTime = second.SampleTime.AddSeconds(2), Cpu = new(10, 1), Networks = [new("eth0", 1, 1)] };
        Assert.Null(ServerMetrics.CpuUsage(second.Cpu, reset.Cpu)); Assert.Equal(0, Assert.Single(ServerMetrics.NetworkRates(second, reset)).UploadBytesPerSecond);
    }
    [Fact]
    public void InvalidSnapshotDoesNotBecomeAZeroUsageServer()
        => Assert.Throws<InvalidDataException>(() => LinuxMonitorParser.Parse("not supported", DateTimeOffset.UtcNow, 0));
}
