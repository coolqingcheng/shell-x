using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using ShellX.Desktop.Controls;
using ShellX.Core.Files;
using ShellX.Desktop.Features.FileBrowser;
using ShellX.Desktop.Services;
using ShellX.Infrastructure.Files;

namespace ShellX.Desktop.Tests;

/// <summary>文件管理器目录树：展开动画参数、路径定位与本地节点过滤。</summary>
public class DirectoryTreeTests
{
    private static RemoteEntry Dir(string name, string path)
        => new(name, path, true, false, 0, DateTime.UtcNow, 420, 1000, 1000);

    private static DirectoryNodeViewModel RemoteNode(IReadOnlyDictionary<string, IReadOnlyList<RemoteEntry>> tree)
    {
        var root = new DirectoryNodeViewModel("/", "/", path => Task.FromResult(tree[path]), _ => Task.CompletedTask, _ => { });
        return root;
    }

    /// <summary>展开与折叠时箭头角度、透明度、位移随之变化，折叠动画结束后才真正隐藏子节点。</summary>
    [AvaloniaFact]
    public async Task RemoteNodeAnimatesExpandAndCollapse()
    {
        var tree = new Dictionary<string, IReadOnlyList<RemoteEntry>>
        {
            ["/"] = [Dir("etc", "/etc")],
            ["/etc"] = []
        };
        var root = RemoteNode(tree);

        await root.ToggleCommand.ExecuteAsync(null);
        Assert.True(root.IsExpanded);
        Assert.True(root.IsChildrenVisible);
        Assert.Equal(90, root.ChevronAngle);
        Assert.Equal(1, root.ExpandOpacity);
        Assert.Equal(0, root.ExpandOffset);
        Assert.Single(root.Children);

        await root.ToggleCommand.ExecuteAsync(null);
        Assert.False(root.IsExpanded);
        Assert.Equal(0, root.ChevronAngle);
        Assert.Equal(0, root.ExpandOpacity);
        Assert.Equal(-6, root.ExpandOffset);
        // 折叠时先保留子节点，等淡出动画播完再隐藏。
        Assert.True(root.IsChildrenVisible);
        await Task.Delay(260);
        Assert.False(root.IsChildrenVisible);
    }

    /// <summary>定位到指定路径时，沿途展开父目录并高亮目标节点。</summary>
    [AvaloniaFact]
    public async Task ExpandToPathOpensAncestorsAndHighlightsTarget()
    {
        var tree = new Dictionary<string, IReadOnlyList<RemoteEntry>>
        {
            ["/"] = [Dir("etc", "/etc")],
            ["/etc"] = [Dir("nginx", "/etc/nginx")],
            ["/etc/nginx"] = [Dir("conf.d", "/etc/nginx/conf.d")],
            ["/etc/nginx/conf.d"] = []
        };
        var root = RemoteNode(tree);

        Assert.True(await root.ExpandToAsync("/etc/nginx/conf.d"));
        Assert.True(root.IsExpanded);
        var etc = Assert.Single(root.Children);
        Assert.True(etc.IsExpanded);
        var nginx = Assert.Single(etc.Children);
        Assert.True(nginx.IsExpanded);
        var conf = Assert.Single(nginx.Children);
        Assert.True(conf.IsCurrent);
        Assert.False(etc.IsCurrent);
    }

    /// <summary>本地目录树：隐藏目录不显示，子目录按名称排序。</summary>
    [AvaloniaFact]
    public async Task LocalNodeSkipsHiddenDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "shellx-tree-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "beta"));
        Directory.CreateDirectory(Path.Combine(root, "alpha"));
        var hidden = Directory.CreateDirectory(Path.Combine(root, "hidden"));
        File.SetAttributes(hidden.FullName, FileAttributes.Hidden);
        try
        {
            var node = new LocalDirectoryNodeViewModel("root", root,
                path => Task.FromResult<IReadOnlyList<LocalEntry>>(
                [
                    new LocalEntry("alpha", Path.Combine(root, "alpha"), true, false, false, 0, DateTime.UtcNow),
                    new LocalEntry("beta", Path.Combine(root, "beta"), true, false, false, 0, DateTime.UtcNow),
                    new LocalEntry("hidden", hidden.FullName, true, false, true, 0, DateTime.UtcNow),
                    new LocalEntry("file.txt", Path.Combine(root, "file.txt"), false, false, false, 0, DateTime.UtcNow)
                ]),
                _ => Task.CompletedTask, _ => { });

            await node.ToggleCommand.ExecuteAsync(null);
            Assert.Equal(["alpha", "beta"], node.Children.Select(c => c.Name).ToArray());
        }
        finally
        {
            File.SetAttributes(hidden.FullName, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
    }

    /// <summary>本地目录树：高亮当前目录，点击节点可以跳转过去。</summary>
    [AvaloniaFact]
    public async Task LocalTreeHighlightsCurrentDirectoryAndNavigates()
    {
        var root = Path.Combine(Path.GetTempPath(), "shellx-tree-" + Guid.NewGuid().ToString("N"));
        var child = Directory.CreateDirectory(Path.Combine(root, "sub")).FullName;
        try
        {
            var files = new FakeLocalFileSystem(root);
            using var browser = new LocalFileBrowserViewModel(files, new DialogService(new TextCodec()), _ => Task.CompletedTask);
            await browser.InitializeAsync();

            var treeRoot = Assert.Single(browser.DirectoryRoots);
            Assert.True(treeRoot.IsCurrent);

            await treeRoot.ToggleCommand.ExecuteAsync(null);
            var sub = Assert.Single(treeRoot.Children);
            Assert.Equal("sub", sub.Name);

            await sub.OpenCommand.ExecuteAsync(null);
            Assert.Equal(child, browser.Path);
            Assert.True(sub.IsCurrent);
        }
        finally { Directory.Delete(root, true); }
    }

    /// <summary>展开时箭头是平滑旋转的，不是直接跳到 90 度。</summary>
    [AvaloniaFact]
    public async Task TreeChevronRotatesSmoothly()
    {
        var node = new DirectoryNodeViewModel("/", "/", _ => Task.FromResult<IReadOnlyList<RemoteEntry>>([]), _ => Task.CompletedTask, _ => { });
        var view = new DirectoryNodeView { DataContext = node };
        var window = new Window { Content = view, Width = 200, Height = 200 };
        window.Show();
        try
        {
            await Task.Delay(80);
            var icon = view.GetVisualDescendants().OfType<StrokeIcon>().First();
            var transform = (RotateTransform)icon.RenderTransform!;
            Assert.Equal(0, transform.Angle);

            await node.ToggleCommand.ExecuteAsync(null);
            await Task.Delay(70);
            var mid = transform.Angle;
            await Task.Delay(400);
            Assert.True(mid > 1 && mid < 89, $"箭头没有平滑旋转：mid={mid}");
            Assert.True(Math.Abs(transform.Angle - 90) < 0.5, $"箭头旋转没有结束：{transform.Angle}");
        }
        finally { window.Close(); }
    }

    /// <summary>远程目录树跟随当前路径：展开祖先目录并高亮当前目录。</summary>
    [AvaloniaFact]
    public async Task RemoteTreeFollowsCurrentPath()
    {
        var files = new FakeRemoteFileSystem(new Dictionary<string, IReadOnlyList<RemoteEntry>>
        {
            ["/"] = [Dir("etc", "/etc")],
            ["/etc"] = [Dir("nginx", "/etc/nginx")],
            ["/etc/nginx"] = []
        });
        using var browser = new FileBrowserViewModel(new DialogService(new TextCodec()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<FileBrowserViewModel>.Instance, new LocalFileSystem());
        browser.Attach(files, CancellationToken.None);
        await browser.NavigateAsync("/etc/nginx");

        var root = Assert.Single(browser.DirectoryRoots);
        Assert.True(root.IsExpanded);
        var etc = Assert.Single(root.Children);
        Assert.True(etc.IsExpanded);
        var nginx = Assert.Single(etc.Children);
        Assert.True(nginx.IsCurrent);
    }

    /// <summary>远程文件系统替身：按目录返回固定条目。</summary>
    private sealed class FakeRemoteFileSystem(IReadOnlyDictionary<string, IReadOnlyList<RemoteEntry>> tree) : IRemoteFileSystem
    {
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
        public Task UploadAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DownloadAsync(string remotePath, string localPath, IProgress<long> progress, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    /// <summary>本地文件系统替身：只有一个根目录，便于测试目录树。</summary>
    private sealed class FakeLocalFileSystem(string root) : ILocalFileSystem
    {
        public string HomeDirectory => root;
        public IReadOnlyList<string> Roots => [root];

        public Task<LocalDirectory> ListAsync(string path, CancellationToken cancellationToken = default)
        {
            var entries = Directory.Exists(path)
                ? Directory.GetFileSystemEntries(path).Select(item =>
                {
                    var info = new DirectoryInfo(item);
                    var isDirectory = info.Attributes.HasFlag(FileAttributes.Directory);
                    var size = isDirectory ? 0 : new FileInfo(item).Length;
                    return new LocalEntry(info.Name, info.FullName, isDirectory,
                        info.Attributes.HasFlag(FileAttributes.ReparsePoint), info.Attributes.HasFlag(FileAttributes.Hidden),
                        size, info.LastWriteTimeUtc);
                }).ToArray()
                : [];
            return Task.FromResult(new LocalDirectory(path, entries));
        }

        public Task<DirectoryUploadPlan> PlanDirectoryUploadAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(new DirectoryUploadPlan(Path.GetFileName(path), [], [], 0));
        public Task<TextDocument> ReadTextAsync(string path, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<TextDocument> SaveTextAsync(TextDocument original, string content, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task CreateAsync(string directory, string name, bool folder) => Task.CompletedTask;
        public Task RenameAsync(string path, string name) => Task.CompletedTask;
        public Task DeleteAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task OpenAsync(string path) => Task.CompletedTask;
    }
}
