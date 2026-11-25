using ClashOverlay.Core.Data;
using ClashOverlay.Core.State;

namespace ClashOverlay.Core.Calc;

/// <summary>Identical copies below the previous hall's max, e.g. "Air Bomb ×3, −1 each".</summary>
public sealed record RushedRow(ItemDef Item, int Level, int Count, int PreviousMax, bool IsUpgrading)
{
    public int BehindEach => Math.Max(0, PreviousMax - Level);

    public int BehindTotal => BehindEach * Count;

    public bool IsUnbuilt => Level == 0;

    /// <summary>Counts towards the rush score (equipment is listed but not scored).</summary>
    public bool IsScored => RushCalculator.IsScored(Item.Category);
}

public sealed record RushCategory(Category Category, int Items, int Levels, bool Scored);

public sealed class RushReport
{
    public required int HallLevel { get; init; }

    /// <summary>The hall level the village is compared with (current − 1).</summary>
    public required int ComparedWith { get; init; }

    /// <summary>Scored rows first, biggest gaps first; walls are summarised in <see cref="WallsBelow"/> instead.</summary>
    public required IReadOnlyList<RushedRow> Rows { get; init; }

    public required IReadOnlyList<RushCategory> Categories { get; init; }

    /// <summary>Levels behind the previous hall's max, summed (walls and equipment are shown but not scored).</summary>
    public int Score { get; init; }

    /// <summary>Score as a share of every scored level the previous hall allows.</summary>
    public double Fraction { get; init; }

    public int WallsBelow { get; init; }

    public bool IsRushed => Score > 0;
}

/// <summary>Anything below the max level allowed at the previous hall level. New copies the current hall added are not rushed.</summary>
public static class RushCalculator
{
    private static readonly HashSet<Category> Unscored = [Category.Walls, Category.Equipment];

    public static bool IsScored(Category category) => !Unscored.Contains(category);

    public static RushReport Calculate(GameData data, VillageState state)
    {
        var previous = state.HallLevel - 1;
        if (previous < 1)
        {
            return new RushReport { HallLevel = state.HallLevel, ComparedWith = previous, Rows = [], Categories = [] };
        }

        var goals = GoalPlanner.Plan(data, state, previous);
        var behind = goals.Where(g => g.Level < g.Target).ToList();
        var rows = behind
            .GroupBy(g => (g.Item.Id, g.Level, g.Target, g.IsUpgrading))
            .Select(grp => new RushedRow(grp.First().Item, grp.Key.Level, grp.Count(), grp.Key.Target, grp.Key.IsUpgrading))
            .Where(r => r.Item.Category != Category.Walls)
            .OrderByDescending(r => r.IsScored)
            .ThenByDescending(r => r.BehindEach)
            .ThenByDescending(r => r.Count)
            .ThenBy(r => r.Item.Name, StringComparer.Ordinal)
            .ToList();

        var order = state.Village == Village.Home ? ProgressCalculator.HomeOrder : ProgressCalculator.BuilderOrder;
        var categories = order
            .Select(c =>
            {
                var inCat = behind.Where(g => g.Item.Category == c).ToList();
                return new RushCategory(c, inCat.Count, inCat.Sum(g => g.Target - g.Level), !Unscored.Contains(c));
            })
            .Where(c => c.Items > 0)
            .ToList();

        var scoredGoals = goals.Where(g => !Unscored.Contains(g.Item.Category)).ToList();
        var score = scoredGoals.Sum(g => Math.Max(0, g.Target - g.Level));
        var possible = scoredGoals.Sum(g => (long)g.Target);

        return new RushReport
        {
            HallLevel = state.HallLevel,
            ComparedWith = previous,
            Rows = rows,
            Categories = categories,
            Score = score,
            Fraction = possible == 0 ? 0 : score / (double)possible,
            WallsBelow = behind.Count(g => g.Item.Category == Category.Walls),
        };
    }
}
