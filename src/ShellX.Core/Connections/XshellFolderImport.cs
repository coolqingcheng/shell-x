namespace ShellX.Core.Connections;

public sealed class XshellFolderImport
{
    public List<string> Folders { get; } = [];
    public List<ConnectionProfile> Profiles { get; } = [];
    public List<string> Notices { get; } = [];

    public static XshellFolderImport Read(string path, string destination)
    {
        var root = new DirectoryInfo(path);
        if (!root.Exists) throw new DirectoryNotFoundException("所选文件夹已不存在。");
        if (root.Parent == null) throw new ArgumentException("请选择会话文件夹。");
        var result = new XshellFolderImport();
        var pending = new Stack<(DirectoryInfo Directory, string Group)>();
        pending.Push((root, ConnectionFolders.Normalize($"{destination}/{root.Name}")));
        while (pending.TryPop(out var current))
        {
            try
            {
                var entries = current.Directory.GetFileSystemInfos();
                result.Folders.Add(current.Group);
                foreach (var entry in entries.OrderBy(e => e.Name, StringComparer.Ordinal))
                {
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    try
                    {
                        if (entry is DirectoryInfo child)
                        {
                            ConnectionFolders.ValidateName(child.Name);
                            pending.Push((child, ConnectionFolders.Normalize($"{current.Group}/{child.Name}")));
                        }
                        else if (entry.Extension.Equals(".xsh", StringComparison.OrdinalIgnoreCase))
                        {
                            var session = XshellSessionImporter.ParseFile(entry.FullName, includeCredentials: false);
                            result.Profiles.Add(session.Profile with { Group = current.Group });
                            if (session.Warning != null) result.Notices.Add($"{current.Group}/{entry.Name}：{session.Warning}");
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
                    {
                        result.Notices.Add($"{current.Group}/{entry.Name}：{ex.Message}");
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (current.Directory == root) throw;
                result.Notices.Add($"{current.Group}：{ex.Message}");
            }
        }
        return result;
    }
}
