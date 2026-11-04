using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.Logging.Abstractions;
using ShellX.Core.Files;
using ShellX.Desktop.Controls;
using ShellX.Desktop.Features.FileBrowser;
using ShellX.Desktop.Services;
using ShellX.Infrastructure.Files;

namespace ShellX.Desktop.Tests;

public class FileIconGeometryTests
{
    [Fact]
    public void EveryCategoryHasAnIcon()
        => Assert.All(FileTypes.All, category => Assert.False(string.IsNullOrWhiteSpace(FileIconView.GeometryFor(category))));

    [AvaloniaTheory]
    [InlineData(FileTypes.Image)][InlineData(FileTypes.Video)][InlineData(FileTypes.Audio)]
    [InlineData(FileTypes.Archive)][InlineData(FileTypes.Code)][InlineData(FileTypes.Config)]
    [InlineData(FileTypes.Program)][InlineData(FileTypes.Database)][InlineData(FileTypes.Text)]
    [InlineData(FileTypes.Sheet)][InlineData(FileTypes.Folder)][InlineData(FileTypes.Link)]
    [InlineData(FileTypes.Pdf)][InlineData(FileTypes.Document)][InlineData(FileTypes.File)]
    [InlineData("unknown-token")][InlineData("")]
    public void IconGeometryParses(string category)
    {
        var geometry = Avalonia.Media.Geometry.Parse(FileIconView.GeometryFor(category));
        Assert.True(geometry.Bounds.Width > 0 && geometry.Bounds.Height > 0);
    }

    private static readonly string[] SampleNames =
    [
        "assets", "photo.png", "movie.mkv", "music.flac", "backup.tar.gz", "server.sh", "config.yaml",
        "access.log", "report.csv", "manual.pdf", "app.exe", "site.db", "合同.docx", "id_rsa.pub", "readme", "notes.dat"
    ];

    // 有 SHELLX_PREVIEW_PATH 时导出左右两个文件面板的预览图，用来肉眼核对图标。
    [AvaloniaFact]
    public async Task FilePanesRenderTypedIcons()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-icons-" + Guid.NewGuid().ToString("N"))).FullName;
        using var browser = new FileBrowserViewModel(new DialogService(new TextCodec()), NullLogger<FileBrowserViewModel>.Instance, new LocalFileSystem(root));
        browser.IsAvailable = true;
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        foreach (var name in SampleNames.Skip(1)) await File.WriteAllTextAsync(Path.Combine(root, name), "preview");
        await browser.Local.InitializeAsync();
        foreach (var name in SampleNames)
            browser.Entries.Add(new RemoteEntry(name, "/" + name, name == "assets", false, 1024, DateTime.UtcNow, 420, 1000, 1000));
        Assert.Equal(FileTypes.Folder, browser.Entries[0].Category);
        Assert.Equal(FileTypes.Image, browser.Entries[1].Category);
        Assert.Equal(FileTypes.Video, browser.Entries[2].Category);
        Assert.Equal(FileTypes.Archive, browser.Entries[4].Category);
        Assert.Equal(FileTypes.Pdf, browser.Entries[9].Category);
        Assert.Equal(FileTypes.Program, browser.Entries[10].Category);
        Assert.Equal(FileTypes.File, browser.Entries[^1].Category);
        try
        {
            foreach (var (view, name, dataContext) in new (UserControl, string, object)[]
                     {
                         (new RemoteFileBrowserView(), "file-icons-remote.png", browser),
                         (new LocalFileBrowserView(), "file-icons-local.png", browser.Local)
                     })
            {
                view.DataContext = dataContext;
                var window = new Window { Content = view, Width = 620, Height = 640 };
                window.Show();
                try
                {
                    await Task.Delay(120);
                    if (Environment.GetEnvironmentVariable("SHELLX_PREVIEW_PATH") is { Length: > 0 } preview)
                    {
                        using var image = new RenderTargetBitmap(new PixelSize(620, 640), new Vector(96, 96));
                        image.Render(window);
                        image.Save(Path.Combine(Path.GetDirectoryName(preview)!, name), PngBitmapEncoderOptions.Default);
                    }
                }
                finally { window.Close(); }
            }
        }
        finally { Directory.Delete(root, true); }
    }
}