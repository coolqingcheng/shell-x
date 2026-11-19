using System.Globalization;
using System.Text.RegularExpressions;
using ShellX.Core.Monitoring;
using ShellX.Infrastructure.Monitoring;

namespace ShellX.Infrastructure.Tests;

/// <summary>
/// 监控解析器改用 Span 扫描后，行为必须和改动前的正则实现完全一致。
/// 这里保留改动前的原始实现作为基准，用真实采样和随机采样逐字段比对。
/// </summary>
public sealed partial class MonitoringParserParityTests
{
    [Fact]
    public void RealSampleMatchesLegacyParser()
    {
        var sample = BuildSample(seed: 20240101, processes: 400, disks: 6, networks: 3, crlf: false);
        Compare(LegacyParser.Parse(sample, Stamp, 12), LinuxMonitorParser.Parse(sample, Stamp, 12));
    }

    [Fact]
    public void RandomSamplesMatchLegacyParser()
    {
        for (var seed = 0; seed < 60; seed++)
        {
            var crlf = seed % 2 == 0;
            var sample = BuildSample(seed: seed * 7919, processes: 5 + seed % 40, disks: seed % 5, networks: seed % 4, crlf: crlf);
            Compare(LegacyParser.Parse(sample, Stamp, 12), LinuxMonitorParser.Parse(sample, Stamp, 12));
        }
    }

    [Fact]
    public void WeirdSamplesMatchLegacyParser()
    {
        var samples = new[]
        {
            // 只有标记、没有内容
            "__SX_CPU__\n__SX_MEMORY__\n",
            // 分段顺序颠倒、内容里混着空行
            "__SX_DISK__\n/dev/sda1 100 20 80 20% /\n\n__SX_CPU__\ncpu 1 2 3 4 5 6 7 8 9\n__SX_MEMORY__\nMemTotal: 100 kB\n",
            // CRLF 与多余空格
            "__SX_CPU__\r\n  cpu   1 2 3 4 5  \r\n__SX_MEMORY__\r\nMemTotal:  2048 kB\r\n",
            // 同名分段出现两次，取最后一段
            "__SX_CPU__\ncpu 1 1 1 1 1\n__SX_CPU__\ncpu 9 9 9 9 9 9 9 9 9\n__SX_MEMORY__\nMemTotal: 100 kB\n",
            // 未知分段夹在中间
            "__SX_CPU__\ncpu 1 2 3 4 5\n__SX_OTHER__\nfoo\nbar\n__SX_MEMORY__\nMemTotal: 100 kB\n",
            // 挂载点带空格与转义
            "__SX_DISK__\n/dev/sda1 100 20 80 20% /mnt/my\\040disk\n",
            // 进程命令行带空格
            "__SX_PROCESS__\n123 1.5 2048 nginx: worker\n",
            // 字段数量不足
            "__SX_CPU__\ncpu 1 2 3\n__SX_MEMORY__\nMemTotal: 100 kB\n",
        };
        foreach (var sample in samples)
        {
            ServerSnapshot? expected = null; ServerSnapshot? actual = null;
            Exception? expectedError = null; Exception? actualError = null;
            try { expected = LegacyParser.Parse(sample, Stamp, 12); } catch (Exception ex) { expectedError = ex; }
            try { actual = LinuxMonitorParser.Parse(sample, Stamp, 12); } catch (Exception ex) { actualError = ex; }
            if (expectedError is not null || actualError is not null)
            {
                Assert.Equal(expectedError?.GetType(), actualError?.GetType());
                continue;
            }
            Compare(expected!, actual!);
        }
    }

    [Fact]
    public void MissingEverythingThrowsLikeLegacyParser()
    {
        Assert.Throws<InvalidDataException>(() => LinuxMonitorParser.Parse("not supported", Stamp, 0));
    }

    private static readonly DateTimeOffset Stamp = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static void Compare(ServerSnapshot expected, ServerSnapshot actual)
    {
        Assert.Equal(expected.Hostname, actual.Hostname);
        Assert.Equal(expected.OperatingSystem, actual.OperatingSystem);
        Assert.Equal(expected.Kernel, actual.Kernel);
        Assert.Equal(expected.Addresses, actual.Addresses);
        Assert.Equal(expected.Uptime, actual.Uptime);
        Assert.Equal(expected.LoadAverage, actual.LoadAverage);
        Assert.Equal(expected.Cpu, actual.Cpu);
        Assert.Equal(expected.MemoryTotal, actual.MemoryTotal);
        Assert.Equal(expected.MemoryAvailable, actual.MemoryAvailable);
        Assert.Equal(expected.SwapTotal, actual.SwapTotal);
        Assert.Equal(expected.SwapFree, actual.SwapFree);
        Assert.Equal(expected.ResponseMilliseconds, actual.ResponseMilliseconds);
        Assert.Equal(expected.Networks.Count, actual.Networks.Count);
        for (var i = 0; i < expected.Networks.Count; i++) Assert.Equal(expected.Networks[i], actual.Networks[i]);
        Assert.Equal(expected.Processes.Count, actual.Processes.Count);
        for (var i = 0; i < expected.Processes.Count; i++) Assert.Equal(expected.Processes[i], actual.Processes[i]);
        Assert.Equal(expected.Disks.Count, actual.Disks.Count);
        for (var i = 0; i < expected.Disks.Count; i++) Assert.Equal(expected.Disks[i], actual.Disks[i]);
    }

    /// <summary>拼一份和远端采样格式一样的输出，覆盖各种空白与取值。</summary>
    private static string BuildSample(int seed, int processes, int disks, int networks, bool crlf)
    {
        var random = new Random(seed);
        var builder = new System.Text.StringBuilder();
        void Line(string text) => builder.Append(text).Append(crlf ? "\r\n" : "\n");
        Line("__SX_SYSTEM__");
        Line("host-" + seed);
        Line("Linux 6.1.0 x86_64");
        Line("Debian GNU/Linux 12");
        Line("192.0.2.10");
        Line("__SX_UPTIME__");
        Line((90000 + seed * 3.5).ToString("0.00", CultureInfo.InvariantCulture) + " 2000.00");
        Line("__SX_LOAD__");
        Line("0.10 0.20 0.30 1/100 1234");
        Line("__SX_CPU__");
        Line("cpu 100 20 50 800 30 0 0 0 40 5");
        Line("__SX_MEMORY__");
        Line("MemTotal: 1000000 kB");
        if (seed % 3 != 0) Line("MemAvailable: 750000 kB"); else Line("MemFree: 100000 kB");
        Line("Buffers: 50000 kB");
        Line("Cached: 550000 kB");
        Line("SReclaimable: 1000 kB");
        Line("Shmem: 500 kB");
        Line("SwapTotal: 200000 kB");
        Line("SwapFree: 150000 kB");
        Line("__SX_NETWORK__");
        Line("Inter-| Receive | Transmit");
        for (var i = 0; i < networks; i++)
            Line($"eth{i}: {random.Next(0, 100000)} 0 0 0 0 0 0 0 {random.Next(0, 100000)} 0 0 0 0 0 0 0");
        Line("__SX_PROCESS__");
        for (var i = 0; i < processes; i++)
        {
            var pid = random.Next(1, 40000);
            var cpu = (random.NextDouble() * 100).ToString("0.##", CultureInfo.InvariantCulture);
            var rss = random.Next(100, 900000);
            var name = seed % 5 == 0 ? "nginx: worker process" : "bash";
            Line(seed % 4 == 0 ? $"{pid}  {cpu} {rss}  {name}" : $"{pid} {cpu} {rss} {name}");
        }
        Line("__SX_DISK__");
        Line("Filesystem 1024-blocks Used Available Capacity Mounted on");
        for (var i = 0; i < disks; i++)
        {
            var size = random.Next(1000, 9999999);
            var used = random.Next(0, size);
            var free = size - used;
            var percent = size == 0 ? 0 : (int)(used * 100L / size);
            var suffix = seed % 3 == 0 ? "\\040space" : "";
            Line($"/dev/sd{i} {size} {used} {free} {percent}% /mnt/data{suffix}");
        }
        return builder.ToString();
    }

    /// <summary>改动前的原始实现：正则切分 + 字典分段，只作为比对基准。</summary>
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
