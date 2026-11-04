using Avalonia;
using ShellX.Desktop.Services;

namespace ShellX.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 更新引导模式：等待主进程退出后静默安装更新包，不启动界面。
        if (UpdateBootstrapper.TryRun(args)) return;
        NativeDependencies.Initialize();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}