using System.Diagnostics;
using ShellX.Core.Files;

namespace ShellX.Infrastructure.Files;

public sealed class LocalFileSystem(string? initialDirectory = null) : ILocalFileSystem
{
    public string HomeDirectory { get; } = initialDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public IReadOnlyList<string> Roots => Directory.GetLogicalDrives();

    private static FileInfo EditableFile(string path)
    {
        var info = new FileInfo(path);
        if ((info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new IOException("请选择实际文本文件，不能通过文件夹或链接编辑。");
        TextEditorLimits.ValidateSize(info.Length);
        return info;
    }
    public Task<TextDocument> ReadTextAsync(string path, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var info = EditableFile(path);
        using var input = File.OpenRead(path);
        return EditableText.Read(input, path, info.Length, info.LastWriteTimeUtc, cancellationToken);
    }, cancellationToken);

    public Task<TextDocument> SaveTextAsync(TextDocument original, string content, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var bytes = EditableText.Encode(original, content);
        var info = EditableFile(original.Path);
        if (info.Length != original.Size || info.LastWriteTimeUtc != original.ModifiedUtc)
            throw new IOException("本地文件已被其他程序修改，请重新打开后再编辑。");
        using (var output = new FileStream(original.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
        {
            if (!EditableText.Matches(original, EditableText.ReadBytes(output, cancellationToken)))
                throw new IOException("本地文件内容已变化，请重新打开后再编辑。");
            cancellationToken.ThrowIfCancellationRequested();
            output.Position = 0; output.Write(bytes); output.SetLength(bytes.Length); output.Flush(true);
        }
        return EditableText.Saved(original, content, bytes, File.GetLastWriteTimeUtc(original.Path));
    }, cancellationToken);

    public Task<DirectoryUploadPlan> PlanDirectoryUploadAsync(string path, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var root = new DirectoryInfo(Path.GetFullPath(path));
        if ((root.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("请选择实际目录，不能通过目录链接上传。");
        var directories = new List<string> { "" }; var files = new List<UploadFile>(); var skipped = 0;
        var pending = new Stack<(DirectoryInfo Directory, string Relative)>(); pending.Push((root, ""));
        while (pending.TryPop(out var current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var item in current.Directory.EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((item.Attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                var relative = current.Relative.Length == 0 ? item.Name : current.Relative + "/" + item.Name;
                if (item is DirectoryInfo directory) { directories.Add(relative); pending.Push((directory, relative)); }
                else if (item is FileInfo file) files.Add(new(file.FullName, relative, file.Length));
            }
        }
        return new DirectoryUploadPlan(root.Name, directories, files, skipped);
    }, cancellationToken);

    public Task<LocalDirectory> ListAsync(string path, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var directory = new DirectoryInfo(Path.GetFullPath(path));
        var entries = new List<LocalEntry>();
        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var attributes = entry.Attributes;
                var isDirectory = (attributes & FileAttributes.Directory) != 0;
                entries.Add(new(entry.Name, entry.FullName, isDirectory, (attributes & FileAttributes.ReparsePoint) != 0,
                    (attributes & FileAttributes.Hidden) != 0 || entry.Name.StartsWith('.'),
                    !isDirectory && entry is FileInfo file ? file.Length : 0, entry.LastWriteTimeUtc));
            }
            catch (FileNotFoundException) { } // 枚举期间被其他程序移除的项目不加入列表。
            catch (DirectoryNotFoundException) { }
        }
        return new LocalDirectory(directory.FullName, entries);
    }, cancellationToken);

    private static string ChildPath(string directory, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.Contains('/') || name.Contains('\\') || name.EndsWith('.') || name.EndsWith(' '))
            throw new ArgumentException("请输入有效的本地文件名称，不能包含路径分隔符或结尾空格/点。");
        return Path.Combine(directory, name);
    }

    public Task CreateAsync(string directory, string name, bool folder) => Task.Run(() =>
    {
        var target = ChildPath(directory, name);
        if (File.Exists(target) || Directory.Exists(target)) throw new IOException("本地同名项目已存在。");
        if (folder) Directory.CreateDirectory(target);
        else { using var stream = new FileStream(target, FileMode.CreateNew); }
    });

    public Task RenameAsync(string path, string name) => Task.Run(() =>
    {
        var target = ChildPath(Path.GetDirectoryName(path) ?? throw new IOException("不能重命名磁盘根目录。"), name);
        if (Directory.Exists(path)) Directory.Move(path, target);
        else File.Move(path, target);
    });

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        if (root != null && string.Equals(Path.TrimEndingDirectorySeparator(full), Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase))
            throw new IOException("不能删除磁盘根目录。");
        FileAttributes attributes;
        try { attributes = File.GetAttributes(full); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        { throw new IOException("本地项目不存在或已被其他程序删除。", ex); }
        FileSystemInfo entry = (attributes & FileAttributes.Directory) != 0 ? new DirectoryInfo(full) : new FileInfo(full);
        DeleteEntry(entry, cancellationToken);
    }, cancellationToken);

    private static void DeleteEntry(FileSystemInfo entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            if (entry is DirectoryInfo) Directory.Delete(entry.FullName, false);
            else File.Delete(entry.FullName);
            return;
        }
        if (entry is not DirectoryInfo directory)
        {
            File.Delete(entry.FullName);
            return;
        }
        foreach (var child in directory.EnumerateFileSystemInfos()) DeleteEntry(child, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.Delete(directory.FullName, false);
    }

    public Task OpenAsync(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return Task.CompletedTask;
    }
}
