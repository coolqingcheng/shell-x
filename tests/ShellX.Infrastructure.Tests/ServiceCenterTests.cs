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
}