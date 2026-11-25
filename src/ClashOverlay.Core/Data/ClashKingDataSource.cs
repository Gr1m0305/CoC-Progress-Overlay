using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClashOverlay.Core.Data;

/// <summary>
/// Reads ClashKing's static data (<c>assets/static_data/*.json</c> from ClashKingInc/ClashKingAssets, GPL-3.0) from a
/// local folder the app downloaded it into. See docs/DATA_NOTES.md for the format and its quirks.
/// </summary>
public sealed class ClashKingDataSource(string dataDir, ILogger? logger = null) : IGameDataSource
{
    public const string Repository = "ClashKingInc/ClashKingAssets";

    /// <summary>The files the app needs, without extension.</summary>
    public static readonly IReadOnlyList<string> RequiredFiles =
    [
        "buildings", "traps", "troops", "spells", "heroes", "pets", "equipment", "guardians", "helpers", "resources",
        "league_tiers", "builder_leagues", "war_leagues",
    ];

    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    public string Name => "ClashKing";

    public string DataDir => dataDir;

    public bool IsAvailable => RequiredFiles.All(f => File.Exists(Path.Combine(dataDir, f + ".json")));

    public GameData Load()
    {
        if (!IsAvailable)
        {
            var missing = RequiredFiles.Where(f => !File.Exists(Path.Combine(dataDir, f + ".json")));
            throw new GameDataException($"Game data is missing ({string.Join(", ", missing)}). Use \"Check for data update\".");
        }

        try
        {
            var manifest = StaticDataManifest.TryRead(dataDir);
            var info = new DataSourceInfo(Name, manifest?.Commit, manifest?.CommitDate, manifest?.DownloadedAt, manifest?.Origin);
            var builder = new Builder(_logger);
            builder.AddStructures(Items("buildings"), isTrap: false);
            builder.AddStructures(Items("traps"), isTrap: true);
            builder.AddUnits(Items("troops"), "troops");
            builder.AddUnits(Items("spells"), "spells");
            builder.AddUnits(Items("heroes"), "heroes");
            builder.AddUnits(Items("pets"), "pets");
            builder.AddUnits(Items("equipment"), "equipment");
            builder.AddUnits(Items("guardians"), "guardians");
            builder.AddUnits(Items("helpers"), "helpers");
            var data = builder.Build(Leagues("league_tiers"), Leagues("builder_leagues"), Leagues("war_leagues"), info);
            if (data.MaxTownHall < 1 || data.Items.Count < 50)
            {
                throw new GameDataException("The game data looks incomplete (no Town Hall levels).");
            }

            return data;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new GameDataException("The game data could not be read: " + ex.Message, ex);
        }
    }

    private List<JsonElement> Items(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dataDir, file + ".json")));
        var root = doc.RootElement;
        var items = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("items", out var arr) ? arr : root;
        if (items.ValueKind != JsonValueKind.Array)
        {
            throw new GameDataException($"{file}.json has no items list.");
        }

        return items.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private List<LeagueDef> Leagues(string file) =>
        Items(file).Select(e => new LeagueDef(Int(e, "_id"), Str(e, "name") ?? string.Empty, Int(e, "trophy_start"))).ToList();

    internal static int Int(JsonElement e, string name, int fallback = 0) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? (int)n : fallback;

    internal static long Long(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;

    internal static int? NullableInt(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? (int)n : null;

    internal static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    internal static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>Turns raw JSON items into <see cref="ItemDef"/>s, resolving the data's quirks.</summary>
    private sealed class Builder(ILogger logger)
    {
        private readonly List<RawItem> _raw = [];

        private sealed class RawItem
        {
            public required JsonElement Json { get; init; }
            public required string Group { get; init; }
            public bool IsTrap { get; init; }
            public int Id => Int(Json, "_id");
        }

        public void AddStructures(IEnumerable<JsonElement> items, bool isTrap)
        {
            foreach (var e in items)
            {
                _raw.Add(new RawItem { Json = e, Group = isTrap ? "traps" : "buildings", IsTrap = isTrap });
            }
        }

        public void AddUnits(IEnumerable<JsonElement> items, string group)
        {
            foreach (var e in items)
            {
                _raw.Add(new RawItem { Json = e, Group = group });
            }
        }

        public GameData Build(IReadOnlyList<LeagueDef> tiers, IReadOnlyList<LeagueDef> bb, IReadOnlyList<LeagueDef> war, DataSourceInfo info)
        {
            // First hall level that lists each building/trap, for levels whose required_townhall is null.
            var firstHall = new Dictionary<int, int>();
            foreach (var hallId in new[] { GameData.TownHallId, GameData.BuilderHallId })
            {
                var hall = _raw.FirstOrDefault(r => r.Id == hallId && r.Group == "buildings");
                if (hall is null || !hall.Json.TryGetProperty("levels", out var levels))
                {
                    continue;
                }

                foreach (var level in levels.EnumerateArray())
                {
                    var lv = Int(level, "level");
                    if (!level.TryGetProperty("unlocks", out var unlocks) || unlocks.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var u in unlocks.EnumerateArray())
                    {
                        var id = Int(u, "_id");
                        if (Int(u, "quantity") > 0 && !firstHall.ContainsKey(id))
                        {
                            firstHall[id] = lv;
                        }
                    }
                }
            }

            var items = new List<ItemDef>();
            var seen = new HashSet<int>();
            foreach (var raw in _raw)
            {
                if (!seen.Add(raw.Id))
                {
                    logger.LogWarning("Duplicate id {Id} in {Group}; keeping the first", raw.Id, raw.Group);
                    continue;
                }

                var item = raw.Group switch
                {
                    "buildings" or "traps" => Structure(raw, firstHall),
                    _ => Unit(raw),
                };
                if (item is not null)
                {
                    items.Add(item);
                }
            }

            return new GameData(items, tiers, bb, war, info);
        }

        private static Village VillageOf(JsonElement e) =>
            string.Equals(Str(e, "village"), "builderBase", StringComparison.OrdinalIgnoreCase) ? Village.BuilderBase : Village.Home;

        private ItemDef? Structure(RawItem raw, Dictionary<int, int> firstHall)
        {
            var e = raw.Json;
            var id = raw.Id;
            var name = Str(e, "name") ?? $"#{id}";
            var village = VillageOf(e);
            var type = Str(e, "type") ?? string.Empty;
            var resource = GameEnumText.ParseResource(Str(e, "upgrade_resource"));

            var kind = raw.IsTrap ? ItemKind.Trap
                : id == GameData.TownHallId ? ItemKind.TownHall
                : id == GameData.BuilderHallId ? ItemKind.BuilderHall
                : type == "Wall" ? ItemKind.Wall
                : ItemKind.Building;

            var category = kind switch
            {
                ItemKind.Trap => Category.Traps,
                ItemKind.Wall => Category.Walls,
                ItemKind.TownHall or ItemKind.BuilderHall => Category.None,
                _ => type switch
                {
                    "Defense" => Category.Defenses,
                    "Army" => Category.ArmyBuildings,
                    "Resource" => Category.Resources,
                    "Worker" when village == Village.Home => Category.Defenses, // Builder's Huts shoot from TH14
                    "BB Builder Hall" => Category.ArmyBuildings, // O.T.T.O's Outpost
                    _ => Category.None,
                },
            };

            // Things with nothing to work towards: halls (the anchor), gem-bought or automatic huts, the Crafting
            // Station (its seasonal modules rotate), helpers' hut.
            var counts = category != Category.None
                && id != GameData.CraftingStationId
                && resource != Resource.Gems
                && type is not ("Worker2" or "Helper");

            var levels = new List<LevelDef>();
            var previousHall = 1;
            if (e.TryGetProperty("levels", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var l in arr.EnumerateArray().OrderBy(x => Int(x, "level")))
                {
                    var lv = Int(l, "level");
                    var hall = NullableInt(l, "required_townhall");
                    int requiredHall;
                    if (hall is null)
                    {
                        requiredHall = Math.Max(previousHall, firstHall.GetValueOrDefault(id, previousHall));
                        logger.LogDebug("{Name} level {Level} has no required hall; using {Hall}", name, lv, requiredHall);
                    }
                    else
                    {
                        requiredHall = Math.Max(hall.Value, kind == ItemKind.TownHall || kind == ItemKind.BuilderHall ? 0 : 1);
                    }

                    // A hall level is "required" at the previous level in the data (TH19 needs TH18); having it means
                    // being that hall level.
                    if (kind is ItemKind.TownHall or ItemKind.BuilderHall)
                    {
                        requiredHall = lv;
                    }

                    previousHall = requiredHall;
                    var alt = GameEnumText.ParseResource(Str(l, "alt_upgrade_resource"));
                    levels.Add(new LevelDef
                    {
                        Level = lv,
                        RequiredHall = requiredHall,
                        Costs = [new ResourceAmount(resource, Long(l, "build_cost"))],
                        AltResource = alt,
                        TimeSeconds = Long(l, "build_time"),
                        MergeSources = MergeSources(l),
                        Unlocks = Unlocks(l),
                    });
                }
            }

            return new ItemDef
            {
                Id = id,
                Name = name,
                Kind = kind,
                Village = village,
                Category = category,
                Track = village == Village.Home ? UpgradeTrack.Builder : UpgradeTrack.BuilderBaseBuilder,
                UpgradeResource = resource,
                Levels = levels,
                SourceType = type,
                CanGearUp = e.TryGetProperty("gear_up", out var g) && g.ValueKind == JsonValueKind.Object,
                CountsTowardsProgress = counts && levels.Count > 0,
            };
        }

        private static List<MergeSource> MergeSources(JsonElement level)
        {
            if (!level.TryGetProperty("merge_requirement", out var m) || m.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return m.EnumerateArray()
                .GroupBy(s => (Id: Int(s, "_id"), Level: Int(s, "level"), Geared: Bool(s, "geared_up")))
                .Select(g => new MergeSource(g.Key.Id, g.Key.Level, g.Key.Geared, g.Count()))
                .ToList();
        }

        private static List<HallUnlock> Unlocks(JsonElement level)
        {
            if (!level.TryGetProperty("unlocks", out var u) || u.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return u.EnumerateArray().Select(x => new HallUnlock(Int(x, "_id"), Int(x, "quantity"))).Where(x => x.ItemId > 0).ToList();
        }

        private static ItemDef? Unit(RawItem raw)
        {
            var e = raw.Json;
            var id = raw.Id;
            var name = Str(e, "name") ?? $"#{id}";
            var village = VillageOf(e);
            var production = Str(e, "production_building");
            var isSeasonal = Bool(e, "is_seasonal");
            var isSuper = e.TryGetProperty("super_troop", out var st) && st.ValueKind == JsonValueKind.Object;
            var isSquad = (e.TryGetProperty("squad", out var sq) && sq.ValueKind == JsonValueKind.Object)
                || string.Equals(production, "Squad Barracks", StringComparison.OrdinalIgnoreCase);

            var (kind, category, track) = raw.Group switch
            {
                "troops" when string.Equals(production, "Workshop", StringComparison.OrdinalIgnoreCase)
                    => (ItemKind.Siege, Category.SiegeMachines, UpgradeTrack.Laboratory),
                "troops" => (ItemKind.Troop, Category.Troops, village == Village.Home ? UpgradeTrack.Laboratory : UpgradeTrack.StarLaboratory),
                "spells" => (ItemKind.Spell, Category.Spells, UpgradeTrack.Laboratory),
                "heroes" => (ItemKind.Hero, Category.Heroes, village == Village.Home ? UpgradeTrack.Builder : UpgradeTrack.BuilderBaseBuilder),
                "pets" => (ItemKind.Pet, Category.Pets, UpgradeTrack.PetHouse),
                "equipment" => (ItemKind.Equipment, Category.Equipment, UpgradeTrack.Blacksmith),
                "guardians" => (ItemKind.Guardian, Category.Guardians, UpgradeTrack.Builder),
                "helpers" => (ItemKind.Helper, Category.Helpers, UpgradeTrack.Gems),
                _ => (ItemKind.Troop, Category.None, UpgradeTrack.None),
            };

            var resource = GameEnumText.ParseResource(Str(e, "upgrade_resource"));
            var levels = new List<LevelDef>();
            if (e.TryGetProperty("levels", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                var ordered = arr.EnumerateArray().OrderBy(x => Int(x, "level")).ToList();
                var previousHall = 1;
                for (var i = 0; i < ordered.Count; i++)
                {
                    var l = ordered[i];
                    // The data stores the cost/time of going from this level to the next; shift it so each level holds
                    // what it takes to reach it. Unlocking level 1 is free (it comes with the building that trains it).
                    var prev = i > 0 ? ordered[i - 1] : (JsonElement?)null;
                    var costs = prev is { } p ? UnitCost(p, resource) : [];
                    var hall = NullableInt(l, "required_townhall") ?? previousHall;
                    previousHall = Math.Max(1, hall);
                    levels.Add(new LevelDef
                    {
                        Level = Int(l, "level"),
                        RequiredHall = Math.Max(1, hall),
                        RequiredLab = NullableInt(l, "required_lab_level") ?? 0,
                        RequiredHeroHall = NullableInt(l, "required_hero_tavern_level") ?? 0,
                        RequiredPetHouse = NullableInt(l, "required_pet_house_level") ?? 0,
                        RequiredBlacksmith = NullableInt(l, "required_blacksmith_level") ?? 0,
                        Costs = costs,
                        TimeSeconds = prev is { } pt ? Long(pt, "upgrade_time") : 0,
                    });
                }
            }

            return new ItemDef
            {
                Id = id,
                Name = name,
                Kind = kind,
                Village = village,
                Category = category,
                Track = track,
                UpgradeResource = resource,
                Levels = levels,
                ProductionBuilding = production,
                ProductionBuildingLevel = Int(e, "production_building_level"),
                IsSeasonal = isSeasonal,
                IsSuperTroop = isSuper,
                IsSquad = isSquad,
                Rarity = Str(e, "rarity"),
                HeroName = Str(e, "hero"),
                CountsTowardsProgress = category is not (Category.None or Category.Helpers) && !isSeasonal && !isSuper && !isSquad && levels.Count > 0,
            };
        }

        private static List<ResourceAmount> UnitCost(JsonElement level, Resource resource)
        {
            if (!level.TryGetProperty("upgrade_cost", out var c))
            {
                return [];
            }

            if (c.ValueKind == JsonValueKind.Object)
            {
                var ores = new List<ResourceAmount>();
                foreach (var p in c.EnumerateObject())
                {
                    var r = GameEnumText.ParseResource(p.Name);
                    if (r != Resource.None && p.Value.TryGetInt64(out var amount) && amount > 0)
                    {
                        ores.Add(new ResourceAmount(r, amount));
                    }
                }

                return ores;
            }

            return c.ValueKind == JsonValueKind.Number && c.TryGetInt64(out var n) && n > 0
                ? [new ResourceAmount(resource, n)]
                : [];
        }
    }
}
