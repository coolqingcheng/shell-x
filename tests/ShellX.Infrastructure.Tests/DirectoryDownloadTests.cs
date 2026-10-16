using ShellX.Core.Files;
using ShellX.Infrastructure.Files;

namespace ShellX.Infrastructure.Tests;

public sealed class DirectoryDownloadTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-dir-download-" + Guid.NewGuid().ToString("N"))).FullName;
    private static RemoteEntry Entry(string name, bool directory = false, bool link = false) => new(name, "/source/" + name, directory, link, 3, DateTime.UtcNow, 420, 0, 0);
    private static RemoteEntry Source => Entry("source", true) with { FullPath = "/source" };

    [Theory]
    [InlineData("../escape")]
    [InlineData("..\\escape")]
    [InlineData("trailing.")]
    [InlineData("CON.txt")]
    public async Task InvalidRemoteNamesFailBeforeWritingAnyFiles(string name)
    {
        if (name == "CON.txt" && !OperatingSystem.IsWindows()) return;
        var remote = new Files([Entry("valid.txt"), Entry(name)]);
        await Assert.ThrowsAsync<IOException>(() => new DirectoryDownloader(remote).DownloadAsync(Source, _root, _ => Task.FromResult(true), _ => { }, new Progress<long>()));
        Assert.Empty(Directory.GetFileSystemEntries(_root)); Assert.Equal(0, remote.Downloads);
    }

    [Fact]
    public async Task DirectoryDownloadSkipsLinksAndPreservesExistingFilesOnCancellation()
    {
        var remote = new Files([Entry("link", link: true), Entry("one"), Entry("two")]);
        using var cancel = new CancellationTokenSource();
        remote.AfterDownload = () => cancel.Cancel();
        var destination = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        await File.WriteAllTextAsync(Path.Combine(destination, "two"), "keep");
        var progress = new List<long>(); long total = -1;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DirectoryDownloader(remote).DownloadAsync(Source, _root,
            _ => Task.FromResult(true), value => total = value, new CallbackProgress(progress.Add), cancel.Token));
        Assert.Equal(6, total); Assert.Equal(1, remote.Downloads);
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(destination, "two")));
        Assert.False(File.Exists(Path.Combine(destination, "link")));
    }

    [Fact]
    public async Task ExistingLocalFileCannotBeUsedAsDirectory()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "source"), "keep");
        await Assert.ThrowsAsync<IOException>(() => new DirectoryDownloader(new Files([])).DownloadAsync(Source, _root,
            _ => Task.FromResult(true), _ => { }, new Progress<long>()));
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(_root, "source")));
    }
    public void Dispose() => Directory.Delete(_root, true);
    private sealed class CallbackProgress(Action<long> action) : IProgress<long>
    { public void Report(long value) => action(value); }
    private sealed class Files(RemoteEntry[] entries) : IRemoteFileSystem
    {
        public int Downloads { get; private set; }
        public Action? AfterDownload { get; set; }
        public Task<RemoteDirectory> ListAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(new RemoteDirectory(path, entries));
        public async Task DownloadAsync(string remotePath, string localPath, IProgress<long> progress, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); await File.WriteAllTextAsync(localPath, "new", cancellationToken); Downloads++; progress.Report(3); AfterDownload?.Invoke(); }
        public Task CreateAsync(string path, bool directory, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RenameAsync(string path, string newPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetPermissionsAsync(string path, int permissions, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TextDocument> ReadTextAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TextDocument> SaveTextAsync(TextDocument original, string content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UploadAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
