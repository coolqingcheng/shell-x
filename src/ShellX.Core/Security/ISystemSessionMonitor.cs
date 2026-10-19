namespace ShellX.Core.Security;

public readonly record struct SystemSessionLockState(bool IsLocked, DateTimeOffset ChangedAt);

public interface ISystemSessionMonitor : IDisposable
{
    bool IsSupported { get; }
    // 未取得可靠状态时返回 null，通知可能来自后台线程。
    SystemSessionLockState? CurrentState { get; }
    event Action<SystemSessionLockState>? StateChanged;
    void Start();
}
