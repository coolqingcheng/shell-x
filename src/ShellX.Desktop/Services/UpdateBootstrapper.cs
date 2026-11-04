using System.Diagnostics;

namespace ShellX.Desktop.Services;

/// <summary>
/// 更新引导模式：ShellX.exe --apply-update "安装包" --parent-pid 123 --app-path "主程序" [--restart]
/// 等待主进程退出后静默运行安装包，并按需重新启动程序。
/// </summary>
internal static class UpdateBootstrapper
{
    public static bool TryRun(string[] args)
    {
        string? installer = null;
        string? application = null;
        var parentPid = 0;
        var restart = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--apply-update" when i + 1 < args.Length: installer = args[++i]; break;
                case "--app-path" when i + 1 < args.Length: application = args[++i]; break;
                case "--parent-pid" when i + 1 < args.Length: int.TryParse(args[++i], out parentPid); break;
                case "--restart": restart = true; break;
            }
        }
        if (string.IsNullOrWhiteSpace(installer)) return false;
        Run(installer, application, parentPid, restart);
        return true;
    }

    private static void Run(string installer, string? application, int parentPid, bool restart)
    {
        WaitForParent(parentPid);
        try
        {
            var arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /CLOSEAPPLICATIONS";
            using var process = Process.Start(new ProcessStartInfo(installer, arguments) { UseShellExecute = true });
            process?.WaitForExit(600000);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "运行更新安装包失败：{Installer}", installer);
        }
        if (!restart || string.IsNullOrWhiteSpace(application) || !File.Exists(application)) return;
        try { Process.Start(new ProcessStartInfo(application) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) { Serilog.Log.Error(ex, "更新后重启 ShellX 失败"); }
    }

    private static void WaitForParent(int parentPid)
    {
        if (parentPid <= 0) return;
        var deadline = DateTime.UtcNow.AddMinutes(3);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var process = Process.GetProcessById(parentPid);
                if (process.HasExited) break;
                process.WaitForExit(3000);
            }
            catch (ArgumentException) { break; }
            catch (InvalidOperationException) { break; }
            catch { break; }
        }
        Thread.Sleep(800);
    }
}