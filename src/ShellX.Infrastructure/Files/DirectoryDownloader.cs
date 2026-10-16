using ShellX.Core.Files;

namespace ShellX.Infrastructure.Files;

// 先扫描并验证整个目录树，再落盘；不跟随远端符号链接和本地重解析点。
public sealed class DirectoryDownloader(IRemoteFileSystem remote)
{
    public async Task<bool> DownloadAsync(RemoteEntry source, string destination, Func<string, Task<bool>> confirmMerge,
        Action<long> setTotal, IProgress<long> progress, CancellationToken cancellationToken = default)
    {
        if (!source.IsDirectory || source.IsSymbolicLink) throw new IOException("请选择实际远程目录。");
        var root = ChildPath(destination, source.Name);
        EnsureDirectory(root);
        if (Directory.Exists(root) && !await confirmMerge(root)) return false;
        var directories = new List<string> { root };
        var files = new List<(string Remote, string Local, long Size)>();
        var pending = new Stack<(string Remote, string Local, int Depth)>();
        pending.Push((source.FullPath, root, 0));
        long total = 0;
        while (pending.TryPop(out var current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current.Depth > 128) throw new IOException("目录层级超过 128 层，已停止下载。");
            var names = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var entry in (await remote.ListAsync(current.Remote, cancellationToken)).Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.IsSymbolicLink) continue;
                var local = ChildPath(current.Local, entry.Name);
                if (!names.Add(entry.Name)) throw new IOException("远端存在本地无法区分的同名项目：" + entry.Name);
                var remotePath = RemotePath.Combine(current.Remote, entry.Name);
                if (entry.IsDirectory)
                {
                    EnsureDirectory(local); directories.Add(local);
                    pending.Push((remotePath, local, current.Depth + 1));
                }
                else
                {
                    EnsureFile(local); files.Add((remotePath, local, entry.Size)); total = checked(total + entry.Size);
                }
                if (directories.Count + files.Count > 100_000) throw new IOException("单次目录下载超过 100000 个项目，请分批下载。");
            }
        }
        setTotal(total);
        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested(); EnsureDirectory(directory); Directory.CreateDirectory(directory);
        }
        long completed = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested(); EnsureFile(file.Local);
            await remote.DownloadAsync(file.Remote, file.Local, new OffsetProgress(progress, completed), cancellationToken);
            completed += file.Size; progress.Report(completed);
        }
        return true;
    }

    public static string ChildPath(string directory, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.Contains('/') || name.Contains('\\') ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') || name.EndsWith(' '))
            throw new IOException("远程名称无法保存到本地：" + name);
        if (OperatingSystem.IsWindows())
        {
            var stem = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
                (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && "123456789¹²³".Contains(stem[3])))
                throw new IOException("远程名称是 Windows 保留名称：" + name);
        }
        return Path.Combine(Path.GetFullPath(directory), name);
    }

    public static void EnsureFile(string path)
    {
        RejectLinks(path);
        if (Directory.Exists(path)) throw new IOException("目标位置存在同名文件夹：" + path);
    }
    private static void EnsureDirectory(string path)
    {
        RejectLinks(path);
        if (File.Exists(path)) throw new IOException("目标位置存在同名文件：" + path);
    }
    private static void RejectLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("下载目标不能包含符号链接或目录联接：" + current);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
    private sealed class OffsetProgress(IProgress<long> progress, long offset) : IProgress<long>
    { public void Report(long value) => progress.Report(offset + value); }
}
