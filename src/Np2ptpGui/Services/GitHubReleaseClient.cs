namespace Np2ptpGui.Services;

using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

public sealed record GitHubReleaseAsset(string Name, string BrowserDownloadUrl);
public sealed record GitHubRelease(string TagName, IReadOnlyList<GitHubReleaseAsset> Assets);

public sealed class GitHubReleaseClient
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/LuanBogoqb/np2ptp/releases/latest";
    private readonly HttpClient _http;

    public GitHubReleaseClient(HttpClient http)
    {
        _http = http;
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("np2ptp-gui");
        }
    }

    public async Task<GitHubRelease> GetLatestReleaseAsync(CancellationToken ct)
    {
        var dto = await _http.GetFromJsonAsync<ReleaseDto>(LatestReleaseUrl, ct)
            ?? throw new System.InvalidOperationException("GitHub API returned no release data");
        var assets = dto.Assets.Select(a => new GitHubReleaseAsset(a.Name, a.BrowserDownloadUrl)).ToList();
        return new GitHubRelease(dto.TagName, assets);
    }

    public Task<byte[]> DownloadAssetAsync(string url, CancellationToken ct) =>
        _http.GetByteArrayAsync(url, ct);

    private sealed record ReleaseDto(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("assets")] List<AssetDto> Assets);

    private sealed record AssetDto(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);
}
