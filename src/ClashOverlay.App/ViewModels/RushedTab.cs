using System.Windows.Media;
using ClashOverlay.Core.Data;
using ClashOverlay.Core.Display;

namespace ClashOverlay.App.ViewModels;

internal sealed record RushRow(ImageSource? Image, string Name, string CountText, string LevelText, Badge Behind, bool IsScored, bool IsUpgrading, string? Note);

internal sealed record RushChip(string Name, string Detail);

/// <summary>Everything below the previous hall's max level, biggest gaps first.</summary>
internal sealed class RushedTab : TabViewModel
{
    public RushedTab(TabContext ctx)
    {
        var rush = ctx.Report.Rush;
        var prev = $"{ctx.HallShort}{rush.ComparedWith}";
        IsRushed = rush.IsRushed;
        Headline = rush.IsRushed ? $"{Fmt.Plural(rush.Score, "level")} below the {prev} max" : $"Nothing rushed";
        SubLine = rush.IsRushed
            ? $"Missing {Fmt.Percent(rush.Fraction)} of the levels {prev} allows. Walls and Hero Equipment are listed but not counted."
            : $"Every building, trap, hero, troop, spell and pet is at least at the {prev} max.";
        foreach (var c in rush.Categories.Where(c => c.Items > 0))
        {
            Chips.Add(new RushChip(c.Category.DisplayName(ctx.Village), c.Scored ? $"{Fmt.Plural(c.Items, "item")} · −{c.Levels}" : $"{c.Items} · not counted"));
        }

        foreach (var r in rush.Rows)
        {
            var level = r.IsUnbuilt ? "Not built" : $"Level {r.Level}";
            Rows.Add(new RushRow(
                ctx.Img(r.Item, Math.Max(1, r.Level), 96),
                r.Item.Name,
                r.Count > 1 ? $"×{r.Count}" : "",
                $"{level} · {prev} max {r.PreviousMax}",
                Badge.Rushed(r.BehindEach),
                r.IsScored,
                r.IsUpgrading,
                !r.IsScored ? "not counted" : r.IsUpgrading ? "upgrading" : null));
        }

        if (rush.WallsBelow > 0)
        {
            WallsText = $"{Fmt.Plural(rush.WallsBelow, "wall")} below the {prev} max (not counted).";
        }
    }

    public override string Key => "rushed";

    public override string Title => "Rushed";

    public bool IsRushed { get; }

    public string Headline { get; }

    public string SubLine { get; }

    public List<RushChip> Chips { get; } = [];

    public List<RushRow> Rows { get; } = [];

    public string? WallsText { get; }
}
