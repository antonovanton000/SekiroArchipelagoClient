using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

public sealed class GitHubReleaseProvider
{
    private readonly HttpClient _http;
    private readonly string _owner;
    private readonly string _repo;

    public GitHubReleaseProvider(HttpClient http, string owner, string repo)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
    }

    public async Task<LatestReleaseInfo?> GetLatestAsync(
        bool includePrerelease,
        string assetName = "randomizerAP.zip",
        CancellationToken ct = default)
    {
        EnsureGitHubHeaders();

        var url = $"https://api.github.com/repos/{_owner}/{_repo}/releases?per_page=30&page=1";

        using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"GitHub API error {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {body}");

        var releases = JsonSerializer.Deserialize<GitHubRelease[]>(body, JsonOptions)
                       ?? Array.Empty<GitHubRelease>();

        var filtered = releases.Where(r => r.Draft == false);

        if (!includePrerelease)
            filtered = filtered.Where(r => r.Prerelease == false);

        var release = filtered.FirstOrDefault();
        if (release == null)
            return null;

        var version = ExtractVersion(release);
        if (version == null)
            return null;

        var assets = release.Assets ?? Array.Empty<GitHubAsset>();
        if (assets.Length == 0)
            throw new InvalidOperationException("Release has no assets attached.");

        var asset = assets.FirstOrDefault(a => a.Name.Equals(assetName, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"Asset '{assetName}' not found in release assets.");

        if (string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
            throw new InvalidOperationException("Asset has empty browser_download_url.");

        return new LatestReleaseInfo(
            Version: version,
            DownloadUrl: asset.BrowserDownloadUrl,
            AssetName: asset.Name,
            ReleaseTitle: release.Name ?? release.TagName ?? "(untitled)",
            IsPrerelease: release.Prerelease
        );
    }

    private void EnsureGitHubHeaders()
    {
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SekiroArchipelagoUpdater", "1.0"));

        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _http.DefaultRequestHeaders.Remove("X-GitHub-Api-Version");
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    private static Version? ExtractVersion(GitHubRelease r)
    {

        if (!string.IsNullOrWhiteSpace(r.TagName))
        {
            var tag = r.TagName.Trim();
            if (tag.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                tag = tag[1..];

            if (Version.TryParse(tag, out var vFromTag))
                return vFromTag;
        }

        if (!string.IsNullOrWhiteSpace(r.Name))
        {
            var m = VersionRegex.Match(r.Name);
            if (m.Success && Version.TryParse(m.Groups["ver"].Value, out var vFromName))
                return vFromName;
        }

        return null;
    }

    private static readonly Regex VersionRegex =
        new(@"v(?<ver>\d+\.\d+\.\d+(?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("assets")] public GitHubAsset[]? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = "";
    }
}

public sealed record LatestReleaseInfo(
    Version Version,
    string DownloadUrl,
    string AssetName,
    string ReleaseTitle,
    bool IsPrerelease);
