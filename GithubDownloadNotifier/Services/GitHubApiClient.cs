using System.Net.Http.Headers;
using System.Text.Json;
using GithubDownloadNotifier.Models;

namespace GithubDownloadNotifier.Services;

public sealed record GitHubRelease(
    string Tag,
    string Name,
    DateTimeOffset? PublishedAt,
    bool IsPrerelease,
    bool IsDraft,
    string Url);

public sealed class GitHubApiClient
{
    private readonly HttpClient _httpClient;

    public GitHubApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GithubDownloadNotifier", "1.0"));
        }

        if (!_httpClient.DefaultRequestHeaders.Accept.Any())
        {
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        }
    }

    public async Task<int> GetDownloadTotalAsync(
        string owner,
        string repository,
        DownloadScope scope,
        CancellationToken cancellationToken = default)
    {
        var releasePath = scope == DownloadScope.AllReleases ? "releases?per_page=100" : "releases/latest";
        using var document = await GetJsonAsync(owner, repository, releasePath, cancellationToken);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return SumReleaseAssetDownloads(document.RootElement);
        }

        var total = 0;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            total += SumReleaseAssetDownloads(release);
        }

        return total;
    }

    public async Task<GitHubRelease> GetLatestReleaseAsync(
        string owner,
        string repository,
        CancellationToken cancellationToken = default)
    {
        using var document = await GetJsonAsync(owner, repository, "releases/latest", cancellationToken);
        var root = document.RootElement;

        return new GitHubRelease(
            root.GetProperty("tag_name").GetString() ?? string.Empty,
            root.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("published_at", out var publishedAt) && publishedAt.TryGetDateTimeOffset(out var value) ? value : null,
            root.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean(),
            root.TryGetProperty("draft", out var draft) && draft.GetBoolean(),
            root.TryGetProperty("html_url", out var url) ? url.GetString() ?? string.Empty : string.Empty);
    }

    private async Task<JsonDocument> GetJsonAsync(
        string owner,
        string repository,
        string path,
        CancellationToken cancellationToken)
    {
        var requestUri = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}/{path}";
        using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static int SumReleaseAssetDownloads(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        var total = 0;
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.TryGetProperty("download_count", out var count) && count.TryGetInt32(out var value))
            {
                total += value;
            }
        }

        return total;
    }
}
