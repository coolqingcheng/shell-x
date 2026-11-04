using ShellX.Core.Files;

namespace ShellX.Core.Tests;

public class FileTypeTests
{
    [Theory]
    [InlineData("photo.png", FileTypes.Image, "图片")]
    [InlineData("PHOTO.JPEG", FileTypes.Image, "图片")]
    [InlineData("music.flac", FileTypes.Audio, "音频")]
    [InlineData("movie.mkv", FileTypes.Video, "视频")]
    [InlineData("backup.tar.gz", FileTypes.Archive, "压缩包")]
    [InlineData("index.html", FileTypes.Code, "代码")]
    [InlineData("server.sh", FileTypes.Code, "代码")]
    [InlineData("config.yaml", FileTypes.Config, "配置")]
    [InlineData(".env", FileTypes.Config, "配置")]
    [InlineData("id_rsa.pub", FileTypes.Config, "配置")]
    [InlineData("access.log", FileTypes.Text, "文本")]
    [InlineData("report.csv", FileTypes.Sheet, "表格")]
    [InlineData("manual.pdf", FileTypes.Pdf, "PDF")]
    [InlineData("合同.docx", FileTypes.Document, "文档")]
    [InlineData("app.exe", FileTypes.Program, "程序")]
    [InlineData("site.db", FileTypes.Database, "数据库")]
    [InlineData("notes", FileTypes.File, "文件")]
    public void FilesAreClassifiedByExtension(string name, string category, string label)
    {
        Assert.Equal(category, FileTypes.Category(name, false, false));
        Assert.Equal(label, FileTypes.Label(category));
    }

    [Fact]
    public void DirectoriesAndLinksWinOverExtension()
    {
        Assert.Equal(FileTypes.Folder, FileTypes.Category("archive.zip", true, false));
        Assert.Equal(FileTypes.Link, FileTypes.Category("photo.png", false, true));
        Assert.Equal("文件夹", FileTypes.Label(FileTypes.Folder));
        Assert.Equal("链接", FileTypes.Label(FileTypes.Link));
    }

    [Theory]
    [InlineData("Dockerfile")][InlineData("Makefile")][InlineData("docker-compose.yml")]
    public void ScriptNamesWithoutKnownExtensionStillClassify(string name)
        => Assert.NotEqual(FileTypes.File, FileTypes.Category(name, false, false));

    [Fact]
    public void EveryCategoryHasALabel()
        => Assert.All(FileTypes.All, category => Assert.False(string.IsNullOrWhiteSpace(FileTypes.Label(category))));

    [Fact]
    public void EmptyNameFallsBackToPlainFile()
        => Assert.Equal(FileTypes.File, FileTypes.Category("  ", false, false));
}