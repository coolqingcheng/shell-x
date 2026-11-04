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
            var release = await manager.CheckAsync("owner", "repo");
            Assert.NotNull(release);
            Assert.True(manager.UpdateAvailable);

            var path = await manager.DownloadAsync(release!);
            Assert.NotNull(path);
            Assert.True(File.Exists(path));
            var pending = manager.LoadPending();
            Assert.NotNull(pending);
            Assert.Equal("99.0.0", pending!.Version);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task UpdateSettingsArePersisted()
    {
        var vm = new SettingsViewModel(new AppSettings(), new LockPasswordHasher(), ["Consolas"]);
        vm.UpdateOwner = "some-user";
        vm.UpdateRepository = "shell-x";
        vm.CheckUpdateOnStartup = false;
        vm.AutoDownloadUpdate = true;
        AppSettings? saved = null;
        vm.Saved += value => saved = value;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("", vm.Error);
        Assert.NotNull(saved);
        Assert.Equal("some-user", saved!.UpdateRepositoryOwner);
        Assert.Equal("shell-x", saved.UpdateRepositoryName);
        Assert.False(saved.CheckUpdateOnStartup);
        Assert.True(saved.AutoDownloadUpdate);
    }
}