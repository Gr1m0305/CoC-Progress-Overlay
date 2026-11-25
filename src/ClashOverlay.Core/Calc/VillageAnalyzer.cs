using ClashOverlay.Core.Data;
using ClashOverlay.Core.State;

namespace ClashOverlay.Core.Calc;

/// <summary>One line of the "New at TH19" card.</summary>
/// <param name="Added">Copies the hall level adds (0 for units).</param>
/// <param name="IsNewItem">The item did not exist at the previous hall level.</param>
/// <param name="NewLevels">Levels the hall level adds to the item's max.</param>
public sealed record HallNewsItem(ItemDef Item, int Added, bool IsNewItem, int NewLevels, int MaxLevel);

public sealed class HallNews
{
    public required int HallLevel { get; init; }

    /// <summary>Brand-new buildings, traps and units first, then extra copies.</summary>
    public required IReadOnlyList<HallNewsItem> Items { get; init; }

    /// <summary>How many existing items got higher max levels.</summary>
    public int ItemsWithNewLevels { get; init; }
}

public sealed record AnalyzerOptions(ProgressMode Mode = ProgressMode.Levels, int? BuildersOverride = null)
{
    /// <summary>Gold Pass builder and research boosts for the times shown (not for progress by time).</summary>
    public TimeBoosts Boosts { get; init; } = TimeBoosts.None;
}

/// <summary>Everything the overlay shows for one village.</summary>
public sealed class VillageReport
{
    public required VillageState State { get; init; }

    public required IReadOnlyList<Goal> Goals { get; init; }

    public required VillageProgress Progress { get; init; }

    public required RushReport Rush { get; init; }

    public required UpgradePlan Upgrades { get; init; }

    public required HallNews News { get; init; }

    public required AnalyzerOptions Options { get; init; }

    public double OverallFraction => Progress.Fraction(Options.Mode);
}

public static class VillageAnalyzer
{
    public static VillageReport Analyze(GameData data, VillageState state, AnalyzerOptions? options = null)
    {
        options ??= new AnalyzerOptions();
        var goals = GoalPlanner.Plan(data, state, state.HallLevel);
        return new VillageReport
        {
            State = state,
            Goals = goals,
            Progress = ProgressCalculator.Calculate(goals, state, options.BuildersOverride, options.Boosts),
            Rush = RushCalculator.Calculate(data, state),
            Upgrades = UpgradePlanner.Plan(goals, state, options.Boosts),
            News = News(data, state.Village, state.HallLevel),
            Options = options,
        };
    }

    public static HallNews News(GameData data, Village village, int hall)
    {
        var items = new List<HallNewsItem>();
        var withNewLevels = 0;
        if (hall >= 1)
        {
            foreach (var item in data.ItemsIn(village).Where(i => i.CountsTowardsProgress).OrderBy(i => i.Id))
            {
                var max = item.MaxLevelFor(hall);
                if (max == 0)
                {
                    continue;
                }

                var prevMax = item.MaxLevelFor(hall - 1);
                var added = item.IsStructure ? data.GrossCount(item.Id, hall) - data.GrossCount(item.Id, hall - 1) : 0;
                var isNew = prevMax == 0;
                if (isNew || added > 0)
                {
                    items.Add(new HallNewsItem(item, Math.Max(0, added), isNew, max - prevMax, max));
                }
                else if (max > prevMax)
                {
                    withNewLevels++;
                }
            }
        }

        return new HallNews
        {
            HallLevel = hall,
            Items = items.OrderByDescending(i => i.IsNewItem).ThenBy(i => i.Item.IsUnit).ThenBy(i => i.Item.Name, StringComparer.Ordinal).ToList(),
            ItemsWithNewLevels = withNewLevels,
        };
    }
}
