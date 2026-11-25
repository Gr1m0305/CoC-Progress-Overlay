using System.Globalization;
using System.Windows.Media;
using ClashOverlay.Core.Calc;
using ClashOverlay.Core.Data;
using ClashOverlay.Core.Display;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClashOverlay.App.ViewModels;

/// <summary>Copies of one item at one level that are not maxed yet, with the time and cost to max them all.</summary>
internal sealed record MaxRow(
    ImageSource? Image,
    string Name,
    string CountText,
    string LevelText,
    string TimeText,
    IReadOnlyList<CostItem> Cost,
    string? Note,
    Category Category,
    long Seconds);

/// <summary>One type's lines under a heading such as "DEFENSES · 11".</summary>
internal sealed record MaxGroup(string Title, IReadOnlyList<MaxRow> Rows);

/// <summary>
/// Everything that can still be maxed at the current hall level, in one place: buildings, traps, walls, heroes, troops,
/// spells, siege machines, pets and equipment, each with its level, the hall's max, and the time and cost left.
/// </summary>
internal sealed partial class MaxTab : TabViewModel
{
    private readonly TabContext _ctx;
    private readonly List<MaxRow> _all = [];
    private readonly bool _ready;

    public MaxTab(TabContext ctx)
    {
        _ctx = ctx;
        var r = ctx.Report;
        var boosts = r.Options.Boosts;
        var hall = $"{ctx.HallShort}{ctx.Hall}";
        var order = (ctx.Village == Village.Home ? ProgressCalculator.HomeOrder : ProgressCalculator.BuilderOrder).ToList();
        var open = r.Goals.Where(g => !g.IsMaxed).ToList();

        foreach (var grp in open.GroupBy(g => (g.Item.Id, g.Level, g.Target, g.IsUpgrading)))
        {
            var first = grp.First();
            var item = first.Item;
            var count = grp.Count();
            var seconds = grp.Sum(g => g.TimeLeftWith(boosts));
            var cost = new ResourceTotals();
            foreach (var g in grp)
            {
                cost.Add(g.CostLeft);
            }

            var from = first.Level > 0 ? $"Level {first.Level}"
                : first.IsUpgrading ? "Being built"
                : item.IsStructure ? "Not built" : "Not unlocked";
            var max = first.Target >= item.MaxLevelFor(ctx.Hall) ? $"max {first.Target}" : $"{first.Target} for a merge";
            var time = seconds > 0 ? Fmt.Duration(seconds) : item.TimeToReach(first.Level + 1) == 0 && !first.IsUpgrading ? "instant" : "";
            _all.Add(new MaxRow(
                ctx.Img(item, Math.Max(1, first.Level), 96),
                item.Name,
                count > 1 ? $"×{count}" : "",
                $"{from} → {max}",
                time,
                ctx.Costs(cost),
                first.IsUpgrading ? "upgrading" : null,
                item.Category,
                seconds));
        }

        // Types in the overview's order; the biggest jobs first within each.
        _all = _all
            .OrderBy(x => order.IndexOf(x.Category) is var i and >= 0 ? i : order.Count)
            .ThenByDescending(x => x.Seconds)
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .ToList();

        var levels = open.Sum(g => g.LevelsLeft);
        var items = r.Goals.Count;
        var maxed = items - open.Count;
        Headline = open.Count == 0 ? $"Everything is maxed for {hall}" : $"{Fmt.Plural(levels, "level")} left to max {hall}";
        SubLine = open.Count == 0
            ? "Every building, trap, wall, hero and unit is at its max for this hall level."
            : $"{maxed} of {items} maxed · {Fmt.Plural(open.Count, "thing")} still to upgrade. Each line is what is left to take it to the {hall} max.";

        var filters = new List<Choice>();
        if (_all.Count > 0)
        {
            filters.Add(new Choice("all", "All", _all.Count.ToString(CultureInfo.InvariantCulture)));
        }

        foreach (var c in order.Where(c => _all.Any(x => x.Category == c)))
        {
            filters.Add(new Choice(c.ToString(), c.DisplayName(ctx.Village), _all.Count(x => x.Category == c).ToString(CultureInfo.InvariantCulture)));
        }

        Filters = filters;
        Filter = Filters.Count > 0 ? Choice.Pick(Filters, ctx.Owner.RememberedView($"{Key}/filter")) : null;
        _ready = true;
        Fill();
    }

    public override string Key => "tomax";

    public override string Title => "To max";

    public string Headline { get; }

    public string SubLine { get; }

    public bool IsDone => _all.Count == 0;

    public IReadOnlyList<Choice> Filters { get; }

    [ObservableProperty]
    private Choice? _filter;

    /// <summary>The lines shown, under a heading per type.</summary>
    [ObservableProperty]
    private IReadOnlyList<MaxGroup> _groups = [];

    public IEnumerable<MaxRow> Rows => Groups.SelectMany(g => g.Rows);

    partial void OnFilterChanged(Choice? value)
    {
        if (_ready && value is not null)
        {
            _ctx.Owner.RememberView($"{Key}/filter", value.Key);
            Fill();
        }
    }

    private void Fill()
    {
        Category? only = Filter is { Key: not "all" } f && Enum.TryParse<Category>(f.Key, out var c) ? c : null;
        Groups = _all
            .Where(x => only is null || x.Category == only)
            .GroupBy(x => x.Category)
            .Select(g => new MaxGroup($"{g.Key.DisplayName(_ctx.Village).ToUpperInvariant()} · {g.Count()}", g.ToList()))
            .ToList();
    }
}
