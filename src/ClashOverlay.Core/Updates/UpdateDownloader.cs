using System.Security.Cryptography;

namespace ClashOverlay.Core.Updates;

public sealed record UpdateDownload(string? Path, string? Error)
{
    public bool Ok => Path is not null;
}

/// <summary>
/// Downloads a release's installer into <c>%LOCALAPPDATA%\ClashOverlay\updates</c> and checks it before anything
/// runs it: the file's SHA-256 must equal GitHub's digest of the upload and the line for it in the release's
/// <c>SHA256SUMS.txt</c> (whichever the release has; both when it has both, and at least one). A file that does not
/// match is deleted. Older downloads are removed.
/// </summary>
public sealed class UpdateDownloader(HttpClient http, string folder)
{
    /// <summary>Far above the real installer (about 65 MB); anything bigger is not ours.</summary>
    public const long MaxSize = 400L * 1024 * 1024;

    private const int MaxChecksumFile = 64 * 1024;

    public static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(15);

    public string Folder => folder;

    public async Task<UpdateDownload> DownloadAsync(ReleaseInfo release, CancellationToken ct = default)
    {
        if (release.Setup is not { } setup)
        {
            return Fail($"Version {release.VersionText} has no installer to download.");
        }

        if (setup.Size > MaxSize)
        {
            return Fail("The installer is larger than expected, so it was not downloaded.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(DownloadTimeout);
        try
        {
            string? listed = null;
            if (release.Checksums is { } sums)
            {
                var text = await GetSmallTextAsync(sums.Url, timeout.Token).ConfigureAwait(false);
                if (text is null)
                {
                    return Fail("The checksum list could not be downloaded.");
                }

                if (!ParseChecksums(text).TryGetValue(setup.Name, out listed))
                {
                    return Fail("The checksum list does not include the installer.");
                }
            }

            if (setup.Sha256 is not null && listed is not null && setup.Sha256 != listed)
            {
                return Fail("The installer's checksums do not agree, so it was not installed.");
            }

            var expected = setup.Sha256 ?? listed;
            if (expected is null)
            {
                return Fail("The release has no checksum for the installer, so it was not installed.");
            }

            Directory.CreateDirectory(folder);
            var target = Path.Combine(folder, setup.Name);
            if (File.Exists(target) && await Sha256Async(target, timeout.Token).ConfigureAwait(false) == expected)
            {
                RemoveOthers(setup.Name);
                return new UpdateDownload(target, null);
            }

            var part = target + ".part";
            var actual = await DownloadToAsync(setup.Url, part, timeout.Token).ConfigureAwait(false);
            if (actual is null)
            {
                TryDelete(part);
                return Fail("The installer is larger than expected, so it was not downloaded.");
            }

            if (actual != expected)
            {
                TryDelete(part);
                return Fail("The downloaded installer did not match its checksum, so it was deleted.");
            }

            File.Move(part, target, overwrite: true);
            RemoveOthers(setup.Name);
            return new UpdateDownload(target, null);
        }
        catch (HttpRequestException)
        {
            return Fail("GitHub could not be reached to download the update.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Fail("Downloading the update took too long.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Fail($"The update could not be saved: {e.Message}");
        }
    }

    /// <summary><c>hash  name</c> lines (sha256sum format, also with <c>*name</c>) → name to lower-case hash.</summary>
    public static Dictionary<string, string> ParseChecksums(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var space = line.IndexOf(' ');
            if (space != 64)
            {
                continue;
            }

            var hash = line[..64];
            var name = line[64..].TrimStart(' ', '*').Trim();
            if (name.Length > 0 && ReleaseChecker.Sha256Pattern().IsMatch(hash))
            {
                map.TryAdd(name, hash.ToLowerInvariant());
            }
        }

        return map;
    }

    public static async Task<string> Sha256Async(string path, CancellationToken ct = default)
    {
        await using var file = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(file, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<string?> GetSmallTextAsync(string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxChecksumFile)
        {
            return null;
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        return bytes.Length > MaxChecksumFile ? null : System.Text.Encoding.UTF8.GetString(bytes);
    }

    /// <summary>Streams to <paramref name="path"/> while hashing. Null when the file is over <see cref="MaxSize"/>.</summary>
    private async Task<string?> DownloadToAsync(string url, string path, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxSize)
        {
            return null;
        }

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > MaxSize)
                {
                    return null;
                }

                sha.AppendData(buffer, 0, read);
                await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            }
        }

        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>Deletes earlier installers and partial downloads, keeping <paramref name="keep"/>.</summary>
    public void RemoveOthers(string? keep)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(folder))
        {
            if (!string.Equals(Path.GetFileName(file), keep, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(file);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static UpdateDownload Fail(string error) => new(null, error);
}
