using ClashOverlay.Core.Data;

namespace ClashOverlay.Core.State;

public enum LevelSource
{
    None,
    Export,
    Api,

    /// <summary>Set by hand in the overlay ("+1") since the last export or API sync.</summary>
    Manual,
}

/// <summary>One building, trap or wall the player has.</summary>
public sealed class StructureInstance
{
    public required ItemDef Item { get; init; }

    public required int Level { get; init; }

    /// <summary>Seconds left on a running upgrade to <c>Level + 1</c>, as of <see cref="VillageState.AsOf"/>.</summary>
    public long? TimerLeft { get; init; }

    public bool GearedUp { get; init; }

    /// <summary>Raised by a manual level edit.</summary>
    public bool Edited { get; init; }

    public bool IsUpgrading => TimerLeft is > 0;
}

/// <summary>A troop, spell, siege machine, hero, pet, equipment, guardian or helper.</summary>
public sealed class UnitState
{
    public required ItemDef Item { get; init; }

    public required int Level { get; init; }

    public long? TimerLeft { get; init; }

    public LevelSource Source { get; init; }

    /// <summary>The level the API reported, when it reports this unit at all.</summary>
    public int? ApiLevel { get; init; }

    public bool IsUpgrading => TimerLeft is > 0;
}

/// <summary>
/// What the player has in one village, after merging the export with the API and moving running upgrades forward to
/// <see cref="AsOf"/>.
/// </summary>
public sealed class VillageState
{
    public required Village Village { get; init; }

    public required int HallLevel { get; init; }

    public required IReadOnlyList<StructureInstance> Structures { get; init; }

    public required IReadOnlyDictionary<int, UnitState> Units { get; init; }

    public required DateTimeOffset AsOf { get; init; }

    public DateTimeOffset? ExportTime { get; init; }

    public DateTimeOffset? ApiTime { get; init; }

    /// <summary>True when building levels are known (an export was imported).</summary>
    public bool HasStructures { get; init; }

    /// <summary>Builders from the export (Builder's Huts + B.O.B; Master Builder + O.T.T.O).</summary>
    public int Builders { get; init; }

    public int LevelOf(int buildingId) => Structures.Where(s => s.Item.Id == buildingId).Select(s => s.Level).DefaultIfEmpty(0).Max();

    public int LabLevel => LevelOf(Village == Village.Home ? GameData.LaboratoryId : GameData.StarLaboratoryId);

    public int HeroHallLevel => LevelOf(GameData.HeroHallId);

    public int PetHouseLevel => LevelOf(GameData.PetHouseId);

    public int BlacksmithLevel => LevelOf(GameData.BlacksmithId);

    public int UnitLevel(int id) => Units.TryGetValue(id, out var u) ? u.Level : 0;

    public IEnumerable<StructureInstance> InstancesOf(int itemId) => Structures.Where(s => s.Item.Id == itemId);
}

/// <summary>The API's levels for one account, mapped away from the API's DTOs so Core does not depend on them.</summary>
public sealed class ApiPlayerSnapshot
{
    public required DateTimeOffset FetchedAt { get; init; }

    public int TownHallLevel { get; init; }

    public int BuilderHallLevel { get; init; }

    public IReadOnlyList<ApiUnitLevel> Units { get; init; } = [];
}

/// <param name="Name">The API's name (matched to the static data by name).</param>
/// <param name="Village">"home" or "builderBase".</param>
public sealed record ApiUnitLevel(string Name, int Level, int MaxLevel, string Village);
