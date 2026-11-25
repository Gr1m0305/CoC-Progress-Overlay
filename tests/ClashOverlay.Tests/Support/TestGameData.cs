using ClashOverlay.Core.Data;
using ClashOverlay.Core.State;

namespace ClashOverlay.Tests.Support;

/// <summary>
/// A tiny hand-made data set (three Town Hall levels) so the calculations can be checked with exact numbers that do not
/// change when the real data is updated.
/// </summary>
public static class TestGameData
{
    public const int Cannon = 100;
    public const int ArcherTower = 101;
    public const int DoubleCannon = 102;
    public const int AirBomb = 103;
    public const int Barbarian = 200;
    public const int Archer = 201;
    public const int EventTroop = 202;
    public const int King = 300;
    public const int Puppet = 400;
    public const int EpicGear = 401;
    public const int BbCannon = 500;
    public const int BbTroop = 501;

    public static GameData Create()
    {
        var items = new List<ItemDef>
        {
            Hall(GameData.TownHallId, Village.Home, ItemKind.TownHall,
                [
                    [(Cannon, 1), (ArcherTower, 1), (GameData.WallId, 10), (GameData.BuildersHutId, 5), (GameData.LaboratoryId, 1)],
                    [(Cannon, 1), (AirBomb, 2), (GameData.WallId, 5), (GameData.HeroHallId, 1)],
                    [(Cannon, 2), (DoubleCannon, 1), (GameData.WallId, 5)],
                ]),
            Building(Cannon, "Cannon", Category.Defenses, Resource.Gold, [(1, 100, 60), (1, 200, 120), (2, 300, 180), (3, 400, 240)]),
            Building(ArcherTower, "Archer Tower", Category.Defenses, Resource.Gold, [(1, 150, 60), (2, 250, 600)]),
            Building(DoubleCannon, "Double Cannon", Category.Defenses, Resource.Gold, [(3, 1000, 3600), (3, 2000, 7200)],
                merge: [new MergeSource(Cannon, 4, false, 2)]),
            Building(AirBomb, "Air Bomb", Category.Traps, Resource.Gold, [(2, 50, 0), (2, 75, 30), (3, 100, 60)], kind: ItemKind.Trap),
            Building(GameData.WallId, "Wall", Category.Walls, Resource.Gold, [(1, 10, 0), (2, 20, 0), (3, 40, 0)], kind: ItemKind.Wall, altFrom: 2),
            Building(GameData.BuildersHutId, "Builder's Hut", Category.Defenses, Resource.Gold, [(1, 0, 0), (3, 500, 900)]),
            Building(GameData.LaboratoryId, "Laboratory", Category.ArmyBuildings, Resource.Elixir, [(1, 100, 60), (2, 500, 600), (3, 900, 1200)]),
            Building(GameData.HeroHallId, "Hero Hall", Category.ArmyBuildings, Resource.Elixir, [(2, 300, 300), (3, 600, 600)]),
            Unit(Barbarian, "Barbarian", ItemKind.Troop, Category.Troops, UpgradeTrack.Laboratory, Resource.Elixir,
                [(1, 0, 0, 0), (2, 2, 50, 3600), (3, 3, 100, 7200)]),
            Unit(Archer, "Archer", ItemKind.Troop, Category.Troops, UpgradeTrack.Laboratory, Resource.Elixir,
                [(1, 1, 0, 0), (2, 2, 80, 1800)]),
            Unit(EventTroop, "Party Troop", ItemKind.Troop, Category.Troops, UpgradeTrack.Laboratory, Resource.Elixir,
                [(1, 1, 0, 0), (1, 1, 10, 60)], seasonal: true),
            Unit(King, "Barbarian King", ItemKind.Hero, Category.Heroes, UpgradeTrack.Builder, Resource.DarkElixir,
                [(2, 1, 0, 0), (2, 1, 1000, 3600), (3, 2, 2000, 7200), (3, 2, 3000, 10800)], heroHall: true),
            Unit(Puppet, "Barbarian Puppet", ItemKind.Equipment, Category.Equipment, UpgradeTrack.Blacksmith, Resource.ShinyOre,
                [(2, 0, 0, 0), (2, 0, 120, 0), (3, 0, 240, 0)]),
            Unit(EpicGear, "Giant Gauntlet", ItemKind.Equipment, Category.Equipment, UpgradeTrack.Blacksmith, Resource.StarryOre,
                [(2, 0, 0, 0), (2, 0, 10, 0)], epic: true),
            Hall(GameData.BuilderHallId, Village.BuilderBase, ItemKind.BuilderHall, [[(BbCannon, 2)], [(BbCannon, 1)]]),
            Building(BbCannon, "Cannon", Category.Defenses, Resource.BuilderGold, [(1, 1000, 600), (2, 3000, 1200)], village: Village.BuilderBase),
            Unit(BbTroop, "Raged Barbarian", ItemKind.Troop, Category.Troops, UpgradeTrack.StarLaboratory, Resource.BuilderElixir,
                [(1, 0, 0, 0), (2, 0, 500, 900)], village: Village.BuilderBase),
            Building(GameData.StarLaboratoryId, "Star Laboratory", Category.ArmyBuildings, Resource.BuilderElixir, [(1, 0, 0)], village: Village.BuilderBase),
        };

        return new GameData(items, [], [], [], new DataSourceInfo("test", null, null, null, null));
    }

    private static ItemDef Hall(int id, Village village, ItemKind kind, List<(int Id, int Qty)>[] unlocks) => new()
    {
        Id = id,
        Name = kind == ItemKind.TownHall ? "Town Hall" : "Builder Hall",
        Kind = kind,
        Village = village,
        Category = Category.None,
        Track = UpgradeTrack.Builder,
        CountsTowardsProgress = false,
        Levels = unlocks.Select((u, i) => new LevelDef
        {
            Level = i + 1,
            RequiredHall = i + 1,
            Unlocks = u.Select(x => new HallUnlock(x.Id, x.Qty)).ToList(),
        }).ToList(),
    };

    /// <param name="levels">(required hall, cost to reach, time to reach) for level 1, 2, ...</param>
    private static ItemDef Building(int id, string name, Category category, Resource resource, (int Hall, long Cost, long Time)[] levels,
        ItemKind kind = ItemKind.Building, int altFrom = 0, IReadOnlyList<MergeSource>? merge = null, Village village = Village.Home) => new()
    {
        Id = id,
        Name = name,
        Kind = kind,
        Village = village,
        Category = category,
        Track = village == Village.Home ? UpgradeTrack.Builder : UpgradeTrack.BuilderBaseBuilder,
        UpgradeResource = resource,
        Levels = levels.Select((l, i) => new LevelDef
        {
            Level = i + 1,
            RequiredHall = l.Hall,
            Costs = [new ResourceAmount(resource, l.Cost)],
            TimeSeconds = l.Time,
            AltResource = altFrom > 0 && i + 1 >= altFrom ? Resource.Elixir : Resource.None,
            MergeSources = i == 0 && merge is not null ? merge : [],
        }).ToList(),
    };

    /// <param name="levels">(required hall, required lab/hero hall, cost to reach, time to reach) per level.</param>
    private static ItemDef Unit(int id, string name, ItemKind kind, Category category, UpgradeTrack track, Resource resource,
        (int Hall, int Req, long Cost, long Time)[] levels, bool seasonal = false, bool heroHall = false, bool epic = false,
        Village village = Village.Home) => new()
    {
        Id = id,
        Name = name,
        Kind = kind,
        Village = village,
        Category = category,
        Track = track,
        UpgradeResource = resource,
        IsSeasonal = seasonal,
        Rarity = kind == ItemKind.Equipment ? epic ? "Epic" : "Common" : null,
        CountsTowardsProgress = !seasonal,
        Levels = levels.Select((l, i) => new LevelDef
        {
            Level = i + 1,
            RequiredHall = l.Hall,
            RequiredLab = heroHall ? 0 : l.Req,
            RequiredHeroHall = heroHall ? l.Req : 0,
            Costs = l.Cost > 0 ? [new ResourceAmount(resource, l.Cost)] : [],
            TimeSeconds = l.Time,
        }).ToList(),
    };

    public static VillageState Home(GameData data, int hall, IEnumerable<(int Id, int Level, long? Timer)> structures,
        IEnumerable<(int Id, int Level, long? Timer)>? units = null, int builders = 5) => new()
    {
        Village = Village.Home,
        HallLevel = hall,
        Structures = structures.Select(s => new StructureInstance { Item = data.Get(s.Id)!, Level = s.Level, TimerLeft = s.Timer }).ToList(),
        Units = (units ?? []).ToDictionary(u => u.Id, u => new UnitState { Item = data.Get(u.Id)!, Level = u.Level, TimerLeft = u.Timer }),
        AsOf = DateTimeOffset.UnixEpoch,
        HasStructures = true,
        Builders = builders,
    };

    public static IEnumerable<(int, int, long?)> Many(int id, int level, int count) => Enumerable.Repeat((id, level, (long?)null), count);
}
