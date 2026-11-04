namespace ShellX.Core.Updates;

/// <summary>一次发布中的单个可下载文件。</summary>
public sealed record UpdateAsset(string Name, string DownloadUrl, long Size);

/// <summary>GitHub Release 的精简信息。</summary>
public sealed record UpdateRelease(string Tag, string Name, string Notes, string HtmlUrl, DateTimeOffset? PublishedAt, IReadOnlyList<UpdateAsset> Assets)
{
    public string Version => Tag.Trim().TrimStart('v', 'V');
    public UpdateAsset? Installer => UpdateAssetSelector.SelectInstaller(Assets);
}

public sealed record UpdateDownloadProgress(long BytesDownloaded, long TotalBytes)
{
    public double Percent => TotalBytes <= 0 ? 0 : Math.Clamp(BytesDownloaded * 100.0 / TotalBytes, 0, 100);
}

/// <summary>从发布资产中挑选安装包。</summary>
public static class UpdateAssetSelector
{
    public static UpdateAsset? SelectInstaller(IReadOnlyList<UpdateAsset> assets)
    {
        var executables = assets.Where(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (executables.Length == 0) return null;
        return executables.FirstOrDefault(a => a.Name.Contains("setup", StringComparison.OrdinalIgnoreCase))
            ?? executables.FirstOrDefault(a => a.Name.Contains("installer", StringComparison.OrdinalIgnoreCase))
            ?? executables[0];
    }
}

public interface IUpdateService
{
    Task<UpdateRelease?> GetLatestReleaseAsync(string owner, string repository, CancellationToken cancellationToken = default);
    Task<string> DownloadAsync(UpdateAsset asset, string destinationPath, IProgress<UpdateDownloadProgress>? progress = null, CancellationToken cancellationToken = default);
}