namespace ShellX.Core.Services;

/// <summary>一条监听中的端口，由服务端的 ss/netstat 输出解析而来（非 root 可能拿不到 PID、进程名与路径）。</summary>
public sealed record ListeningPort(string Protocol, string State, string Address, int Port, string ProcessName, string ProcessId, string Raw)
{
    /// <summary>服务端 /proc/&lt;pid&gt;/exe 解析出的可执行文件路径；无权限或系统不支持时为空。</summary>
    public string ProcessPath { get; init; } = "";

    public string PortText => Port > 0 ? Port.ToString() : "—";
    public string ProcessDisplay => string.IsNullOrWhiteSpace(ProcessName) ? "—" : ProcessName;
    public string PidDisplay => string.IsNullOrWhiteSpace(ProcessId) ? "—" : ProcessId;
    public string ProcessPathDisplay => string.IsNullOrWhiteSpace(ProcessPath) ? "—" : ProcessPath;

    /// <summary>按端口号、进程 PID、进程名、进程路径、本地地址或状态做过滤（搜索框统一入口）。</summary>
    public bool Matches(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var text = query.Trim();
        return PortText.Contains(text, StringComparison.OrdinalIgnoreCase)
            || ProcessId.Contains(text, StringComparison.OrdinalIgnoreCase)
            || ProcessName.Contains(text, StringComparison.OrdinalIgnoreCase)
            || ProcessPath.Contains(text, StringComparison.OrdinalIgnoreCase)
            || Address.Contains(text, StringComparison.OrdinalIgnoreCase)
            || State.Contains(text, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>读取服务端监听端口清单（与服务管理共用同一条 SSH exec 通道）。</summary>
public interface IPortUsageReader
{
    Task<IReadOnlyList<ListeningPort>> PortsAsync(CancellationToken cancellationToken = default);
}