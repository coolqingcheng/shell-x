using Renci.SshNet;
using ShellX.Core.Logs;

namespace ShellX.Infrastructure.Ssh;

/// <summary>
/// 用远端命令读取日志。tail / sed / grep 都在服务端执行，文件不会被整体拉回本地内存：
/// 尾部与增量只取需要的行区间，检索用 grep 并限制结果条数。
/// </summary>
internal sealed class SshLogReader(SshClient ssh) : IRemoteLogReader
{
    internal const string TotalMarker = "__SX_LOG_TOTAL__";
    internal const int MaxLineLength = 2000;
    internal const int MaxReturnedLines = 5000;

    public async Task<LogFileInfo> StatAsync(string path, CancellationToken cancellationToken = default)
    {
        var command = ssh.CreateCommand($"stat -c '%s|%Y' -- {Quote(path)}");
        command.CommandTimeout = TimeSpan.FromSeconds(20);
        await command.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        var output = (command.Result ?? "").Trim();
        if (command.ExitStatus != 0 || output.Length == 0)
        {
            var error = (command.Error ?? "").Trim();
            throw new InvalidOperationException(error.Length > 0 ? error : $"读取文件信息失败（exit={command.ExitStatus}）。");
        }
        var parts = output.Split('|');
        var size = long.TryParse(parts[0].Trim(), out var parsed) ? parsed : 0;
        var modified = parts.Length > 1 && long.TryParse(parts[1].Trim(), out var stamp) ? stamp : 0;
        return new(path, size, DateTimeOffset.FromUnixTimeSeconds(modified).UtcDateTime);
    }

    public async Task<LogReadResult> TailAsync(string path, int lines, CancellationToken cancellationToken = default)
    {
        var count = Math.Clamp(lines <= 0 ? 200 : lines, 1, MaxReturnedLines);
        // 先取总行数再取尾部，行号才能对上真实文件位置。
        var command = ssh.CreateCommand(
            $"t=$(wc -l < {Quote(path)} 2>/dev/null | tr -d ' '); " +
            $"printf '{TotalMarker}%s\\n' \"$t\"; " +
            $"tail -n {count} {Quote(path)} | cat -n");
        command.CommandTimeout = TimeSpan.FromSeconds(30);
        await command.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        var (total, rows) = ParseNumbered(command.Result ?? "");
        if (command.ExitStatus != 0 && rows.Count == 0) throw Failure(command);
        var start = Math.Max(1, total - rows.Count + 1);
        var lines2 = rows.Select(row => new LogLine(start + row.Relative - 1, Trim(row.Text))).ToList();
        return new(lines2, total, rows.Count >= count);
    }

    public async Task<LogReadResult> ReadAsync(string path, long fromLine, CancellationToken cancellationToken = default)
    {
        var start = Math.Max(1, fromLine);
        var command = ssh.CreateCommand(
            $"t=$(wc -l < {Quote(path)} 2>/dev/null | tr -d ' '); " +
            $"printf '{TotalMarker}%s\\n' \"$t\"; " +
            $"sed -n '{start},$p' {Quote(path)} | head -n {MaxReturnedLines} | cat -n");
        command.CommandTimeout = TimeSpan.FromSeconds(30);
        await command.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        var (total, rows) = ParseNumbered(command.Result ?? "");
        if (command.ExitStatus != 0 && rows.Count == 0) throw Failure(command);
        var lines2 = rows.Select(row => new LogLine(start + row.Relative - 1, Trim(row.Text))).ToList();
        return new(lines2, total, rows.Count >= MaxReturnedLines);
    }

    public async Task<LogSearchResult> SearchAsync(string path, LogSearchQuery query, CancellationToken cancellationToken = default)
    {
        var pattern = query.Pattern ?? "";
        if (pattern.Length == 0) return LogSearchResult.Empty;
        var max = Math.Clamp(query.MaxResults <= 0 ? MaxReturnedLines : query.MaxResults, 1, MaxReturnedLines);
        var context = Math.Clamp(query.ContextLines, 0, 20);
        // 默认按固定字符串匹配，避免用户输入的正则把远端 grep 打成别的意思。
        var mode = query.UseRegex ? "-E" : "-F";
        var command = ssh.CreateCommand(
            $"grep -n {mode} -C {context} -m {max} -- {Quote(pattern)} {Quote(path)}");
        command.CommandTimeout = TimeSpan.FromSeconds(60);
        await command.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        if (command.ExitStatus is > 1) throw Failure(command);
        return ParseSearch(command.Result ?? "", max);
    }

    private static InvalidOperationException Failure(SshCommand command)
    {
        var error = (command.Error ?? "").Trim();
        return new InvalidOperationException(error.Length > 0 ? error : $"读取日志失败（exit={command.ExitStatus}）。");
    }

    /// <summary>路径与检索词统一用单引号包裹，单引号本身按 shell 规则转义，杜绝命令注入。</summary>
    internal static string Quote(string value) => "'" + (value ?? "").Replace("'", "'\\''") + "'";

    /// <summary>单行过长时截断，避免一条超长日志把界面和内存拖垮。</summary>
    internal static string Trim(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= MaxLineLength) return text ?? "";
        return text[..MaxLineLength] + " …（已截断）";
    }

    /// <summary>解析 cat -n 的输出：第一行是总行数标记，之后每行是“序号 + 制表符 + 内容”。</summary>
    internal static (long Total, List<(int Relative, string Text)> Rows) ParseNumbered(string output)
    {
        long total = 0;
        var rows = new List<(int, string)>();
        var lines = (output ?? "").Replace("\r\n", "\n").Split('\n');
        var start = 0;
        if (lines.Length > 0 && lines[0].StartsWith(TotalMarker, StringComparison.Ordinal))
        {
            long.TryParse(lines[0][TotalMarker.Length..].Trim(), out total);
            start = 1;
        }
        for (var i = start; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0) continue;
            var tab = line.IndexOf('\t');
            if (tab <= 0) continue;
            if (!int.TryParse(line[..tab].Trim(), out var relative)) continue;
            rows.Add((relative, line[(tab + 1)..]));
        }
        return (total, rows);
    }

    /// <summary>解析 grep -n -C 的输出：命中行用“:”分隔，上下文行用“-”分隔，“--”是分组分隔线。</summary>
    internal static LogSearchResult ParseSearch(string output, int maxResults)
    {
        var rows = new List<LogLine>();
        var matches = 0;
        foreach (var raw in (output ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.Length == 0 || raw == "--") continue;
            var index = 0;
            while (index < raw.Length && char.IsAsciiDigit(raw[index])) index++;
            if (index == 0 || index >= raw.Length) continue;
            if (!long.TryParse(raw[..index], out var number)) continue;
            var separator = raw[index];
            if (separator is not (':' or '-')) continue;
            var isMatch = separator == ':';
            if (isMatch) matches++;
            rows.Add(new(number, Trim(raw[(index + 1)..]), isMatch));
            if (rows.Count >= maxResults) break;
        }
        return new(rows, matches, rows.Count >= maxResults);
    }
}