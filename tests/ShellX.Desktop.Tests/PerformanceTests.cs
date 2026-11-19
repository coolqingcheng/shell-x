using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using ShellX.Core.Connections;
using ShellX.Core.Files;
using ShellX.Core.Monitoring;
using ShellX.Core.Security;
using ShellX.Core.Sessions;
using ShellX.Core.Settings;
using ShellX.Core.Tunnels;
using ShellX.Desktop.Controls;
using ShellX.Desktop.Features.Monitoring;
using ShellX.Desktop.Features.Security;
using ShellX.Desktop.Features.Shell;
using ShellX.Infrastructure.Monitoring;
using ShellX.Infrastructure.Security;
using ShellX.Desktop.Features.Workspace;
using ShellX.Desktop.Services;
using ShellX.Infrastructure.Files;

namespace ShellX.Desktop.Tests;

/// <summary>
/// 性能测试：量化监控刷新的分配/耗时，以及标签关闭后资源是否真的释放。
/// 这些用例只做测量，不修改产品行为；阈值放宽到"明显退化"才失败，避免抖动误报。
/// </summary>
public sealed class PerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task MonitorRefreshAllocationsStayBounded()
    {
        var snapshot = BuildSnapshot(processes: 600, disks: 20, networks: 8);
        var monitor = new ServerMonitorViewModel("host", new TestDialogs(), NullLogger<ServerMonitorViewModel>.Instance, _ => Task.CompletedTask);
        monitor.Start(new StaticSource(snapshot), CancellationToken.None);
        await monitor.RefreshCommand.ExecuteAsync(null);          // 预热：首帧包含 JIT 与集合初始化

        const int rounds = 20;
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < rounds; i++) await monitor.RefreshCommand.ExecuteAsync(null);
        watch.Stop();
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        var perRound = allocated / (double)rounds;
        var perRoundMs = watch.Elapsed.TotalMilliseconds / rounds;

        output.WriteLine($"监控刷新（600 进程 / 20 磁盘 / 8 网卡）：每次 {perRound:N0} 字节，{perRoundMs:0.00} ms");
        Assert.True(perRound < 512 * 1024, $"单次刷新分配 {perRound:N0} 字节过高，2 秒一次会持续制造垃圾。");
        Assert.True(perRoundMs < 25, $"单次刷新耗时 {perRoundMs:0.00} ms 过高。");
        Assert.Equal(8, monitor.Processes.Count);
        Assert.Equal(20, monitor.Disks.Count);
    }

    [Fact]
    public async Task MonitorRefreshAllocatesLittleWhenNothingChanged()
    {
        var snapshot = BuildSnapshot(processes: 600, disks: 20, networks: 8);
        var monitor = new ServerMonitorViewModel("host", new TestDialogs(), NullLogger<ServerMonitorViewModel>.Instance, _ => Task.CompletedTask);
        monitor.Start(new StaticSource(snapshot), CancellationToken.None);
        await monitor.RefreshCommand.ExecuteAsync(null);
        var before = GC.GetTotalAllocatedBytes(precise: true);
        const int rounds = 50;
        for (var i = 0; i < rounds; i++) await monitor.RefreshCommand.ExecuteAsync(null);
        var perRound = (GC.GetTotalAllocatedBytes(precise: true) - before) / (double)rounds;
        output.WriteLine($"监控刷新（数据不变）：每次 {perRound:N0} 字节");
        Assert.True(perRound < 256 * 1024, $"数据不变时单次刷新仍分配 {perRound:N0} 字节。");
    }

    [AvaloniaFact]
    public async Task ClosingTabStopsMonitorSampling()
    {
        var registry = new SharedMonitorRegistry(new TestDialogs(), NullLoggerFactory.Instance);
        var source = new CountingSource();
        var tab = await NewTab(registry, source);
        await tab.ConnectWithSecretAsync(new ConnectionSecret("secret"));
        Assert.True(tab.IsConnected);
        await WaitUntil(() => source.Reads > 0);
        var readsAfterConnect = source.Reads;

        await tab.DisposeAsync();
        await Task.Delay(200);
        var readsAfterClose = source.Reads;
        await Task.Delay(1200);                                   // 越过一个采样周期
        output.WriteLine($"连接时采样 {readsAfterConnect} 次，关闭后 {readsAfterClose} 次，等待 1.2 秒后 {source.Reads} 次");
        Assert.Equal(readsAfterClose, source.Reads);             // 关闭后不得再有采样
    }

    [AvaloniaFact]
    public async Task ClosedTabIsReleasedToGarbageCollector()
    {
        var registry = new SharedMonitorRegistry(new TestDialogs(), NullLoggerFactory.Instance);
        var weak = await CreateAndDisposeTab(registry, new CountingSource());
        await DrainDispatcher();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(weak.IsAlive, "关闭的标签仍然被引用，内存不会释放。");
    }

    [AvaloniaFact]
    public async Task RepeatedConnectCyclesKeepManagedMemoryBounded()
    {
        var registry = new SharedMonitorRegistry(new TestDialogs(), NullLoggerFactory.Instance);
        await WarmUp(registry);
        var before = GC.GetTotalMemory(true);
        const int cycles = 25;
        for (var i = 0; i < cycles; i++)
        {
            var tab = await NewTab(registry, new CountingSource());
            await tab.ConnectWithSecretAsync(new ConnectionSecret("secret"));
            await tab.DisposeAsync();
        }
        await DrainDispatcher();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var after = GC.GetTotalMemory(true);
        var growth = after - before;
        output.WriteLine($"{cycles} 次连接/关闭：托管内存 {before / 1024 / 1024:0.0} MB → {after / 1024 / 1024:0.0} MB（增长 {growth / 1024 / 1024:0.0} MB）");
        Assert.True(growth < 24 * 1024 * 1024, $"{cycles} 次连接/关闭后内存增长 {growth / 1024 / 1024:0.0} MB，疑似泄漏。");
    }

    [AvaloniaFact]
    public async Task TunnelRetryLoopStopsAfterTabClosed()
    {
        var registry = new SharedMonitorRegistry(new TestDialogs(), NullLoggerFactory.Instance);
        var profile = new ConnectionProfile
        {
            Name = "隧道重试", Host = "127.0.0.1", Port = 22, Username = "root",
            TunnelAutoRetry = true, TunnelRetrySeconds = 3,
            Tunnels = [new TunnelDefinition { Name = "数据库", BindPort = 13306, TargetPort = 3306 }]
        };
        var tunnels = new RecordingTunnelService();
        var tab = await NewTab(registry, new CountingSource(), profile, tunnels);
        tab.Session = new FakeSession(tunnels, new CountingSource());
        tab.IsConnected = true;
        await Task.Delay(1200);                                   // 触发一轮重试调度
        var attemptsBefore = tunnels.StartAttempts;
        await tab.DisposeAsync();
        await Task.Delay(1500);
        output.WriteLine($"关闭前重试 {attemptsBefore} 次，关闭后累计 {tunnels.StartAttempts} 次");
        Assert.True(tunnels.StartAttempts <= attemptsBefore, "标签关闭后隧道重试仍在继续。");
    }

    [Fact]
    public void TerminalByteStreamKeepsGarbageLow()
    {
        var chunk = string.Join("\r\n", Enumerable.Range(0, 400).Select(i =>
            $"[{i}] INFO  build step {i} finished in /home/user/project/src/module{i}/file.cs status=ok url=https://example.com/build/{i} ip=10.0.0.{i % 255}"));
        var bytes = System.Text.Encoding.UTF8.GetBytes(chunk);
        using var source = new MemoryStream(bytes);
        using var stream = new HighlightedTerminalStream(source, new TerminalHighlighter());
        var buffer = new byte[32 * 1024];
        stream.Read(buffer, 0, buffer.Length);                       // 预热
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var watch = Stopwatch.StartNew();
        var total = 0;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) total += read;
        watch.Stop();
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        output.WriteLine($"终端字节流（{bytes.Length / 1024} KB）：{watch.Elapsed.TotalMilliseconds:0.0} ms，分配 {allocated:N0} 字节（放大 {allocated / (double)bytes.Length:0.0} 倍）");
        Assert.True(total > 0);
        Assert.True(allocated < bytes.Length * 1.5, $"终端字节流分配 {allocated:N0} 字节，放大倍数过高。");
    }

    [Fact]
    public void MonitorParserHandlesLargeProcessTableCheaply()
    {
        var payload = BuildSamplePayload(processes: 600, disks: 20, networks: 8);
        LinuxMonitorParser.Parse(payload, DateTimeOffset.UtcNow, 12);       // 预热
        const int rounds = 20;
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < rounds; i++) LinuxMonitorParser.Parse(payload, DateTimeOffset.UtcNow, 12);
        watch.Stop();
        var perRound = (GC.GetTotalAllocatedBytes(precise: true) - before) / (double)rounds;
        var perRoundMs = watch.Elapsed.TotalMilliseconds / rounds;
        output.WriteLine($"监控采样解析（600 进程）：每次 {perRound:N0} 字节，{perRoundMs:0.00} ms");
        Assert.True(perRoundMs < 15, $"单次采样解析耗时 {perRoundMs:0.00} ms 过高。");
        Assert.True(perRound < 1024 * 1024, $"单次采样解析分配 {perRound:N0} 字节过高。");
    }

    [Fact]
    public void TerminalHighlighterKeepsUpWithFloodingOutput()
    {
        var highlighter = new TerminalHighlighter();
        var chunk = string.Join("\r\n", Enumerable.Range(0, 400).Select(i =>
            $"[{i}] INFO  build step {i} finished in /home/user/project/src/module{i}/file.cs status=ok url=https://example.com/build/{i} ip=10.0.0.{i % 255}"));
        highlighter.Transform(chunk);
        const int rounds = 50;
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < rounds; i++) highlighter.Transform(chunk);
        watch.Stop();
        var perRound = (GC.GetTotalAllocatedBytes(precise: true) - before) / (double)rounds;
        var perRoundMs = watch.Elapsed.TotalMilliseconds / rounds;
        output.WriteLine($"终端高亮（每次约 {chunk.Length / 1024} KB）：每次 {perRound:N0} 字节，{perRoundMs:0.00} ms");
        Assert.True(perRoundMs < 120, $"单次高亮耗时 {perRoundMs:0.00} ms 过高。");
    }

    [AvaloniaFact]
    public async Task ClosingAllWindowTabsReleasesEverything()
    {
        var profile = new ConnectionProfile { Name = "压测", Host = "127.0.0.1", Port = 22, Username = "root" };
        var source = new CountingSource();
        var registry = new SharedMonitorRegistry(new TestDialogs(), NullLoggerFactory.Instance);
        var vm = new MainWindowViewModel(new FakeRepository(profile), new FakeCredentials(), new FakeHosts(),
            new FakeSettingsStore(), new FakeFactory(source, new RecordingTunnelService()), new TestDialogs(),
            NullLoggerFactory.Instance, new LocalFileSystem(Path.Combine(Path.GetTempPath(), "shellx-perf-" + Guid.NewGuid().ToString("N"))),
            new AppLockService(new LockPasswordHasher()), new FakeExporter());
        var weak = await OpenAndCloseTabs(vm, registry, source, 3);
        await DrainDispatcher();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(weak.IsAlive, "窗口关闭全部标签后，标签对象仍然被引用。");
        Assert.Empty(vm.Tabs);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> OpenAndCloseTabs(MainWindowViewModel vm, SharedMonitorRegistry registry, IServerMonitor source, int count)
    {
        var tabs = new List<WorkspaceTabViewModel>();
        for (var i = 0; i < count; i++)
        {
            var tab = new WorkspaceTabViewModel(new ConnectionProfile { Name = "压测" + i, Host = "127.0.0.1", Port = 22, Username = "root" },
                new FakeFactory(source, new RecordingTunnelService()), new TestDialogs(), NullLoggerFactory.Instance,
                () => Task.FromResult<ConnectionSecret?>(null), _ => Task.FromResult(true), _ => Task.CompletedTask, _ => Task.CompletedTask,
                new AppSettings(), new LocalFileSystem(Path.Combine(Path.GetTempPath(), "shellx-perf-" + Guid.NewGuid().ToString("N"))),
                monitors: registry);
            tabs.Add(tab);
        }
        var weak = new WeakReference(tabs[0]);
        foreach (var tab in tabs) await tab.DisposeAsync();
        return weak;
    }

    private static string BuildSamplePayload(int processes, int disks, int networks)
    {
        var text = new StringBuilder();
        text.AppendLine("__SX_SYSTEM__\nfixture-linux\nLinux 6.1.0 x86_64\nDebian GNU/Linux 12\n127.0.0.1");
        text.AppendLine("__SX_UPTIME__\n90061 1000");
        text.AppendLine("__SX_LOAD__\n0.12 0.08 0.05 1/20 123");
        text.AppendLine("__SX_CPU__\ncpu  100 0 100 800 0 0 0 0 0 0");
        text.AppendLine("__SX_MEMORY__\nMemTotal: 8388608 kB\nMemAvailable: 6291456 kB\nSwapTotal: 1048576 kB\nSwapFree: 1048576 kB");
        text.AppendLine("__SX_NETWORK__");
        for (var i = 0; i < networks; i++) text.AppendLine($"eth{i}: 100000 0 0 0 0 0 0 0 20000 0 0 0 0 0 0 0");
        text.AppendLine("__SX_PROCESS__");
        for (var i = 0; i < processes; i++) text.AppendLine($"{1000 + i} {i % 100}.{i % 10} {1024 * (i % 64)} proc{i}");
        text.AppendLine("__SX_DISK__\nFilesystem 1024-blocks Used Available Capacity Mounted on");
        for (var i = 0; i < disks; i++) text.AppendLine($"/dev/sd{i} 20971520 4194304 16777216 20% /mnt{i}");
        return text.ToString();
    }

    // ---------- 辅助 ----------

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> CreateAndDisposeTab(SharedMonitorRegistry registry, IServerMonitor source)
    {
        var tab = await NewTab(registry, source);
        await tab.ConnectWithSecretAsync(new ConnectionSecret("secret"));
        var weak = new WeakReference(tab);
        await tab.DisposeAsync();
        return weak;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task WarmUp(SharedMonitorRegistry registry)
    {
        var tab = await NewTab(registry, new CountingSource());
        await tab.ConnectWithSecretAsync(new ConnectionSecret("secret"));
        await tab.DisposeAsync();
    }

    private static async Task<WorkspaceTabViewModel> NewTab(SharedMonitorRegistry registry, IServerMonitor source,
        ConnectionProfile? profile = null, RecordingTunnelService? tunnels = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "shellx-perf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        profile ??= new ConnectionProfile { Name = "性能测试", Host = "127.0.0.1", Port = 22, Username = "root" };
        var service = tunnels ?? new RecordingTunnelService();
        return new WorkspaceTabViewModel(profile, new FakeFactory(source, service), new TestDialogs(), NullLoggerFactory.Instance,
            () => Task.FromResult<ConnectionSecret?>(null), _ => Task.FromResult(true),
            _ => Task.CompletedTask, _ => Task.CompletedTask, new AppSettings(), new LocalFileSystem(root),
            monitors: registry);
    }

    private static async Task DrainDispatcher()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { });
        await Task.Delay(120);
        await Dispatcher.UIThread.InvokeAsync(() => { });
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(20);
    }

    private static ServerSnapshot BuildSnapshot(int processes, int disks, int networks)
    {
        var processList = new List<ServerProcess>(processes);
        for (var i = 0; i < processes; i++) processList.Add(new(1000 + i, i % 100, 1024 * 1024 * (i % 64), "proc" + i));
        var diskList = new List<ServerDisk>(disks);
        for (var i = 0; i < disks; i++) diskList.Add(new("/dev/sd" + i, "/mnt" + i, 1024L * 1024 * 1024 * 100, 1024L * 1024 * 1024 * 50, 50));
        var networkList = new List<NetworkCounters>(networks);
        for (var i = 0; i < networks; i++) networkList.Add(new("eth" + i, (ulong)(1000 + i), (ulong)(2000 + i)));
        return new ServerSnapshot(DateTimeOffset.UtcNow, "host", "Linux", "kernel", "127.0.0.1", TimeSpan.FromHours(3), "0.1 0.2 0.3",
            new(100, 60), 1024L * 1024 * 1024 * 4, 1024L * 1024 * 1024 * 2, 0, 0, networkList, processList, diskList, 12);
    }

    private sealed class StaticSource(ServerSnapshot snapshot) : IServerMonitor
    {
        public Task<ServerSnapshot> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(snapshot);
        }
    }

    private sealed class CountingSource : IServerMonitor
    {
        private int _reads;
        public int Reads => Volatile.Read(ref _reads);
        public Task<ServerSnapshot> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _reads);
            return Task.FromResult(BuildSnapshot(processes: 20, disks: 2, networks: 2));
        }
    }

    private sealed class FakeFactory(IServerMonitor monitor, ITunnelService tunnels) : IRemoteSessionFactory
    {
        public Task<IRemoteSession> ConnectAsync(ConnectionProfile profile, ConnectionSecret secret,
            Func<HostKeyChallenge, Task<bool>> verifyHost, CancellationToken cancellationToken)
            => Task.FromResult<IRemoteSession>(new FakeSession(tunnels, monitor));
    }

    private sealed class FakeSession(ITunnelService tunnels, IServerMonitor monitor) : IRemoteSession
    {
        public ITerminalChannel Terminal { get; } = new FakeTerminal();
        public IRemoteFileSystem? Files => null;
        public string? FileError => null;
        public ITunnelService Tunnels { get; } = tunnels;
        public IServerMonitor Monitor { get; } = monitor;
        public event EventHandler<string>? Disconnected;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeTerminal : ITerminalChannel
    {
        public Stream Stream => Stream.Null;
        public void Resize(int columns, int rows) { }
    }

    private sealed class RecordingTunnelService : ITunnelService
    {
        private readonly Dictionary<Guid, TunnelStatus> _statuses = [];
        private int _attempts;
        public int StartAttempts => Volatile.Read(ref _attempts);
        public event EventHandler<TunnelStatus>? StatusChanged;
        public bool IsRunning(Guid id) => false;
        public TunnelStatus GetStatus(Guid id) => _statuses.GetValueOrDefault(id) ?? new(id, TunnelState.Stopped, false);
        public Task StartAsync(TunnelDefinition definition, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _attempts);
            var status = new TunnelStatus(definition.Id, TunnelState.Failed, false, "端口被占用");
            _statuses[definition.Id] = status; StatusChanged?.Invoke(this, status);
            return Task.CompletedTask;
        }
        public Task StopAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CheckAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TestDialogs : IDialogService
    {
        public Task<ConnectionEditResult?> EditConnectionAsync(ConnectionProfile? profile, ConnectionSecret? secret)
            => Task.FromResult<ConnectionEditResult?>(null);
        public Task<bool> ConfirmAsync(string title, string message, bool danger = false) => Task.FromResult(true);
        public Task<string?> PromptAsync(string title, string label, string value = "", bool secret = false) => Task.FromResult<string?>(null);
        public Task ShowErrorAsync(string message) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> PickUploadAsync() => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> PickDownloadAsync(string name) => Task.FromResult<string?>(null);
        public Task EditFileAsync(TextDocument document, Func<TextDocument, string, Task<TextDocument>> save) => Task.CompletedTask;
        public Task<int?> PermissionsAsync(RemoteEntry entry) => Task.FromResult<int?>(null);
        public Task<TunnelDefinition?> EditTunnelAsync() => Task.FromResult<TunnelDefinition?>(null);
        public Task<AppSettings?> SettingsAsync(AppSettings settings) => Task.FromResult<AppSettings?>(null);
    }

    private sealed class FakeRepository(ConnectionProfile profile) : IConnectionRepository
    {
        public Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ConnectionProfile>>([profile]);
        public Task SaveAsync(IReadOnlyList<ConnectionProfile> connections, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeCredentials : ICredentialStore
    {
        public Task<ConnectionSecret?> ReadAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<ConnectionSecret?>(new ConnectionSecret("secret"));
        public Task SaveAsync(Guid id, ConnectionSecret secret, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeHosts : IHostKeyStore
    {
        public Task<string?> GetAsync(string host, int port, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>("fingerprint");
        public Task TrustAsync(HostKeyChallenge host, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(new AppSettings());
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeExporter : IConfigurationExporter
    {
        public Task ExportAsync(string path, ConnectionCatalog catalog, AppSettings settings, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
