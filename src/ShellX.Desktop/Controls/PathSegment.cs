namespace ShellX.Desktop.Controls;

/// <summary>路径栏中的一段，显示名称与对应的完整路径。</summary>
public sealed class PathSegment
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public bool ShowSeparator { get; init; }
    public bool IsEllipsis { get; init; }
    public bool IsLast { get; init; }
}

/// <summary>把完整路径拆成可点击的路径段。</summary>
public static class PathSegmentBuilder
{
    public static IReadOnlyList<PathSegment> Split(string? path, bool windows)
        => windows ? SplitWindows(path) : SplitUnix(path);

    private static IReadOnlyList<PathSegment> SplitWindows(string? path)
    {
        var result = new List<PathSegment>();
        var text = (path ?? "").Trim();
        if (text.Length == 0) return result;

        if (text.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var parts = text[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) { result.Add(new PathSegment { Name = text, Path = text, IsLast = true }); return result; }
            var take = Math.Min(2, parts.Length);
            var root = @"\\" + string.Join("\\", parts.Take(take));
            result.Add(new PathSegment { Name = root, Path = root, IsLast = take == parts.Length });
            var unc = root;
            for (var i = take; i < parts.Length; i++)
            {
                unc += "\\" + parts[i];
                result.Add(new PathSegment { Name = parts[i], Path = unc, ShowSeparator = true, IsLast = i == parts.Length - 1 });
            }
            return result;
        }

        var segments = text.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) { result.Add(new PathSegment { Name = text, Path = text, IsLast = true }); return result; }
        var drive = segments[0] + "\\";
        result.Add(new PathSegment { Name = drive, Path = drive, IsLast = segments.Length == 1 });
        var current = drive;
        for (var i = 1; i < segments.Length; i++)
        {
            current += segments[i];
            result.Add(new PathSegment { Name = segments[i], Path = current, ShowSeparator = true, IsLast = i == segments.Length - 1 });
        }
        return result;
    }

    private static IReadOnlyList<PathSegment> SplitUnix(string? path)
    {
        var result = new List<PathSegment>();
        var text = (path ?? "").Trim();
        if (text.Length == 0) return result;
        if (!text.StartsWith('/')) { result.Add(new PathSegment { Name = text, Path = text, IsLast = true }); return result; }

        var segments = text.Split('/', StringSplitOptions.RemoveEmptyEntries);
        result.Add(new PathSegment { Name = "/", Path = "/", IsLast = segments.Length == 0 });
        var current = string.Empty;
        for (var i = 0; i < segments.Length; i++)
        {
            current += "/" + segments[i];
            result.Add(new PathSegment { Name = segments[i], Path = current, ShowSeparator = true, IsLast = i == segments.Length - 1 });
        }
        return result;
    }
}
