using System.Collections.ObjectModel;
using System.Windows.Media;
using ClashOverlay.Core.Calc;
using ClashOverlay.Core.Data;
using ClashOverlay.Core.Display;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClashOverlay.App.ViewModels;

internal enum UpgradeList
{
    Builder,
    Laboratory,
}

/// <summary>
/// Builder upgrades or (Star) Laboratory research, longest first, with the level strip of the selected item, the
/// track's running upgrades and the "New at TH19" card.
/// </summary>
internal sealed partial class UpgradesTab : TabViewModel
{
    private readonly TabContext _ctx;
    private readonly UpgradeList _list;

    public UpgradesTab(TabContext ctx, UpgradeList list)
    {
        _ctx = ctx;
        _list = list;
        var r = ctx.Report;
        var home = ctx.Village == Village.Home;
        var accent = home ? Palette.Gold : Palette.Blue;
        IReadOnlyList<UpgradeRow> rows = list == UpgradeList.Builder ? r.Upgrades.Builder : r.Upgrades.Laboratory;
        // What can start now first, then what waits for a higher hall, lab or Pet House; longest first within each.
        foreach (var row in rows.OrderBy(r => r.LockedReason is not null))
        {
            Rows.Add(new UpgradeRowVm(ctx, row, accent));
        }

        UpgradeTrack[] tracks;
        if (list == UpgradeList.Builder)
        {
            AddWalls(ctx);
            tracks = [home ? UpgradeTrack.Builder : UpgradeTrack.BuilderBaseBuilder];
            var busy = r.Upgrades.Active.Count(a => tracks.Contains(a.Track));
            var p = r.Progress;
            Summary = rows.Count == 0
                ? "Every building is maxed for this hall level."
                : $"{Fmt.Plural(rows.Count, "upgrade")} left · {Fmt.Duration(p.BuilderTime)} of work · {Fmt.Plural(p.Builders, "builder")}, {busy} busy";
        }
        else
        {
            tracks = [home ? UpgradeTrack.Laboratory : UpgradeTrack.StarLaboratory];
            var lab = r.State.LabLevel;
            var labName = home ? "Laboratory" : "Star Laboratory";
            Summary = rows.Count == 0
                ? "All research is done for this hall level."
                : $"{labName} {lab} · {Fmt.Plural(rows.Count, "upgrade")} left · {Fmt.Duration(r.Progress.LabTime)} of research";
        }

        foreach (var a in r.Upgrades.Active.Where(a => tracks.Contains(a.Track)))
        {
            Active.Add(new ActiveRow(ctx, a));
        }

        var edits = ctx.Data.EditCounts.Where(kv => ctx.Game.Get(kv.Key)?.Village == ctx.Village).Sum(kv => kv.Value);
        EditsText = edits == 0 ? null : $"{Fmt.Plural(edits, "hand edit")} waiting for the next export or sync";
        BuildNews(ctx, list);
        var keep = ctx.Owner.RememberedSelection(Key);
        SelectedRow = Rows.FirstOrDefault(x => x.Item.Id == keep) ?? Rows.FirstOrDefault();
    }

    public override string Key => _list == UpgradeList.Builder ? "builder" : _ctx.Village == Village.Home ? "lab" : "starlab";

    public override string Title => _list == UpgradeList.Builder ? "Builder upgrades" : _ctx.Village == Village.Home ? "Laboratory" : "Star Laboratory";

    public string Summary { get; }

    /// <summary>"2 hand edits waiting…" in this village, with "Undo last" next to it.</summary>
    public string? EditsText { get; }

    public ObservableCollection<UpgradeRowVm> Rows { get; } = [];

    public List<ActiveRow> Active { get; } = [];

    public string NewsTitle { get; private set; } = "";

    public List<NewsItem> News { get; } = [];

    public string? NewsFooter { get; private set; }

    public bool IsBuilder => _list == UpgradeList.Builder;

    [ObservableProperty]
    private UpgradeRowVm? _selectedRow;

    [ObservableProperty]
    private IReadOnlyList<LevelCell> _strip = [];

    [ObservableProperty]
    private string _stripTitle = "";

    [ObservableProperty]
    private string _stripDetail = "";

    [RelayCommand]
    private Task UndoLatest() => _ctx.Owner.UndoLatestAsync();

    partial void OnSelectedRowChanged(UpgradeRowVm? value)
    {
        if (value is null)
        {
            Strip = [];
            StripTitle = "";
            StripDetail = "";
            return;
        }

        _ctx.Owner.RememberSelection(Key, value.Item.Id);
        var item = value.Item;
        var state = _ctx.Report.State;
        var hallMax = item.MaxLevelFor(_ctx.Hall);
        var owned = new Dictionary<int, int>();
        if (item.IsStructure)
        {
            foreach (var s in state.InstancesOf(item.Id))
            {
                owned[s.Level] = owned.GetValueOrDefault(s.Level) + 1;
            }
        }
        else if (state.UnitLevel(item.Id) is > 0 and var level)
        {
            owned[level] = 1;
        }

        Strip = Enumerable.Range(1, item.TopLevel)
            .Select(l => new LevelCell(_ctx.Img(item, l, 72), l, owned.GetValueOrDefault(l), l > hallMax, l == hallMax, item.Name))
            .ToList();
        StripTitle = item.Name;
        var have = item.IsStructure
            ? string.Join(", ", owned.OrderByDescending(kv => kv.Key).Select(kv => kv.Value > 1 ? $"{kv.Value}× level {kv.Key}" : $"level {kv.Key}"))
            : owned.Count > 0 ? $"level {owned.Keys.First()}" : "not unlocked";
        StripDetail = $"You have {(have.Length == 0 ? "none built" : have)} · max {hallMax} at {_ctx.HallShort}{_ctx.Hall} · {item.TopLevel} in the game";
    }

    private void AddWalls(TabContext ctx)
    {
        var wallId = ctx.Village == Village.Home ? GameData.WallId : GameData.BuilderWallId;
        if (ctx.Game.Get(wallId) is not { } wall)
        {
            return;
        }

        var max = wall.MaxLevelFor(ctx.Hall);
        foreach (var g in ctx.Report.State.InstancesOf(wallId).GroupBy(w => w.Level).Where(g => g.Key < max).OrderBy(g => g.Key))
        {
            var cost = new ResourceTotals();
            cost.Add(wall.GetLevel(g.Key + 1));
            Rows.Add(new UpgradeRowVm(ctx, wall, g.Key, g.Count(), max, 0, cost));
        }
    }

    private void BuildNews(TabContext ctx, UpgradeList list)
    {
        var hall = ctx.Hall;
        NewsTitle = $"NEW AT {ctx.HallShort}{hall}";
        if (hall <= 1)
        {
            return;
        }

        var labTrack = ctx.Village == Village.Home ? UpgradeTrack.Laboratory : UpgradeTrack.StarLaboratory;
        bool Belongs(ItemDef i) => list == UpgradeList.Builder ? i.IsStructure : i.Track == labTrack;
        foreach (var n in ctx.Report.News.Items.Where(n => Belongs(n.Item)))
        {
            var detail = n.IsNewItem ? n.Item.IsStructure && n.Added > 1 ? $"New · {n.Added} to build" : "New"
                : $"+{n.Added} to build";
            News.Add(new NewsItem(ctx.Img(n.Item, 1, 72), n.Item.Name, detail, n.IsNewItem));
        }

        var higher = ctx.Game.ItemsIn(ctx.Village)
            .Where(i => i.CountsTowardsProgress && Belongs(i) && i.MaxLevelFor(hall - 1) > 0 && i.MaxLevelFor(hall) > i.MaxLevelFor(hall - 1))
            .ToList();
        if (higher.Count > 0)
        {
            NewsFooter = $"{Fmt.Plural(higher.Count, "item")} got higher max levels: {string.Join(", ", higher.Take(6).Select(i => $"{i.Name} +{i.MaxLevelFor(hall) - i.MaxLevelFor(hall - 1)}"))}{(higher.Count > 6 ? "…" : "")}";
        }
        else if (News.Count == 0)
        {
            NewsFooter = "Nothing new here at this hall level.";
        }
    }

    public override void Tick(DateTimeOffset now)
    {
        foreach (var a in Active)
        {
            a.Tick(now);
        }
    }
}
