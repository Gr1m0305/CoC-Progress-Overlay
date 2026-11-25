using ClashOverlay.Core.Data;
using ClashOverlay.Core.State;

namespace ClashOverlay.Core.Calc;

/// <summary>The next upgrade of one or more identical copies.</summary>
public sealed class UpgradeRow
{
    public required ItemDef Item { get; init; }

    public required int FromLevel { get; init; }

    public int ToLevel => FromLevel + 1;

    public required int Count { get; init; }

    /// <summary>The level everything should reach (the hall's max, or the level a merge needs).</summary>
    public required int Target { get; init; }

    public long Time => Item.TimeToReach(ToLevel);

    public ResourceTotals Cost
    {
        get
        {
            var t = new ResourceTotals();
            t.Add(Item.GetLevel(ToLevel));
            return t;
        }
    }

    /// <summary>Building not built yet ("new, build").</summary>
    public bool IsBuild => FromLevel == 0;

    public bool IsMergeFeed { get; init; }

    public bool IsNewAtHall { get; init; }

    /// <summary>"Needs Lab 16" and the like, when the upgrade can't start yet.</summary>
    public string? LockedReason { get; init; }

    public bool IsLocked => LockedReason is not null;
}

/// <summary>An upgrade that is running now.</summary>
public sealed record ActiveUpgrade(ItemDef Item, int FromLevel, long SecondsLeft, UpgradeTrack Track)
{
    public int ToLevel => FromLevel + 1;
}

public sealed class UpgradePlan
{
    /// <summary>Longest first; new buildings included as "build" rows.</summary>
    public required IReadOnlyList<UpgradeRow> Builder { get; init; }

    /// <summary>Laboratory (or Star Laboratory) research, longest first.</summary>
    public required IReadOnlyList<UpgradeRow> Laboratory { get; init; }

    public required IReadOnlyList<UpgradeRow> PetHouse { get; init; }

    public required IReadOnlyList<UpgradeRow> Blacksmith { get; init; }

    /// <summary>Shortest time left first.</summary>
    public required IReadOnlyList<ActiveUpgrade> Active { get; init; }
}

public static class UpgradePlanner
{
    public static UpgradePlan Plan(IReadOnlyList<Goal> goals, VillageState state)
    {
        var home = state.Village == Village.Home;
        var builderTrack = home ? UpgradeTrack.Builder : UpgradeTrack.BuilderBaseBuilder;
        var labTrack = home ? UpgradeTrack.Laboratory : UpgradeTrack.StarLaboratory;
        var labName = home ? "Lab" : "Star Lab";

        var builder = Rows(goals.Where(g => g.Item.Track == builderTrack && g.Item.Category != Category.Walls), g =>
            g.Item.Kind == ItemKind.Hero && g.Item.GetLevel(g.Level + 1) is { RequiredHeroHall: > 0 } l && state.HeroHallLevel < l.RequiredHeroHall
                ? $"Needs Hero Hall {l.RequiredHeroHall}"
                : null);

        var lab = Rows(goals.Where(g => g.Item.Track == labTrack), g =>
            g.Item.GetLevel(g.Level + 1) is { RequiredLab: > 0 } l && state.LabLevel < l.RequiredLab ? $"Needs {labName} {l.RequiredLab}" : null);

        var pets = Rows(goals.Where(g => g.Item.Track == UpgradeTrack.PetHouse), g =>
            g.Item.GetLevel(g.Level + 1) is { RequiredPetHouse: > 0 } l && state.PetHouseLevel < l.RequiredPetHouse ? $"Needs Pet House {l.RequiredPetHouse}" : null);

        var smith = Rows(goals.Where(g => g.Item.Track == UpgradeTrack.Blacksmith), g =>
            g.Item.GetLevel(g.Level + 1) is { RequiredBlacksmith: > 0 } l && state.BlacksmithLevel < l.RequiredBlacksmith ? $"Needs Blacksmith {l.RequiredBlacksmith}" : null);

        var active = new List<ActiveUpgrade>();
        foreach (var s in state.Structures.Where(s => s.IsUpgrading))
        {
            active.Add(new ActiveUpgrade(s.Item, s.Level, s.TimerLeft!.Value, s.Item.Track));
        }

        foreach (var u in state.Units.Values.Where(u => u.IsUpgrading))
        {
            active.Add(new ActiveUpgrade(u.Item, u.Level, u.TimerLeft!.Value, u.Item.Track));
        }

        return new UpgradePlan
        {
            Builder = builder,
            Laboratory = lab,
            PetHouse = pets,
            Blacksmith = smith,
            Active = active.OrderBy(a => a.SecondsLeft).ThenBy(a => a.Item.Name, StringComparer.Ordinal).ToList(),
        };
    }

    private static List<UpgradeRow> Rows(IEnumerable<Goal> goals, Func<Goal, string?> locked) =>
        goals
            .Where(g => g.Level < g.Target && !g.IsUpgrading && g.Item.GetLevel(g.Level + 1) is not null)
            .GroupBy(g => (g.Item.Id, g.Level, g.Target, g.IsMergeFeed))
            .Select(grp =>
            {
                var first = grp.First();
                return new UpgradeRow
                {
                    Item = first.Item,
                    FromLevel = first.Level,
                    Count = grp.Count(),
                    Target = first.Target,
                    IsMergeFeed = first.IsMergeFeed,
                    IsNewAtHall = grp.All(g => g.IsNewAtHall),
                    LockedReason = locked(first),
                };
            })
            .OrderByDescending(r => r.Time)
            .ThenBy(r => r.Item.Name, StringComparer.Ordinal)
            .ThenBy(r => r.FromLevel)
            .ToList();
}
