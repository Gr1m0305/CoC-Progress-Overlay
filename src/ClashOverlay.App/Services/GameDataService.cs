using ClashOverlay.Core.Data;
using Microsoft.Extensions.Logging;

namespace ClashOverlay.App.Services;

/// <summary>The ClashKing static data on disk, loaded once and reloaded after an update.</summary>
internal sealed class GameDataService(string dataDir, HttpClient http, ILoggerFactory loggers)
{
    private readonly ILogger _log = loggers.CreateLogger("GameData");
    private readonly SemaphoreSlim _updating = new(1, 1);

    public GameData? Data { get; private set; }

    public string? Problem { get; private set; }

    public string DataDir => dataDir;

    public event EventHandler? Changed;

    public bool TryLoad()
    {
        var source = new ClashKingDataSource(dataDir, loggers.CreateLogger<ClashKingDataSource>());
        if (!source.IsAvailable)
        {
            Problem = "Game data has not been downloaded yet.";
            return false;
        }

        try
        {
            Data = source.Load();
            Problem = null;
            _log.LogInformation("Game data loaded: {Items} items, TH{Th}, BH{Bh}, commit {Commit}",
                Data.Items.Count, Data.MaxTownHall, Data.MaxBuilderHall, Data.Source.Commit);
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (GameDataException e)
        {
            Problem = e.Message;
            _log.LogError(e, "Game data could not be loaded");
            return false;
        }
    }

    /// <summary>Downloads newer data if there is any and reloads it.</summary>
    public async Task<DataUpdateResult> UpdateAsync(bool force, CancellationToken ct = default)
    {
        await _updating.WaitAsync(ct);
        try
        {
            var result = await new StaticDataUpdater(http, dataDir, loggers.CreateLogger<StaticDataUpdater>()).UpdateAsync(force, ct);
            if (result.Outcome == DataUpdateOutcome.Updated || Data is null)
            {
                TryLoad();
            }

            return result;
        }
        finally
        {
            _updating.Release();
        }
    }
}
