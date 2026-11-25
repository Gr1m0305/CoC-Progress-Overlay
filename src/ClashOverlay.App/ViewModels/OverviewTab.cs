using System.Windows.Media;
using ClashOverlay.Core.Calc;
using ClashOverlay.Core.Data;
using ClashOverlay.Core.Display;
using CommunityToolkit.Mvvm.Input;

namespace ClashOverlay.App.ViewModels;

internal sealed record CategoryRow(string Name, double Fraction, string Percent, string Detail, Brush Bar, string Tooltip, bool IsMaxed);

/// <summary>Overall progress for the hall level, per-category bars, time and resources to max, running upgrades.</summary>
internal sealed partial class OverviewTab : TabViewModel
{
    private readonly TabContext _ctx;

    public OverviewTab(TabContext ctx)
    {
        _ctx = ctx;
        var r = ctx.Report;
        var p = r.Progress;
        var mode = r.Options.Mode;
        var home = ctx.Village == Village.Home;

        HallImage = ctx.Img(ctx.Game.Hall(ctx.Village), ctx.Hall, 256);
        HallTitle = $"{ctx.Village.HallName()} {ctx.Hall}";
        IsMaxHall = ctx.Hall >= ctx.Game.MaxHall(ctx.Village);
        HallSub = IsMaxHall ? "Highest level in the game" : $"Max is {ctx.HallShort}{ctx.Game.MaxHall(ctx.Village)}";
        Fraction = p.Fraction(mode);
        Percent = Fmt.Percent(Fraction);
        IsTimeMode = mode == ProgressMode.Time;
        OtherModeText = IsTimeMode ? $"{Fmt.Percent(p.Overall.LevelFraction)} by levels" : $"{Fmt.Percent(p.Overall.TimeFraction)} by upgrade time";
        ItemsText = $"{ctx.N(p.Overall.ItemsMaxed)} of {ctx.N(p.Overall.ItemsTotal)} upgrades maxed";
        Bar = home ? Palette.GoldBar : Palette.BlueBar;

        if (home && ctx.Hall > 1)
        {
            var rush = r.Rush;
            IsRushed = rush.IsRushed;
            RushText = rush.IsRushed
                ? $"Rushed · {Fmt.Plural(rush.Score, "level")} below {ctx.HallShort}{rush.ComparedWith} max"
                : $"Not rushed · everything is at {ctx.HallShort}{rush.ComparedWith} max";
        }

        foreach (var c in p.Categories.Where(c => !c.IsEmpty && c.Category != Category.Walls))
        {
            var f = c.Fraction(mode);
            var left = c.TimeLeft > 0 ? $" · {Fmt.Duration(c.TimeLeft)} left" : "";
            Categories.Add(new CategoryRow(c.Category.DisplayName(ctx.Village), f, Fmt.Percent(f), $"{c.ItemsMaxed}/{c.ItemsTotal} maxed",
                Palette.BarFor(c.Category, ctx.Village), $"{ctx.N(c.LevelsDone)} of {ctx.N(c.LevelsMax)} levels{left}", c.ItemsMaxed == c.ItemsTotal));
        }

        var walls = p.Walls;
        if (walls.Total > 0)
        {
            Categories.Add(new CategoryRow("Walls", walls.Fraction, Fmt.Percent(walls.Fraction), $"{walls.AtMax}/{walls.Total} at {walls.MaxLevel}",
                home ? Palette.GoldBar : Palette.BlueBar,
                string.Join("  ", walls.ByLevel.Take(6).Select(l => $"L{l.Level} ×{l.Count}")), walls.AtMax == walls.Total));
        }

        var builders = p.Builders;
        BuilderTime = p.BuilderTime > 0 ? Fmt.Duration(p.BuilderTimePerBuilder) : "Done";
        var boosts = r.Options.Boosts;
        var builderBoost = home && boosts.Builder > 0 ? $" · {boosts.Builder}% boost" : "";
        BuilderDetail = p.BuilderTime > 0 ? $"{Fmt.Duration(p.BuilderTime)} of work · {Fmt.Plural(builders, "builder")}{builderBoost}" : "Every building is maxed";
        LabTime = p.LabTime > 0 ? Fmt.Duration(p.LabTime) : "Done";
        LabTitle = (home ? "Laboratory" : "Star Laboratory") + (home && boosts.Research > 0 ? $" · {boosts.Research}% boost" : "");
        if (home)
        {
            var petHouseAt = ctx.Game.Get(GameData.PetHouseId)?.MaxLevelFor(ctx.Hall) ?? 0;
            PetTime = p.PetHouseTime > 0 ? Fmt.Duration(p.PetHouseTime) : petHouseAt == 0 ? "Not yet" : "Done";
            HeroText = p.LongestHero is { } hero ? $"{hero.Name}: {Fmt.Duration(p.LongestHeroTime)}" : "All heroes maxed";
        }

        Costs = ctx.Costs(p.TotalCostLeft);
        foreach (var a in r.Upgrades.Active)
        {
            Active.Add(new ActiveRow(ctx, a));
        }

        ActiveTitle = r.Upgrades.Active.Count == 0 ? "NOTHING UPGRADING" : $"IN PROGRESS · {r.Upgrades.Active.Count}";
        ActiveEmpty = r.Upgrades.Active.Count == 0 ? "No builder, laboratory or Pet House was busy in the last export." : null;
    }

    public override string Key => "overview";

    public override string Title => "Overview";

    public ImageSource? HallImage { get; }

    public string HallTitle { get; }

    public string HallSub { get; }

    public bool IsMaxHall { get; }

    public double Fraction { get; }

    public string Percent { get; }

    public bool IsTimeMode { get; }

    public bool IsLevelMode => !IsTimeMode;

    public string OtherModeText { get; }

    public string ItemsText { get; }

    public Brush Bar { get; }

    public bool IsRushed { get; }

    public string? RushText { get; }

    public List<CategoryRow> Categories { get; } = [];

    public string BuilderTime { get; }

    public string BuilderDetail { get; }

    public string LabTitle { get; }

    public string LabTime { get; }

    public string? PetTime { get; }

    public string? HeroText { get; }

    public IReadOnlyList<CostItem> Costs { get; }

    public List<ActiveRow> Active { get; } = [];

    public string ActiveTitle { get; }

    public string? ActiveEmpty { get; }

    [RelayCommand]
    private void SetMode(string mode) => _ctx.Owner.SetProgressMode(mode == "Time" ? ProgressMode.Time : ProgressMode.Levels);

    public override void Tick(DateTimeOffset now)
    {
        foreach (var a in Active)
        {
            a.Tick(now);
        }
    }
}
