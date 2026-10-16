using System.Text;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using ShellX.Core.Files;
using ShellX.Infrastructure.Files;

namespace ShellX.Infrastructure.Ssh;

internal sealed class SftpFileSystem(SftpClient client) : IRemoteFileSystem
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _home = client.WorkingDirectory;
    internal async Task<T> ExecuteAsync<T>(Func<T> action, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { return await Task.Run(() => { ct.ThrowIfCancellationRequested(); return action(); }, ct).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
    private Task ExecuteAsync(Action action, CancellationToken ct) => ExecuteAsync(() => { action(); return true; }, ct);

    private static RemoteEntry Entry(ISftpFile f) => new(f.Name, f.FullName, f.IsDirectory, f.IsSymbolicLink,
        f.Length, f.LastWriteTimeUtc, Permissions(f.Attributes), f.UserId, f.GroupId);

    private ISftpFile UnresolvedEntry(string path)
    {
        var name = path.TrimEnd('/').Split('/').Last();
        RemotePath.ValidateName(name);
        // 不对末级路径做 realpath；否则删除/重命名链接会误操作链接目标。
        return client.ListDirectory(RemotePath.Parent(path)).FirstOrDefault(f => f.Name == name)
            ?? throw new FileNotFoundException("远程项目不存在。", path);
    }
    private RemoteEntry DisplayEntry(ISftpFile file)
    {
        var entry = Entry(file);
        if (!file.IsSymbolicLink) return entry;
        try { return entry with { IsDirectory = client.GetAttributes(file.FullName).IsDirectory }; }
        catch (Renci.SshNet.Common.SshException) { return entry; }
    }

    private static int Permissions(SftpFileAttributes a) =>
        (a.IsUIDBitSet ? 2048 : 0) | (a.IsGroupIDBitSet ? 1024 : 0) | (a.IsStickyBitSet ? 512 : 0) |
        (a.OwnerCanRead ? 256 : 0) | (a.OwnerCanWrite ? 128 : 0) | (a.OwnerCanExecute ? 64 : 0) |
        (a.GroupCanRead ? 32 : 0) | (a.GroupCanWrite ? 16 : 0) | (a.GroupCanExecute ? 8 : 0) |
        (a.OthersCanRead ? 4 : 0) | (a.OthersCanWrite ? 2 : 0) | (a.OthersCanExecute ? 1 : 0);

    public Task<RemoteDirectory> ListAsync(string path, CancellationToken cancellationToken = default)
        => ExecuteAsync(() =>
        {
            // 仅解析路径，不修改客户端工作目录，目录树展开不会改变文件面板的相对路径基准。
            var directory = client.Get(string.IsNullOrWhiteSpace(path) || path == "~" ? _home : path);
            if (!directory.IsDirectory) throw new IOException("目标不是目录。");
            return new RemoteDirectory(directory.FullName,
                client.ListDirectory(directory.FullName).Where(x => x.Name is not "." and not "..").Select(DisplayEntry).ToArray());
        }, cancellationToken);

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
        => ExecuteAsync(() => client.Exists(path), cancellationToken);
    public Task CreateAsync(string path, bool directory, CancellationToken cancellationToken = default)
        => ExecuteAsync(() =>
        {
            if (client.Exists(path)) throw new IOException("同名文件或目录已存在。");
            if (directory) client.CreateDirectory(path);
            else { using var stream = client.Open(path, FileMode.CreateNew, FileAccess.Write); }
        }, cancellationToken);
    public Task RenameAsync(string path, string newPath, CancellationToken cancellationToken = default)
        => ExecuteAsync(() => { if (client.Exists(newPath)) throw new IOException("目标名称已存在。"); UnresolvedEntry(path).MoveTo(newPath); }, cancellationToken);
    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        => ExecuteAsync(() => DeleteCore(UnresolvedEntry(path), cancellationToken), cancellationToken);
    private void DeleteCore(ISftpFile entry, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (entry.FullName.TrimEnd('/') is "" or "." or "..") throw new IOException("不能删除根目录或相对目录标记。");
        if (entry.IsDirectory && !entry.IsSymbolicLink)
        {
            foreach (var child in client.ListDirectory(entry.FullName).Where(x => x.Name is not "." and not "..")) DeleteCore(child, ct);
            entry.Delete();
        }
        else entry.Delete();
    }

    public Task SetPermissionsAsync(string path, int permissions, CancellationToken cancellationToken = default)
        => ExecuteAsync(() =>
        {
            if (permissions is < 0 or > 4095) throw new ArgumentException("权限必须介于 0000–7777。");
            // SSH.NET 的 mode 参数使用十进制表示的八进制数字，例如 755，而非位掩码 493。
            var entry = UnresolvedEntry(path);
            if (entry.IsSymbolicLink) throw new IOException("请在目标实际目录中修改权限，不能通过符号链接修改。");
            entry.SetPermissions(short.Parse(Convert.ToString(permissions, 8), System.Globalization.CultureInfo.InvariantCulture));
        }, cancellationToken);

    public Task<TextDocument> ReadTextAsync(string path, CancellationToken cancellationToken = default)
        => ExecuteAsync(() => ReadCore(path, cancellationToken), cancellationToken);
    private TextDocument ReadCore(string path, CancellationToken token)
    {
        var info = client.GetAttributes(path);
        TextEditorLimits.ValidateSize(info.Size);
        using var input = client.OpenRead(path);
        return EditableText.Read(input, path, info.Size, info.LastWriteTimeUtc, token);
    }

    public Task<TextDocument> SaveTextAsync(TextDocument original, string content, CancellationToken cancellationToken = default)
        => ExecuteAsync(() =>
        {
            var bytes = EditableText.Encode(original, content);
            var current = client.GetAttributes(original.Path);
            if (current.Size != original.Size || current.LastWriteTimeUtc != original.ModifiedUtc)
                throw new IOException("远程文件已被其他程序修改，请重新打开文件后再编辑。");
            using (var input = client.OpenRead(original.Path))
                if (!EditableText.Matches(original, EditableText.ReadBytes(input, cancellationToken)))
                    throw new IOException("远程文件内容已变化，请重新打开文件后再编辑。");
            cancellationToken.ThrowIfCancellationRequested();
            // 打开已有文件后写入并截断，保留原有的权限、所有者和链接语义。
            using (var output = client.Open(original.Path, FileMode.Open, FileAccess.Write))
            { output.Write(bytes); output.SetLength(bytes.Length); output.Flush(); }
            var updated = client.GetAttributes(original.Path);
            return EditableText.Saved(original, content, bytes, updated.LastWriteTimeUtc);
        }, cancellationToken);

    public Task UploadAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken cancellationToken = default)
        => ExecuteAsync(() =>
        {
            using var input = File.OpenRead(localPath);
            using var output = client.Open(remotePath, FileMode.Create, FileAccess.Write);
            Copy(input, output, progress, cancellationToken);
        }, cancellationToken);
    public Task DownloadAsync(string remotePath, string localPath, IProgress<long> progress, CancellationToken cancellationToken = default)
        => ExecuteAsync(() =>
        {
            var temporary = localPath + ".shellx-" + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                using (var input = client.OpenRead(remotePath))
                using (var output = File.Create(temporary)) Copy(input, output, progress, cancellationToken);
                File.Move(temporary, localPath, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }, cancellationToken);
    private static void Copy(Stream input, Stream output, IProgress<long> progress, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        long total = 0;
        int read;
        while ((read = input.Read(buffer)) > 0)
        { ct.ThrowIfCancellationRequested(); output.Write(buffer, 0, read); total += read; progress.Report(total); }
        ct.ThrowIfCancellationRequested();
        output.Flush();
    }
}
