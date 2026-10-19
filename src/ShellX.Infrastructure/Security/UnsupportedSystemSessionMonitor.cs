using ShellX.Core.Security;

namespace ShellX.Infrastructure.Security;

public sealed class UnsupportedSystemSessionMonitor : ISystemSessionMonitor
{
    public bool IsSupported => false;
    public SystemSessionLockState? CurrentState => null;
    public event Action<SystemSessionLockState>? StateChanged { add { } remove { } }
    public void Start() { }
    public void Dispose() { }
}
