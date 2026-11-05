namespace ShellX.Core.Services;

public enum ServiceAction { Start, Stop, Restart, Enable, Disable }

/// <summary>目标服务器上的 systemd 服务，由 systemctl list-units 与 list-unit-files 合并得到。</summary>
public sealed record RemoteService(string Unit, string Load, string Active, string Sub, string Description, string EnableState)
{
    public bool IsActive => string.Equals(Active, "active", StringComparison.OrdinalIgnoreCase);
    public bool IsFailed => string.Equals(Active, "failed", StringComparison.OrdinalIgnoreCase);
    public string StateText => string.IsNullOrWhiteSpace(Sub) || string.Equals(Sub, "-", StringComparison.Ordinal) ? Active : $"{Active} / {Sub}";
    public string EnabledText => string.IsNullOrWhiteSpace(EnableState) ? "—" : EnableState;

    // 与隧道状态一致的着色，便于一眼区分运行中、失败与未运行。
    public string StateColor => (Active ?? "").Trim().ToLowerInvariant() switch
    {
        "active" => "#73C991",
        "failed" => "#F48771",
        "activating" or "reloading" or "deactivating" => "#D7BA7D",
        _ => "#858585"
    };
}

/// <summary>systemctl 操作结果：保留原始输出与退出码，便于把服务端原因直接显示给用户。</summary>
public sealed record ServiceActionResult(bool Success, string Message, int ExitCode);

/// <summary>读取并操作目标服务器上的系统服务（由会话提供，默认不支持的平台返回 null）。</summary>
public interface IServiceCenter
{
    Task<IReadOnlyList<RemoteService>> ListAsync(CancellationToken cancellationToken = default);
    Task<string> StatusAsync(string unit, CancellationToken cancellationToken = default);
    Task<ServiceActionResult> ExecuteAsync(string unit, ServiceAction action, CancellationToken cancellationToken = default);
}