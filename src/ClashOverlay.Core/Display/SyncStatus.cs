namespace ClashOverlay.Core.Display;

/// <summary>The header line "API synced 3 min ago · Export 2 h ago" and whether the export needs refreshing.</summary>
public sealed record SyncStatus(string ApiText, string ExportText, bool ExportWarning, string? WarningReason, bool ApiWarning)
{
    public static readonly TimeSpan ExportMaxAge = TimeSpan.FromDays(3);

    public string Text => $"{ApiText} · {ExportText}";

    /// <param name="exportHall">Town Hall level in the export (0 when it has none).</param>
    /// <param name="apiHall">Town Hall level the API reports (0 when unknown).</param>
    /// <param name="hallChangedAt">When the API first showed the current Town Hall level.</param>
    public static SyncStatus Compute(DateTimeOffset now, DateTimeOffset? apiTime, bool apiStale, DateTimeOffset? exportTime,
        int exportHall, int apiHall, DateTimeOffset? hallChangedAt)
    {
        var api = apiTime is { } a ? $"API synced {Fmt.Ago(now - a)}" : "API not synced";
        if (exportTime is not { } e)
        {
            return new SyncStatus(api, "No export", true, "Import your village export to see buildings, traps and timers.", apiStale);
        }

        var export = $"Export {Fmt.Ago(now - e)}";
        string? reason = null;
        if (apiHall > 0 && exportHall > 0 && apiHall > exportHall)
        {
            reason = $"The API shows Town Hall {apiHall}, the export Town Hall {exportHall}. Export your village again.";
        }
        else if (hallChangedAt is { } changed && changed > e)
        {
            reason = "Your Town Hall changed after this export. Export your village again.";
        }
        else if (now - e > ExportMaxAge)
        {
            reason = "The export is more than 3 days old. Export your village again for current buildings and timers.";
        }

        return new SyncStatus(api, export, reason is not null, reason, apiStale);
    }
}
