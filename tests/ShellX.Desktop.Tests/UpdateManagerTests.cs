using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ShellX.Core.Settings;
using ShellX.Core.Updates;
using ShellX.Desktop.Features.Settings;
using ShellX.Desktop.Services;
using ShellX.Infrastructure.Persistence;
using ShellX.Infrastructure.Security;

namespace ShellX.Desktop.Tests;

public sealed class UpdateManagerTests
{
    private sealed class FakeUpdateService : IUpdateService
    {
        public Task<UpdateRelease?> GetLatestReleaseAsync(string owner, string repository, CancellationToken cancellationToken = default)
            => Task.FromResult<UpdateRelease?>(new UpdateRelease("v99.0.0", "ShellX 99", "说明", "https://example.invalid/release",
                DateTimeOffset.UnixEpoch, [new UpdateAsset("ShellX-Setup-99.0.0.exe", "https://example.invalid/setup", 4)]));

        public async Task<string> DownloadAsync(UpdateAsset asset, string destinationPath, IProgress<UpdateDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await File.WriteAllTextAsync(destinationPath, "installer", cancellationToken);
            progress?.Report(new UpdateDownloadProgress(9, 9));
            return destinationPath;
        }
    }

    [Fact]
    public async Task DownloadingUpdateRegistersPendingInstaller()
    {
        var root = Path.Combine(Path.GetTempPath(), "shellx-update-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var manager = new UpdateManager(new FakeUpdateService(), paths, NullLogger<UpdateManager>.Instance);
            var release = await manager.CheckAsync("owner", "repo", TestContext.Current.CancellationToken);
            Assert.NotNull(release);
            Assert.True(manager.UpdateAvailable);

            var path = await manager.DownloadAsync(release!, cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(path);
            Assert.True(File.Exists(path));
            var pending = manager.LoadPending();
            Assert.NotNull(pending);
            Assert.Equal("99.0.0", pending!.Version);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void PendingUpdateOlderThanCurrentIsDroppedInsteadOfDowngrading()
    {
        // 待安装包比现在还旧时不能在退出时自动安装：那会把程序装回旧版。
        var root = Path.Combine(Path.GetTempPath(), "shellx-update-stale-" + Guid.NewGuid().ToString("N"));
        try
        {
            var manager = new UpdateManager(new FakeUpdateService(), new AppPaths(root), NullLogger<UpdateManager>.Instance);
            Directory.CreateDirectory(manager.UpdatesFolder);
            var installer = Path.Combine(manager.UpdatesFolder, "ShellX-Setup-0.0.1.exe");
            File.WriteAllText(installer, "installer");
            File.WriteAllText(manager.PendingFile, JsonSerializer.Serialize(new PendingUpdate(installer, "0.0.1", "https://example.invalid/release")));

            Assert.Null(manager.LoadPending());
            Assert.False(File.Exists(manager.PendingFile));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void PruneDownloadsKeepsOnlyTheInstallerThatIsGoingToBeApplied()
    {
        // 更新目录会越攒越大：旧安装包要清掉，只留真正要装的那一份。
        var root = Path.Combine(Path.GetTempPath(), "shellx-update-prune-" + Guid.NewGuid().ToString("N"));
        try
        {
            var manager = new UpdateManager(new FakeUpdateService(), new AppPaths(root), NullLogger<UpdateManager>.Instance);
            Directory.CreateDirectory(manager.UpdatesFolder);
            var keep = Path.Combine(manager.UpdatesFolder, "ShellX-Setup-999.0.0.exe");
            var old = Path.Combine(manager.UpdatesFolder, "ShellX-Setup-0.0.2.exe");
            var partial = Path.Combine(manager.UpdatesFolder, "ShellX-Setup-0.0.3.exe.part");
            File.WriteAllText(keep, "installer");
            File.WriteAllText(old, "installer");
            File.WriteAllText(partial, "partial");
            File.WriteAllText(manager.PendingFile, JsonSerializer.Serialize(new PendingUpdate(keep, "999.0.0", "https://example.invalid/release")));

            manager.PruneDownloads();

            Assert.True(File.Exists(keep));
            Assert.False(File.Exists(old));
            Assert.False(File.Exists(partial));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task UpdatePromptShowsReleaseNotesAndFallsBackWhenNotesAreEmpty()
    {
        var root = Path.Combine(Path.GetTempPath(), "shellx-update-prompt-" + Guid.NewGuid().ToString("N"));
        try
        {
            var manager = new UpdateManager(new FakeUpdateService(), new AppPaths(root), NullLogger<UpdateManager>.Instance);
            var release = await manager.CheckAsync("owner", "repo", TestContext.Current.CancellationToken);
            Assert.NotNull(release);
            var prompt = manager.BuildUpdatePrompt(release!);
            Assert.Contains("本次更新内容", prompt);
            Assert.Contains("说明", prompt);
            Assert.Contains(release!.Version, prompt);
            Assert.Contains("是否立即更新并重启", prompt);

            var withoutNotes = release with { Notes = "   " };
            var fallback = manager.BuildUpdatePrompt(withoutNotes);
            Assert.Contains("本次发布没有提供更新说明", fallback);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public async Task UpdateSettingsArePersisted()
    {
        var vm = new SettingsViewModel(new AppSettings(), new LockPasswordHasher(), ["Consolas"]);
        vm.CheckUpdateOnStartup = false;
        AppSettings? saved = null;
        vm.Saved += value => saved = value;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("", vm.Error);
        Assert.NotNull(saved);
        Assert.False(saved.CheckUpdateOnStartup);
    }
}