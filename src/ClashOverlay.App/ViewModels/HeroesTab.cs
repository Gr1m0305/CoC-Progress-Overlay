using System.Windows.Media;
using ClashOverlay.Core.Calc;
using ClashOverlay.Core.Data;
using ClashOverlay.Core.Display;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClashOverlay.App.ViewModels;

internal sealed record GearChip(ImageSource? Image, string Name, int Level, bool IsEpic)
{
    public string LevelText => Level > 0 ? Level.ToString(System.Globalization.CultureInfo.InvariantCulture) : "–";

    public bool Dim => Level == 0;
}

/// <summary>A hero or pet: level against the hall's max, the running upgrade, the next level's cost, hand edits.</summary>
internal sealed partial class UnitCard : ObservableObject
{
    private readonly TabContext _ctx;
    private readonly DateTimeOffset? _endsAt;

    public UnitCard(TabContext ctx, Goal goal, ImageSource? image, string? lockedReason)
    {
        _ctx = ctx;
        Item = goal.Item;
        Level = goal.Level;
        Image = image;
        Name = goal.Item.Name;
        LevelText = goal.Level == 0 ? "Not unlocked" : $"Level {goal.Level}";
        MaxText = $"/ {goal.Target}";
        Fraction = goal.Target == 0 ? 1 : goal.LevelsDone / (double)goal.Target;
        IsMaxed = goal.IsMaxed;
        LockedReason = lockedReason;
        var next = goal.Item.GetLevel(goal.Level + 1);
        if (goal.IsUpgrading)
        {
            _endsAt = ctx.Report.State.AsOf.AddSeconds(goal.TimerLeft!.Value);
            _status = $"Upgrading to {goal.Level + 1}";
        }
        else if (goal.IsMaxed)
        {
            _status = goal.Target >= goal.Item.TopLevel ? "Max level" : $"Maxed for {ctx.HallShort}{ctx.Hall}";
        }
        else if (next is not null)
        {
            _status = $"Next: {Fmt.Duration(ctx.Report.Options.Boosts.Apply(goal.Item, next.TimeSeconds))}";
            var cost = new ResourceTotals();
            cost.Add(next);
            NextCost = ctx.Costs(cost);
        }
        else
        {
            _status = "";
        }

        Edits = ctx.Edits(goal.Item.Id);
        CanRaise = goal.Level + 1 <= goal.Item.TopLevel;
        Bar = goal.IsMaxed ? Palette.GreenBar : Palette.BarFor(goal.Item.Category, ctx.Village);
    }

    public ItemDef Item { get; }

    public int Level { get; }

    public ImageSource? Image { get; }

    public string Name { get; }

    public string LevelText { get; }

    public string MaxText { get; }

    public double Fraction { get; }

    public bool IsMaxed { get; }

    public bool NotUnlocked => Level == 0;

    public Brush Bar { get; }

    public string? LockedReason { get; }

    public IReadOnlyList<CostItem> NextCost { get; } = [];

    public List<GearChip> Gear { get; } = [];

    public int Edits { get; }

    public bool CanUndo => Edits > 0;

    public bool CanRaise { get; }

    public string EditedText => Edits == 1 ? "edited" : $"edited ×{Edits}";

    [ObservableProperty]
    private string _status;

    [ObservableProperty]
    private string? _timer;

    public void Tick(DateTimeOffset now)
    {
        if (_endsAt is { } end)
        {
            var left = (long)Math.Ceiling((end - now).TotalSeconds);
            Timer = left <= 0 ? "done" : Fmt.Duration(left);
        }
    }

    [RelayCommand]
    private Task Raise() => _ctx.Owner.RaiseAsync(Item, Level, 1);

    [RelayCommand]
    private Task Undo() => _ctx.Owner.UndoAsync(Item);
}

/// <summary>Heroes with their equipment, and pets.</summary>
internal sealed class HeroesTab : TabViewModel
{
    public HeroesTab(TabContext ctx)
    {
        var r = ctx.Report;
        var locked = r.Upgrades.Builder.Concat(r.Upgrades.PetHouse)
            .Where(u => u.LockedReason is not null)
            .GroupBy(u => u.Item.Id)
            .ToDictionary(g => g.Key, g => g.First().LockedReason);
        var equipment = ctx.Game.ItemsIn(ctx.Village).Where(i => i.Kind == ItemKind.Equipment && i.CountsTowardsProgress).ToList();

        foreach (var g in r.Goals.Where(g => g.Item.Kind == ItemKind.Hero).OrderBy(g => g.Item.FirstHall).ThenBy(g => g.Item.Id))
        {
            var card = new UnitCard(ctx, g, ctx.Images.HeroPortrait(g.Item, 160), locked.GetValueOrDefault(g.Item.Id));
            foreach (var e in equipment.Where(e => string.Equals(e.HeroName, g.Item.Name, StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.FirstHall).ThenBy(e => e.Id))
            {
                card.Gear.Add(new GearChip(ctx.Img(e, 1, 48), e.Name, r.State.UnitLevel(e.Id), e.IsEpic));
            }

            Heroes.Add(card);
        }

        foreach (var g in r.Goals.Where(g => g.Item.Kind == ItemKind.Pet).OrderBy(g => g.Item.FirstHall).ThenBy(g => g.Item.Id))
        {
            Pets.Add(new UnitCard(ctx, g, ctx.Img(g.Item, 1, 96), locked.GetValueOrDefault(g.Item.Id)));
        }

        var p = r.Progress;
        var heroCat = p.For(Category.Heroes);
        var petCat = p.For(Category.Pets);
        Summary = $"Hero Hall {r.State.HeroHallLevel} · heroes {Fmt.Percent(heroCat?.LevelFraction ?? 1)}"
            + (p.LongestHero is { } h ? $" · longest: {h.Name} {Fmt.Duration(p.LongestHeroTime)}" : "");
        PetSummary = $"Pet House {r.State.PetHouseLevel} · pets {Fmt.Percent(petCat?.LevelFraction ?? 1)}"
            + (p.PetHouseTime > 0 ? $" · {Fmt.Duration(p.PetHouseTime)} left" : "");
    }

    public override string Key => "heroes";

    public override string Title => "Heroes & Pets";

    public string Summary { get; }

    public string PetSummary { get; }

    public List<UnitCard> Heroes { get; } = [];

    public List<UnitCard> Pets { get; } = [];

    public override void Tick(DateTimeOffset now)
    {
        foreach (var c in Heroes.Concat(Pets))
        {
            c.Tick(now);
        }
    }
}
