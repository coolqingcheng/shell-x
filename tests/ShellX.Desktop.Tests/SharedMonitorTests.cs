using Avalonia.Headless.XUnit;
using Microsoft.Extensions.Logging.Abstractions;
using ShellX.Core.Connections;
using ShellX.Core.Monitoring;
using ShellX.Desktop.Features.Monitoring;

namespace ShellX.Desktop.Tests;

public sealed class SharedMonitorTests
{
    [AvaloniaFact]
    public async Task SameEndpointSamplesOnceAndRetainsHistoryAndNavigationWhenFirstTabCloses()
    {
        var registry = new SharedMonitorRegistry(null!, NullLoggerFactory.Instance);
        var profile = new ConnectionProfile { Host = "EXAMPLE.com.", Username = "root" };
        var selected = 1; var navigation = new List<string>();
        await using var first = registry.Acquire(profile, () => selected == 1, path => { navigation.Add("1:" + path); return Task.CompletedTask; }, null);
        await using var second = registry.Acquire(profile with { Host = "example.com" }, () => selected == 2, path => { navigation.Add("2:" + path); return Task.CompletedTask; }, null);
        await using var other = registry.Acquire(profile with { Port = 2222 }, () => false, _ => Task.CompletedTask, null);
        await using var user = registry.Acquire(profile with { Username = "other" }, () => false, _ => Task.CompletedTask, null);
        Assert.Same(first.Monitor, second.Monitor); Assert.NotSame(first.Monitor, other.Monitor); Assert.NotSame(first.Monitor, user.Monitor);
        var one = new CountingSource("one"); var two = new CountingSource("two");
        await first.SetSourceAsync(one); await second.SetSourceAsync(two);
        await first.Monitor.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(2, one.Reads); Assert.Equal(0, two.Reads);
        var history = first.Monitor.ResponseHistory;
        selected = 2;
        await first.Monitor.OpenDiskCommand.ExecuteAsync(first.Monitor.Disks.Single());
        Assert.Equal(["2:/data"], navigation);
        await first.DisposeAsync(); Assert.True(second.Monitor.IsMonitoring);
        Assert.Same(history, second.Monitor.ResponseHistory);
        await second.Monitor.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(1, two.Reads); Assert.Equal("two", second.Monitor.Hostname);
        Assert.Equal(history.Count + 1, second.Monitor.ResponseHistory.Count);
        await second.DisposeAsync(); Assert.False(second.Monitor.IsMonitoring);
        await second.SetSourceAsync(one); Assert.False(second.Monitor.IsMonitoring);
    }

    [AvaloniaFact]
    public async Task ClosingSamplingOwnerCancelsItsInflightCommandAndContinuesWithAnotherTab()
    {
        var registry = new SharedMonitorRegistry(null!, NullLoggerFactory.Instance);
        var profile = new ConnectionProfile { Host = "host" };
        await using var first = registry.Acquire(profile, () => false, _ => Task.CompletedTask, null);
        await using var second = registry.Acquire(profile, () => true, _ => Task.CompletedTask, null);
        var blocking = new BlockingSource(); var fallback = new CountingSource("fallback");
        await first.SetSourceAsync(blocking); await second.SetSourceAsync(fallback);
        Assert.True(blocking.Started);
        await first.SetSourceAsync(null);
        for (var i = 0; i < 100 && fallback.Reads == 0; i++) await Task.Delay(10);
        Assert.True(blocking.Canceled); Assert.Equal(1, fallback.Reads);
        Assert.Equal("fallback", second.Monitor.Hostname); Assert.True(second.Monitor.IsMonitoring);
        Assert.DoesNotContain("失败", second.Monitor.Status);
    }

    private sealed class CountingSource(string name) : IServerMonitor
    {
        public int Reads { get; private set; }
        public Task<ServerSnapshot> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Reads++;
            return Task.FromResult(new ServerSnapshot(DateTimeOffset.UtcNow, name, "Linux", "kernel", "127.0.0.1", TimeSpan.FromHours(1), "0", new((ulong)Reads * 100, (ulong)Reads * 50),
                1024, 512, 0, 0, [], [new(1, 10, 256, "sshd")], [new("disk", "/data", 100, 50, 50)], Reads));
        }
    }
    private sealed class BlockingSource : IServerMonitor
    {
        public bool Started { get; private set; }
        public bool Canceled { get; private set; }
        public async Task<ServerSnapshot> ReadAsync(CancellationToken cancellationToken)
        {
            Started = true;
            try { await Task.Delay(Timeout.Infinite, cancellationToken); throw new InvalidOperationException(); }
            catch (OperationCanceledException) { Canceled = true; throw; }
        }
    }
}
