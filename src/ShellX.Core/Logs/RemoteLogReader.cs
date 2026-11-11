namespace ShellX.Core.Logs;

/// <summary>远端日志文件的基本信息，由服务端的 stat 取得。</summary>
public sealed record LogFileInfo(string Path, long Size, DateTime ModifiedUtc)
{
    public string SizeText => Size < 1024 ? $"{Size} B"
        : Size < 1024 * 1024 ? $"{Size / 1024.0:0.#} KB"
        : Size < 1024L * 1024 * 1024 ? $"{Size / (1024.0 * 1024):0.#} MB"
        : $"{Size / (1024.0 * 1024 * 1024):0.##} GB";
    public string ModifiedText => ModifiedUtc == default ? "—" : ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}

/// <summary>一行日志，行号从 1 开始；检索结果里用 IsMatch 标记命中行。</summary>
public sealed record LogLine(long Number, string Text, bool IsMatch = false)
{
    public string NumberText => Number > 0 ? Number.ToString() : "";
}

/// <summary>一次读取的结果：行列表、文件总行数、是否被截断。</summary>
public sealed record LogReadResult(IReadOnlyList<LogLine> Lines, long TotalLines, bool Truncated)
{
    public static LogReadResult Empty { get; } = new([], 0, false);
}

/// <summary>检索条件：默认按固定字符串匹配，可切换正则；上下文行数与服务端上限由调用方给出。</summary>
public sealed record LogSearchQuery(string Pattern, bool UseRegex, int ContextLines = 2, int MaxResults = 5000);

/// <summary>检索结果：命中行与上下文合并后的行列表。</summary>
public sealed record LogSearchResult(IReadOnlyList<LogLine> Lines, int MatchCount, bool Truncated)
{
    public static LogSearchResult Empty { get; } = new([], 0, false);
}

/// <summary>
/// 只读的远端日志读取接口。所有操作都在服务端执行命令，文件本身不会被整体读进内存：
/// 尾部与增量用 tail/sed 限定范围，检索用 grep 并限制结果条数。
/// </summary>
public interface IRemoteLogReader
{
    /// <summary>取文件大小与修改时间。</summary>
    Task<LogFileInfo> StatAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>取文件末尾若干行，并带上真实行号。</summary>
    Task<LogReadResult> TailAsync(string path, int lines, CancellationToken cancellationToken = default);

    /// <summary>取从指定行号开始的全部内容（跟随模式增量拉取用）。</summary>
    Task<LogReadResult> ReadAsync(string path, long fromLine, CancellationToken cancellationToken = default);

    /// <summary>在服务端检索日志。</summary>
    Task<LogSearchResult> SearchAsync(string path, LogSearchQuery query, CancellationToken cancellationToken = default);
}