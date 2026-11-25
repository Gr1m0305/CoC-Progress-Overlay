using ClashOverlay.Core;
using ClashOverlay.Core.Export;
using ClashOverlay.Core.State;
using Microsoft.Extensions.Logging;

namespace ClashOverlay.App.Services;

/// <summary>
/// Per-account files: the latest village export and the levels raised by hand since. In demo mode everything stays in
/// memory, seeded from the bundled fixtures, and nothing is written.
/// </summary>
internal sealed class AccountStore(AppPaths paths, ILogger logger, IReadOnlyDictionary<string, string>? demoExports = null)
{
    private readonly Dictionary<string, IReadOnlyList<LevelEdit>> _memoryEdits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _memoryExports = demoExports is null ? new() : new(demoExports);

    public bool InMemory => demoExports is not null;

    /// <summary>An account's export or hand edits changed (the tag), or the account was deleted.</summary>
    public event EventHandler<string>? Changed;

    public VillageExport? LoadExport(string tag, out string? problem)
    {
        problem = null;
        string? json;
        if (InMemory)
        {
            json = _memoryExports.GetValueOrDefault(PlayerTag.Normalize(tag));
        }
        else
        {
            var path = paths.ExportFile(tag);
            json = File.Exists(path) ? File.ReadAllText(path) : null;
        }

        if (json is null)
        {
            return null;
        }

        try
        {
            return ExportParser.Parse(json);
        }
        catch (ExportFormatException e)
        {
            problem = e.Message;
            logger.LogWarning("Saved export for {Tag} could not be read: {Message}", tag, e.Message);
            return null;
        }
    }

    /// <summary>Checks and stores an export. Building edits older than it are dropped.</summary>
    public VillageExport Import(string json, string expectedTag)
    {
        var export = ExportParser.Parse(json);
        if (!string.Equals(PlayerTag.Normalize(export.Tag), PlayerTag.Normalize(expectedTag), StringComparison.Ordinal))
        {
            throw new ExportFormatException($"This export is for {PlayerTag.Normalize(export.Tag)}, not {PlayerTag.Normalize(expectedTag)}.");
        }

        if (InMemory)
        {
            _memoryExports[PlayerTag.Normalize(expectedTag)] = json;
        }
        else
        {
            var path = paths.ExportFile(expectedTag);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path + ".tmp", json);
            File.Move(path + ".tmp", path, overwrite: true);
        }

        logger.LogInformation("Imported export for {Tag} taken {Timestamp}", export.Tag, export.Timestamp);
        Changed?.Invoke(this, PlayerTag.Normalize(expectedTag));
        return export;
    }

    public IReadOnlyList<LevelEdit> LoadEdits(string tag) =>
        InMemory ? _memoryEdits.GetValueOrDefault(PlayerTag.Normalize(tag)) ?? [] : LevelEdits.Load(paths.EditsFile(tag));

    public void SaveEdits(string tag, IReadOnlyList<LevelEdit> edits)
    {
        if (InMemory)
        {
            _memoryEdits[PlayerTag.Normalize(tag)] = edits;
            return;
        }

        LevelEdits.Save(paths.EditsFile(tag), edits);
    }

    /// <summary>"Clear manual changes": drops every hand edit of the account.</summary>
    public void ClearEdits(string tag)
    {
        SaveEdits(tag, []);
        logger.LogInformation("Cleared the hand edits of {Tag}", tag);
        Changed?.Invoke(this, PlayerTag.Normalize(tag));
    }

    /// <summary>Removes the account's export, edits and cached API answers.</summary>
    public void Delete(string tag)
    {
        if (InMemory)
        {
            _memoryExports.Remove(PlayerTag.Normalize(tag));
            _memoryEdits.Remove(PlayerTag.Normalize(tag));
            Changed?.Invoke(this, PlayerTag.Normalize(tag));
            return;
        }

        foreach (var dir in new[] { paths.AccountDir(tag), paths.AccountCacheDir(tag) })
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch (IOException e)
            {
                logger.LogWarning(e, "Could not delete {Dir}", dir);
            }
        }

        logger.LogInformation("Deleted the data of account {Tag}", tag);
        Changed?.Invoke(this, PlayerTag.Normalize(tag));
    }
}
