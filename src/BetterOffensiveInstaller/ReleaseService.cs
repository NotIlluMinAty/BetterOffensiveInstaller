using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using System.Collections.Generic;

namespace BetterOffensiveInstaller;

public sealed class ReleaseService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public ReleaseService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/IlluM1nAty/BetterOffensive/releases/latest");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubReleaseDto>(stream, SerializerOptions, cancellationToken)
            ?? throw new InstallerAppException(InstallerErrorKind.GitHubEmptyPayload);

        var fullAsset = GetWindowsAsset(release.Assets, "-windows");
        var patchAsset = GetWindowsAsset(release.Assets, "-windows");

        if (fullAsset is null || patchAsset is null)
        {
            throw new InstallerAppException(InstallerErrorKind.GitHubMissingAssets);
        }

        return new ReleaseInfo(
            release.TagName,
            string.IsNullOrWhiteSpace(release.Name) ? release.TagName : release.Name,
            release.PublishedAt,
            new ReleaseAsset(fullAsset.Name, fullAsset.BrowserDownloadUrl, fullAsset.Size),
            new ReleaseAsset(patchAsset.Name, patchAsset.BrowserDownloadUrl, patchAsset.Size));
    }

    private static GitHubAssetDto GetWindowsAsset(IEnumerable<GitHubAssetDto> assets, string releaseTypeToken)
    {
        var windowsAsset = assets.FirstOrDefault(asset =>
            asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
            asset.Name.Contains(releaseTypeToken, StringComparison.OrdinalIgnoreCase) &&
            asset.Name.Contains("windows", StringComparison.OrdinalIgnoreCase));

        return windowsAsset ?? throw new InstallerAppException(InstallerErrorKind.GitHubMissingAssets);
    }

    private sealed class GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; init; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("published_at")]
        public DateTimeOffset PublishedAt { get; init; }

        [JsonPropertyName("assets")]
        public List<GitHubAssetDto> Assets { get; init; } = new();
    }

    private sealed class GitHubAssetDto
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; init; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; init; }
    }
}

public sealed record ReleaseAsset(string Name, string DownloadUrl, long SizeBytes);

public sealed record ReleaseInfo(
    string TagName,
    string Title,
    DateTimeOffset PublishedAt,
    ReleaseAsset FullAsset,
    ReleaseAsset PatchAsset)
{
    public ReleaseAsset GetAsset(InstallMode mode) => mode == InstallMode.Full ? FullAsset : PatchAsset;
}
