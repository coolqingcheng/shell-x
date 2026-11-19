using System.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.RegularExpressions;
using Renci.SshNet;
using ShellX.Core.Monitoring;

namespace ShellX.Infrastructure.Monitoring;

internal sealed class LinuxServerMonitor(SshClient ssh, ILogger log) : IServerMonitor
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

    private static string Snippet(string output)
    {
        var text = output.Replace("\r", "").Trim();
        return text.Length <= 1200 ? text : text[..1200] + " …";
    }

    // C# 原始字符串会原样保留源文件的换行符，Windows 检出后是 CRLF，
    // 远端 bash 遇到 \r 会直接报语法错误导致没有任何输出，这里统一成 LF。
    internal static readonly string SampleScript = SampleCommand.Replace("\r\n", "\n").Replace("\r", "\n");

    public async Task<ServerSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        using var command = ssh.CreateCommand(SampleScript);
        command.CommandTimeout = TimeSpan.FromSeconds(8);
        var stopwatch = Stopwatch.StartNew();
        await command.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();
        if (command.Result.Contains("__SX_UNSUPPORTED__", StringComparison.Ordinal))
            throw new NotSupportedException("资源监控目前支持可读取 /proc 的 Linux 服务器。");
        try
        {
            return LinuxMonitorParser.Parse(command.Result, DateTimeOffset.UtcNow, stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (InvalidDataException ex)
        {
            // 记录原始输出，便于定位远端 /proc 或命令差异。
            log.LogWarning("服务器监控解析失败：{Reason}\n退出码={Exit} 错误输出={Error}\n原始输出长度={Length}\n原始输出：{Raw}", ex.Message, command.ExitStatus, command.Error, command.Result.Length, Snippet(command.Result));
            throw;
        }
    }
}

public static class LinuxMonitorParser
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    // 采样输出按 __SX_XXX__ 分段；同名分段以最后一次出现为准。
    private enum Section { None, System, Uptime, Load, Cpu, Memory, Network, Process, Disk }

    /// <summary>
    /// 解析一次服务器采样。整段输出只扫描一遍，字段直接用 Span 切，
    /// 不再把每一行拆成字符串数组：一次采样上千行，中间字符串是主要的垃圾来源。
    /// </summary>
    public static ServerSnapshot Parse(string output, DateTimeOffset time, double responseMilliseconds)
    {
        var text = output.AsSpan();
        var bounds = new SectionBounds[9];
        for (var i = 0; i < bounds.Length; i++) bounds[i] = SectionBounds.Empty;

        // 第一遍：只找分段边界，不复制任何内容。
        var current = Section.None;
        var contentStart = 0;
        var position = 0;
        while (position < text.Length)
        {
            var lineStart = position;
            var newline = text[lineStart..].IndexOf('\n');
            var lineEnd = newline < 0 ? text.Length : lineStart + newline;
            var next = newline < 0 ? text.Length : lineEnd + 1;
            var line = text[lineStart..lineEnd];
            if (line.Length > 0 && line[^1] == '\r') line = line[..^1];
            if (IsMarker(line, out var kind))
            {
                if (current != Section.None) bounds[(int)current] = new SectionBounds(contentStart, lineStart);
                current = kind;
                contentStart = next;
            }
            position = next;
        }
        if (current != Section.None) bounds[(int)current] = new SectionBounds(contentStart, text.Length);

        // ---- CPU ----
        if (!FirstLine(text, bounds[(int)Section.Cpu], out var cpuText)) throw new InvalidDataException("服务器未返回有效的 CPU 采样。");
        if (TokenCount(cpuText) < 5 || !Token(cpuText, 0).SequenceEqual("cpu")) throw new InvalidDataException("服务器未返回有效的 CPU 采样。");
        Span<ulong> counters = stackalloc ulong[8];
        for (var i = 0; i < counters.Length; i++)
        {
            if (i + 1 >= TokenCount(cpuText)) break;
            counters[i] = ulong.TryParse(Token(cpuText, i + 1), NumberStyles.Integer, Invariant, out var value) ? value : 0UL;
        }
        var total = 0UL;
        for (var i = 0; i < counters.Length; i++) total += counters[i];
        var idle = counters[3] + counters[4];

        // ---- 内存 ----
        long memTotal = 0, memFree = 0, buffers = 0, cached = 0, reclaimable = 0, shmem = 0, swapTotal = 0, swapFree = 0;
        long memAvailable = 0; var hasAvailable = false;
        foreach (var line in new SectionLines(text, bounds[(int)Section.Memory]))
        {
            var colon = line.IndexOf(':');
            if (colon < 0) continue;
            var key = line[..colon];
            var bytes = Long(FirstToken(line[(colon + 1)..])) * 1024;
            if (key.SequenceEqual("MemTotal")) memTotal = bytes;
            else if (key.SequenceEqual("MemAvailable")) { memAvailable = bytes; hasAvailable = true; }
            else if (key.SequenceEqual("MemFree")) memFree = bytes;
            else if (key.SequenceEqual("Buffers")) buffers = bytes;
            else if (key.SequenceEqual("Cached")) cached = bytes;
            else if (key.SequenceEqual("SReclaimable")) reclaimable = bytes;
            else if (key.SequenceEqual("Shmem")) shmem = bytes;
            else if (key.SequenceEqual("SwapTotal")) swapTotal = bytes;
            else if (key.SequenceEqual("SwapFree")) swapFree = bytes;
        }
        if (memTotal <= 0) throw new InvalidDataException("服务器未返回有效的内存信息。");
        var available = hasAvailable ? memAvailable : memFree + buffers + cached + reclaimable - shmem;

        // ---- 网卡 ----
        var networks = new List<NetworkCounters>();
        foreach (var line in new SectionLines(text, bounds[(int)Section.Network]))
        {
            var colon = line.IndexOf(':');
            if (colon < 0) continue;
            var values = line[(colon + 1)..];
            if (TokenCount(values) < 9) continue;
            if (ulong.TryParse(Token(values, 0), NumberStyles.Integer, Invariant, out var rx) && ulong.TryParse(Token(values, 8), NumberStyles.Integer, Invariant, out var tx))
                networks.Add(new(line[..colon].Trim().ToString(), rx, tx));
        }

        // ---- 进程 ----
        var processes = new List<ServerProcess>();
        foreach (var line in new SectionLines(text, bounds[(int)Section.Process]))
        {
            if (!Split4(line, out var pidText, out var cpuText2, out var rssText, out var command)) continue;
            if (!int.TryParse(pidText, NumberStyles.Integer, Invariant, out var pid)) continue;
            processes.Add(new(pid, Number(cpuText2), Long(rssText) * 1024, command.ToString()));
        }

        // ---- 磁盘 ----
        var disks = new List<ServerDisk>();
        foreach (var line in new SectionLines(text, bounds[(int)Section.Disk]))
        {
            if (!Split6(line, out var device, out var size, out _, out var availableText, out var capacity, out var mount)) continue;
            var totalBytes = Long(size);
            if (totalBytes <= 0) continue;
            disks.Add(new(device.ToString(), mount.ToString().Replace("\\040", " "), totalBytes * 1024, Long(availableText) * 1024, Number(capacity.TrimEnd('%'))));
        }

        // ---- 系统信息 ----
        var hostName = "—"; var operatingSystem = "Linux"; var kernel = "—"; var addresses = "";
        var systemIndex = 0;
        foreach (var line in new SectionLines(text, bounds[(int)Section.System]))
        {
            if (systemIndex == 0) hostName = line.ToString();
            else if (systemIndex == 1) kernel = line.ToString();
            else if (systemIndex == 2) operatingSystem = line.ToString();
            else if (systemIndex == 3) { addresses = line.ToString(); break; }
            systemIndex++;
        }

        // ---- 运行时间与负载 ----
        var uptimeSeconds = 0d;
        if (FirstLine(text, bounds[(int)Section.Uptime], out var uptimeLine)) uptimeSeconds = Number(FirstToken(uptimeLine));

        var loadAverage = "—";
        if (FirstLine(text, bounds[(int)Section.Load], out var loadLine))
        {
            var parts = new List<string>(3);
            var count = TokenCount(loadLine);
            for (var i = 0; i < count && i < 3; i++) parts.Add(Token(loadLine, i).ToString());
            loadAverage = string.Join("  ", parts);
        }

        return new(time, hostName, operatingSystem, kernel, addresses,
            TimeSpan.FromSeconds(Math.Clamp(uptimeSeconds, 0, TimeSpan.MaxValue.TotalSeconds / 2)), loadAverage, new(total, idle),
            memTotal, Math.Clamp(available, 0, memTotal), swapTotal, swapFree,
            networks, processes, disks, responseMilliseconds);
    }

    private readonly record struct SectionBounds(int Start, int End)
    {
        public static readonly SectionBounds Empty = new(-1, -1);
    }

    /// <summary>分段里的有效行：跳过空行，行首尾空白已经去掉。</summary>
    private ref struct SectionLines(ReadOnlySpan<char> text, SectionBounds bounds)
    {
        private readonly ReadOnlySpan<char> _text = text;
        private readonly int _end = bounds.End;
        private int _position = bounds.Start;

        public ReadOnlySpan<char> Current { get; private set; }

        public SectionLines GetEnumerator() => this;

        public bool MoveNext()
        {
            Current = default;
            while (_position < _end)
            {
                var start = _position;
                var newline = _text[start..].IndexOf('\n');
                int end;
                if (newline < 0) { end = _text.Length; _position = _text.Length; }
                else { end = start + newline; _position = end + 1; }
                var line = _text[start..end];
                if (line.Length > 0 && line[^1] == '\r') line = line[..^1];
                var trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                Current = trimmed;
                return true;
            }
            return false;
        }
    }

    /// <summary>取分段里的第一行有效内容；没有则返回 false。</summary>
    private static bool FirstLine(ReadOnlySpan<char> text, SectionBounds bounds, out ReadOnlySpan<char> line)
    {
        if (bounds.Start < 0) { line = default; return false; }
        foreach (var item in new SectionLines(text, bounds)) { line = item; return true; }
        line = default;
        return false;
    }

    private static bool IsMarker(ReadOnlySpan<char> line, out Section kind)
    {
        kind = Section.None;
        if (line.Length < 7 || !line.StartsWith("__SX_") || !line.EndsWith("__")) return false;
        var name = line[5..^2];
        if (name.SequenceEqual("SYSTEM")) kind = Section.System;
        else if (name.SequenceEqual("UPTIME")) kind = Section.Uptime;
        else if (name.SequenceEqual("LOAD")) kind = Section.Load;
        else if (name.SequenceEqual("CPU")) kind = Section.Cpu;
        else if (name.SequenceEqual("MEMORY")) kind = Section.Memory;
        else if (name.SequenceEqual("NETWORK")) kind = Section.Network;
        else if (name.SequenceEqual("PROCESS")) kind = Section.Process;
        else if (name.SequenceEqual("DISK")) kind = Section.Disk;
        return true;                                     // 未知分段也照样结束上一段
    }

    /// <summary>空白分隔符，语义等价于原来正则里的 \s。</summary>
    private static bool IsSeparator(char c)
        => c == ' ' || (c >= '\t' && c <= '\r') || c == '\u0085' || c == '\u00a0' || c == '\u1680'
        || (c >= '\u2000' && c <= '\u200a') || c == '\u2028' || c == '\u2029' || c == '\u202f' || c == '\u205f' || c == '\u3000';

    /// <summary>取下一段空白分隔的内容；没有下一段时返回 false。</summary>
    private static bool NextToken(ReadOnlySpan<char> line, scoped ref int position, out ReadOnlySpan<char> token)
    {
        token = default;
        while (position < line.Length && IsSeparator(line[position])) position++;
        if (position >= line.Length) return false;
        var start = position;
        while (position < line.Length && !IsSeparator(line[position])) position++;
        token = line[start..position];
        return true;
    }

    /// <summary>第 index 段内容（从 0 开始）；不存在时返回空。</summary>
    private static ReadOnlySpan<char> Token(ReadOnlySpan<char> line, int index)
    {
        var position = 0;
        for (var i = 0; i <= index; i++)
        {
            if (!NextToken(line, ref position, out var token)) return default;
            if (i == index) return token;
        }
        return default;
    }

    /// <summary>内容一共分成几段。</summary>
    private static int TokenCount(ReadOnlySpan<char> line)
    {
        var position = 0; var count = 0;
        while (NextToken(line, ref position, out _)) count++;
        return count;
    }

    /// <summary>第一段内容。</summary>
    private static ReadOnlySpan<char> FirstToken(ReadOnlySpan<char> line)
    {
        var position = 0;
        NextToken(line, ref position, out var token);
        return token;
    }

    /// <summary>切出前 4 段，最后一段是剩余的全部内容（进程命令行可能带空格）。</summary>
    private static bool Split4(ReadOnlySpan<char> line, out ReadOnlySpan<char> first, out ReadOnlySpan<char> second, out ReadOnlySpan<char> third, out ReadOnlySpan<char> rest)
    {
        first = second = third = rest = default;
        var position = 0;
        if (!NextToken(line, ref position, out first)) return false;
        if (!NextToken(line, ref position, out second)) return false;
        if (!NextToken(line, ref position, out third)) return false;
        if (!TryRemainder(line, ref position, out rest)) return false;
        return true;
    }

    /// <summary>切出前 6 段，最后一段是剩余的全部内容（挂载点可能带空格）。</summary>
    private static bool Split6(ReadOnlySpan<char> line, out ReadOnlySpan<char> first, out ReadOnlySpan<char> second, out ReadOnlySpan<char> third, out ReadOnlySpan<char> fourth, out ReadOnlySpan<char> fifth, out ReadOnlySpan<char> sixth)
    {
        first = second = third = fourth = fifth = sixth = default;
        var position = 0;
        if (!NextToken(line, ref position, out first)) return false;
        if (!NextToken(line, ref position, out second)) return false;
        if (!NextToken(line, ref position, out third)) return false;
        if (!NextToken(line, ref position, out fourth)) return false;
        if (!NextToken(line, ref position, out fifth)) return false;
        if (!TryRemainder(line, ref position, out sixth)) return false;
        return true;
    }

    /// <summary>剩余的全部内容（跳过前面的空白，末尾空白去掉）。</summary>
    private static bool TryRemainder(ReadOnlySpan<char> line, scoped ref int position, out ReadOnlySpan<char> rest)
    {
        rest = default;
        while (position < line.Length && IsSeparator(line[position])) position++;
        if (position >= line.Length) return false;
        rest = line[position..].TrimEnd();
        return true;
    }

    private static long Long(ReadOnlySpan<char> value) => long.TryParse(value, NumberStyles.Integer, Invariant, out var n) ? n : 0;
    private static double Number(ReadOnlySpan<char> value) => double.TryParse(value, NumberStyles.Float, Invariant, out var n) && double.IsFinite(n) ? n : 0;
}
