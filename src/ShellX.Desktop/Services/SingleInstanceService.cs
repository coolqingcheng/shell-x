namespace ShellX.Desktop.Services;

/// <summary>
/// 单实例控制：重复启动 ShellX 时不再开第二个窗口，而是把已在运行的窗口激活到前台。
/// 使用命名 Mutex 判定首实例，命名事件把"激活"请求传给首实例。
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    private const string DefaultMutexName = @"Local\ShellX.SingleInstance";
    private const string DefaultActivateEventName = @"Local\ShellX.ActivateMainWindow";
    private const int ActivateIndex = 0;
    private const int StopIndex = 1;

    private readonly string _activateEventName;
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activateEvent;
    private readonly EventWaitHandle _stopEvent;
    private CancellationTokenSource? _listener;
    private bool _disposed;

    /// <summary>是否是首个实例；为 false 时应当通知已有实例并退出。</summary>
    public bool IsPrimary { get; }

    public SingleInstanceService() : this(DefaultMutexName, DefaultActivateEventName)
    {
    }

    /// <summary>使用自定义命名，便于测试之间互不干扰。</summary>
    internal SingleInstanceService(string mutexName, string activateEventName)
    {
        _activateEventName = activateEventName;
        _mutex = new Mutex(true, mutexName, out var created);
        IsPrimary = created;
        // 手动复位：信号被触发后会一直保持，直到真正被处理才复位，
        // 这样失效实例即使误唤醒也不会把信号吞掉。
        _activateEvent = new EventWaitHandle(false, EventResetMode.ManualReset, activateEventName);
        // 实例私有的停止事件，释放时立即唤醒监听线程，不必等轮询超时。
        _stopEvent = new EventWaitHandle(false, EventResetMode.ManualReset);
    }

    /// <summary>通知已在运行的实例把窗口带到前台。</summary>
    public static void RequestActivation() => RequestActivation(DefaultActivateEventName);

    internal static void RequestActivation(string activateEventName)
    {
        try
        {
            using var handle = new EventWaitHandle(false, EventResetMode.ManualReset, activateEventName);
            handle.Set();
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "通知已有实例激活失败"); }
    }

    /// <summary>首实例监听激活请求；回调可能来自后台线程，需要自行切回 UI 线程。</summary>
    public void Listen(Action activate)
    {
        if (!IsPrimary || _disposed || activate == null) return;
        var cancellation = _listener = new CancellationTokenSource();
        _ = Task.Run(() =>
        {
            var token = cancellation.Token;
            while (!token.IsCancellationRequested && !_disposed)
            {
                int index;
                try
                {
                    // WaitAny 返回 0=激活事件被触发，1=停止事件被触发，WaitTimeout=本次等待超时。
                    // 只有真正收到激活请求时才回调，否则窗口会不停被顶到前台且关不掉。
                    index = WaitHandle.WaitAny([_activateEvent, _stopEvent], 500);
                }
                catch (ObjectDisposedException) { break; }
                catch (AbandonedMutexException) { break; }
                if (index == StopIndex) break;
                if (index != ActivateIndex) continue;
                if (token.IsCancellationRequested || _disposed) break;
                try
                {
                    activate();
                    // 只有真正处理了请求才复位，避免已失效的实例把信号吞掉。
                    try { _activateEvent.Reset(); } catch (ObjectDisposedException) { }
                }
                catch (Exception ex) { Serilog.Log.Warning(ex, "激活已有窗口失败"); }
            }
        }, CancellationToken.None);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _stopEvent.Set(); } catch { }
        _listener?.Cancel();
        try { _activateEvent.Dispose(); } catch { }
        try { _stopEvent.Dispose(); } catch { }
        try { _mutex.ReleaseMutex(); } catch { }
        _mutex.Dispose();
    }
}