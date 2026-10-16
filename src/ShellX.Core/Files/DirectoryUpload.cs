namespace ShellX.Core.Files;

// 相对路径统一使用 /；Directories 包含空字符串表示所选目录本身，父目录排在子目录前。
public sealed record UploadFile(string LocalPath, string RelativePath, long Size);
public sealed record DirectoryUploadPlan(string Name, IReadOnlyList<string> Directories, IReadOnlyList<UploadFile> Files, int SkippedLinks)
{
    public long TotalBytes => Files.Sum(file => file.Size);
}

public sealed class DirectoryUploader(IRemoteFileSystem remote)
{
    public async Task<bool> UploadAsync(DirectoryUploadPlan plan, string destination, Func<string, Task<bool>> confirmMerge,
        IProgress<long> progress, CancellationToken cancellationToken = default)
    {
        var root = RemotePath.Combine(destination, plan.Name);
        var existing = (await remote.ListAsync(destination, cancellationToken)).Entries.FirstOrDefault(e => e.Name == plan.Name);
        if (existing != null)
        {
            RequireDirectory(existing);
            var merge = await confirmMerge(root);
            cancellationToken.ThrowIfCancellationRequested();
            if (!merge) return false;
        }
        cancellationToken.ThrowIfCancellationRequested();
        var listings = new Dictionary<string, Dictionary<string, RemoteEntry>>(StringComparer.Ordinal);
        foreach (var relative in plan.Directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Resolve(root, relative);
            var entry = relative.Length == 0 ? existing : listings[RemotePath.Parent(path)].GetValueOrDefault(relative.Split('/').Last());
            if (entry == null) await remote.CreateAsync(path, true, cancellationToken);
            else RequireDirectory(entry);
            listings[path] = entry == null ? new(StringComparer.Ordinal)
                : (await remote.ListAsync(path, cancellationToken)).Entries.ToDictionary(e => e.Name, StringComparer.Ordinal);
        }
        long completed = 0;
        foreach (var file in plan.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Resolve(root, file.RelativePath);
            var entry = listings[RemotePath.Parent(target)].GetValueOrDefault(file.RelativePath.Split('/').Last());
            if (entry is { IsDirectory: true } or { IsSymbolicLink: true })
                throw new IOException("远端同名项目不是普通文件，无法覆盖：" + target);
            await remote.UploadAsync(file.LocalPath, target, new OffsetProgress(progress, completed), cancellationToken);
            completed += file.Size; progress.Report(completed);
        }
        return true;
    }
    private static string Resolve(string root, string relative) => relative.Length == 0 ? root : relative.Split('/').Aggregate(root, RemotePath.Combine);
    private static void RequireDirectory(RemoteEntry entry)
    {
        if (!entry.IsDirectory || entry.IsSymbolicLink) throw new IOException("远端同名项目不是普通目录，无法合并：" + entry.FullPath);
    }
    private sealed class OffsetProgress(IProgress<long> progress, long offset) : IProgress<long>
    { public void Report(long value) => progress.Report(offset + value); }
}
