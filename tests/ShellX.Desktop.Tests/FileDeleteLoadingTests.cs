using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using ShellX.Core.Connections;
using ShellX.Core.Files;
using ShellX.Core.Settings;
using ShellX.Core.Tunnels;
using ShellX.Desktop.Controls;
using ShellX.Desktop.Features.FileBrowser;
using ShellX.Desktop.Services;
using ShellX.Infrastructure.Files;

namespace ShellX.Desktop.Tests;

/// <summary>文件管理器删除：删除完成前条目保持可见并显示加载指示。</summary>
public sealed class FileDeleteLoadingTests
{
    [AvaloniaFact]
    public async Task RemoteDeleteKeepsEntryVisibleUntilDeleteFinishes()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-delete-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var gate = new TaskCompletionSource();
            using var browser = new FileBrowserViewModel(new Dialogs(), NullLogger<FileBrowserViewModel>.Instance, new LocalFileSystem(root));
            browser.Attach(new BlockingRemoteFileSystem(gate.Task), CancellationToken.None, null, "测试");
            var entry = new RemoteEntry("a.txt", "/a.txt", false, false, 12, DateTime.UtcNow, 420, 1000, 1000);
            browser.Entries.Add(entry);
            browser.SelectedEntry = entry;

            var deleting = browser.DeleteCommand.ExecuteAsync(null);
            await Task.Delay(80);

            Assert.True(entry.IsDeleting);            // 删除中
            Assert.Contains(entry, browser.Entries);  // 删完才消失

            gate.SetResult();
            await deleting;

            Assert.False(entry.IsDeleting);
            Assert.DoesNotContain(browser.Entries, e => e.FullPath == "/a.txt");
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task LocalDeleteKeepsEntryVisibleUntilDeleteFinishes()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-delete-local-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var gate = new TaskCompletionSource();
            using var vm = new LocalFileBrowserViewModel(new BlockingLocalFileSystem(gate.Task), new Dialogs(), _ => Task.CompletedTask);
            var entry = new LocalEntry("a.txt", Path.Combine(root, "a.txt"), false, false, false, 12, DateTime.UtcNow);
            vm.Entries.Add(entry);
            vm.SelectedEntry = entry;

            var deleting = vm.DeleteCommand.ExecuteAsync(null);
            await Task.Delay(80);

            Assert.True(entry.IsDeleting);
            Assert.Contains(entry, vm.Entries);

            gate.SetResult();
            await deleting;

            Assert.False(entry.IsDeleting);
            Assert.DoesNotContain(vm.Entries, e => e.FullPath == entry.FullPath);
        }
        finally { Directory.Delete(root, true); }
    }

    /// <summary>删除中显示加载指示，并隐藏文件图标，避免两者重叠。</summary>
    [AvaloniaFact]
    public async Task FileRowShowsSpinnerWhileDeleting()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-delete-spinner-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            using var browser = new FileBrowserViewModel(new Dialogs(), NullLogger<FileBrowserViewModel>.Instance, new LocalFileSystem(root));
            browser.IsAvailable = true;
            browser.Entries.Add(new RemoteEntry("a.txt", "/a.txt", false, false, 12, DateTime.UtcNow, 420, 1000, 1000) { IsDeleting = true });
            var view = new RemoteFileBrowserView { DataContext = browser };
            var window = new Window { Content = view, Width = 560, Height = 360 };
            window.Show();
            try
            {
                await Task.Delay(150);
                var grid = view.FindControl<DataGrid>("FileGrid")!;
                var spinner = grid.GetVisualDescendants().OfType<BusySpinner>().FirstOrDefault();
                Assert.NotNull(spinner);
                Assert.True(spinner!.IsVisible);
                Assert.DoesNotContain(grid.GetVisualDescendants().OfType<FileIconView>(), icon => icon.IsVisible);
            }
            finally { window.Close(); }
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class Dialogs : IDialogService
    {
        public Task<bool> ConfirmAsync(string title, string message, bool danger = false) => Task.FromResult(true);
        public Task<ConnectionEditResult?> EditConnectionAsync(ConnectionProfile? profile, ConnectionSecret? secret) => Task.FromResult<ConnectionEditResult?>(null);
        public Task<string?> PromptAsync(string title, string label, string value = "", bool secret = false) => Task.FromResult<string?>(null);
        public Task ShowErrorAsync(string message) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> PickUploadAsync() => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> PickDownloadAsync(string name) => Task.FromResult<string?>(null);
        public Task EditFileAsync(TextDocument document, Func<TextDocument, string, Task<TextDocument>> save) => Task.CompletedTask;
        public Task<int?> PermissionsAsync(RemoteEntry entry) => Task.FromResult<int?>(null);
        public Task<TunnelDefinition?> EditTunnelAsync() => Task.FromResult<TunnelDefinition?>(null);
        public Task<AppSettings?> SettingsAsync(AppSettings settings) => Task.FromResult<AppSettings?>(null);
    }

    /// <summary>删除被阻塞的远程文件系统，用来观察删除过程中的界面状态。</summary>
    private sealed class BlockingRemoteFileSystem(Task gate) : IRemoteFileSystem
    {
        public Task<RemoteDirectory> ListAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(new RemoteDirectory(path, []));
        public Task CreateAsync(string path, bool directory, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RenameAsync(string path, string newPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public async Task DeleteAsync(string path, CancellationToken cancellationToken = default) => await gate;
        public Task SetPermissionsAsync(string path, int permissions, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<TextDocument> ReadTextAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(new TextDocument(path, "", 0, DateTime.UtcNow));
        public Task<TextDocument> SaveTextAsync(TextDocument original, string content, CancellationToken cancellationToken = default) => Task.FromResult(original);
        public Task UploadAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DownloadAsync(string remotePath, string localPath, IProgress<long> progress, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    /// <summary>删除被阻塞的本地文件系统，用来观察删除过程中的界面状态。</summary>
    private sealed class BlockingLocalFileSystem(Task gate) : ILocalFileSystem
    {
        public string HomeDirectory => Path.GetTempPath();
        public IReadOnlyList<string> Roots => [];
        public Task<LocalDirectory> ListAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(new LocalDirectory(path, []));
        public Task<DirectoryUploadPlan> PlanDirectoryUploadAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(new DirectoryUploadPlan(Path.GetFileName(path), [], [], 0));
        public Task<TextDocument> ReadTextAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(new TextDocument(path, "", 0, DateTime.UtcNow));
        public Task<TextDocument> SaveTextAsync(TextDocument original, string content, CancellationToken cancellationToken = default) => Task.FromResult(original);
        public Task CreateAsync(string directory, string name, bool folder) => Task.CompletedTask;
        public Task RenameAsync(string path, string name) => Task.CompletedTask;
        public async Task DeleteAsync(string path, CancellationToken cancellationToken = default) => await gate;
        public Task OpenAsync(string path) => Task.CompletedTask;
    }
}
