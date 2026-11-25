using System.Windows.Media;
using ClashOverlay.App.Services;
using ClashOverlay.Core.Assets;
using ClashOverlay.Core.Calc;
using ClashOverlay.Core.Data;
using ClashOverlay.Core.Display;
using ClashOverlay.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClashOverlay.App.ViewModels;

/// <summary>What every tab needs to build its rows.</summary>
internal sealed class TabContext(OverlayViewModel owner, AccountData data, VillageReport report, GameData game)
{
    public OverlayViewModel Owner { get; } = owner;

    public AccountData Data { get; } = data;

    public VillageReport Report { get; } = report;

    public GameData Game { get; } = game;

    public Village Village => Report.State.Village;

    public int Hall => Report.State.HallLevel;

    public string HallShort => Village.HallShort();

    public ImageCache Images => Owner.Host.Images;

    public NumberFormat NumberFormat => Owner.Host.Settings.NumberFormat;

    public string N(long value) => Fmt.Number(value, NumberFormat);

    public ImageSource? Img(ItemDef? item, int level, int size = 96) => Images.Item(item, level, size);

    public ImageSource? Res(Resource r) => AssetPaths.ForResource(r) is { } p ? Images.Remote(p, 48) : null;

    public int Edits(int itemId) => Data.EditCounts.GetValueOrDefault(itemId);

    public IReadOnlyList<CostItem> Costs(ResourceTotals totals, Resource altLabel = Resource.Gold)
    {
        var list = totals.NonZero.Select(kv => new CostItem(Res(kv.Key), N(kv.Value), Palette.For(kv.Key), kv.Key.DisplayName())).ToList();
        if (totals.GoldOrElixir > 0)
        {
            var village = Village == Village.Home;
            var (a, b) = village ? (Resource.Gold, Resource.Elixir) : (Resource.BuilderGold, Resource.BuilderElixir);
            list.Add(new CostItem(Res(a), N(totals.GoldOrElixir), Palette.For(a), $"{a.DisplayName()} or {b.DisplayName()} (walls)", Res(b)));
        }

        return list;
    }
}

/// <summary>An amount with its resource icon; <see cref="Icon2"/> for "gold or elixir".</summary>
internal sealed record CostItem(ImageSource? Icon, string Text, Brush Brush, string Tooltip, ImageSource? Icon2 = null);

internal sealed record Badge(string Text, Brush Background, Brush Foreground, string? Tooltip = null)
{
    private static readonly Brush Dark = Frozen(Color.FromRgb(0x14, 0x18, 0x1F));
    private static readonly Brush Soft = Frozen(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Light = Frozen(Color.FromRgb(0xE6, 0xE9, 0xEF));

    public static Badge New(Brush accent) => new("NEW", accent, Dark, "Added at this hall level");

    public static Badge Build() => new("BUILD", Palette.Green, Dark, "Not built yet");

    public static Badge Merge() => new("MERGE", Palette.Orange, Dark, "Needed for a merge");

    public static Badge Locked(string reason) => new(reason, Soft, Light, "Can't start yet");

    public static Badge Edited(int n) => new(n == 1 ? "EDITED" : $"EDITED ×{n}", Palette.BlueSoft, Palette.Blue, "Raised by hand since the last export");

    public static Badge Rushed(int behind) => new($"−{behind}", Palette.Red, Brushes.White, "Levels below the previous hall's max");

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

/// <summary>A running upgrade with a live countdown.</summary>
internal sealed partial class ActiveRow : ObservableObject
{
    private readonly DateTimeOffset _endsAt;

    public ActiveRow(TabContext ctx, ActiveUpgrade a)
    {
        Image = ctx.Img(a.Item, a.ToLevel, 64);
        Name = a.Item.Name;
        LevelText = $"→ {a.ToLevel}";
        Track = a.Track switch
        {
            UpgradeTrack.Laboratory or UpgradeTrack.StarLaboratory => "Lab",
            UpgradeTrack.PetHouse => "Pets",
            UpgradeTrack.Blacksmith => "Smith",
            _ => "Builder",
        };
        Brush = a.Track is UpgradeTrack.Laboratory or UpgradeTrack.StarLaboratory ? Palette.Elixir
            : a.Track == UpgradeTrack.PetHouse ? Palette.Orange
            : ctx.Village == Village.BuilderBase ? Palette.Blue : Palette.Gold;
        _endsAt = ctx.Report.State.AsOf.AddSeconds(a.SecondsLeft);
        Total = Math.Max(1, a.Item.TimeToReach(a.ToLevel));
        _timeLeft = Fmt.Duration(a.SecondsLeft);
    }

    public ImageSource? Image { get; }

    public string Name { get; }

    public string LevelText { get; }

    public string Track { get; }

    public Brush Brush { get; }

    public long Total { get; }

    [ObservableProperty]
    private string _timeLeft;

    [ObservableProperty]
    private double _fraction;

    public void Tick(DateTimeOffset now)
    {
        var left = (long)Math.Ceiling((_endsAt - now).TotalSeconds);
        TimeLeft = left <= 0 ? "Done" : Fmt.Duration(left);
        Fraction = Math.Clamp(1 - left / (double)Total, 0, 1);
    }
}

/// <summary>
/// One line of an upgrade list: current and next level images, levels, time, cost, badges, and the hand-edit buttons
/// ("+1" marks one copy upgraded, "All" every copy at this level, "Undo" the latest edit of this item).
/// </summary>
internal sealed partial class UpgradeRowVm : ObservableObject
{
    private readonly TabContext _ctx;

    public UpgradeRowVm(TabContext ctx, UpgradeRow row, Brush accent)
        : this(ctx, row.Item, row.FromLevel, row.Count, row.Target, row.IsBuild ? 0 : row.Time, row.Cost)
    {
        if (row.IsNewAtHall)
        {
            Badges.Add(Badge.New(accent));
        }

        if (row.IsBuild)
        {
            Badges.Add(Badge.Build());
        }

        if (row.IsMergeFeed)
        {
            Badges.Add(Badge.Merge());
        }

        if (row.LockedReason is { } why)
        {
            Badges.Add(Badge.Locked(why));
            IsLocked = true;
        }

        if (row.IsBuild)
        {
            Time = Fmt.Duration(row.Time);
        }
    }

    public UpgradeRowVm(TabContext ctx, ItemDef item, int from, int count, int target, long time, ResourceTotals cost)
    {
        _ctx = ctx;
        Item = item;
        FromLevel = from;
        Count = count;
        Target = target;
        CurrentImage = ctx.Img(item, Math.Max(1, from), 96);
        NextImage = ctx.Img(item, from + 1, 96);
        Name = item.Name;
        CountText = count > 1 ? $"×{count}" : "";
        LevelText = from == 0 ? $"New → {from + 1}" : $"Lv {from} → {from + 1}";
        TargetText = target > from + 1 ? $"max {target}" : "last level";
        Time = time > 0 ? Fmt.Duration(time) : item.TimeToReach(from + 1) > 0 ? Fmt.Duration(item.TimeToReach(from + 1)) : "instant";
        Cost = ctx.Costs(cost);
        var edits = ctx.Edits(item.Id);
        if (edits > 0)
        {
            Badges.Add(Badge.Edited(edits));
        }

        CanUndo = edits > 0;
        CanRaise = from + 1 <= item.TopLevel;
    }

    public ItemDef Item { get; }

    public int FromLevel { get; }

    public int Count { get; }

    public int Target { get; }

    public ImageSource? CurrentImage { get; }

    public ImageSource? NextImage { get; }

    public string Name { get; }

    public string CountText { get; }

    public string LevelText { get; }

    public string TargetText { get; }

    public string Time { get; }

    public IReadOnlyList<CostItem> Cost { get; }

    public List<Badge> Badges { get; } = [];

    public bool IsLocked { get; }

    public bool CanRaise { get; }

    public bool CanRaiseAll => CanRaise && Count > 1;

    public string RaiseAllText => $"All ×{Count}";

    public bool CanUndo { get; }

    [RelayCommand]
    private Task RaiseOne() => _ctx.Owner.RaiseAsync(Item, FromLevel, 1);

    [RelayCommand]
    private Task RaiseAll() => _ctx.Owner.RaiseAsync(Item, FromLevel, Count);

    [RelayCommand]
    private Task Undo() => _ctx.Owner.UndoAsync(Item);
}

/// <summary>One level in the strip of every level of the selected building.</summary>
internal sealed record LevelCell(ImageSource? Image, int Level, int Owned, bool AboveHall, bool IsHallMax, string Name)
{
    public bool IsOwned => Owned > 0;

    /// <summary>"×4" when several copies are at this level, "max" on the hall's max level.</summary>
    public string Mark => Owned > 1 ? $"×{Owned}" : IsHallMax ? "max" : "";

    public string Tooltip => $"Level {Level}" + (Owned > 0 ? Owned == 1 ? " · you have 1" : $" · you have {Owned}" : "") + (AboveHall ? " · needs a higher hall" : "") + (IsHallMax ? " · max at your hall" : "");
}

internal sealed record NewsItem(ImageSource? Image, string Name, string Detail, bool IsNew);

/// <summary>A labelled number for stat grids.</summary>
internal sealed record Stat(string Label, string Value, ImageSource? Icon = null);
