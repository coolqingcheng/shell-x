namespace ShellX.Core.Updates;

/// <summary>比较发布标签与本地版本号，支持 v1.2.3、1.2.3、1.2.3-beta 等写法。</summary>
public static class UpdateVersion
{
    public static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.Trim().TrimStart('v', 'V');
        var cut = trimmed.IndexOfAny(['-', '+', ' ']);
        if (cut > 0) trimmed = trimmed[..cut];
        if (!Version.TryParse(trimmed, out var parsed)) return false;
        version = parsed;
        return true;
    }

    public static bool IsNewer(string? candidate, string? current)
    {
        if (!TryParse(candidate, out var next) || !TryParse(current, out var have)) return false;
        return next > have;
    }
}