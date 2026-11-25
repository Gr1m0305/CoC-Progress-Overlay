namespace ClashOverlay.Core.Data;

/// <summary>An amount of one resource.</summary>
public readonly record struct ResourceAmount(Resource Resource, long Amount);

/// <summary>One building another one is merged from: <c>Count</c> copies of item <c>SourceId</c> at <c>Level</c>.</summary>
public sealed record MergeSource(int SourceId, int Level, bool GearedUp, int Count);

/// <summary>A building or trap that a hall level adds.</summary>
public sealed record HallUnlock(int ItemId, int Quantity);

/// <summary>
/// One level of an item, normalised so that <see cref="Costs"/> and <see cref="TimeSeconds"/> are what it takes to
/// <b>reach</b> this level (for units the source data stores "from this level to the next"; the loader shifts it).
/// Requirements are what is needed to <b>have</b> this level.
/// </summary>
public sealed class LevelDef
{
    public required int Level { get; init; }

    /// <summary>Town Hall (or Builder Hall for Builder Base items) needed for this level.</summary>
    public required int RequiredHall { get; init; }

    public int RequiredLab { get; init; }

    public int RequiredHeroHall { get; init; }

    public int RequiredPetHouse { get; init; }

    public int RequiredBlacksmith { get; init; }

    public IReadOnlyList<ResourceAmount> Costs { get; init; } = [];

    /// <summary>Walls can be paid with gold or elixir from level 5: the alternative resource.</summary>
    public Resource AltResource { get; init; }

    public long TimeSeconds { get; init; }

    /// <summary>Buildings consumed to build this level (merged buildings, Town Hall 17's Eagle Artillery).</summary>
    public IReadOnlyList<MergeSource> MergeSources { get; init; } = [];

    /// <summary>Hall items only: what this hall level adds.</summary>
    public IReadOnlyList<HallUnlock> Unlocks { get; init; } = [];

    public long CostOf(Resource r)
    {
        long sum = 0;
        foreach (var c in Costs)
        {
            if (c.Resource == r)
            {
                sum += c.Amount;
            }
        }

        return sum;
    }
}

public sealed class ItemDef
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public required ItemKind Kind { get; init; }

    public required Village Village { get; init; }

    public required Category Category { get; init; }

    public required UpgradeTrack Track { get; init; }

    public Resource UpgradeResource { get; init; }

    /// <summary>Levels in order; <c>Levels[0]</c> is level 1.</summary>
    public required IReadOnlyList<LevelDef> Levels { get; init; }

    /// <summary>The source data's type string (Defense, Army, Resource, Worker...), for buildings.</summary>
    public string? SourceType { get; init; }

    public string? ProductionBuilding { get; init; }

    public int ProductionBuildingLevel { get; init; }

    public bool IsSeasonal { get; init; }

    public bool IsSuperTroop { get; init; }

    public bool IsSquad { get; init; }

    /// <summary>Equipment: Common or Epic.</summary>
    public string? Rarity { get; init; }

    /// <summary>Equipment: the hero it belongs to.</summary>
    public string? HeroName { get; init; }

    public bool CanGearUp { get; init; }

    /// <summary>Counted towards max and rushed totals.</summary>
    public bool CountsTowardsProgress { get; init; } = true;

    public int TopLevel => Levels.Count == 0 ? 0 : Levels[^1].Level;

    public bool IsUnit => Kind is ItemKind.Troop or ItemKind.Siege or ItemKind.Spell or ItemKind.Hero or ItemKind.Pet
        or ItemKind.Equipment or ItemKind.Guardian or ItemKind.Helper;

    public bool IsStructure => Kind is ItemKind.Building or ItemKind.Trap or ItemKind.Wall or ItemKind.TownHall or ItemKind.BuilderHall;

    public bool IsEpic => string.Equals(Rarity, "Epic", StringComparison.OrdinalIgnoreCase);

    /// <summary>Level <paramref name="level"/> (1-based), or null when out of range.</summary>
    public LevelDef? GetLevel(int level) => level >= 1 && level <= Levels.Count && Levels[level - 1].Level == level
        ? Levels[level - 1]
        : Levels.FirstOrDefault(l => l.Level == level);

    /// <summary>Highest level whose required hall is at most <paramref name="hallLevel"/> (0 = not available yet).</summary>
    public int MaxLevelFor(int hallLevel)
    {
        var max = 0;
        foreach (var l in Levels)
        {
            if (l.RequiredHall <= hallLevel && l.Level > max)
            {
                max = l.Level;
            }
        }

        return max;
    }

    /// <summary>Lowest hall level at which this item has at least one level (int.MaxValue if never).</summary>
    public int FirstHall => Levels.Count == 0 ? int.MaxValue : Levels.Min(l => l.RequiredHall);

    public long TimeToReach(int level) => GetLevel(level)?.TimeSeconds ?? 0;

    /// <summary>Total time to go from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public long TimeBetween(int from, int to)
    {
        long sum = 0;
        for (var l = from + 1; l <= to; l++)
        {
            sum += TimeToReach(l);
        }

        return sum;
    }

    public override string ToString() => $"{Name} ({Id})";
}
