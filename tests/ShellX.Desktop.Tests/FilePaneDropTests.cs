using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Platform.Storage;
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

/// <summary>
/// 文件管理器的拖放：整块面板是一个放置区。
/// 拖到文件行、文件夹行、面包屑还是空白处，效果都一样——
/// 一律上传到当前打开的目录，落点不再决定目标目录。
/// </summary>
public sealed class FilePaneDropTests
{
    [AvaloniaFact]
    public async Task DroppingOnARowUploadsToTheCurrentDirectory()
    {
        var local = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-pane-drop-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var source = Path.Combine(local, "payload.txt");
            await File.WriteAllTextAsync(source, "hello");
            var files = new RecordingRemoteFileSystem(new Dictionary<string, IReadOnlyList<RemoteEntry>>
            {
                ["/"] = [Entry("etc", true), Entry("readme.txt", false)]
            });
            using var pane = new RemoteFilesPaneViewModel(new PaneDialogs(), NullLogger<RemoteFilesPaneViewModel>.Instance, new StubLocalFileSystem());
            pane.Attach(files, CancellationToken.None, "测试服务器");
            var view = new RemoteFilesPaneView { DataContext = pane };
            var window = new Window { Content = view, Width = 900, Height = 600 }; window.Show();
            try
            {
                await WaitForAsync(() => pane.Entries.Count >= 2);
                await WaitForAsync(() => view.GetVisualDescendants().OfType<FileTreeNodeView>().Any());

                var transfer = new DataTransfer();
                transfer.Add(DataTransferItem.CreateFile((await window.StorageProvider.TryGetFileFromPathAsync(source))!));

                var row = Row(view, "etc");
                row.RaiseEvent(new DragEventArgs(DragDrop.DragOverEvent, transfer, row, new Point(0, 0), KeyModifiers.None));

                // 整块面板高亮，而不是某一行。
                Assert.True(pane.IsDropTarget);
                // 行本身不再是放置目标：没有行级高亮，也没有放置状态属性。
                var border = row.GetVisualDescendants().OfType<Border>().First(b => b.Name == "Row");
                Assert.DoesNotContain("droptarget", border.Classes);
                Assert.Null(typeof(FileTreeNodeViewModel).GetProperty("IsDropTarget"));

                // 落在“etc”这一行上，但目标是当前目录，不是 etc 目录。
                row.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, transfer, row, new Point(0, 0), KeyModifiers.None));
                await WaitForAsync(() => files.LastUpload != null);
                Assert.Equal("/payload.txt", files.LastUpload?.Remote);
                Assert.False(pane.IsDropTarget);
            }
            finally { window.Close(); }
        }
        finally { Directory.Delete(local, true); }
    }

    [AvaloniaFact]
    public async Task TheWholePaneAcceptsTheDropNotJustTheList()
    {
        var local = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-pane-drop-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var source = Path.Combine(local, "payload.txt");
            await File.WriteAllTextAsync(source, "hello");
            var files = new RecordingRemoteFileSystem(new Dictionary<string, IReadOnlyList<RemoteEntry>>
            {
                ["/"] = [Entry("readme.txt", false)]
            });
            using var pane = new RemoteFilesPaneViewModel(new PaneDialogs(), NullLogger<RemoteFilesPaneViewModel>.Instance, new StubLocalFileSystem());
            pane.Attach(files, CancellationToken.None, "测试服务器");
            var view = new RemoteFilesPaneView { DataContext = pane };
            var window = new Window { Content = view, Width = 900, Height = 600 }; window.Show();
            try
            {
                await WaitForAsync(() => pane.Entries.Count >= 1);
                await WaitForAsync(() => view.GetVisualDescendants().OfType<FileTreeNodeView>().Any());

                var transfer = new DataTransfer();
                transfer.Add(DataTransferItem.CreateFile((await window.StorageProvider.TryGetFileFromPathAsync(source))!));

                // 拖到面包屑上：同样是面板级放置。
                var bar = view.GetVisualDescendants().OfType<PathBar>().First();
                bar.RaiseEvent(new DragEventArgs(DragDrop.DragOverEvent, transfer, bar, new Point(0, 0), KeyModifiers.None));
                Assert.True(pane.IsDropTarget);
                Assert.Contains("/", pane.DropHint);

                bar.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, transfer, bar, new Point(0, 0), KeyModifiers.None));
                await WaitForAsync(() => files.LastUpload != null);
                Assert.Equal("/payload.txt", files.LastUpload?.Remote);
            }
            finally { window.Close(); }
        }
        finally { Directory.Delete(local, true); }
    }

    [AvaloniaFact]
    public async Task DraggingOutOfThePaneDoesNotHighlightIt()
    {
        var local = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-pane-drop-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var source = Path.Combine(local, "payload.txt");
            await File.WriteAllTextAsync(source, "hello");
            var files = new RecordingRemoteFileSystem(new Dictionary<string, IReadOnlyList<RemoteEntry>>
            {
                ["/"] = [Entry("readme.txt", false)]
            });
            using var pane = new RemoteFilesPaneViewModel(new PaneDialogs(), NullLogger<RemoteFilesPaneViewModel>.Instance, new StubLocalFileSystem());
            pane.Attach(files, CancellationToken.None, "测试服务器");
            var view = new RemoteFilesPaneView { DataContext = pane };
            var window = new Window { Content = view, Width = 900, Height = 600 }; window.Show();
            try
            {
                await WaitForAsync(() => pane.Entries.Count >= 1);
                await WaitForAsync(() => view.GetVisualDescendants().OfType<FileTreeNodeView>().Any());

                // 从面板往外拖：载荷里带着本应用的选择令牌，还挂了真实文件。
                var row = Row(view, "readme.txt");
                using var payload = pane.CreateDragPayload(pane.Entries[0])!;
                payload.Data.Add(DataTransferItem.CreateFile((await window.StorageProvider.TryGetFileFromPathAsync(source))!));

                row.RaiseEvent(new DragEventArgs(DragDrop.DragOverEvent, payload.Data, row, new Point(0, 0), KeyModifiers.None));

                // 往外挂文件不是往面板里塞，面板不能自己高亮自己。
                Assert.False(pane.IsDropTarget);
            }
            finally { window.Close(); }
        }
        finally { Directory.Delete(local, true); }
    }

    private static FileTreeNodeView Row(Visual root, string name)
        => root.GetVisualDescendants().OfType<FileTreeNodeView>()
            .First(node => (node.DataContext as FileTreeNodeViewModel)?.Name == name);

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 80; i++)
        {
            if (condition()) return;
            await Task.Delay(25);
        }
        Assert.True(condition());
    }

    private static RemoteEntry Entry(string name, bool directory, long size = 0)
        => new(name, "/" + name, directory, false, size, DateTime.UtcNow, 493, 0, 0);

    /// <summary>远程文件系统替身：记录上传和下载。</summary>
    private sealed class RecordingRemoteFileSystem(IReadOnlyDictionary<string, IReadOnlyList<RemoteEntry>> tree) : IRemoteFileSystem
    {
        public (string Local, string Remote)? LastUpload { get; private set; }
        public (string Remote, string Local)? LastDownload { get; private set; }

        public Task<RemoteDirectory> ListAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(new RemoteDirectory(path, tree.TryGetValue(path, out var entries) ? entries : []));
        public Task CreateAsync(string path, bool directory, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RenameAsync(string path, string newPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetPermissionsAsync(string path, int permissions, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<TextDocument> ReadTextAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(new TextDocument(path, "", 0, DateTime.UtcNow));
        public Task<TextDocument> SaveTextAsync(TextDocument original, string content, CancellationToken cancellationToken = default)
            => Task.FromResult(original);
        public Task UploadAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken cancellationToken = default)
        { LastUpload = (localPath, remotePath); progress.Report(1); return Task.CompletedTask; }
        public Task DownloadAsync(string remotePath, string localPath, IProgress<long> progress, CancellationToken cancellationToken = default)
        { LastDownload = (remotePath, localPath); progress.Report(5); return Task.CompletedTask; }
        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    /// <summary>对话框替身：这里都用默认确认。</summary>
    private sealed class PaneDialogs : IDialogService
    {
        public Task<string?> PickDownloadFolderAsync() => Task.FromResult<string?>(null);
        public Task<ConnectionEditResult?> EditConnectionAsync(ConnectionProfile? profile, ConnectionSecret? secret) => Task.FromResult<ConnectionEditResult?>(null);
        public Task<bool> ConfirmAsync(string title, string message, bool danger = false) => Task.FromResult(true);
        public Task<string?> PromptAsync(string title, string label, string value = "", bool secret = false) => Task.FromResult<string?>(null);
        public Task ShowErrorAsync(string message) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> PickUploadAsync() => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> PickDownloadAsync(string name) => Task.FromResult<string?>(null);
        public Task EditFileAsync(TextDocument document, Func<TextDocument, string, Task<TextDocument>> save) => Task.CompletedTask;
        public Task<int?> PermissionsAsync(RemoteEntry entry) => Task.FromResult<int?>(null);
        public Task<TunnelDefinition?> EditTunnelAsync() => Task.FromResult<TunnelDefinition?>(null);
        public Task<AppSettings?> SettingsAsync(AppSettings settings) => Task.FromResult<AppSettings?>(null);
    }

    /// <summary>本地文件系统替身：这个面板不读本地目录。</summary>
    private sealed class StubLocalFileSystem : ILocalFileSystem
    {
        public string HomeDirectory => Path.GetTempPath();
        public IReadOnlyList<string> Roots => [Path.GetTempPath()];
        public Task<LocalDirectory> ListAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(new LocalDirectory(path, []));
        public Task<DirectoryUploadPlan> PlanDirectoryUploadAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(new DirectoryUploadPlan(Path.GetFileName(path), [], [], 0));
        public Task<TextDocument> ReadTextAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(new TextDocument(path, "", 0, DateTime.UtcNow));
        public Task<TextDocument> SaveTextAsync(TextDocument original, string content, CancellationToken cancellationToken = default)
            => Task.FromResult(original);
        public Task CreateAsync(string directory, string name, bool folder) => Task.CompletedTask;
        public Task RenameAsync(string path, string name) => Task.CompletedTask;
        public Task DeleteAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task OpenAsync(string path) => Task.CompletedTask;
    }
}
