using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClashOverlay.Core.Data;

public enum DataUpdateOutcome
{
    UpToDate,
    Updated,
    Failed,
}

public sealed record DataUpdateResult(DataUpdateOutcome Outcome, string Message, StaticDataManifest? Manifest);

/// <summary>
/// Downloads ClashKing's static data into the app's data folder. Prefers the GitHub copy at the newest commit that
/// touched <c>assets/static_data</c> (so the manifest can name the commit); falls back to the CDN copy, checked against
/// the CDN's own SHA-256 list. Files are written to a staging folder, validated by loading them, then swapped in.
/// </summary>
public sealed class StaticDataUpdater
{
    public const string GitHubCommitsUrl =
        "https://api.github.com/repos/ClashKingInc/ClashKingAssets/commits?path=assets/static_data&per_page=1";

    public const string GitHubRawFormat =
        "https://raw.githubusercontent.com/ClashKingInc/ClashKingAssets/{0}/assets/static_data/{1}.json";

    public const string CdnFormat = "https://assets.clashk.ing/static_data/{0}.json";

    public const string CdnManifestUrl = "https://assets.clashk.ing/manifest.json";

    private readonly HttpClient _http;
    private readonly string _dataDir;
    private readonly ILogger _logger;

    public StaticDataUpdater(HttpClient http, string dataDir, ILogger? logger = null)
    {
        _http = http;
        _dataDir = dataDir;
        _logger = logger ?? NullLogger.Instance;
    }

    public static HttpClient CreateHttpClient(string userAgentVersion)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ClashOverlay", SafeVersion(userAgentVersion)));
        return http;
    }

    private static string SafeVersion(string v) => string.IsNullOrWhiteSpace(v) ? "0.0.0" : v.Replace(' ', '-');

    public async Task<DataUpdateResult> UpdateAsync(bool force = false, CancellationToken ct = default)
    {
        var current = StaticDataManifest.TryRead(_dataDir);
        var haveAll = new ClashKingDataSource(_dataDir).IsAvailable;

        (string Sha, DateTimeOffset? Date)? commit = null;
        try
        {
            commit = await LatestCommitAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning("GitHub commit lookup failed ({Error}); trying the CDN copy", ex.Message);
        }

        if (commit is { } c && !force && haveAll && current?.Commit == c.Sha)
        {
            return new DataUpdateResult(DataUpdateOutcome.UpToDate, $"Game data is up to date ({Short(c.Sha)}).", current);
        }

        var staging = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_dataDir))!, "data.staging");
        try
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }

            Directory.CreateDirectory(staging);
            StaticDataManifest manifest;
            if (commit is { } cc)
            {
                manifest = await DownloadAsync(staging, f => string.Format(GitHubRawFormat, cc.Sha, f), null, ct).ConfigureAwait(false);
                manifest.Origin = "github";
                manifest.Commit = cc.Sha;
                manifest.CommitDate = cc.Date;
            }
            else
            {
                var shas = await CdnShasAsync(ct).ConfigureAwait(false);
                if (!force && haveAll && current is not null && shas.Count > 0 && SameFiles(current, shas))
                {
                    return new DataUpdateResult(DataUpdateOutcome.UpToDate, "Game data is up to date.", current);
                }

                manifest = await DownloadAsync(staging, f => string.Format(CdnFormat, f), shas, ct).ConfigureAwait(false);
                manifest.Origin = "cdn";
            }

            manifest.DownloadedAt = DateTimeOffset.UtcNow;

            // Validate before swapping in: a broken download must not replace working data.
            var data = new ClashKingDataSource(staging, _logger).Load();
            manifest.Write(staging);
            Swap(staging);
            var when = manifest.CommitDate is { } d ? $" from {d:yyyy-MM-dd}" : string.Empty;
            var what = manifest.Commit is { } sha ? $" ({Short(sha)}{when})" : " (CDN copy)";
            _logger.LogInformation("Game data updated{What}: {Items} items, Town Hall {Th}", what, data.Items.Count, data.MaxTownHall);
            return new DataUpdateResult(DataUpdateOutcome.Updated, $"Game data updated{what}. Town Hall {data.MaxTownHall} is the highest.", manifest);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or GameDataException
                                       or JsonException or UnauthorizedAccessException or InvalidDataException)
        {
            _logger.LogWarning(ex, "Game data update failed");
            TryDelete(staging);
            var keep = haveAll ? " The data you already have is kept." : string.Empty;
            return new DataUpdateResult(DataUpdateOutcome.Failed, $"Could not update the game data: {ex.Message}.{keep}", current);
        }
    }

    private static string Short(string sha) => sha.Length > 7 ? sha[..7] : sha;

    private async Task<(string Sha, DateTimeOffset? Date)?> LatestCommitAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, GitHubCommitsUrl);
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (resp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            _logger.LogInformation("GitHub API rate limit reached; using the CDN copy");
            return null;
        }

        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var first = doc.RootElement.EnumerateArray().FirstOrDefault();
        if (first.ValueKind != JsonValueKind.Object || !first.TryGetProperty("sha", out var sha))
        {
            return null;
        }

        DateTimeOffset? date = null;
        if (first.TryGetProperty("commit", out var c) && c.TryGetProperty("committer", out var who)
            && who.TryGetProperty("date", out var d) && d.TryGetDateTimeOffset(out var parsed))
        {
            date = parsed;
        }

        return (sha.GetString()!, date);
    }

    /// <summary>CDN manifest's SHA-256 per static data file name (without extension).</summary>
    private async Task<Dictionary<string, string>> CdnShasAsync(CancellationToken ct)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var resp = await _http.GetAsync(CdnManifestUrl, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false));
            if (doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("stats", out var stats))
            {
                foreach (var s in stats.EnumerateArray())
                {
                    var path = ClashKingDataSource.Str(s, "path");
                    var sha = ClashKingDataSource.Str(s, "sha");
                    if (path is not null && sha is not null)
                    {
                        result[Path.GetFileNameWithoutExtension(path)] = sha;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            _logger.LogWarning("CDN manifest unavailable ({Error}); files will not be checked", ex.Message);
        }

        return result;
    }

    private static bool SameFiles(StaticDataManifest current, Dictionary<string, string> shas) =>
        ClashKingDataSource.RequiredFiles.All(f =>
            shas.TryGetValue(f, out var sha)
            && current.Files.Any(x => x.Name == f && string.Equals(x.Sha256, sha, StringComparison.OrdinalIgnoreCase)));

    private async Task<StaticDataManifest> DownloadAsync(string dir, Func<string, string> url, Dictionary<string, string>? expected, CancellationToken ct)
    {
        var manifest = new StaticDataManifest();
        foreach (var file in ClashKingDataSource.RequiredFiles)
        {
            var bytes = await _http.GetByteArrayAsync(url(file), ct).ConfigureAwait(false);
            var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (expected is not null && expected.TryGetValue(file, out var want) && !string.Equals(want, sha, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"{file}.json did not match the CDN's checksum");
            }

            using (JsonDocument.Parse(bytes))
            {
                // just checking it is JSON
            }

            await File.WriteAllBytesAsync(Path.Combine(dir, file + ".json"), bytes, ct).ConfigureAwait(false);
            manifest.Files.Add(new StaticDataFile { Name = file, Sha256 = sha, Size = bytes.Length });
        }

        return manifest;
    }

    private void Swap(string staging)
    {
        Directory.CreateDirectory(_dataDir);
        // The manifest goes last, so it never names data that is not fully in place.
        foreach (var file in Directory.GetFiles(staging).OrderBy(f => Path.GetFileName(f) == StaticDataManifest.FileName))
        {
            File.Copy(file, Path.Combine(_dataDir, Path.GetFileName(file)), overwrite: true);
        }

        TryDelete(staging);
    }

    private static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
