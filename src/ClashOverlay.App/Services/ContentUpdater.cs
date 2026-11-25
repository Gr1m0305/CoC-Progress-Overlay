using System.Globalization;
using ClashOverlay.AssetFetch;
using ClashOverlay.Core.Assets;
using ClashOverlay.Core.Data;
using Microsoft.Extensions.Logging;

namespace ClashOverlay.App.Services;

internal sealed record ContentUpdateResult(bool Ok, string Title, string Detail);

/// <summary>
/// ClashKing game data and images, downloaded at first run and on "Check for data update". One run at a time; nothing
/// is bundled with the app.
/// </summary>
internal sealed class ContentUpdater(AppHost host)
{
    private readonly ILogger _log = host.Loggers.CreateLogger("ContentUpdate");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool IsRunning { get; private set; }

    /// <summary>Progress text while running ("Images: 400 of 1,239 checked").</summary>
    public string? Status { get; private set; }

    public event EventHandler? StatusChanged;

    /// <summary>New images are on disk; the overlay should load them again.</summary>
    public event EventHandler? ImagesChanged;

    /// <summary>True when the images were never downloaded (first run, or the folder was cleared).</summary>
    public bool ImagesMissing => LocalAssetManifest.Read(host.Paths.Assets).Files.Count == 0;

    public DateTimeOffset? ImagesUpdatedAt => LocalAssetManifest.Read(host.Paths.Assets).UpdatedAt;

    public async Task<ContentUpdateResult> RunAsync(bool force, CancellationToken ct = default)
    {
        if (host.IsDemo)
        {
            return new ContentUpdateResult(false, "Demo mode", "Data updates are off in the demo.");
        }

        if (!await _gate.WaitAsync(0, ct))
        {
            return new ContentUpdateResult(false, "Already updating", Status ?? "An update is running.");
        }

        IsRunning = true;
        try
        {
            Report("Checking game data…");
            var data = await host.Data.UpdateAsync(force, ct);
            _log.LogInformation("Game data: {Outcome} {Message}", data.Outcome, data.Message);
            if (host.Data.Data is not { } game)
            {
                return new ContentUpdateResult(false, "Game data could not be downloaded", data.Message);
            }

            var dataText = data.Outcome switch
            {
                DataUpdateOutcome.Updated => $"Game data updated{Commit(game)}.",
                DataUpdateOutcome.UpToDate => $"Game data is up to date{Commit(game)}.",
                _ => $"Game data could not be checked ({data.Message}); the saved copy is still used.",
            };

            Report("Checking images…");
            using var http = AssetFetcher.CreateHttpClient(host.Version);
            var fetcher = new AssetFetcher(http, host.Paths.Assets, host.Loggers.CreateLogger<AssetFetcher>());
            var progress = new Progress<AssetFetchProgress>(p =>
                Report($"Images: {p.Done.ToString("N0", CultureInfo.InvariantCulture)} of {p.Total.ToString("N0", CultureInfo.InvariantCulture)} checked, {p.Downloaded} downloaded"));
            var images = await fetcher.RunAsync(AssetPaths.Wanted(game), AssetFetcher.ExtraFolders, force, progress, ct);
            _log.LogInformation("Images: {Summary}", images.Summary);
            if (images.Downloaded > 0)
            {
                host.Images.Clear();
                ImagesChanged?.Invoke(this, EventArgs.Empty);
            }

            var imageText = images.Error is not null
                ? $"Images could not be checked ({images.Error})."
                : images.Downloaded > 0 ? $"{images.Downloaded} new images." : "Images are up to date.";
            var ok = data.Outcome != DataUpdateOutcome.Failed && images.Ok;
            return new ContentUpdateResult(ok, ok ? "Data and images checked" : "Update finished with problems", $"{dataText} {imageText}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            _log.LogWarning(e, "Data or image update failed");
            return new ContentUpdateResult(false, "Update failed", e.Message);
        }
        finally
        {
            IsRunning = false;
            Report(null);
            _gate.Release();
        }
    }

    private static string Commit(GameData game) =>
        game.Source.CommitDate is { } d ? $" (ClashKing, {d:yyyy-MM-dd})" : "";

    private void Report(string? status)
    {
        Status = status;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
