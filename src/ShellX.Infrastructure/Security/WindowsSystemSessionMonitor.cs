using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using ShellX.Core.Security;

namespace ShellX.Infrastructure.Security;

[SupportedOSPlatform("windows")]
public sealed class WindowsSystemSessionMonitor(ILogger<WindowsSystemSessionMonitor> log) : ISystemSessionMonitor
{
    private readonly object _gate = new();
    private bool _started;
    private bool _disposed;
    private SystemSessionLockState? _currentState;
    public bool IsSupported { get; private set; } = true;
    public SystemSessionLockState? CurrentState { get { lock (_gate) return _currentState; } }
    public event Action<SystemSessionLockState>? StateChanged;

    public void Start()
    {
        SystemSessionLockState? initial;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started) return;
            try
            {
                SystemEvents.SessionSwitch += SessionSwitched;
                _started = true;
                IsSupported = true;
            }
            catch (Exception ex)
            {
                IsSupported = false;
                log.LogWarning(ex, "无法注册 Windows 锁屏状态通知");
                return;
            }
            initial = ReadCurrentState();
            _currentState = initial;
        }
        if (initial is { } state) StateChanged?.Invoke(state);
    }

    private void SessionSwitched(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is not (SessionSwitchReason.SessionLock or SessionSwitchReason.SessionUnlock)) return;
        SystemSessionLockState state;
        lock (_gate)
        {
            if (_disposed) return;
            var isLocked = e.Reason == SessionSwitchReason.SessionLock;
            if (_currentState?.IsLocked == isLocked) return;
            state = new(isLocked, DateTimeOffset.UtcNow);
            _currentState = state;
        }
        StateChanged?.Invoke(state);
    }

    private SystemSessionLockState? ReadCurrentState()
    {
        const int currentSession = -1;
        const int sessionInfoEx = 25;
        if (!WTSQuerySessionInformation(IntPtr.Zero, currentSession, sessionInfoEx, out var buffer, out var length))
        {
            log.LogWarning(new Win32Exception(Marshal.GetLastWin32Error()), "无法读取当前锁屏状态，将等待系统通知");
            return null;
        }
        try
        {
            // WTSINFOEX 的 Data 联合体按 8 字节对齐，只读取其固定头部。
            if (length < Marshal.SizeOf<SessionInfoHeader>()) return null;
            var info = Marshal.PtrToStructure<SessionInfoHeader>(buffer);
            return info.Level == 1 && info.SessionFlags is 0 or 1
                ? new(info.SessionFlags == 0, DateTimeOffset.UtcNow) : null;
        }
        finally { WTSFreeMemory(buffer); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (_started) SystemEvents.SessionSwitch -= SessionSwitched;
            StateChanged = null;
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct SessionInfoHeader
    {
        [FieldOffset(0)] public uint Level;
        [FieldOffset(16)] public int SessionFlags;
    }

    [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformation(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);
}
