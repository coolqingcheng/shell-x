namespace ShellX.Core.Files;

public static class RemotePath
{
    public static string Combine(string directory, string name)
    {
        ValidateName(name);
        return directory == "/" ? "/" + name : directory.TrimEnd('/') + "/" + name;
    }

    public static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.Contains('/') || name.Contains('\0'))
            throw new ArgumentException("名称不能为空，也不能包含 / 或空字符。");
    }

    public static string Parent(string path)
    {
        var trimmed = path.TrimEnd('/');
        var index = trimmed.LastIndexOf('/');
        return index <= 0 ? "/" : trimmed[..index];
    }
}
