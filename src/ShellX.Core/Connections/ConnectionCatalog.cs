namespace ShellX.Core.Connections;

public sealed record ConnectionCatalog(IReadOnlyList<ConnectionProfile> Connections, IReadOnlyList<string> Folders);

public readonly record struct ConnectionEndpoint(string Host, int Port, string Username)
{
    public static ConnectionEndpoint From(ConnectionProfile profile)
        => new(profile.Host.Trim().TrimEnd('.').ToLowerInvariant(), profile.Port, profile.Username);
}

public static class ConnectionFolders
{
    public const string Default = "默认分组";
    public static string Normalize(string path)
    {
        var names = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var name in names) ValidateName(name);
        return names.Length == 0 ? Default : string.Join('/', names);
    }
    public static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.Contains('/') || name.Contains('\\') || name.Any(char.IsControl))
            throw new ArgumentException("文件夹名称不能为空，不能包含路径分隔符、控制字符或单独的点。");
    }
    public static IReadOnlyList<string> Expand(IEnumerable<string> paths)
    {
        var folders = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var parts = Normalize(path).Split('/');
            for (var i = 1; i <= parts.Length; i++) folders.Add(string.Join('/', parts.Take(i)));
        }
        return folders.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static bool Contains(string parent, string child) => child == parent || child.StartsWith(parent + "/", StringComparison.Ordinal);
}
