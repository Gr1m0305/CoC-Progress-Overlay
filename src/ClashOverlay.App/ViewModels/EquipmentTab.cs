using System.Windows.Media;
using ClashOverlay.Core.Calc;
using ClashOverlay.Core.Data;
using ClashOverlay.Core.Display;
using CommunityToolkit.Mvvm.Input;

namespace ClashOverlay.App.ViewModels;

/// <summary>One piece of equipment: level against the Blacksmith/hall max and the ore still needed.</summary>
internal sealed partial class GearRow
{
    private readonly TabContext _ctx;

    public GearRow(TabContext ctx, Goal goal)
    {
        _ctx = ctx;
        Item = goal.Item;
        Level = goal.Level;
        Image = ctx.Img(goal.Item, 1, 72);
        Name = goal.Item.Name;
        IsEpic = goal.Item.IsEpic;
        LevelText = goal.Level == 0 ? "Not unlocked" : $"{goal.Level} / {goal.Target}";
        Fraction = goal.Target == 0 ? 1 : goal.LevelsDone / (double)goal.Target;
        IsMaxed = goal.IsMaxed;
        OreLeft = goal.IsMaxed ? [] : ctx.Costs(goal.CostLeft);
        Edits = ctx.Edits(goal.Item.Id);
        CanRaise = goal.Level + 1 <= goal.Item.TopLevel && goal.Level > 0;
    }

    public ItemDef Item { get; }

    public int Level { get; }

    public ImageSource? Image { get; }

    public string Name { get; }

    public bool IsEpic { get; }

    public string LevelText { get; }

    public double Fraction { get; }

    public bool IsMaxed { get; }

    public bool NotUnlocked => Level == 0;

    public Brush Bar => IsMaxed ? Palette.GreenBar : IsEpic ? Palette.ElixirBar : Palette.OrangeBar;

    public IReadOnlyList<CostItem> OreLeft { get; }

    public int Edits { get; }

    public bool CanUndo => Edits > 0;

    public bool CanRaise { get; }

    [RelayCommand]
    private Task Raise() => _ctx.Owner.RaiseAsync(Item, Level, 1);

    [RelayCommand]
    private Task Undo() => _ctx.Owner.UndoAsync(Item);
}

internal sealed record GearGroup(string Hero, ImageSource? Portrait, string Detail, List<GearRow> Items);

/// <summary>Hero Equipment by hero, with the ore needed to max everything for this hall level.</summary>
internal sealed class EquipmentTab : TabViewModel
{
    public EquipmentTab(TabContext ctx)
    {
        var r = ctx.Report;
        var heroes = ctx.Game.ItemsIn(ctx.Village).Where(i => i.Kind == ItemKind.Hero).ToDictionary(h => h.Name, StringComparer.OrdinalIgnoreCase);
        var goals = r.Goals.Where(g => g.Item.Kind == ItemKind.Equipment).ToList();
        foreach (var grp in goals.GroupBy(g => g.Item.HeroName ?? "Other")
                     .OrderBy(g => heroes.TryGetValue(g.Key, out var h) ? h.FirstHall : 99)
                     .ThenBy(g => heroes.TryGetValue(g.Key, out var h) ? h.Id : int.MaxValue))
        {
            var rows = grp.OrderBy(g => g.Item.IsEpic).ThenBy(g => g.Item.FirstHall).ThenBy(g => g.Item.Id).Select(g => new GearRow(ctx, g)).ToList();
            var maxed = rows.Count(x => x.IsMaxed);
            heroes.TryGetValue(grp.Key, out var hero);
            Groups.Add(new GearGroup(grp.Key, hero is null ? null : ctx.Images.HeroPortrait(hero, 96), $"{maxed}/{rows.Count} maxed", rows));
        }

        var cat = r.Progress.For(Category.Equipment);
        Ore = cat is null ? [] : ctx.Costs(cat.CostLeft);
        Summary = $"Blacksmith {r.State.BlacksmithLevel} · {cat?.ItemsMaxed ?? 0} of {cat?.ItemsTotal ?? 0} maxed for {ctx.HallShort}{ctx.Hall}";
        OreTitle = Ore.Count == 0 ? "Every piece is maxed for this hall level." : "Ore to max everything:";
    }

    public override string Key => "equipment";

    public override string Title => "Equipment";

    public string Summary { get; }

    public string OreTitle { get; }

    public IReadOnlyList<CostItem> Ore { get; }

    public List<GearGroup> Groups { get; } = [];
}
