using System.Windows.Media;
using ClashOverlay.Core.Calc;
using ClashOverlay.Core.Data;
using ClashOverlay.Core.Display;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClashOverlay.App.ViewModels;

/// <summary>One piece of equipment: level against the Blacksmith/hall max, the next level's ore and the ore still needed.</summary>
internal sealed partial class GearRow
{
    private readonly TabContext _ctx;

    public GearRow(TabContext ctx, Goal goal, string hero, ImageSource? heroImage)
    {
        _ctx = ctx;
        Item = goal.Item;
        Level = goal.Level;
        Image = ctx.Img(goal.Item, 1, 72);
        Name = goal.Item.Name;
        Hero = hero;
        HeroImage = heroImage;
        IsEpic = goal.Item.IsEpic;
        LevelText = goal.Level == 0 ? "Not unlocked" : $"{goal.Level} / {goal.Target}";
        Fraction = goal.Target == 0 ? 1 : goal.LevelsDone / (double)goal.Target;
        IsMaxed = goal.IsMaxed;
        OreLeft = goal.IsMaxed ? [] : ctx.Costs(goal.CostLeft);
        NextTotals = new ResourceTotals();
        if (CanUpgrade)
        {
            NextTotals.Add(goal.Item.GetLevel(goal.Level + 1));
        }

        NextCost = ctx.Costs(NextTotals);
        Edits = ctx.Edits(goal.Item.Id);
        CanRaise = goal.Level + 1 <= goal.Item.TopLevel && goal.Level > 0;
    }

    public ItemDef Item { get; }

    public int Level { get; }

    public ImageSource? Image { get; }

    public string Name { get; }

    public string Hero { get; }

    public ImageSource? HeroImage { get; }

    public bool IsEpic { get; }

    public string LevelText { get; }

    public double Fraction { get; }

    public bool IsMaxed { get; }

    public bool NotUnlocked => Level == 0;

    /// <summary>Unlocked and below this hall's max.</summary>
    public bool CanUpgrade => Level > 0 && !IsMaxed;

    public Brush Bar => IsMaxed ? Palette.GreenBar : IsEpic ? Palette.ElixirBar : Palette.OrangeBar;

    public IReadOnlyList<CostItem> OreLeft { get; }

    /// <summary>Ore for the next level, for sorting and the flat list.</summary>
    public ResourceTotals NextTotals { get; }

    public IReadOnlyList<CostItem> NextCost { get; }

    public int Edits { get; }

    public bool CanUndo => Edits > 0;

    public bool CanRaise { get; }

    [RelayCommand]
    private Task Raise() => _ctx.Owner.RaiseAsync(Item, Level, 1);

    [RelayCommand]
    private Task Undo() => _ctx.Owner.UndoAsync(Item);
}

internal sealed record GearGroup(string Hero, ImageSource? Portrait, string Detail, List<GearRow> Items);

/// <summary>
/// Hero Equipment by hero, with the ore needed to max everything for this hall level. Sorting by the next level's ore
/// (cheapest or priciest first, optionally one ore only) switches to one list of everything that can be upgraded.
/// </summary>
internal sealed partial class EquipmentTab : TabViewModel
{
    private static readonly Resource[] Ores = [Resource.ShinyOre, Resource.GlowyOre, Resource.StarryOre];

    private readonly TabContext _ctx;
    private readonly List<GearRow> _all = [];
    private readonly bool _ready;

    public EquipmentTab(TabContext ctx)
    {
        _ctx = ctx;
        var r = ctx.Report;
        var heroes = ctx.Game.ItemsIn(ctx.Village).Where(i => i.Kind == ItemKind.Hero).ToDictionary(h => h.Name, StringComparer.OrdinalIgnoreCase);
        var goals = r.Goals.Where(g => g.Item.Kind == ItemKind.Equipment).ToList();
        foreach (var grp in goals.GroupBy(g => g.Item.HeroName ?? "Other")
                     .OrderBy(g => heroes.TryGetValue(g.Key, out var h) ? h.FirstHall : 99)
                     .ThenBy(g => heroes.TryGetValue(g.Key, out var h) ? h.Id : int.MaxValue))
        {
            heroes.TryGetValue(grp.Key, out var hero);
            var portrait = hero is null ? null : ctx.Images.HeroPortrait(hero, 96);
            var rows = grp.OrderBy(g => g.Item.IsEpic).ThenBy(g => g.Item.FirstHall).ThenBy(g => g.Item.Id).Select(g => new GearRow(ctx, g, grp.Key, portrait)).ToList();
            var maxed = rows.Count(x => x.IsMaxed);
            Groups.Add(new GearGroup(grp.Key, portrait, $"{maxed}/{rows.Count} maxed", rows));
            _all.AddRange(rows);
        }

        var cat = r.Progress.For(Category.Equipment);
        Ore = cat is null ? [] : ctx.Costs(cat.CostLeft);
        Summary = $"Blacksmith {r.State.BlacksmithLevel} · {cat?.ItemsMaxed ?? 0} of {cat?.ItemsTotal ?? 0} maxed for {ctx.HallShort}{ctx.Hall}";
        OreTitle = Ore.Count == 0 ? "Every piece is maxed for this hall level." : "Ore to max everything:";

        Sorts =
        [
            new("hero", "By hero"),
            new("cheapest", "Cheapest next"),
            new("priciest", "Priciest next"),
        ];
        var used = Ores.Where(o => _all.Any(g => g.CanUpgrade && g.NextTotals[o] > 0)).ToList();
        OreFilters = used.Count > 1 ? [new Choice("all", "All ore"), .. used.Select(o => new Choice(o.ToString(), o.DisplayName(), null, ctx.Res(o)))] : [];
        Sort = Choice.Pick(Sorts, ctx.Owner.RememberedView($"{Key}/sort"));
        OreFilter = OreFilters.Count > 0 ? Choice.Pick(OreFilters, ctx.Owner.RememberedView($"{Key}/ore")) : null;
        _ready = true;
        Fill();
    }

    public override string Key => "equipment";

    public override string Title => "Equipment";

    public string Summary { get; }

    public string OreTitle { get; }

    public IReadOnlyList<CostItem> Ore { get; }

    public List<GearGroup> Groups { get; } = [];

    public IReadOnlyList<Choice> Sorts { get; }

    public IReadOnlyList<Choice> OreFilters { get; }

    public bool IsByHero => Sort?.Key is null or "hero";

    public bool IsSorted => !IsByHero;

    public bool ShowOreFilter => IsSorted && OreFilters.Count > 0;

    public string? FlatEmpty => IsSorted && Flat.Count == 0
        ? OreFilter is { Key: not "all" } f ? $"Nothing left to upgrade with {f.Label}." : "Every piece you have is maxed for this hall level."
        : null;

    [ObservableProperty]
    private Choice? _sort;

    [ObservableProperty]
    private Choice? _oreFilter;

    /// <summary>Everything that can be upgraded, in the chosen order (empty when grouped by hero).</summary>
    [ObservableProperty]
    private IReadOnlyList<GearRow> _flat = [];

    partial void OnSortChanged(Choice? value)
    {
        if (_ready && value is not null)
        {
            _ctx.Owner.RememberView($"{Key}/sort", value.Key);
            Fill();
        }
    }

    partial void OnOreFilterChanged(Choice? value)
    {
        if (_ready && value is not null)
        {
            _ctx.Owner.RememberView($"{Key}/ore", value.Key);
            Fill();
        }
    }

    private void Fill()
    {
        if (IsByHero)
        {
            Flat = [];
        }
        else
        {
            Resource? only = OreFilter is { Key: not "all" } f && Enum.TryParse<Resource>(f.Key, out var o) ? o : null;
            var rows = _all.Where(g => g.CanUpgrade && (only is null || g.NextTotals[only.Value] > 0));
            // One ore: by that ore. All ore: shiny first (every level takes it), then glowy, then starry.
            Resource[] keys = only is { } one ? [one] : Ores;
            IOrderedEnumerable<GearRow>? ordered = null;
            foreach (var key in keys)
            {
                ordered = Sort?.Key == "priciest"
                    ? ordered is null ? rows.OrderByDescending(g => g.NextTotals[key]) : ordered.ThenByDescending(g => g.NextTotals[key])
                    : ordered is null ? rows.OrderBy(g => g.NextTotals[key]) : ordered.ThenBy(g => g.NextTotals[key]);
            }

            Flat = ordered!.ThenBy(g => g.Name, StringComparer.Ordinal).ToList();
        }

        OnPropertyChanged(nameof(IsByHero));
        OnPropertyChanged(nameof(IsSorted));
        OnPropertyChanged(nameof(ShowOreFilter));
        OnPropertyChanged(nameof(FlatEmpty));
    }
}
