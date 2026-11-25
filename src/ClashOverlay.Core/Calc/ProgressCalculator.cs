using ClashOverlay.Core.Data;
using ClashOverlay.Core.State;

namespace ClashOverlay.Core.Calc;

public enum ProgressMode
{
    /// <summary>Levels done / levels at max.</summary>
    Levels,

    /// <summary>Upgrade time done / total upgrade time to max.</summary>
    Time,
}

public sealed class CategoryProgress
{
    public required Category Category { get; init; }

    public long LevelsDone { get; init; }

    public long LevelsMax { get; init; }

    public long TimeDone { get; init; }

    public long TimeMax { get; init; }

    public long TimeLeft { get; init; }

    public required ResourceTotals CostLeft { get; init; }

    public int ItemsMaxed { get; init; }

    public int ItemsTotal { get; init; }

    public double LevelFraction => LevelsMax == 0 ? 1 : (double)LevelsDone / LevelsMax;

    public double TimeFraction => TimeMax == 0 ? LevelFraction : (double)TimeDone / TimeMax;

    public double Fraction(ProgressMode mode) => mode == ProgressMode.Time ? TimeFraction : LevelFraction;

    public bool IsEmpty => ItemsTotal == 0;
}

public sealed record WallLevelCount(int Level, int Count);

public sealed class WallSummary
{
    public int Total { get; init; }

    public int MaxLevel { get; init; }

    public int AtMax { get; init; }

    /// <summary>Highest level first.</summary>
    public IReadOnlyList<WallLevelCount> ByLevel { get; init; } = [];

    /// <summary>Gold or elixir still to spend (walls from level 5 take either).</summary>
    public long GoldOrElixirLeft { get; init; }

    /// <summary>Cost of low walls that only take one resource (e.g. builder gold, or gold before level 5).</summary>
    public required ResourceTotals OtherCostLeft { get; init; }

    public long LevelsDone { get; init; }

    public long LevelsMax { get; init; }

    public double Fraction => LevelsMax == 0 ? 1 : (double)LevelsDone / LevelsMax;
}

public sealed class VillageProgress
{
    public required Village Village { get; init; }

    public required int HallLevel { get; init; }

    public required IReadOnlyList<CategoryProgress> Categories { get; init; }

    /// <summary>Every category except walls.</summary>
    public required CategoryProgress Overall { get; init; }

    public required WallSummary Walls { get; init; }

    /// <summary>Everything still to pay except walls (see <see cref="WallSummary"/>).</summary>
    public required ResourceTotals CostLeft { get; init; }

    /// <summary>Everything still to pay, walls included (their gold-or-elixir share is in <see cref="ResourceTotals.GoldOrElixir"/>).</summary>
    public required ResourceTotals TotalCostLeft { get; init; }

    public long BuilderTime { get; init; }

    public int Builders { get; init; }

    public long BuilderTimePerBuilder => Builders <= 0 ? BuilderTime : (long)Math.Ceiling(BuilderTime / (double)Builders);

    public long LabTime { get; init; }

    public long PetHouseTime { get; init; }

    public ItemDef? LongestHero { get; init; }

    public long LongestHeroTime { get; init; }

    public CategoryProgress? For(Category c) => Categories.FirstOrDefault(x => x.Category == c);

    public double Fraction(ProgressMode mode) => Overall.Fraction(mode);
}

public static class ProgressCalculator
{
    public static readonly IReadOnlyList<Category> HomeOrder =
    [
        Category.Defenses, Category.ArmyBuildings, Category.Resources, Category.Traps, Category.Walls, Category.Heroes,
        Category.Troops, Category.Spells, Category.SiegeMachines, Category.Pets, Category.Guardians, Category.Equipment,
    ];

    public static readonly IReadOnlyList<Category> BuilderOrder =
    [
        Category.Defenses, Category.ArmyBuildings, Category.Resources, Category.Traps, Category.Walls, Category.Heroes, Category.Troops,
    ];

    public static VillageProgress Calculate(IReadOnlyList<Goal> goals, VillageState state, int? buildersOverride = null)
    {
        var order = state.Village == Village.Home ? HomeOrder : BuilderOrder;
        var categories = order.Select(c => Summarise(c, goals.Where(g => g.Item.Category == c).ToList())).ToList();
        var nonWall = goals.Where(g => g.Item.Category != Category.Walls).ToList();
        var overall = Summarise(Category.None, nonWall);
        var total = new ResourceTotals();
        foreach (var g in goals)
        {
            total.Add(g.CostLeft);
        }

        var builderTrack = state.Village == Village.Home ? UpgradeTrack.Builder : UpgradeTrack.BuilderBaseBuilder;
        var labTrack = state.Village == Village.Home ? UpgradeTrack.Laboratory : UpgradeTrack.StarLaboratory;
        var heroes = goals.Where(g => g.Item.Kind == ItemKind.Hero).OrderByDescending(g => g.TimeLeft).FirstOrDefault();

        return new VillageProgress
        {
            Village = state.Village,
            HallLevel = state.HallLevel,
            Categories = categories,
            Overall = overall,
            Walls = Walls(goals.Where(g => g.Item.Category == Category.Walls).ToList()),
            CostLeft = overall.CostLeft,
            TotalCostLeft = total,
            BuilderTime = goals.Where(g => g.Item.Track == builderTrack).Sum(g => g.TimeLeft),
            Builders = buildersOverride is > 0 ? buildersOverride.Value : state.Builders,
            LabTime = goals.Where(g => g.Item.Track == labTrack).Sum(g => g.TimeLeft),
            PetHouseTime = goals.Where(g => g.Item.Track == UpgradeTrack.PetHouse).Sum(g => g.TimeLeft),
            LongestHero = heroes is { TimeLeft: > 0 } ? heroes.Item : null,
            LongestHeroTime = heroes?.TimeLeft ?? 0,
        };
    }

    public static CategoryProgress Summarise(Category category, IReadOnlyList<Goal> goals)
    {
        var cost = new ResourceTotals();
        foreach (var g in goals)
        {
            cost.Add(g.CostLeft);
        }

        return new CategoryProgress
        {
            Category = category,
            LevelsDone = goals.Sum(g => (long)g.LevelsDone),
            LevelsMax = goals.Sum(g => (long)g.Target),
            TimeDone = goals.Sum(g => g.TimeDone),
            TimeMax = goals.Sum(g => g.TimeToTarget),
            TimeLeft = goals.Sum(g => g.TimeLeft),
            CostLeft = cost,
            ItemsMaxed = goals.Count(g => g.IsMaxed),
            ItemsTotal = goals.Count,
        };
    }

    private static WallSummary Walls(IReadOnlyList<Goal> walls)
    {
        var other = new ResourceTotals();
        long goldOrElixir = 0;
        foreach (var w in walls)
        {
            var c = w.CostLeft;
            goldOrElixir += c.GoldOrElixir;
            foreach (var kv in c.NonZero)
            {
                other.Add(kv.Key, kv.Value);
            }
        }

        return new WallSummary
        {
            Total = walls.Count,
            MaxLevel = walls.Count == 0 ? 0 : walls.Max(w => w.Target),
            AtMax = walls.Count(w => w.IsMaxed),
            ByLevel = walls.GroupBy(w => w.Level).OrderByDescending(g => g.Key).Select(g => new WallLevelCount(g.Key, g.Count())).ToList(),
            GoldOrElixirLeft = goldOrElixir,
            OtherCostLeft = other,
            LevelsDone = walls.Sum(w => (long)w.LevelsDone),
            LevelsMax = walls.Sum(w => (long)w.Target),
        };
    }
}
