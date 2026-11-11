namespace ShellX.Desktop.Services;

/// <summary>
/// 单实例控制：重复启动 ShellX 时不再开第二个窗口，而是把已在运行的窗口激活到前台。
/// 使用命名 Mutex 判定首实例，命名事件把"激活"请求传给首实例。
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = @"Local\ShellX.SingleInstance";
    private const string ActivateEventName = @"Local\ShellX.ActivateMainWindow";
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activateEvent;
    private CancellationTokenSource? _listener;
    private bool _disposed;

    /// <summary>是否是首个实例；为 false 时应当通知已有实例并退出。</summary>
    public bool IsPrimary { get; }

    public SingleInstanceService()
    {
        _mutex = new Mutex(true, MutexName, out var created);
        IsPrimary = created;
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
    }

    /// <summary>通知已在运行的实例把窗口带到前台。</summary>
    public static void RequestActivation()
    {
        try
        {
            using var handle = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
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
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (WaitHandle.WaitAny([_activateEvent], 500) == 0) continue;
                }
                catch (ObjectDisposedException) { break; }
                catch (AbandonedMutexException) { break; }
                if (token.IsCancellationRequested) break;
                try { activate(); }
                catch (Exception ex) { Serilog.Log.Warning(ex, "激活已有窗口失败"); }
            }
        }, CancellationToken.None);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _listener?.Cancel();
        try { _activateEvent.Dispose(); } catch { }
        try { _mutex.ReleaseMutex(); } catch { }
        _mutex.Dispose();
    }
}
