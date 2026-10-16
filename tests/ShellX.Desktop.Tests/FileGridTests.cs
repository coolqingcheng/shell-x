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
using ShellX.Desktop.Features.FileBrowser;
using ShellX.Desktop.Services;
using ShellX.Infrastructure.Files;

namespace ShellX.Desktop.Tests;

public sealed class FileGridTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NarrowFilePanesSupportDraggingAndDoubleClickingColumnEdges(bool remote)
    {
        var name = "一个需要较宽列才能完整显示的配置文件-nginx.conf";
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-columns-" + Guid.NewGuid().ToString("N"))).FullName;
        await File.WriteAllTextAsync(Path.Combine(root, name), "test");
        using var browser = new FileBrowserViewModel(new DialogService(new TextCodec()), NullLogger<FileBrowserViewModel>.Instance, new LocalFileSystem(root));
        browser.IsAvailable = true;
        browser.Entries.Add(new RemoteEntry(name, "/" + name, false, false, 4321, DateTime.UtcNow, 420, 1000, 1000));
        UserControl view = remote ? new RemoteFileBrowserView { DataContext = browser } : new LocalFileBrowserView { DataContext = browser.Local };
        var grid = view.FindControl<DataGrid>(remote ? "FileGrid" : "LocalGrid")!;
        var window = new Window { Content = view, Width = 560, Height = 360 }; window.Show();
        try
        {
            await browser.Local.InitializeAsync();
            await Task.Delay(60);
            var path = view.FindControl<TextBox>("PathInput")!;
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
            Assert.True(grid.CanUserResizeColumns);
            var column = grid.Columns[0];
            var header = grid.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(h => Equals(h.Content, "名称"));
            var original = column.ActualWidth;
            Assert.True(original > 200);
            var edge = header.TranslatePoint(new Point(header.Bounds.Width - 2, header.Bounds.Height / 2), window)!.Value;
            window.MouseMove(edge); window.MouseDown(edge, MouseButton.Left);
            window.MouseMove(edge + new Vector(-65, 0)); window.MouseUp(edge + new Vector(-65, 0), MouseButton.Left);
            await Task.Delay(30);
            Assert.True(column.ActualWidth < original - 40, $"{original} -> {column.ActualWidth}");
            var resized = column.ActualWidth;
            // A fresh gesture after the double-click interval must reset the pixel width to content width.
            await Task.Delay(550);
            edge = header.TranslatePoint(new Point(header.Bounds.Width - 2, header.Bounds.Height / 2), window)!.Value;
            window.MouseMove(edge);
            window.MouseDown(edge, MouseButton.Left); window.MouseUp(edge, MouseButton.Left);
            window.MouseDown(edge, MouseButton.Left); window.MouseUp(edge, MouseButton.Left);
            await Task.Delay(40);
            Assert.True(column.Width.IsAuto);
            Assert.True(column.ActualWidth > resized + 40);
            if (Environment.GetEnvironmentVariable("SHELLX_PREVIEW_PATH") is { Length: > 0 } preview)
            {
                using var bitmap = new RenderTargetBitmap(new PixelSize(560, 360), new Vector(96, 96));
                bitmap.Render(window); bitmap.Save(Path.Combine(Path.GetDirectoryName(preview)!, remote ? "remote-columns.png" : "local-columns.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.Close(); Directory.Delete(root, true); }
    }
}
