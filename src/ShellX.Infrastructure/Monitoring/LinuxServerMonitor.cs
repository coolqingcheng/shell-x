using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Renci.SshNet;
using ShellX.Core.Monitoring;

namespace ShellX.Infrastructure.Monitoring;

internal sealed class LinuxServerMonitor(SshClient ssh) : IServerMonitor
{
    // 使用独立 exec 通道读取指标，不向交互终端写入命令，不安装远端代理。
    internal const string SampleCommand = """
        LC_ALL=C; export LC_ALL
        if [ ! -r /proc/stat ] || [ ! -r /proc/meminfo ]; then printf '__SX_UNSUPPORTED__\n'; exit 42; fi
        printf '__SX_SYSTEM__\n'; hostname; uname -srm; if [ -r /etc/os-release ]; then (. /etc/os-release; printf '%s\n' "$PRETTY_NAME"); else printf 'Linux\n'; fi; hostname -I 2>/dev/null
        printf '__SX_UPTIME__\n'; cat /proc/uptime
        printf '__SX_LOAD__\n'; cat /proc/loadavg
        printf '__SX_CPU__\n'; head -n 1 /proc/stat
        printf '__SX_MEMORY__\n'; cat /proc/meminfo
        printf '__SX_NETWORK__\n'; cat /proc/net/dev
        printf '__SX_PROCESS__\n'
        processes=$(ps -eo pid=,pcpu=,rss=,comm= 2>/dev/null)
        if ! printf '%s\n' "$processes" | awk 'NF >= 4 { found=1 } END { exit(found ? 0 : 1) }'; then
            processes=$(ps -eo pid,pcpu,rss,comm 2>/dev/null)
        fi
        if ! printf '%s\n' "$processes" | awk 'NF >= 4 { found=1 } END { exit(found ? 0 : 1) }'; then
            processes=$(ps -ax -o pid,pcpu,rss,comm 2>/dev/null)
        fi
        if printf '%s\n' "$processes" | awk 'NF >= 4 { found=1 } END { exit(found ? 0 : 1) }'; then
            printf '%s\n' "$processes"
        else
            page_size=$(getconf PAGESIZE 2>/dev/null || printf '4096\n')
            for proc_stat in /proc/[0-9]*/stat; do
                [ -r "$proc_stat" ] || continue
                proc_dir=${proc_stat%/stat}
                pid=${proc_dir#/proc/}
                rss_pages=$(awk '{print $2}' "$proc_dir/statm" 2>/dev/null)
                [ -n "$rss_pages" ] || continue
                comm=$(tr -d '\n' < "$proc_dir/comm" 2>/dev/null)
                printf '%s 0 %s %s\n' "$pid" "$((rss_pages * page_size))" "$comm"
            done
        fi
        printf '__SX_DISK__\n'; df -Pk -x tmpfs -x devtmpfs -x squashfs 2>/dev/null
        """;

    public async Task<ServerSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        using var command = ssh.CreateCommand(SampleCommand);
        command.CommandTimeout = TimeSpan.FromSeconds(8);
        var stopwatch = Stopwatch.StartNew();
        await command.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();
        if (command.Result.Contains("__SX_UNSUPPORTED__", StringComparison.Ordinal))
            throw new NotSupportedException("资源监控目前支持可读取 /proc 的 Linux 服务器。");
        return LinuxMonitorParser.Parse(command.Result, DateTimeOffset.UtcNow, stopwatch.Elapsed.TotalMilliseconds);
    }
}

public static partial class LinuxMonitorParser
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
