namespace ShellX.Core.Files;

public sealed record LocalEntry(string Name, string FullPath, bool IsDirectory, bool IsSymbolicLink,
    bool IsHidden, long Size, DateTime ModifiedUtc)
{
    public string Icon => IsSymbolicLink ? "↗" : IsDirectory ? "▸" : "≡";
    public string Kind => IsSymbolicLink ? "链接" : IsDirectory ? "文件夹" : "文件";
    public string SizeText => IsDirectory ? "—" : Size < 1024 ? $"{Size} B" : Size < 1048576 ? $"{Size / 1024d:0.#} KB" : $"{Size / 1048576d:0.#} MB";
    public string ModifiedText => ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}

public sealed record LocalDirectory(string Path, IReadOnlyList<LocalEntry> Entries);

public interface ILocalFileSystem
{
    string HomeDirectory { get; }
    IReadOnlyList<string> Roots { get; }
    Task<LocalDirectory> ListAsync(string path, CancellationToken cancellationToken = default);
    Task<DirectoryUploadPlan> PlanDirectoryUploadAsync(string path, CancellationToken cancellationToken = default);
    Task<TextDocument> ReadTextAsync(string path, CancellationToken cancellationToken = default);
    Task<TextDocument> SaveTextAsync(TextDocument original, string content, CancellationToken cancellationToken = default);
    Task CreateAsync(string directory, string name, bool folder);
    Task RenameAsync(string path, string name);
    Task DeleteAsync(string path, CancellationToken cancellationToken = default);
    Task OpenAsync(string path);
}
