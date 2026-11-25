using ClashOverlay.Core.Data;
using ClashOverlay.Core.State;

namespace ClashOverlay.Core.Calc;

/// <summary>
/// Works out what "max for hall level T" means for a player: how many copies of each building the hall allows after
/// merges, which of the player's copies fills which slot, and the level every slot and unit should reach.
/// </summary>
public static class GoalPlanner
{
    /// <summary>All goals at <paramref name="hall"/> (normally the player's hall; the previous one for rushed).</summary>
    public static IReadOnlyList<Goal> Plan(GameData data, VillageState state, int hall)
    {
        var goals = new List<Goal>();
        if (hall <= 0)
        {
            return goals;
        }

        var isCurrent = hall == state.HallLevel;
        foreach (var item in data.ItemsIn(state.Village).Where(i => i.CountsTowardsProgress).OrderBy(i => i.Id))
        {
            if (item.IsStructure)
            {
                AddStructureGoals(data, state, item, hall, isCurrent, goals);
            }
            else if (UnitCounts(data, state, item, hall))
            {
                var target = item.MaxLevelFor(hall);
                var unit = state.Units.GetValueOrDefault(item.Id);
                goals.Add(new Goal
                {
                    Item = item,
                    Level = unit?.Level ?? 0,
                    Target = target,
                    TimerLeft = unit?.TimerLeft,
                    IsNewAtHall = isCurrent && item.MaxLevelFor(hall - 1) == 0,
                });
            }
        }

        return goals;
    }

    /// <summary>Units count when the hall allows them; epic equipment only when the player owns it (event unlocks).</summary>
    private static bool UnitCounts(GameData data, VillageState state, ItemDef item, int hall)
    {
        if (item.MaxLevelFor(hall) <= 0)
        {
            return false;
        }

        if (item.Kind == ItemKind.Equipment && item.IsEpic)
        {
            return state.UnitLevel(item.Id) > 0;
        }

        return true;
    }

    /// <summary>How many copies of a building the player should have at <paramref name="hall"/>, merges allowed for.</summary>
    public static int SlotCount(GameData data, VillageState state, ItemDef item, int hall)
    {
        if (item.MaxLevelFor(hall) <= 0)
        {
            return 0;
        }

        // Gem-bought huts and buildings the hall tables don't list: what the player has.
        if (item.Id == GameData.BuildersHutId || !data.HasCountData(item.Id))
        {
            return state.InstancesOf(item.Id).Count();
        }

        var gross = data.GrossCount(item.Id, hall);
        var consumed = 0;
        foreach (var (target, level, source) in data.MergeUsesOf(item.Id))
        {
            if (target.Village != state.Village)
            {
                continue;
            }

            consumed += BuiltMerges(data, state, target, level) * source.Count;
        }

        return Math.Max(0, gross - consumed);
    }

    /// <summary>Copies of a building that would remain if every merge the hall allows were done.</summary>
    private static int KeptAfterAllMerges(GameData data, ItemDef item, int hall)
    {
        var gross = data.GrossCount(item.Id, hall);
        var consumed = 0;
        foreach (var (target, level, source) in data.MergeUsesOf(item.Id))
        {
            var expected = target.Kind is ItemKind.TownHall or ItemKind.BuilderHall
                ? hall >= level.Level ? 1 : 0
                : target.MaxLevelFor(hall) >= level.Level ? data.GrossCount(target.Id, hall) : 0;
            consumed += expected * source.Count;
        }

        return Math.Max(0, gross - consumed);
    }

    /// <summary>
    /// How many merged buildings the player has built (each consumed its sources). A building under construction
    /// counts: the sources go when construction starts. Town Hall 17's merge counts once the hall is 17 or more.
    /// </summary>
    private static int BuiltMerges(GameData data, VillageState state, ItemDef target, LevelDef level)
    {
        if (target.Kind is ItemKind.TownHall or ItemKind.BuilderHall)
        {
            return state.HallLevel >= level.Level ? 1 : 0;
        }

        var built = state.InstancesOf(target.Id).Count(i => i.Level >= level.Level || (i.Level == level.Level - 1 && i.IsUpgrading));
        return data.HasCountData(target.Id) ? Math.Min(built, data.GrossCount(target.Id, state.HallLevel)) : built;
    }

    private static void AddStructureGoals(GameData data, VillageState state, ItemDef item, int hall, bool isCurrent, List<Goal> goals)
    {
        var slots = SlotCount(data, state, item, hall);
        if (slots == 0)
        {
            return;
        }

        var max = item.MaxLevelFor(hall);
        var kept = item.Id == GameData.BuildersHutId || !data.HasCountData(item.Id) ? slots : Math.Min(slots, KeptAfterAllMerges(data, item, hall));
        var feedTarget = data.MergeUsesOf(item.Id).Select(u => u.Source.Level).DefaultIfEmpty(max).Max();
        var previousSlots = isCurrent && hall > 1 ? SlotCount(data, state, item, hall - 1) : slots;

        var owned = state.InstancesOf(item.Id)
            .OrderByDescending(i => i.Level)
            .ThenByDescending(i => i.IsUpgrading)
            .ToList();

        for (var i = 0; i < slots; i++)
        {
            var instance = i < owned.Count ? owned[i] : null;
            var feed = i >= kept;
            goals.Add(new Goal
            {
                Item = item,
                Level = instance?.Level ?? 0,
                Target = feed ? Math.Min(feedTarget, max) : max,
                TimerLeft = instance?.TimerLeft,
                SlotIndex = i,
                IsMergeFeed = feed,
                IsNewAtHall = isCurrent && i >= previousSlots,
            });
        }
    }
}
