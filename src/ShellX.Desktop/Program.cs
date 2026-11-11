using Avalonia;
using Avalonia.Threading;
using ShellX.Desktop.Services;

namespace ShellX.Desktop;

internal static class Program
{
    private static SingleInstanceService? _singleInstance;

    /// <summary>重复启动时把已在运行的窗口带到前台；由 App 在初始化后注册。</summary>
    public static Action? ActivateRequested { get; set; }

    [STAThread]
    public static void Main(string[] args)
    {
        // 更新引导模式：等待主进程退出后静默安装更新包，不启动界面。
        if (UpdateBootstrapper.TryRun(args)) return;
        NativeDependencies.Initialize();
        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.IsPrimary)
        {
            // 已有实例在运行：激活它的窗口后直接退出，不再开第二个实例。
            SingleInstanceService.RequestActivation();
            _singleInstance.Dispose();
            _singleInstance = null;
            return;
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>退出时释放单实例控制句柄，避免下次启动被误判为"已有实例"。</summary>
    public static void ShutdownSingleInstance()
    {
        _singleInstance?.Dispose();
        _singleInstance = null;
    }

    /// <summary>界面就绪后开始监听"激活已有窗口"的请求。</summary>
    public static void ListenSingleInstance()
    {
        _singleInstance?.Listen(() => Dispatcher.UIThread.Post(() => ActivateRequested?.Invoke()));
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
