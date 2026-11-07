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