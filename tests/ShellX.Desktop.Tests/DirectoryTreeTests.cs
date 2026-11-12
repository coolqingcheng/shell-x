using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using ShellX.Desktop.Controls;
using ShellX.Core.Files;
using ShellX.Desktop.Features.FileBrowser;
using ShellX.Desktop.Services;
using ShellX.Infrastructure.Files;

namespace ShellX.Desktop.Tests;

/// <summary>文件管理器整树：展开动画、加载中、路径定位、虚拟滚动与节点过滤。</summary>
public class DirectoryTreeTests
{
    private static RemoteEntry Dir(string name, string path)
        => new(name, path, true, false, 0, DateTime.UtcNow, 420, 1000, 1000);

    private static FileBrowserViewModel RemoteSink()
        => new(new DialogService(new TextCodec()), NullLogger<FileBrowserViewModel>.Instance, new LocalFileSystem());

    private static RemoteTreeNodeViewModel RemoteNode(IFileTreeSink sink,
        IReadOnlyDictionary<string, IReadOnlyList<RemoteEntry>> tree)
    {
        var root = new RemoteEntry("/", "/", true, false, 0, DateTime.UtcNow, 493, 0, 0);
        return new RemoteTreeNodeViewModel(root, new FakeRemoteFileSystem(tree), CancellationToken.None, sink);
    }

    /// <summary>展开后子节点进入扁平行列表并播放出现动画，收起后动画结束才移除。</summary>
    [AvaloniaFact]
    public async Task RemoteNodeExpandsWithAnimationAndCollapses()
    {
        var tree = new Dictionary<string, IReadOnlyList<RemoteEntry>>
        {
            ["/"] = [Dir("etc", "/etc")],
            ["/etc"] = []
        };
        using var browser = RemoteSink();
        var root = RemoteNode(browser, tree);
        browser.TreeRoots.Add(root);
        browser.FlatNodes.Add(root);

        await root.ToggleCommand.ExecuteAsync(null);
        Assert.True(root.IsExpanded);
        Assert.Single(root.Children);
        Assert.Equal(2, browser.FlatNodes.Count);
        // 新出现的行先标记出来，视图据此播放出现动画。
        Assert.True(browser.FlatNodes[1].IsRevealing);
        await Task.Delay(700);
        Assert.False(browser.FlatNodes[1].IsRevealing);

        await root.ToggleCommand.ExecuteAsync(null);
        Assert.False(root.IsExpanded);
        await Task.Delay(320);
        Assert.Single(browser.FlatNodes);
    }

    /// <summary>
    /// 展开的行是淡入上滑出现的，不是直接出现；行高保持固定。
    /// 动画只走渲染属性，虚拟列表不需要逐帧重新测量。
    /// </summary>
    [AvaloniaFact]
    public async Task TreeRowRevealsWithRenderAnimationAndKeepsRowHeight()
    {
        var tree = new Dictionary<string, IReadOnlyList<RemoteEntry>>
        {
            ["/"] = [Dir("etc", "/etc")],
            ["/etc"] = []
        };
        using var browser = RemoteSink();
        var root = RemoteNode(browser, tree);
        browser.TreeRoots.Add(root);
        browser.FlatNodes.Add(root);
        var view = new RemoteFileBrowserView { DataContext = browser };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();
        try
        {
            await Task.Delay(150);
            await root.ToggleCommand.ExecuteAsync(null);
            // 等虚拟列表把新行渲染出来。
            await Task.Delay(40);
            var child = view.GetVisualDescendants().OfType<FileTreeNodeView>()
                .FirstOrDefault(row => ReferenceEquals(row.DataContext, root.Children[0]));
            Assert.NotNull(child);
            // 采样动画过程：透明度要从 0 涨到 1，中间得有中间态。
            var samples = new List<double>();
            for (var i = 0; i < 10; i++) { await Task.Delay(25); samples.Add(child!.Opacity); }
            Assert.Contains(samples, value => value > 0.01 && value < 0.99);
            // 行高始终固定，不再动画行高。
            Assert.Equal(FileTreeNodeViewModel.RowHeight, child!.Height, 1);
            Assert.Equal(1, child.Opacity, 2);
        }
        finally { window.Close(); }
    }

    /// <summary>右键“复制路径”拿的是选中节点的完整路径。</summary>
    [AvaloniaFact]
    public async Task SelectedTreeNodeExposesFullPathForCopy()
    {
        var tree = new Dictionary<string, IReadOnlyList<RemoteEntry>>
        {
            ["/"] = [Dir("etc", "/etc")],
            ["/etc"] = []
        };
        using var browser = RemoteSink();
        var root = RemoteNode(browser, tree);
        browser.TreeRoots.Add(root);
        browser.FlatNodes.Add(root);
        await root.ToggleCommand.ExecuteAsync(null);

        browser.Select(root.Children[0]);
        Assert.Equal("/etc", browser.SelectedPath);
        Assert.True(browser.SelectedIsDirectory);
    }

    /// <summary>右键“在终端打开”把目录交给外壳，由外壳新建连接。</summary>
    [AvaloniaFact]
    public async Task OpenInTerminalPassesDirectoryToHost()
    {
        var tree = new Dictionary<string, IReadOnlyList<RemoteEntry>>
        {
            ["/"] = [Dir("etc", "/etc")],
            ["/etc"] = []
        };
        using var browser = RemoteSink();
        var root = RemoteNode(browser, tree);
        browser.TreeRoots.Add(root);
        browser.FlatNodes.Add(root);
        await root.ToggleCommand.ExecuteAsync(null);

        string? requested = null;
        browser.OpenTerminalHandler = path => { requested = path; return Task.CompletedTask; };
        browser.Select(root.Children[0]);
        await browser.OpenInTerminalCommand.ExecuteAsync(null);
        Assert.Equal("/etc", requested);
    }

    /// <summary>目录读取慢时先显示加载中，读完才展开，避免动画高度不对。</summary>
    [AvaloniaFact]
    public async Task RemoteNodeShowsLoadingBeforeExpanding()
    {
        using var browser = RemoteSink();
        var root = new RemoteTreeNodeViewModel(new RemoteEntry("/", "/", true, false, 0, DateTime.UtcNow, 493, 0, 0),
            new SlowRemoteFileSystem(), CancellationToken.None, browser);
        var task = root.ToggleCommand.ExecuteAsync(null);
        await Task.Delay(80);
        Assert.True(root.IsLoading);
        Assert.False(root.IsExpanded);
        await task;
        Assert.False(root.IsLoading);
        Assert.True(root.IsExpanded);
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
        using var browser = RemoteSink();
        var root = RemoteNode(browser, tree);

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
            var files = new FakeLocalFileSystem(root);
            using var sink = new LocalFileBrowserViewModel(files, new DialogService(new TextCodec()), _ => Task.CompletedTask);
            var entry = new LocalEntry("root", root, true, false, false, 0, DateTime.UtcNow);
            var node = new LocalTreeNodeViewModel(entry, files, CancellationToken.None, sink);

            await node.ToggleCommand.ExecuteAsync(null);
            Assert.Equal(["alpha", "beta"], node.Children.Select(c => c.Name).ToArray());
        }
        finally
        {
            File.SetAttributes(hidden.FullName, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
    }

    /// <summary>本地目录树：高亮当前目录，打开节点可以跳转过去。</summary>
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

            var treeRoot = Assert.Single(browser.TreeRoots);
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
        using var browser = RemoteSink();
        var node = RemoteNode(browser, new Dictionary<string, IReadOnlyList<RemoteEntry>> { ["/"] = [] });
        var view = new FileTreeNodeView { DataContext = node };
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
            NullLogger<FileBrowserViewModel>.Instance, new LocalFileSystem());
        browser.Attach(files, CancellationToken.None);
        await browser.NavigateAsync("/etc/nginx");

        var root = Assert.Single(browser.TreeRoots);
        Assert.True(root.IsExpanded);
        var etc = Assert.Single(root.Children);
        Assert.True(etc.IsExpanded);
        var nginx = Assert.Single(etc.Children);
        Assert.True(nginx.IsCurrent);
    }

    /// <summary>行数很多时只渲染视口内的行，滚动列表不会卡死。</summary>
    [AvaloniaFact]
    public async Task TreeListVirtualizesLongRows()
    {
        using var browser = RemoteSink();
        var files = new FakeRemoteFileSystem(new Dictionary<string, IReadOnlyList<RemoteEntry>> { ["/"] = [] });
        for (var i = 0; i < 2000; i++)
        {
            var entry = new RemoteEntry($"file{i}.txt", $"/file{i}.txt", false, false, 0, DateTime.UtcNow, 420, 1000, 1000);
            browser.FlatNodes.Add(new RemoteTreeNodeViewModel(entry, files, CancellationToken.None, browser));
        }
        var view = new RemoteFileBrowserView { DataContext = browser };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();
        try
        {
            await Task.Delay(200);
            var realized = view.GetVisualDescendants().OfType<FileTreeNodeView>().Count();
            Assert.True(realized > 0, "树没有渲染任何行");
            Assert.True(realized < 200, $"虚拟滚动没有生效，实际渲染 {realized} 行");
        }
        finally { window.Close(); }
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

    /// <summary>远程文件系统替身：读取很慢，用来观察加载中状态。</summary>
    private sealed class SlowRemoteFileSystem : IRemoteFileSystem
    {
        public async Task<RemoteDirectory> ListAsync(string path, CancellationToken cancellationToken = default)
        {
            await Task.Delay(300, cancellationToken);
            return new RemoteDirectory(path, []);
        }
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
