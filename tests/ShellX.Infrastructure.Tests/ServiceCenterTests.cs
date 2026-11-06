using Microsoft.Extensions.Logging.Abstractions;
using Renci.SshNet;
using ShellX.Core.Services;
using ShellX.Infrastructure.Ssh;

namespace ShellX.Infrastructure.Tests;

public sealed class ServiceCenterTests
{
    private static string Sample() => string.Join("\r\n",
        "accounts-daemon.service loaded active running Accounts Service",
        "ssh.service        loaded active running  OpenBSD Secure Shell server",
        "foo.service        loaded inactive dead   -",
        "orphan.service    loaded inactive dead",
        "__SX_UNITS__",
        "ssh.service disabled",
        "accounts-daemon.service enabled",
        "foo.service static",
        "123 units listed.");

    [Fact]
    public void ParsesUnitColumnsAndMergesEnableState()
    {
        var services = SshServiceCenter.Parse(Sample());
        Assert.Equal(new[] { "accounts-daemon.service", "foo.service", "orphan.service", "ssh.service" }, services.Select(s => s.Unit));
        var ssh = services.Single(s => s.Unit == "ssh.service");
        Assert.Equal("active", ssh.Active);
        Assert.Equal("running", ssh.Sub);
        Assert.Equal("OpenBSD Secure Shell server", ssh.Description);
        Assert.Equal("disabled", ssh.EnableState);
        Assert.Equal("active / running", ssh.StateText);
        Assert.Equal("#73C991", ssh.StateColor);
        Assert.Equal("enabled", services.Single(s => s.Unit == "accounts-daemon.service").EnableState);
        var foo = services.Single(s => s.Unit == "foo.service");
        Assert.Equal("", foo.Description);          // 占位符“-”不当成描述
        Assert.Equal("static", foo.EnableState);
        Assert.Equal("#858585", foo.StateColor);
        Assert.Equal("—", services.Single(s => s.Unit == "orphan.service").EnabledText);   // 没有 unit-file 条目时显示占位
    }

    [Fact]
    public void ParseIgnoresUnknownAndJunkLines()
    {
        var services = SshServiceCenter.Parse("系统没有使用 systemd\nnot-a-unit\n__SX_UNITS__\n123 units listed.");
        Assert.Empty(services);
    }

    [Fact]
    public async Task UnsafeUnitNamesNeverReachTheSshChannel()
    {
        // 单位名会拼进命令行；非法名字必须在本地被拒绝（不会建立任何连接）。
        var center = new SshServiceCenter(new SshClient("127.0.0.1", 22, "shellx"), NullLogger.Instance);
        var result = await center.ExecuteAsync("sshd.service; rm -rf /", ServiceAction.Stop);
        Assert.False(result.Success);
        Assert.Contains("非法字符", result.Message);
        Assert.Equal(-1, result.ExitCode);
        await Assert.ThrowsAsync<ArgumentException>(() => center.StatusAsync("bad unit name"));
    }
    [Fact]
    public void ParsesSsOutputWithAndWithoutProcessInfo()
    {
        var output = string.Join("\n",
            "State Recv-Q Send-Q Local Address:Port Peer Address:Port Process",                          // 表头必须被忽略
            "LISTEN 0 128 0.0.0.0:22 0.0.0.0:* users:((\"sshd\",pid=866,fd=3))",
            "LISTEN 0 128 [::]:8080 [::]:* users:((\"nginx\",pid=999,fd=6))",
            "LISTEN 0 128 127.0.0.1:631 0.0.0.0:*",                                                     // 非 root 看不到进程
            "UNCONN 0 0 0.0.0.0:68 0.0.0.0:* users:((\"dhclient\",pid=700,fd=6))");
        var ports = SshServiceCenter.ParseSs(output);
        Assert.Equal(new[] { 22, 68, 631, 8080 }, ports.Select(p => p.Port));   // 按端口排序
        var ssh = ports[0];
        Assert.Equal("TCP", ssh.Protocol); Assert.Equal("LISTEN", ssh.State);
        Assert.Equal("0.0.0.0", ssh.Address); Assert.Equal("sshd", ssh.ProcessName); Assert.Equal("866", ssh.ProcessId);
        var web = ports.Single(p => p.Port == 8080);
        Assert.Equal("[::]", web.Address); Assert.Equal("nginx", web.ProcessName);
        var noProcess = ports.Single(p => p.Port == 631);
        Assert.Equal("—", noProcess.PidDisplay); Assert.Equal("—", noProcess.ProcessDisplay);
        var udp = ports.Single(p => p.Port == 68);
        Assert.Equal("UDP", udp.Protocol); Assert.Equal("UNCONN", udp.State); Assert.Equal("700", udp.ProcessId);
    }

    [Fact]
    public void ParsesNetstatOutputIncludingRowsWithoutPid()
    {
        var output = string.Join("\n",
            "Active Internet connections (only servers)",
            "Proto Recv-Q Send-Q Local Address Foreign Address State PID/Program name",
            "tcp 0 0 0.0.0.0:3306 0.0.0.0:* LISTEN 1234/mysqld",
            "tcp 0 0 127.0.0.1:8000 0.0.0.0:* LISTEN -",
            "udp 0 0 0.0.0.0:68 0.0.0.0:* 700/dhclient");
        var ports = SshServiceCenter.ParseNetstat(output);
        Assert.Equal(new[] { 68, 3306, 8000 }, ports.Select(p => p.Port));
        var db = ports.Single(p => p.Port == 3306);
        Assert.Equal("TCP", db.Protocol); Assert.Equal("LISTEN", db.State);
        Assert.Equal("mysqld", db.ProcessName); Assert.Equal("1234", db.ProcessId);
        Assert.Equal("—", ports.Single(p => p.Port == 8000).PidDisplay);
        var dhcp = ports.Single(p => p.Port == 68);
        Assert.Equal("UDP", dhcp.Protocol); Assert.Equal("UNCONN", dhcp.State);
        Assert.Equal("dhclient", dhcp.ProcessName); Assert.Equal("700", dhcp.ProcessId);
    }

    [Fact]
    public void ParsePortsPrefersSsAndFallsBackToNetstat()
    {
        var ssSection = "LISTEN 0 128 0.0.0.0:7000 0.0.0.0:* users:((" + '"' + "sshd" + '"' + ",pid=10,fd=3))";
        var netstatSection = "tcp 0 0 0.0.0.0:7100 0.0.0.0:* LISTEN 20/sshd";
        var marker = "__SX_PORTS__";
        Assert.Equal(7000, Assert.Single(SshServiceCenter.ParsePorts(ssSection + "\n" + marker + "\n" + netstatSection)).Port);
        Assert.Equal(7100, Assert.Single(SshServiceCenter.ParsePorts(marker + "\n" + netstatSection)).Port);
        Assert.Empty(SshServiceCenter.ParsePorts(""));
    }
}
