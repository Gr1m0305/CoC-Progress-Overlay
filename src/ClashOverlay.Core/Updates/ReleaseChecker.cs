using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClashOverlay.Core.Updates;

/// <summary>A file attached to a release. <see cref="Sha256"/> is GitHub's digest of it, in lower-case hex, when given.</summary>
public sealed record ReleaseAsset(string Name, string Url, long Size, string? Sha256);

/// <summary>
/// A published release of Clash Tracker on GitHub, with its installer (<c>ClashOverlay-Setup-X.Y.Z.exe</c>) and
/// checksum list (<c>SHA256SUMS.txt</c>) when the release has them.
/// </summary>
public sealed record ReleaseInfo(Version Version, string Tag, string Url, DateTimeOffset? PublishedAt, ReleaseAsset? Setup = null, ReleaseAsset? Checksums = null)
{
    public string VersionText => Version.ToString(3);

    public static string SetupName(Version version) => $"ClashOverlay-Setup-{version.ToString(3)}.exe";

    public const string ChecksumsName = "SHA256SUMS.txt";
}

public enum ReleaseCheckOutcome
{
    UpToDate,
    UpdateAvailable,

    /// <summary>The repository has no published release yet.</summary>
    NoRelease,
    Failed,
}

public sealed record ReleaseCheckResult(ReleaseCheckOutcome Outcome, ReleaseInfo? Latest, string? Error = null);

/// <summary>
/// Asks GitHub for the newest published release (one unauthenticated call to <c>releases/latest</c>, which leaves out
/// drafts and pre-releases) and compares it with the running version. It only reads; <see cref="UpdateDownloader"/>
/// fetches the installer.
/// </summary>
public sealed partial class ReleaseChecker(HttpClient http, string repository)
{
    public string LatestUrl => $"https://api.github.com/repos/{repository}/releases/latest";

    public string ReleasesPage => $"https://github.com/{repository}/releases";

    /// <summary><c>owner/name</c> with GitHub's allowed characters only, so it is safe to put in a URL.</summary>
    public static bool IsValidRepository(string? repository) => repository is not null && RepositoryPattern().IsMatch(repository);

    public async Task<ReleaseCheckResult> CheckAsync(string currentVersion, CancellationToken ct = default)
    {
        var current = ParseVersion(currentVersion) ?? new Version(0, 0, 0);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new ReleaseCheckResult(ReleaseCheckOutcome.NoRelease, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                var why = response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
                    ? "GitHub asked to slow down. Clash Tracker tries again later."
                    : $"GitHub answered {(int)response.StatusCode}.";
                return new ReleaseCheckResult(ReleaseCheckOutcome.Failed, null, why);
            }

            var latest = ParseLatest(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false), ReleasesPage, repository);
            if (latest is null)
            {
                return new ReleaseCheckResult(ReleaseCheckOutcome.NoRelease, null);
            }

            return new ReleaseCheckResult(latest.Version > current ? ReleaseCheckOutcome.UpdateAvailable : ReleaseCheckOutcome.UpToDate, latest);
        }
        catch (HttpRequestException)
        {
            return new ReleaseCheckResult(ReleaseCheckOutcome.Failed, null, "GitHub could not be reached.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ReleaseCheckResult(ReleaseCheckOutcome.Failed, null, "GitHub did not answer in time.");
        }
        catch (JsonException)
        {
            return new ReleaseCheckResult(ReleaseCheckOutcome.Failed, null, "GitHub's answer could not be read.");
        }
    }

    /// <summary><c>v1.2.3</c>, <c>1.2.3</c> or <c>1.2.3-dev</c> → 1.2.3. Anything else → null.</summary>
    public static Version? ParseVersion(string? text)
    {
        var m = VersionPattern().Match(text?.Trim() ?? "");
        if (!m.Success)
        {
            return null;
        }

        static bool Part(Group g, out int value) => int.TryParse(g.Value, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        return Part(m.Groups[1], out var major) && Part(m.Groups[2], out var minor) && Part(m.Groups[3], out var patch)
            ? new Version(major, minor, patch)
            : null;
    }

    /// <summary>
    /// Reads GitHub's release JSON. Drafts, pre-releases and tags that are not a version give null. The page link is
    /// only used when it is on github.com; otherwise <paramref name="fallbackUrl"/>. With <paramref name="repository"/>
    /// the installer and checksum files are picked up too, but only from that repository's own release downloads.
    /// </summary>
    public static ReleaseInfo? ParseLatest(string json, string fallbackUrl, string? repository = null)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || Flag(root, "draft") || Flag(root, "prerelease"))
        {
            return null;
        }

        var tag = root.TryGetProperty("tag_name", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        if (ParseVersion(tag) is not { } version)
        {
            return null;
        }

        var url = root.TryGetProperty("html_url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
        if (url is null || !url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
        {
            url = fallbackUrl;
        }

        DateTimeOffset? published = root.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(p.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
                ? at
                : null;
        ReleaseAsset? setup = null, sums = null;
        if (IsValidRepository(repository) && root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            var prefix = $"https://github.com/{repository}/releases/download/{tag}/";
            foreach (var a in assets.EnumerateArray())
            {
                if (ReadAsset(a, prefix) is not { } asset)
                {
                    continue;
                }

                if (asset.Name == ReleaseInfo.SetupName(version))
                {
                    setup = asset;
                }
                else if (asset.Name == ReleaseInfo.ChecksumsName)
                {
                    sums = asset;
                }
            }
        }

        return new ReleaseInfo(version, tag!, url, published, setup, sums);
    }

    /// <summary>An uploaded asset whose download link is exactly <paramref name="prefix"/> + its name.</summary>
    private static ReleaseAsset? ReadAsset(JsonElement a, string prefix)
    {
        if (a.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? Text(string name) => a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var name = Text("name");
        var link = Text("browser_download_url");
        var state = Text("state");
        if (name is null || link is null || (state is not null && state != "uploaded")
            || !string.Equals(link, prefix + Uri.EscapeDataString(name), StringComparison.Ordinal))
        {
            return null;
        }

        var size = a.TryGetProperty("size", out var z) && z.ValueKind == JsonValueKind.Number && z.TryGetInt64(out var n) ? n : -1;
        var digest = Text("digest");
        var sha = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) && Sha256Pattern().IsMatch(digest[7..])
            ? digest[7..].ToLowerInvariant()
            : null;
        return new ReleaseAsset(name, link, size, sha);
    }

    private static bool Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    [GeneratedRegex(@"^[vV]?(\d{1,9})\.(\d{1,9})\.(\d{1,9})(?:[-+][0-9A-Za-z.-]*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"^[A-Za-z0-9-]{1,39}/[A-Za-z0-9._-]{1,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryPattern();

    [GeneratedRegex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    internal static partial Regex Sha256Pattern();
}
