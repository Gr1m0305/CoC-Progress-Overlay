namespace ClashOverlay.Core.Export;

/// <summary>The export's arrays the app reads. <c>*2</c> sections are the Builder Base.</summary>
public enum ExportSection
{
    Buildings,
    Traps,
    Units,
    SiegeMachines,
    Spells,
    Heroes,
    Pets,
    Equipment,
    Guardians,
    Helpers,
    Buildings2,
    Traps2,
    Units2,
    Heroes2,
}

public sealed record CraftedModule(int Id, int Level);

public sealed record CraftedType(int Id, IReadOnlyList<CraftedModule> Modules);

/// <summary>
/// One line of an export array: <c>{"data": id, "lvl": level, "cnt": count, "timer": seconds}</c>. <see cref="Count"/>
/// identical instances share it. <see cref="Timer"/> is the seconds left on a running upgrade at export time (the level
/// is still the old one).
/// </summary>
public sealed record ExportEntry(ExportSection Section, int Id, int Level, int Count = 1, long? Timer = null)
{
    public int GearUp { get; init; }

    public int Supercharge { get; init; }

    public long? HelperCooldown { get; init; }

    public bool HelperRecurrent { get; init; }

    /// <summary>Crafting Station only: the seasonal defenses and their modules.</summary>
    public IReadOnlyList<CraftedType> Types { get; init; } = [];

    public bool IsUpgrading => Timer is > 0;
}

public sealed record ExportWarning(ExportSection Section, int Id, string Message);

/// <summary>A parsed village export.</summary>
public sealed class VillageExport
{
    public required string Tag { get; init; }

    /// <summary>When the player pressed Copy (UTC). Null for exports without a timestamp.</summary>
    public DateTimeOffset? Timestamp { get; init; }

    public required IReadOnlyList<ExportEntry> Entries { get; init; }

    /// <summary>The original JSON, kept so the account can re-parse it after a data update.</summary>
    public required string RawJson { get; init; }

    public IEnumerable<ExportEntry> In(ExportSection section) => Entries.Where(e => e.Section == section);

    public IEnumerable<ExportEntry> In(params ExportSection[] sections) => Entries.Where(e => sections.Contains(e.Section));

    public bool HasBuilderBase => Entries.Any(e => e.Section is ExportSection.Buildings2 or ExportSection.Units2 or ExportSection.Heroes2);

    public TimeSpan? AgeAt(DateTimeOffset now) => Timestamp is { } t ? now - t : null;
}

/// <summary>The text is not a usable export. The message is written for the player.</summary>
public sealed class ExportFormatException(string message) : Exception(message);
