namespace ShellX.Core.Files;

public sealed record RemoteEntry(string Name, string FullPath, bool IsDirectory, bool IsSymbolicLink,
    long Size, DateTime ModifiedUtc, int Permissions, int UserId, int GroupId)
{
    public string Category => FileTypes.Category(Name, IsDirectory, IsSymbolicLink);
    public string Icon => Category;
    public string Kind => IsSymbolicLink ? "符号链接" : FileTypes.Label(Category);
    public string SizeText => IsDirectory ? "—" : Size < 1024 ? $"{Size} B" : Size < 1048576 ? $"{Size / 1024d:0.#} KB" : $"{Size / 1048576d:0.#} MB";
    public string ModifiedText => ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string PermissionText => Convert.ToString(Permissions & 0xfff, 8).PadLeft(4, '0');
    public string PermissionDisplay
    {
        get
        {
            var text = new[] { IsSymbolicLink ? 'l' : IsDirectory ? 'd' : '-',
                (Permissions & 256) != 0 ? 'r' : '-', (Permissions & 128) != 0 ? 'w' : '-', (Permissions & 64) != 0 ? 'x' : '-',
                (Permissions & 32) != 0 ? 'r' : '-', (Permissions & 16) != 0 ? 'w' : '-', (Permissions & 8) != 0 ? 'x' : '-',
                (Permissions & 4) != 0 ? 'r' : '-', (Permissions & 2) != 0 ? 'w' : '-', (Permissions & 1) != 0 ? 'x' : '-' };
            if ((Permissions & 2048) != 0) text[3] = text[3] == 'x' ? 's' : 'S';
            if ((Permissions & 1024) != 0) text[6] = text[6] == 'x' ? 's' : 'S';
            if ((Permissions & 512) != 0) text[9] = text[9] == 'x' ? 't' : 'T';
            return new string(text);
        }
    }
    public string OwnerText => $"{UserId}:{GroupId}";
}

public sealed record RemoteDirectory(string Path, IReadOnlyList<RemoteEntry> Entries);
public sealed record TextDocument(string Path, string Content, long Size, DateTime ModifiedUtc)
{
    public TextEncodingInfo Encoding { get; init; } = TextEncodingInfo.Utf8;
    public TextEncodingInfo? SaveEncoding { get; init; }
    public ReadOnlyMemory<byte> SourceBytes { get; init; }
    public string EncodingNote { get; init; } = "UTF-8";
}

public interface IRemoteFileSystem
{
    Task<RemoteDirectory> ListAsync(string path, CancellationToken cancellationToken = default);
    Task CreateAsync(string path, bool directory, CancellationToken cancellationToken = default);
    Task RenameAsync(string path, string newPath, CancellationToken cancellationToken = default);
    Task DeleteAsync(string path, CancellationToken cancellationToken = default);
    Task SetPermissionsAsync(string path, int permissions, CancellationToken cancellationToken = default);
    Task<TextDocument> ReadTextAsync(string path, CancellationToken cancellationToken = default);
    Task<TextDocument> SaveTextAsync(TextDocument original, string content, CancellationToken cancellationToken = default);
    Task UploadAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken cancellationToken = default);
    Task DownloadAsync(string remotePath, string localPath, IProgress<long> progress, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default);
}
