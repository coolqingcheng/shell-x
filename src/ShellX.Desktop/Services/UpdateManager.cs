using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using ShellX.Core;
using ShellX.Core.Updates;
using ShellX.Infrastructure.Persistence;

namespace ShellX.Desktop.Services;

/// <summary>已下载、等待安装的更新。</summary>
public sealed record PendingUpdate(string InstallerPath, string Version, string ReleaseUrl);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(PendingUpdate))]
internal partial class UpdateJsonContext : JsonSerializerContext;

/// <summary>负责检查更新、下载安装包以及在关闭或重启时完成安装。</summary>
public sealed partial class UpdateManager : ObservableObject
{
    private readonly IUpdateService _updates;
    private readonly AppPaths _paths;
    private readonly ILogger<UpdateManager> _logs;
    private bool _updaterStarted;

    public UpdateManager(IUpdateService updates, AppPaths paths, ILogger<UpdateManager> logs)
    {
        _updates = updates; _paths = paths; _logs = logs;
    }

    public string CurrentVersion => AppVersion.Value;
    public string CurrentVersionText => $"当前版本 {CurrentVersion}";
    public string UpdatesFolder => Path.Combine(_paths.Root, "updates");
    public string PendingFile => Path.Combine(UpdatesFolder, "pending.json");
    public UpdateRelease? Latest { get; private set; }
    public bool UpdateAvailable => Latest is not null && UpdateVersion.IsNewer(Latest.Tag, CurrentVersion);
    public bool HasInstaller => Latest?.Installer is not null;
    public string ReleaseUrl => Latest?.HtmlUrl ?? "";
    public string UpdateSummary => Latest is null ? "" :
        UpdateAvailable ? $"发现新版本 {Latest.Version}" : $"当前已是最新版本 {CurrentVersion}";

    /// <summary>生成下载完成后的更新确认文案，保证设置页与启动检查显示相同的本次更新内容。</summary>
    public string BuildUpdatePrompt(UpdateRelease release)
    {
        ArgumentNullException.ThrowIfNull(release);
        var notes = string.IsNullOrWhiteSpace(release.Notes)
            ? "本次发布没有提供更新说明。"
            : release.Notes.Trim();
        var link = string.IsNullOrWhiteSpace(release.HtmlUrl) ? "" : $"\n\n发布页面：{release.HtmlUrl}";
        return $"发现新版本 {release.Version}（当前版本 {CurrentVersion}）\n\n" +
               $"本次更新内容：\n{notes}{link}\n\n下载已完成，是否立即更新并重启？";
    }

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _status = "尚未检查更新";
    [ObservableProperty] private string _lastDownloadPath = "";

    public async Task<UpdateRelease?> CheckAsync(string owner, string repository, CancellationToken cancellationToken = default)
    {
        if (IsBusy) return Latest;
        IsBusy = true; Status = "正在检查更新…";
        try
        {
            var release = await _updates.GetLatestReleaseAsync(owner, repository, cancellationToken).ConfigureAwait(false);
            Latest = release;
            Status = release switch
            {
                null => "没有找到可用的发布版本。",
                _ when UpdateVersion.IsNewer(release.Tag, CurrentVersion) => $"发现新版本 {release.Version}",
                _ => $"当前已是最新版本 {CurrentVersion}"
            };
            return release;
        }
        catch (Exception ex)
        {
            Status = "检查更新失败：" + ex.Message;
            _logs.LogWarning(ex, "检查更新失败");
            return null;
        }
        finally { IsBusy = false; }
    }

    /// <summary>下载安装包。传入 targetPath 时保存到指定位置，否则下载到本地更新目录并登记为待安装。</summary>
    public async Task<string?> DownloadAsync(UpdateRelease release, string? targetPath = null, CancellationToken cancellationToken = default)
    {
        if (release.Installer is not { } asset)
        {
            Status = "该发布没有可用的安装包。";
            return null;
        }
        var target = targetPath ?? Path.Combine(UpdatesFolder, asset.Name);
        IsBusy = true; Progress = 0;
        try
        {
            var progress = new Progress<UpdateDownloadProgress>(p =>
            {
                Progress = p.Percent;
                Status = $"正在下载安装包 {p.Percent:F0}%";
            });
            await _updates.DownloadAsync(asset, target, progress, cancellationToken).ConfigureAwait(false);
            Progress = 100;
            LastDownloadPath = target;
            Status = targetPath is null ? $"安装包已下载：{target}" : $"安装包已保存：{target}";
            if (targetPath is null) await SavePendingAsync(new PendingUpdate(target, release.Version, release.HtmlUrl)).ConfigureAwait(false);
            return target;
        }
        catch (Exception ex)
        {
            Status = "下载失败：" + ex.Message;
            _logs.LogWarning(ex, "下载更新包失败");
            return null;
        }
        finally { IsBusy = false; }
    }

    public PendingUpdate? LoadPending()
    {
        try
        {
            if (!File.Exists(PendingFile)) return null;
            var pending = JsonSerializer.Deserialize(File.ReadAllText(PendingFile), UpdateJsonContext.Default.PendingUpdate);
            if (pending is null || !File.Exists(pending.InstallerPath)) { RemovePending(); return null; }
            // 待安装包不比现在新就是降级：多半是上一次下载之后一直没装，留到退出时会把程序装回旧版。
            if (!UpdateVersion.IsNewer(pending.Version, CurrentVersion)) { RemovePending(); return null; }
            return pending;
        }
        catch (Exception ex)
        {
            _logs.LogWarning(ex, "读取待安装更新失败");
            return null;
        }
    }

    /// <summary>整理更新目录：作废比当前版本旧的待安装包，删掉多余的安装包，只留真正要装的那一份。</summary>
    public void PruneDownloads()
    {
        var pending = LoadPending();
        CleanupInstallers(pending?.InstallerPath);
    }

    /// <summary>删除待安装登记。</summary>
    private void RemovePending()
    {
        try { if (File.Exists(PendingFile)) File.Delete(PendingFile); }
        catch (Exception ex) { _logs.LogWarning(ex, "删除待安装登记失败"); }
    }

    /// <summary>删除多余的安装包，只保留 keepPath 指向的那一份。更新目录会越攒越大，不能一直堆着。</summary>
    private void CleanupInstallers(string? keepPath)
    {
        try
        {
            if (!Directory.Exists(UpdatesFolder)) return;
            foreach (var file in Directory.EnumerateFiles(UpdatesFolder, "ShellX-Setup-*.exe*"))
            {
                if (keepPath is not null && string.Equals(Path.GetFullPath(file), Path.GetFullPath(keepPath), StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Delete(file); } catch (Exception) { }
            }
        }
        catch (Exception ex) { _logs.LogWarning(ex, "清理旧更新包失败"); }
    }

    /// <summary>放弃已下载的更新：删除待安装登记和安装包，退出时不再自动安装。</summary>
    public void ClearPending()
    {
        try { if (File.Exists(PendingFile)) File.Delete(PendingFile); CleanupInstallers(null); }
        catch (Exception ex) { _logs.LogWarning(ex, "清除待安装更新失败"); }
    }
    private async Task SavePendingAsync(PendingUpdate pending)
    {
        try
        {
            Directory.CreateDirectory(UpdatesFolder);
            await File.WriteAllTextAsync(PendingFile, JsonSerializer.Serialize(pending, UpdateJsonContext.Default.PendingUpdate)).ConfigureAwait(false);
            // 只留这一次要装的安装包，之前下载的都清掉。
            CleanupInstallers(pending.InstallerPath);
        }
        catch (Exception ex)
        {
            _logs.LogWarning(ex, "保存待安装更新失败");
        }
    }

    /// <summary>启动更新程序：等待主进程退出后运行安装包。</summary>
    public bool TryApply(bool restart)
    {
        var pending = LoadPending();
        if (pending is null)
        {
            Status = "没有已下载的更新包。";
            return false;
        }
        return StartUpdater(pending.InstallerPath, restart);
    }

    private bool StartUpdater(string installerPath, bool restart)
    {
        if (_updaterStarted) return true;
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
        {
            Status = "无法定位当前程序，更新未启动。";
            return false;
        }
        try
        {
            Directory.CreateDirectory(UpdatesFolder);
            var updater = Path.Combine(UpdatesFolder, "ShellX-updater.exe");
            File.Copy(executable, updater, true);
            var arguments = $"--apply-update \"{installerPath}\" --parent-pid {Environment.ProcessId} --app-path \"{executable}\"";
            if (restart) arguments += " --restart";
            Process.Start(new ProcessStartInfo(updater, arguments) { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
            _updaterStarted = true;
            Status = restart ? "正在重启并安装更新…" : "更新已就绪，关闭 ShellX 后自动安装。";
            _logs.LogInformation("已启动更新程序：{Installer}", installerPath);
            return true;
        }
        catch (Exception ex)
        {
            Status = "无法启动更新程序：" + ex.Message;
            _logs.LogWarning(ex, "启动更新程序失败");
            return false;
        }
    }

    /// <summary>关闭程序时调用：若已有下载好的更新包，则退出后自动安装。</summary>
    public void ApplyOnExit()
    {
        if (_updaterStarted) return;
        var pending = LoadPending();
        if (pending is null) return;
        StartUpdater(pending.InstallerPath, restart: false);
    }

    /// <summary>重启并安装更新。</summary>
    public bool ApplyAndRestart()
    {
        if (!TryApply(restart: true)) return false;
        Shutdown();
        return true;
    }

    public static void Shutdown()
    {
        var lifetime = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        lifetime?.Shutdown();
    }
}
