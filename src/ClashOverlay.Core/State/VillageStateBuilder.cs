using ClashOverlay.Core.Data;
using ClashOverlay.Core.Export;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClashOverlay.Core.State;

/// <summary>Both villages of one account.</summary>
public sealed record AccountState(VillageState Home, VillageState BuilderBase, IReadOnlyList<ExportWarning> Warnings)
{
    public VillageState For(Village village) => village == Village.Home ? Home : BuilderBase;
}

/// <summary>
/// The merge rule. Buildings, walls, traps, guardians, helpers and upgrade timers come from the export (the API has
/// none of them). Troop, spell, siege, hero, pet and equipment levels come from whichever source is newer. The Town
/// Hall and Builder Hall levels come from the API when it has them. Running upgrades are moved forward by the time
/// since the export: one whose timer has run out has gained its level.
/// </summary>
public static class VillageStateBuilder
{
    private static readonly ExportSection[] HomeStructures = [ExportSection.Buildings, ExportSection.Traps];
    private static readonly ExportSection[] BuilderStructures = [ExportSection.Buildings2, ExportSection.Traps2];

    private static readonly ExportSection[] HomeUnits =
    [
        ExportSection.Units, ExportSection.SiegeMachines, ExportSection.Spells, ExportSection.Heroes, ExportSection.Pets,
        ExportSection.Equipment, ExportSection.Guardians, ExportSection.Helpers,
    ];

    private static readonly ExportSection[] BuilderUnits = [ExportSection.Units2, ExportSection.Heroes2];

    public static AccountState Build(GameData data, VillageExport? export, ApiPlayerSnapshot? api, DateTimeOffset now, ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;
        var warnings = export is null ? [] : ExportParser.Validate(export, data);
        foreach (var w in warnings)
        {
            logger.LogWarning("Export: {Message}", w.Message);
        }

        var elapsed = export?.Timestamp is { } ts ? Math.Max(0, (long)(now - ts).TotalSeconds) : 0;
        var home = BuildVillage(data, Village.Home, export, api, now, elapsed, HomeStructures, HomeUnits);
        var bb = BuildVillage(data, Village.BuilderBase, export, api, now, elapsed, BuilderStructures, BuilderUnits);
        return new AccountState(home, bb, warnings);
    }

    private static VillageState BuildVillage(GameData data, Village village, VillageExport? export, ApiPlayerSnapshot? api,
        DateTimeOffset now, long elapsed, ExportSection[] structureSections, ExportSection[] unitSections)
    {
        var hallId = village == Village.Home ? GameData.TownHallId : GameData.BuilderHallId;
        var exportHall = 0;
        var structures = new List<StructureInstance>();
        if (export is not null)
        {
            foreach (var e in export.In(structureSections))
            {
                var item = data.Get(e.Id);
                if (item is null || item.Village != village)
                {
                    continue;
                }

                var (level, left) = Advance(e.Level, e.Timer, elapsed);
                if (e.Id == hallId)
                {
                    exportHall = Math.Max(exportHall, level);
                    continue;
                }

                for (var i = 0; i < e.Count; i++)
                {
                    structures.Add(new StructureInstance { Item = item, Level = level, TimerLeft = left, GearedUp = e.GearUp > 0 });
                }
            }
        }

        var apiHall = api is null ? 0 : village == Village.Home ? api.TownHallLevel : api.BuilderHallLevel;
        var hall = apiHall > 0 ? apiHall : exportHall;

        // Units: the export first (with its timers), then the API where it is newer.
        var units = new Dictionary<int, UnitState>();
        var exportLevels = new Dictionary<int, int>();
        if (export is not null)
        {
            foreach (var e in export.In(unitSections))
            {
                var item = data.Get(e.Id);
                if (item is null || item.Village != village)
                {
                    continue;
                }

                var (level, left) = Advance(e.Level, e.Timer, elapsed);
                if (!units.TryGetValue(item.Id, out var existing) || level > existing.Level)
                {
                    units[item.Id] = new UnitState { Item = item, Level = level, TimerLeft = left, Source = LevelSource.Export };
                    exportLevels[item.Id] = e.Level;
                }
            }
        }

        if (api is not null)
        {
            var apiIsNewer = export?.Timestamp is not { } exportTime || api.FetchedAt > exportTime;
            var apiVillage = village == Village.Home ? "home" : "builderBase";
            foreach (var u in api.Units)
            {
                if (!string.Equals(u.Village, apiVillage, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var item = data.FindUnit(u.Name, village);
                if (item is null)
                {
                    continue;
                }

                if (!units.TryGetValue(item.Id, out var current))
                {
                    units[item.Id] = new UnitState { Item = item, Level = u.Level, Source = LevelSource.Api };
                    continue;
                }

                if (!apiIsNewer || u.Level == current.Level)
                {
                    continue; // the export is newer, or they agree (keep the export's running timer)
                }

                // The API is newer and disagrees. Keep a running timer only when the API still shows the level the
                // upgrade started from (the upgrade has not finished yet).
                var started = exportLevels.GetValueOrDefault(item.Id, -1);
                var keepTimer = u.Level == started && current.TimerLeft is null && export?.Entries.Any(x => x.Id == item.Id && x.IsUpgrading) == true;
                long? timer = keepTimer ? 60 : null;
                units[item.Id] = new UnitState { Item = item, Level = u.Level, TimerLeft = timer, Source = LevelSource.Api };
            }
        }

        var builders = village == Village.Home
            ? structures.Count(s => s.Item.Id == GameData.BuildersHutId) + structures.Count(s => s.Item.Id == GameData.BobsHutId && s.Level > 0)
            : structures.Count == 0 ? 0 : 1 + (structures.Any(s => s.Item.Id == GameData.OttosOutpostId && s.Level > 0) ? 1 : 0);

        return new VillageState
        {
            Village = village,
            HallLevel = hall,
            Structures = structures,
            Units = units,
            AsOf = now,
            ExportTime = export?.Timestamp,
            ApiTime = api?.FetchedAt,
            HasStructures = structures.Count > 0 || exportHall > 0,
            Builders = builders,
        };
    }

    /// <summary>A running upgrade's level and time left after <paramref name="elapsed"/> seconds.</summary>
    internal static (int Level, long? Left) Advance(int level, long? timer, long elapsed)
    {
        if (timer is not > 0)
        {
            return (level, null);
        }

        var left = timer.Value - elapsed;
        return left <= 0 ? (level + 1, null) : (level, left);
    }
}
