using System.Text.Json;

namespace ShellX.Desktop.Services;

public sealed record McpLogRow(string Text, string Detail);

public sealed class McpLogFileReader(string logDirectory)
{
    private string? _path;
    private long _offset;

    public string CurrentPath => _path ?? "";
    public IReadOnlyList<McpLogRow> ReadNewRows()
    {
        var latest = Directory.Exists(logDirectory)
            ? Directory.GetFiles(logDirectory, "mcp-audit-*.jsonl").OrderBy(x => x, StringComparer.OrdinalIgnoreCase).LastOrDefault()
            : null;
        if (!string.Equals(latest, _path, StringComparison.OrdinalIgnoreCase))
        {
            _path = latest;
            _offset = 0;
        }
        if (_path == null) return [];

        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length < _offset) _offset = 0;
        stream.Seek(_offset, SeekOrigin.Begin);
        using var reader = new StreamReader(stream);
        var rows = new List<McpLogRow>();
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            rows.Add(Format(line));
        }
        _offset = stream.Position;
        return rows;
    }

    private static McpLogRow Format(string line)
    {
        try
        {
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            var timestamp = root.TryGetProperty("Timestamp", out var time) && DateTimeOffset.TryParse(time.GetString(), out var parsed)
                ? parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "";
            var connection = root.TryGetProperty("ConnectionName", out var name) ? name.GetString() ?? "" : "";
            var status = root.TryGetProperty("Status", out var state) ? StatusText(state.GetString()) : "";
            var command = root.TryGetProperty("Command", out var value) ? value.GetString() ?? "" : "";
            var exit = root.TryGetProperty("ExitCode", out var exitCode) && exitCode.ValueKind == JsonValueKind.Number ? exitCode.GetInt32().ToString() : "—";
            var duration = root.TryGetProperty("DurationMilliseconds", out var elapsed) && elapsed.ValueKind == JsonValueKind.Number ? elapsed.GetInt64() : 0;
            return new($"{timestamp}  [{status}]  {connection}  > {command}  (exit={exit}, {duration} ms)", line);
        }
        catch (JsonException) { return new(line, line); }
    }

    private static string StatusText(string? status) => status switch
    {
        "succeeded" => "成功",
        "denied" => "拒绝",
        "invalid" => "无效",
        "timed_out" => "超时",
        "cancelled" => "已取消",
        "error" => "错误",
        _ => status ?? ""
    };
}