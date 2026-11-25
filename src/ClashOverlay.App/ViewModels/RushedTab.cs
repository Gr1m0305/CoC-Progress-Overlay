using System.Windows.Media;
using ClashOverlay.Core.Data;
using ClashOverlay.Core.Display;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClashOverlay.App.ViewModels;

internal sealed record RushRow(ImageSource? Image, string Name, string CountText, string LevelText, Badge Behind, bool IsScored, bool IsUpgrading, string? Note, Category Category);

/// <summary>Everything below the previous hall's max level, biggest gaps first, filtered by type.</summary>
internal sealed partial class RushedTab : TabViewModel
{
    private readonly TabContext _ctx;
    private readonly List<RushRow> _all = [];
    private readonly bool _ready;

    public RushedTab(TabContext ctx)
    {
        _ctx = ctx;
        var rush = ctx.Report.Rush;
        var prev = $"{ctx.HallShort}{rush.ComparedWith}";
        IsRushed = rush.IsRushed;
        Headline = rush.IsRushed ? $"{Fmt.Plural(rush.Score, "level")} below the {prev} max" : $"Nothing rushed";
        SubLine = rush.IsRushed
            ? $"Missing {Fmt.Percent(rush.Fraction)} of the levels {prev} allows. Walls and Hero Equipment are listed but not counted."
            : $"Every building, trap, hero, troop, spell and pet is at least at the {prev} max.";

        foreach (var r in rush.Rows)
        {
            var level = r.IsUnbuilt ? "Not built" : $"Level {r.Level}";
            _all.Add(new RushRow(
                ctx.Img(r.Item, Math.Max(1, r.Level), 96),
                r.Item.Name,
                r.Count > 1 ? $"×{r.Count}" : "",
                $"{level} · {prev} max {r.PreviousMax}",
                Badge.Rushed(r.BehindEach),
                r.IsScored,
                r.IsUpgrading,
                !r.IsScored ? "not counted" : r.IsUpgrading ? "upgrading" : null,
                r.Item.Category));
        }

        // One chip per type that has something rushed, with how many items and levels.
        var filters = new List<Choice>();
        if (_all.Count > 0)
        {
            filters.Add(new Choice("all", "All", _all.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        foreach (var c in rush.Categories.Where(c => c.Items > 0 && _all.Any(r => r.Category == c.Category)))
        {
            filters.Add(new Choice(c.Category.ToString(), c.Category.DisplayName(ctx.Village), c.Scored ? $"{c.Items} · −{c.Levels}" : $"{c.Items} · not counted"));
        }

        Filters = filters;
        if (rush.WallsBelow > 0)
        {
            WallsText = $"{Fmt.Plural(rush.WallsBelow, "wall")} below the {prev} max (not counted).";
        }

        Filter = Filters.Count > 0 ? Choice.Pick(Filters, ctx.Owner.RememberedView($"{Key}/filter")) : null;
        _ready = true;
        Fill();
    }

    public override string Key => "rushed";

    public override string Title => "Rushed";

    public bool IsRushed { get; }

    public string Headline { get; }

    public string SubLine { get; }

    /// <summary>"All" and each type with rushed items (Defenses, Traps, Heroes, Hero Equipment…).</summary>
    public IReadOnlyList<Choice> Filters { get; }

    public string? WallsText { get; }

    [ObservableProperty]
    private Choice? _filter;

    [ObservableProperty]
    private IReadOnlyList<RushRow> _rows = [];

    partial void OnFilterChanged(Choice? value)
    {
        if (_ready && value is not null)
        {
            _ctx.Owner.RememberView($"{Key}/filter", value.Key);
            Fill();
        }
    }

    private void Fill() =>
        Rows = Filter is { Key: not "all" } f && Enum.TryParse<Category>(f.Key, out var c) ? _all.Where(r => r.Category == c).ToList() : _all;
}
