using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShellX.Core.Updates;

namespace ShellX.Infrastructure.Updates;

internal sealed record GitHubReleaseDto(
    [property: JsonPropertyName("tag_name")] string? TagName,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("html_url")] string? HtmlUrl,
    [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
    [property: JsonPropertyName("assets")] List<GitHubAssetDto>? Assets);

internal sealed record GitHubAssetDto(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("browser_download_url")] string? DownloadUrl,
    [property: JsonPropertyName("size")] long Size);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(GitHubReleaseDto))]
internal partial class GitHubJsonContext : JsonSerializerContext;

/// <summary>通过 GitHub Releases 接口获取最新版本并下载安装包。</summary>
public sealed class GitHubUpdateService : IUpdateService
{
    private readonly HttpClient _http;

    public GitHubUpdateService() : this(new HttpClient { Timeout = TimeSpan.FromSeconds(120) }) { }

    public GitHubUpdateService(HttpClient http)
    {
        _http = http;
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("ShellX-Update");
        if (!_http.DefaultRequestHeaders.Accept.Any())
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<UpdateRelease?> GetLatestReleaseAsync(string owner, string repository, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repository)) return null;
        var url = $"https://api.github.com/repos/{Uri.EscapeDataString(owner.Trim())}/{Uri.EscapeDataString(repository.Trim())}/releases/latest";
        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GitHub 更新接口返回 {(int)response.StatusCode} {response.ReasonPhrase}。");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var dto = await JsonSerializer.DeserializeAsync(stream, GitHubJsonContext.Default.GitHubReleaseDto, cancellationToken).ConfigureAwait(false);
        if (dto is null || string.IsNullOrWhiteSpace(dto.TagName)) return null;
        var assets = (dto.Assets ?? [])
            .Where(a => !string.IsNullOrWhiteSpace(a.Name) && !string.IsNullOrWhiteSpace(a.DownloadUrl))
            .Select(a => new UpdateAsset(a.Name!, a.DownloadUrl!, a.Size))
            .ToArray();
        return new UpdateRelease(dto.TagName!, string.IsNullOrWhiteSpace(dto.Name) ? dto.TagName! : dto.Name!, dto.Body ?? "",
            dto.HtmlUrl ?? "", dto.PublishedAt, assets);
    }

    public async Task<string> DownloadAsync(UpdateAsset asset, string destinationPath,
        IProgress<UpdateDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var folder = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        var temporary = destinationPath + ".part";
        using var response = await _http.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? asset.Size;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (var output = File.Create(temporary))
        {
            var buffer = new byte[81920];
            long downloaded = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                downloaded += read;
                progress?.Report(new UpdateDownloadProgress(downloaded, total));
            }
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        File.Move(temporary, destinationPath, true);
        return destinationPath;
    }
}