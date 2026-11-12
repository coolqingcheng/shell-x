using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using ShellX.Core.Files;
using ShellX.Core.Settings;
using ShellX.Desktop.Controls;
using ShellX.Desktop.Features.FileBrowser;
using ShellX.Desktop.Services;
using ShellX.Infrastructure.Files;

namespace ShellX.Desktop.Tests;

public sealed class FileGridTests
{
    /// <summary>路径栏在两个面板里都保持垂直居中，编辑态也不会偏。</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PathBarStaysVerticallyCenteredInBothPanes(bool remote)
    {
        var name = "一个需要较宽列才能完整显示的配置文件-nginx.conf";
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-columns-" + Guid.NewGuid().ToString("N"))).FullName;
        await File.WriteAllTextAsync(Path.Combine(root, name), "test");
        using var browser = new FileBrowserViewModel(new DialogService(new TextCodec()), NullLogger<FileBrowserViewModel>.Instance, new LocalFileSystem(root));
        browser.IsAvailable = true;
        UserControl view = remote ? new RemoteFileBrowserView { DataContext = browser } : new LocalFileBrowserView { DataContext = browser.Local };
        var window = new Window { Content = view, Width = 560, Height = 360 }; window.Show();
        try
        {
            await browser.Local.InitializeAsync();
            await Task.Delay(60);
            var bar = view.FindControl<PathBar>("PathBar")!;
            bar.BeginEdit();
            await Task.Delay(60);
            var path = bar.EditInput;
            path.Text = remote ? "/etc/nginx/conf.d" : root;
            foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light })
            {
                ThemeService.Apply(theme); await Task.Delay(40);
                var presenter = path.GetVisualDescendants().OfType<TextPresenter>().Single();
                var center = presenter.TranslatePoint(new Point(0, presenter.Bounds.Height / 2), path)!.Value.Y;
                Assert.InRange(Math.Abs(center - path.Bounds.Height / 2), 0, 1);
                Assert.InRange(presenter.TextLayout.Height, 1, path.Bounds.Height - 2);
                Assert.InRange(Math.Abs(path.Bounds.Center.Y - ((Control)path.Parent!).Bounds.Height / 2), 0, 1);
                if (Environment.GetEnvironmentVariable("SHELLX_PREVIEW_PATH") is { Length: > 0 } pathPreview)
                {
                    using var image = new RenderTargetBitmap(new PixelSize(560, 360), new Vector(96, 96));
                    image.Render(window); image.Save(Path.Combine(Path.GetDirectoryName(pathPreview)!, $"{(remote ? "remote" : "local")}-path-{theme}.png"), PngBitmapEncoderOptions.Default);
                }
            }
            ThemeService.Apply(AppTheme.Dark);
        }
        finally { window.Close(); Directory.Delete(root, true); }
    }

    /// <summary>文件树支持 Ctrl+A 全选当前可见的行。</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ControlASelectsAllRowsInFileTree(bool remote)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-selectall-" + Guid.NewGuid().ToString("N"))).FullName;
        await File.WriteAllTextAsync(Path.Combine(root, "one.txt"), "1");
        await File.WriteAllTextAsync(Path.Combine(root, "two.txt"), "2");
        using var browser = new FileBrowserViewModel(new DialogService(new TextCodec()), NullLogger<FileBrowserViewModel>.Instance, new LocalFileSystem(root));
        browser.IsAvailable = true;
        UserControl view = remote ? new RemoteFileBrowserView { DataContext = browser } : new LocalFileBrowserView { DataContext = browser.Local };
        var window = new Window { Content = view, Width = 560, Height = 360 }; window.Show();
        try
        {
            await browser.Local.InitializeAsync();
            await Task.Delay(60);
            if (remote)
            {
                browser.FlatNodes.Clear();
                browser.FlatNodes.Add(new RemoteTreeNodeViewModel(
                    new RemoteEntry("one.txt", "/one.txt", false, false, 1, DateTime.UtcNow, 420, 1000, 1000),
                    new StubRemoteFileSystem(), CancellationToken.None, browser));
                browser.FlatNodes.Add(new RemoteTreeNodeViewModel(
                    new RemoteEntry("two.txt", "/two.txt", false, false, 2, DateTime.UtcNow, 420, 1000, 1000),
                    new StubRemoteFileSystem(), CancellationToken.None, browser));
            }
            else
            {
                browser.Local.FlatNodes.Clear();
                var local = new LocalFileSystem(root);
                browser.Local.FlatNodes.Add(new LocalTreeNodeViewModel(
                    new LocalEntry("one.txt", Path.Combine(root, "one.txt"), false, false, false, 1, DateTime.UtcNow),
                    local, CancellationToken.None, browser.Local));
                browser.Local.FlatNodes.Add(new LocalTreeNodeViewModel(
                    new LocalEntry("two.txt", Path.Combine(root, "two.txt"), false, false, false, 2, DateTime.UtcNow),
                    local, CancellationToken.None, browser.Local));
            }
            view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.A, KeyModifiers = KeyModifiers.Control });
            await Task.Delay(40);
            var selected = remote ? browser.SelectedEntries.Count : browser.Local.SelectedEntries.Count;
            Assert.Equal(2, selected);
        }
        finally { window.Close(); Directory.Delete(root, true); }
    }

    /// <summary>远程文件系统替身：树节点用不到真实读取。</summary>
    private sealed class StubRemoteFileSystem : IRemoteFileSystem
    {
        public Task<RemoteDirectory> ListAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(new RemoteDirectory(path, []));
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
}
