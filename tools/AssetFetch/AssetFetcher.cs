using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ClashOverlay.Core.Assets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClashOverlay.AssetFetch;

public sealed record AssetFetchProgress(int Done, int Total, int Downloaded);

public sealed record AssetFetchResult(
    int Wanted, int Downloaded, int UpToDate, int Missing, int Failed, bool UsedManifest, IReadOnlyList<string> MissingPaths, string? Error = null)
{
    public bool Ok => Error is null && Failed == 0;

    public string Summary => Error ?? $"{Downloaded} downloaded, {UpToDate} up to date, {Missing} not on the CDN, {Failed} failed";
}

/// <summary>
/// Downloads the images the overlay needs from ClashKing's CDN into the local assets folder, converting WebP to PNG.
/// With the CDN manifest it fetches only files whose SHA-256 changed (one request to learn that); without it, it
/// asks <c>HEAD</c> per file and compares <c>Last-Modified</c>. Every download is checked against the manifest's SHA.
/// </summary>
public sealed class AssetFetcher
{
    private readonly HttpClient _http;
    private readonly string _dir;
    private readonly ILogger _log;
    private readonly TimeProvider _time;

    public AssetFetcher(HttpClient http, string assetsDir, ILogger? logger = null, TimeProvider? time = null)
    {
        _http = http;
        _dir = assetsDir;
        _log = logger ?? NullLogger.Instance;
        _time = time ?? TimeProvider.System;
    }

    public int Parallelism { get; init; } = 8;

    public static HttpClient CreateHttpClient(string version) => new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        MaxConnectionsPerServer = 8,
    })
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders = { { "User-Agent", $"ClashOverlay/{version}" } },
    };

    public async Task<CdnManifest?> GetManifestAsync(CancellationToken ct)
    {
        try
        {
            var json = await _http.GetStringAsync(AssetPaths.ManifestUrl, ct).ConfigureAwait(false);
            return CdnManifest.Parse(json);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException || (e is OperationCanceledException && !ct.IsCancellationRequested))
        {
            _log.LogWarning("Asset manifest unavailable ({Error}); checking each file instead", e.GetType().Name);
            return null;
        }
    }

    /// <param name="extraFolders">Whole CDN folders to include as well (e.g. <c>player_labels</c>), when the manifest is available.</param>
    public async Task<AssetFetchResult> RunAsync(IReadOnlyCollection<string> wanted, IReadOnlyCollection<string>? extraFolders = null,
        bool force = false, IProgress<AssetFetchProgress>? progress = null, CancellationToken ct = default)
    {
        var local = LocalAssetManifest.Read(_dir);
        var manifest = await GetManifestAsync(ct).ConfigureAwait(false);

        var all = new HashSet<string>(wanted, StringComparer.Ordinal);
        if (manifest is not null && extraFolders is not null)
        {
            foreach (var folder in extraFolders)
            {
                all.UnionWith(manifest.InFolder(folder));
            }
        }

        var missing = new ConcurrentBag<string>();
        var failed = 0;
        var downloaded = 0;
        var upToDate = 0;
        var done = 0;
        var gate = new object();

        var list = all.OrderBy(p => p, StringComparer.Ordinal).ToList();
        try
        {
            await Parallel.ForEachAsync(list, new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = ct }, async (path, token) =>
            {
                var outcome = await FetchOneAsync(path, manifest, local, force, gate, token).ConfigureAwait(false);
                switch (outcome)
                {
                    case Outcome.Downloaded:
                        Interlocked.Increment(ref downloaded);
                        break;
                    case Outcome.UpToDate:
                        Interlocked.Increment(ref upToDate);
                        break;
                    case Outcome.Missing:
                        missing.Add(path);
                        break;
                    default:
                        Interlocked.Increment(ref failed);
                        break;
                }

                var n = Interlocked.Increment(ref done);
                progress?.Report(new AssetFetchProgress(n, list.Count, downloaded));
                if (n % 200 == 0)
                {
                    lock (gate)
                    {
                        local.Write(_dir); // keep what we have if the run is stopped
                    }
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            lock (gate)
            {
                local.UpdatedAt = _time.GetUtcNow();
                local.Write(_dir);
            }
        }

        var missingList = missing.OrderBy(p => p, StringComparer.Ordinal).ToList();
        _log.LogInformation("Assets: {Downloaded} downloaded, {UpToDate} up to date, {Missing} not on the CDN, {Failed} failed (manifest: {Manifest})",
            downloaded, upToDate, missingList.Count, failed, manifest is not null);
        return new AssetFetchResult(list.Count, downloaded, upToDate, missingList.Count, failed, manifest is not null, missingList);
    }

    private enum Outcome
    {
        Downloaded,
        UpToDate,
        Missing,
        Failed,
    }

    private async Task<Outcome> FetchOneAsync(string path, CdnManifest? manifest, LocalAssetManifest local, bool force, object gate, CancellationToken ct)
    {
        var relative = AssetPaths.LocalRelative(path);
        var full = Path.Combine(_dir, relative);
        LocalAsset? known;
        lock (gate)
        {
            local.Files.TryGetValue(path, out known);
        }

        var onDisk = File.Exists(full);
        string? sha = null;
        DateTimeOffset? lastModified = null;

        if (manifest is not null)
        {
            if (!manifest.ShaByPath.TryGetValue(path, out sha))
            {
                return Outcome.Missing;
            }

            if (!force && onDisk && known?.Sha is { Length: > 0 } knownSha && string.Equals(knownSha, sha, StringComparison.OrdinalIgnoreCase))
            {
                return Outcome.UpToDate;
            }
        }
        else
        {
            try
            {
                using var head = new HttpRequestMessage(HttpMethod.Head, AssetPaths.CdnBase + path);
                using var response = await _http.SendAsync(head, ct).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return Outcome.Missing;
                }

                if (!response.IsSuccessStatusCode)
                {
                    return Outcome.Failed;
                }

                lastModified = response.Content.Headers.LastModified;
                if (!force && onDisk && known?.LastModified is { } seen && lastModified is { } now && seen >= now)
                {
                    return Outcome.UpToDate;
                }
            }
            catch (Exception e) when (e is HttpRequestException || (e is OperationCanceledException && !ct.IsCancellationRequested))
            {
                return Outcome.Failed;
            }
        }

        byte[] bytes;
        try
        {
            using var response = await _http.GetAsync(AssetPaths.CdnBase + path, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return Outcome.Missing;
            }

            if (!response.IsSuccessStatusCode)
            {
                _log.LogDebug("Asset {Path}: HTTP {Status}", path, (int)response.StatusCode);
                return Outcome.Failed;
            }

            bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            lastModified ??= response.Content.Headers.LastModified;
        }
        catch (Exception e) when (e is HttpRequestException || (e is OperationCanceledException && !ct.IsCancellationRequested))
        {
            _log.LogDebug("Asset {Path}: {Error}", path, e.GetType().Name);
            return Outcome.Failed;
        }

        if (sha is { Length: > 0 } && !string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), sha, StringComparison.OrdinalIgnoreCase))
        {
            _log.LogWarning("Asset {Path}: download does not match the manifest's SHA-256; skipped", path);
            return Outcome.Failed;
        }

        byte[] png;
        try
        {
            png = ImageConverter.ToPng(bytes);
        }
        catch (InvalidDataException)
        {
            _log.LogWarning("Asset {Path}: not a readable image", path);
            return Outcome.Failed;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var tmp = full + ".tmp";
        await File.WriteAllBytesAsync(tmp, png, ct).ConfigureAwait(false);
        File.Move(tmp, full, overwrite: true);
        lock (gate)
        {
            local.Files[path] = new LocalAsset { Sha = sha, LastModified = lastModified, Local = relative.Replace('\\', '/') };
        }

        return Outcome.Downloaded;
    }
}
