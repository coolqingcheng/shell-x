using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ShellX.Core.Monitoring;
using ShellX.Core.Sessions;
using ShellX.Desktop.Controls;
using ShellX.Desktop.Services;
using ShellX.Infrastructure.Monitoring;

namespace ShellX.Desktop.Tests;

/// <summary>
/// 性能报告：把终端链路和监控采样的 CPU、内存占用量化出来。
/// 每个用例都打印一行"指标 + 数值"，配合 scripts/measure-performance.ps1 汇总成 docs/performance.md。
/// </summary>
[Collection("Performance")]
public sealed partial class PerformanceReportTests(ITestOutputHelper output)
{
    private const string Report = "PERF";

    [Fact]
    public void TerminalHighlightThroughput()
    {
        var payload = BuildLogPayload(1024 * 1024);                       // 1 MB 真实日志
        var highlighter = new TerminalHighlighter();
        highlighter.Transform(payload);                                   // 预热
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var watch = Stopwatch.StartNew();
        var rounds = 5;
        for (var i = 0; i < rounds; i++) highlighter.Transform(payload);
        watch.Stop();
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        var ms = watch.Elapsed.TotalMilliseconds / rounds;
        Report2("终端语义着色", $"{payload.Length / 1024} KB", $"{ms:0.00} ms", $"{ms / (payload.Length / 1024d):0.000} ms/KB", $"{allocated / (double)rounds:N0} B");
        Assert.True(ms / (payload.Length / 1024d) < 1.0, "着色耗时超过 1 ms/KB。");
    }

    [Fact]
    public async Task TerminalPipelineThroughput()
    {
        var payload = BuildLogPayload(1024 * 1024);
        var bytes = Encoding.UTF8.GetBytes(payload);
        await MeasureTerminalPipeline("终端字节流（高亮+通道）", bytes);
    }

    [Fact]
    public async Task TerminalPipelineDoesNotAllocatePerChunk()
    {
        // 通道里每块数据都走对象池：连续推 200 块，托管分配不应该随数据量线性增长。
        var source = new PushSource();
        var channel = new SessionTerminalChannel();
        channel.Attach(source);
        var consumed = 0L;
        var consumer = Task.Run(async () =>
        {
            var buffer = new byte[16 * 1024];
            while (true)
            {
                var read = await channel.Stream.ReadAsync(buffer);
                if (read == 0) break;
                Interlocked.Add(ref consumed, read);
            }
        });
        var block = Encoding.UTF8.GetBytes(new string('x', 4096));
        const int chunks = 400;
        await Task.Delay(100);                                  // 让消费端先跑起来
        var before = GC.GetTotalAllocatedBytes(precise: true);
        for (var i = 0; i < chunks; i++) await source.Push(block);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (Volatile.Read(ref consumed) < chunks * (long)block.Length && DateTime.UtcNow < deadline) await Task.Delay(20);
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        Report2("终端通道（400 块推入）", $"{chunks * block.Length / 1024} KB", "—", "—", $"{allocated:N0} B");
        Assert.True(Volatile.Read(ref consumed) >= chunks * (long)block.Length, "通道没有把数据全部送出去。");
        Assert.True(allocated < chunks * block.Length / 2, $"通道推入 {chunks} 块却分配了 {allocated:N0} 字节，池化没有生效。");
        await channel.DisposeAsync();
        await consumer;
    }

    [Fact]
    public void TerminalPipelineLegacyBaseline()
    {
        // 改动前的终端链路：着色结果先整块复制进 _pending，通道再为每一块新建一个数组。
        // 这里照着老实现复刻一遍，作为优化前后的对照基准。
        var payload = BuildLogPayload(1024 * 1024);
        var bytes = Encoding.UTF8.GetBytes(payload);
        using var source = new MemoryStream(bytes);
        var highlighter = new TerminalHighlighter();
        var decoder = Encoding.UTF8.GetDecoder();
        var input = new byte[32 * 1024];
        var characters = new char[32 * 1024 + 2];
        var pending = new byte[64 * 1024];
        var sink = 0;
        // 预热
        Drain(source, highlighter, decoder, input, characters, pending, ref sink);
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 8; i++) Drain(source, highlighter, decoder, input, characters, pending, ref sink);
        watch.Stop();
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        var perRound = allocated / 8;
        var ms = watch.Elapsed.TotalMilliseconds / 8;
        output.WriteLine($"{Report}|终端字节流（改动前）|1024 KB|{ms:0.00} ms|{ms / 1024:0.000} ms/KB|{perRound:N0} B（放大 {perRound / (double)bytes.Length:0.0} 倍）");
        Assert.True(sink > 0);
    }

    /// <summary>复刻改动前的终端链路：读一块 → 解码 → 着色 → 复制进 pending → 为这一块新建数组。</summary>
    private static void Drain(Stream source, TerminalHighlighter highlighter, Decoder decoder, byte[] input, char[] characters,
        byte[] pending, ref int sink)
    {
        int read;
        while ((read = source.Read(input, 0, input.Length)) > 0)
        {
            var decoded = decoder.GetChars(input, 0, read, characters, 0, false);
            var (produced, count) = highlighter.TransformToUtf8(characters.AsSpan(0, decoded));
            if (pending.Length < count) pending = new byte[count];
            produced.AsSpan(0, count).CopyTo(pending);
            var chunk = pending.AsSpan(0, count).ToArray();               // 旧实现：每块都新建数组
            sink += chunk.Length;
        }
        source.Position = 0;
    }

    [Fact]
    public void MonitorSampleCost()
    {
        var payload = BuildSamplePayload(processes: 600, disks: 20, networks: 8);
        LinuxMonitorParser.Parse(payload, DateTimeOffset.UtcNow, 12);
        const int rounds = 20;
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < rounds; i++) LinuxMonitorParser.Parse(payload, DateTimeOffset.UtcNow, 12);
        watch.Stop();
        var perRound = (GC.GetTotalAllocatedBytes(precise: true) - before) / (double)rounds;
        var perRoundMs = watch.Elapsed.TotalMilliseconds / rounds;
        Report2("监控采样解析（600 进程）", $"{payload.Length / 1024} KB", $"{perRoundMs:0.00} ms", $"{perRoundMs / 2:0.000} ms/次(2秒)", $"{perRound:N0} B");

        // 与改动前的正则实现对比，给出优化幅度。
        LegacyParser.Parse(payload, DateTimeOffset.UtcNow, 12);
        var legacyBefore = GC.GetTotalAllocatedBytes(precise: true);
        var legacyWatch = Stopwatch.StartNew();
        for (var i = 0; i < rounds; i++) LegacyParser.Parse(payload, DateTimeOffset.UtcNow, 12);
        legacyWatch.Stop();
        var legacyBytes = (GC.GetTotalAllocatedBytes(precise: true) - legacyBefore) / (double)rounds;
        var legacyMs = legacyWatch.Elapsed.TotalMilliseconds / rounds;
        Report2("监控采样解析（改动前）", $"{payload.Length / 1024} KB", $"{legacyMs:0.00} ms", $"{legacyMs / 2:0.000} ms/次(2秒)", $"{legacyBytes:N0} B");
        Assert.True(perRound < legacyBytes, "新的 Span 解析器分配比旧实现还多。");
    }

    [Fact]
    public void MultiConnectionMonitorCost()
    {
        // 每个连接每 2 秒采一次样，连接多了就是线性放大。
        var payload = BuildSamplePayload(processes: 300, disks: 10, networks: 4);
        foreach (var connections in new[] { 1, 5, 10 })
        {
            LinuxMonitorParser.Parse(payload, DateTimeOffset.UtcNow, 12);
            const int rounds = 10;
            var before = GC.GetTotalAllocatedBytes(precise: true);
            var watch = Stopwatch.StartNew();
            for (var i = 0; i < rounds; i++) LinuxMonitorParser.Parse(payload, DateTimeOffset.UtcNow, 12);
            watch.Stop();
            var perSample = (GC.GetTotalAllocatedBytes(precise: true) - before) / (double)rounds;
            var perSampleMs = watch.Elapsed.TotalMilliseconds / rounds;
            var cpuPerSecond = perSampleMs * connections / 2;             // 2 秒一次
            var garbagePerSecond = perSample * connections / 2;
            Report2($"{connections} 个连接监控", "300 进程", $"{perSampleMs:0.00} ms/次", $"{cpuPerSecond:0.00} % 单核/秒", $"{garbagePerSecond / 1024:0.0} KB/秒");
        }
    }

    [Fact]
    public async Task ProcessWorkingSetDuringStreaming()
    {
        // 终端持续刷屏时的进程内存：托管堆不应该跟着屏幕输出一起长。
        var payload = BuildLogPayload(64 * 1024);
        var bytes = Encoding.UTF8.GetBytes(payload);
        using var source = new ReplayStream(bytes);
        using var stream = new HighlightedTerminalStream(source, new TerminalHighlighter());
        var buffer = new byte[32 * 1024];
        await ReadExactly(stream, buffer, bytes.Length);                   // 预热
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var watch = Stopwatch.StartNew();
        const int iterations = 200;                                       // 约 12 MB 输出
        for (var i = 0; i < iterations; i++) await ReadExactly(stream, buffer, bytes.Length);
        watch.Stop();
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        var process = Process.GetCurrentProcess();
        process.Refresh();
        Report2("持续刷屏 12 MB", $"{iterations * bytes.Length / 1024 / 1024} MB", $"{watch.Elapsed.TotalMilliseconds:0} ms", $"{watch.Elapsed.TotalMilliseconds / (iterations * bytes.Length / 1024d):0.00} ms/KB", $"{allocated:N0} B 分配 / 工作集 {process.WorkingSet64 / 1024 / 1024} MB");
        Assert.True(allocated < 512 * 1024, $"刷屏过程分配 {allocated:N0} 字节过高。");
    }

    /// <summary>从流里读满指定字节数。</summary>
    private static async Task ReadExactly(Stream stream, byte[] buffer, int count)
    {
        var read = 0;
        while (read < count)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, count - read)));
            if (n == 0) throw new InvalidOperationException("流提前结束。");
            read += n;
        }
    }

    private void Report2(string name, string size, string time, string cpu, string memory)
        => output.WriteLine($"{Report}|{name}|{size}|{time}|{cpu}|{memory}");

    private async Task MeasureTerminalPipeline(string name, byte[] bytes)
    {
        using var source = new MemoryStream(bytes);
        using var stream = new HighlightedTerminalStream(source, new TerminalHighlighter());
        var buffer = new byte[32 * 1024];
        await stream.ReadAsync(buffer);                                   // 预热
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var watch = Stopwatch.StartNew();
        var total = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer)) > 0) total += read;
        watch.Stop();
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        var kb = bytes.Length / 1024d;
        var ms = watch.Elapsed.TotalMilliseconds;
        output.WriteLine($"{Report}|{name}|{kb:0} KB|{ms:0.00} ms|{ms / kb:0.000} ms/KB|{allocated:N0} B（放大 {allocated / (double)bytes.Length:0.0} 倍）");
        Assert.True(total > 0);
        Assert.True(allocated < bytes.Length, $"终端字节流分配 {allocated:N0} 字节，放大倍数过高。");
    }

    /// <summary>拼一份接近真实服务器输出的日志文本。</summary>
    private static string BuildLogPayload(int size)
    {
        var builder = new StringBuilder(size + 1024);
        var random = new Random(20240707);
        var index = 0;
        while (builder.Length < size)
        {
            builder.Append('[').Append(index++).Append("] INFO  build step finished in /home/user/project/src/module/file.cs status=ok url=https://example.com/build/ ip=10.0.0.")
                .Append(random.Next(1, 255)).Append(" latency=").Append(random.Next(1, 900)).Append("ms\r\n");
        }
        return builder.ToString();
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

    /// <summary>可以反复重放的字节流，用来测稳态刷屏。</summary>
    private sealed class ReplayStream(byte[] data) : Stream
    {
        private int _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count <= 0) return 0;
            var taken = Math.Min(count, data.Length - _position);
            Array.Copy(data, _position, buffer, offset, taken);
            _position += taken;
            if (_position >= data.Length) _position = 0;                     // 循环播放，永不结束
            return taken;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => new(Read(buffer.Span));                       // 同步完成，不产生 Task
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    private sealed class PushSource : ITerminalChannel
    {
        private readonly System.Threading.Channels.Channel<byte[]> _chunks = System.Threading.Channels.Channel.CreateUnbounded<byte[]>();
        public Stream Stream { get; }
        public PushSource() => Stream = new ChunkStream(_chunks);
        public Task Push(byte[] chunk) => _chunks.Writer.WriteAsync(chunk).AsTask();
        public void Resize(int columns, int rows) { }

        private sealed class ChunkStream(System.Threading.Channels.Channel<byte[]> chunks) : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                var chunk = await chunks.Reader.ReadAsync(cancellationToken);
                var take = Math.Min(count, chunk.Length);
                chunk.AsMemory(0, take).CopyTo(buffer.AsMemory(offset));
                return take;
            }
            public override void Flush() { }
            public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
            public override void SetLength(long v) => throw new NotSupportedException();
            public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
        }
    }

    /// <summary>改动前的正则解析实现，仅用于对比优化幅度。</summary>
    private static partial class LegacyParser
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static string[] Fields(string line, int count = int.MaxValue) => Whitespace().Split(line.Trim(), count);
        private static long Long(string value) => long.TryParse(value, NumberStyles.Integer, Invariant, out var n) ? n : 0;
        private static double Number(string value) => double.TryParse(value, NumberStyles.Float, Invariant, out var n) && double.IsFinite(n) ? n : 0;
        [GeneratedRegex(@"\s+")] private static partial Regex Whitespace();

        public static ServerSnapshot Parse(string output, DateTimeOffset time, double responseMilliseconds)
        {
            var sections = new Dictionary<string, List<string>>(); string? section = null;
            foreach (var line in output.Replace("\r", "").Split('\n'))
            {
                if (line.StartsWith("__SX_", StringComparison.Ordinal) && line.EndsWith("__", StringComparison.Ordinal))
                { section = line; sections[section] = []; }
                else if (section != null && !string.IsNullOrWhiteSpace(line)) sections[section].Add(line.Trim());
            }
            List<string> Get(string key) => sections.GetValueOrDefault($"__SX_{key}__") ?? [];
            var cpuFields = Fields(Get("CPU").FirstOrDefault() ?? "");
            if (cpuFields.Length < 5 || cpuFields[0] != "cpu") throw new InvalidDataException("服务器未返回有效的 CPU 采样。");
            var cpu = cpuFields.Skip(1).Take(8).Select(x => ulong.TryParse(x, out var n) ? n : 0).ToArray();
            var total = cpu.Aggregate(0UL, (sum, n) => sum + n);
            var idle = cpu[3] + (cpu.Length > 4 ? cpu[4] : 0);
            var memory = new Dictionary<string, long>();
            foreach (var line in Get("MEMORY"))
            {
                var pair = line.Split(':', 2); if (pair.Length != 2) continue;
                memory[pair[0]] = Long(Fields(pair[1])[0]) * 1024;
            }
            var memTotal = memory.GetValueOrDefault("MemTotal");
            if (memTotal <= 0) throw new InvalidDataException("服务器未返回有效的内存信息。");
            var available = memory.GetValueOrDefault("MemAvailable", memory.GetValueOrDefault("MemFree") + memory.GetValueOrDefault("Buffers") + memory.GetValueOrDefault("Cached") + memory.GetValueOrDefault("SReclaimable") - memory.GetValueOrDefault("Shmem"));
            var networks = new List<NetworkCounters>();
            foreach (var line in Get("NETWORK"))
            {
                var pair = line.Split(':', 2); if (pair.Length != 2) continue;
                var values = Fields(pair[1]); if (values.Length < 9) continue;
                if (ulong.TryParse(values[0], out var rx) && ulong.TryParse(values[8], out var tx)) networks.Add(new(pair[0].Trim(), rx, tx));
            }
            var processes = new List<ServerProcess>();
            foreach (var line in Get("PROCESS"))
            {
                var values = Fields(line, 4); if (values.Length < 4 || !int.TryParse(values[0], out var pid)) continue;
                processes.Add(new(pid, Number(values[1]), Long(values[2]) * 1024, values[3]));
            }
            var disks = new List<ServerDisk>();
            foreach (var line in Get("DISK"))
            {
                var values = Fields(line, 6); if (values.Length < 6 || !long.TryParse(values[1], out var size) || size <= 0) continue;
                disks.Add(new(values[0], values[5].Replace("\\040", " "), size * 1024, Long(values[3]) * 1024, Number(values[4].TrimEnd('%'))));
            }
            var system = Get("SYSTEM");
            var uptime = Number(Fields(Get("UPTIME").FirstOrDefault() ?? "0")[0]);
            return new(time, system.ElementAtOrDefault(0) ?? "—", system.ElementAtOrDefault(2) ?? "Linux", system.ElementAtOrDefault(1) ?? "—",
                system.ElementAtOrDefault(3) ?? "", TimeSpan.FromSeconds(Math.Clamp(uptime, 0, TimeSpan.MaxValue.TotalSeconds / 2)),
                string.Join("  ", Fields(Get("LOAD").FirstOrDefault() ?? "—").Take(3)), new(total, idle),
                memTotal, Math.Clamp(available, 0, memTotal), memory.GetValueOrDefault("SwapTotal"), memory.GetValueOrDefault("SwapFree"),
                networks, processes, disks, responseMilliseconds);
        }
    }
}
