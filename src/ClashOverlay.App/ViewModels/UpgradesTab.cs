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
/// Builder upgrades or (Star) Laboratory research with a sort (longest, shortest, cheapest, priciest) and a resource
/// filter, the level strip of the selected item, the track's running upgrades and the "New at TH19" card.
/// </summary>
internal sealed partial class UpgradesTab : TabViewModel
{
    /// <summary>The level strip shows at most this many levels (two rows), around the levels the player has.</summary>
    internal const int StripCells = 32;

    private readonly TabContext _ctx;
    private readonly UpgradeList _list;
    private readonly List<UpgradeRowVm> _all = [];
    private readonly (Resource A, Resource B) _wallPair;
    private readonly bool _ready;

    public UpgradesTab(TabContext ctx, UpgradeList list)
    {
        _ctx = ctx;
        _list = list;
        var r = ctx.Report;
        var home = ctx.Village == Village.Home;
        _wallPair = home ? (Resource.Gold, Resource.Elixir) : (Resource.BuilderGold, Resource.BuilderElixir);
        var accent = home ? Palette.Gold : Palette.Blue;
        IReadOnlyList<UpgradeRow> rows = list == UpgradeList.Builder ? r.Upgrades.Builder : r.Upgrades.Laboratory;
        foreach (var row in rows)
        {
            _all.Add(new UpgradeRowVm(ctx, row, accent));
        }

        UpgradeTrack[] tracks;
        var boost = r.Options.Boosts;
        if (list == UpgradeList.Builder)
        {
            AddWalls(ctx);
            tracks = [home ? UpgradeTrack.Builder : UpgradeTrack.BuilderBaseBuilder];
            var busy = r.Upgrades.Active.Count(a => tracks.Contains(a.Track));
            var p = r.Progress;
            var boostText = home && boost.Builder > 0 ? $" · {boost.Builder}% builder boost" : "";
            Summary = rows.Count == 0
                ? "Every building is maxed for this hall level."
                : $"{Fmt.Plural(rows.Count, "upgrade")} left · {Fmt.Duration(p.BuilderTime)} of work · {Fmt.Plural(p.Builders, "builder")}, {busy} busy{boostText}";
        }
        else
        {
            tracks = [home ? UpgradeTrack.Laboratory : UpgradeTrack.StarLaboratory];
            var lab = r.State.LabLevel;
            var labName = home ? "Laboratory" : "Star Laboratory";
            var boostText = home && boost.Research > 0 ? $" · {boost.Research}% research boost" : "";
            Summary = rows.Count == 0
                ? "All research is done for this hall level."
                : $"{labName} {lab} · {Fmt.Plural(rows.Count, "upgrade")} left · {Fmt.Duration(r.Progress.LabTime)} of research{boostText}";
        }

        foreach (var a in r.Upgrades.Active.Where(a => tracks.Contains(a.Track)))
        {
            Active.Add(new ActiveRow(ctx, a));
        }

        var edits = ctx.Data.EditCounts.Where(kv => ctx.Game.Get(kv.Key)?.Village == ctx.Village).Sum(kv => kv.Value);
        EditsText = edits == 0 ? null : $"{Fmt.Plural(edits, "hand edit")} waiting for the next export or sync";
        BuildNews(ctx, list);

        Sorts =
        [
            new("longest", "Longest"),
            new("shortest", "Shortest"),
            new("cheapest", "Cheapest"),
            new("priciest", "Priciest"),
        ];
        var resources = _all.SelectMany(ResourcesOf).Distinct().Order().ToList();
        ResourceFilters = resources.Count > 1
            ? [new Choice("all", "All"), .. resources.Select(x => new Choice(x.ToString(), x.DisplayName(), null, ctx.Res(x)))]
            : [];
        Sort = Choice.Pick(Sorts, ctx.Owner.RememberedView($"{Key}/sort"));
        ResourceFilter = ResourceFilters.Count > 0 ? Choice.Pick(ResourceFilters, ctx.Owner.RememberedView($"{Key}/resource")) : null;
        _ready = true;
        Fill(ctx.Owner.RememberedSelection(Key));
    }

    public override string Key => _list == UpgradeList.Builder ? "builder" : _ctx.Village == Village.Home ? "lab" : "starlab";

    public override string Title => _list == UpgradeList.Builder ? "Builder upgrades" : _ctx.Village == Village.Home ? "Laboratory" : "Star Laboratory";

    public string Summary { get; }

    /// <summary>"2 hand edits waiting…" in this village, with "Undo last" next to it.</summary>
    public string? EditsText { get; }

    public List<ActiveRow> Active { get; } = [];

    public string NewsTitle { get; private set; } = "";

    public List<NewsItem> News { get; } = [];

    public string? NewsFooter { get; private set; }

    public bool IsBuilder => _list == UpgradeList.Builder;

    public IReadOnlyList<Choice> Sorts { get; }

    /// <summary>"All" and each resource the list costs; empty when everything costs the same resource.</summary>
    public IReadOnlyList<Choice> ResourceFilters { get; }

    public bool HasResourceFilter => ResourceFilters.Count > 0;

    /// <summary>Shown when the resource filter leaves nothing.</summary>
    public string? EmptyText => Rows.Count == 0 && _all.Count > 0 ? $"Nothing here costs {ResourceFilter?.Label}." : null;

    [ObservableProperty]
    private IReadOnlyList<UpgradeRowVm> _rows = [];

    [ObservableProperty]
    private Choice? _sort;

    [ObservableProperty]
    private Choice? _resourceFilter;

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

    partial void OnSortChanged(Choice? value)
    {
        if (_ready && value is not null)
        {
            _ctx.Owner.RememberView($"{Key}/sort", value.Key);
            Fill(SelectedRow?.Item.Id ?? -1);
        }
    }

    partial void OnResourceFilterChanged(Choice? value)
    {
        if (_ready && value is not null)
        {
            _ctx.Owner.RememberView($"{Key}/resource", value.Key);
            Fill(SelectedRow?.Item.Id ?? -1);
        }
    }

    /// <summary>Applies the filter and sort; what can start now comes before what waits for a higher hall or lab.</summary>
    private void Fill(int keepItem)
    {
        Resource? only = ResourceFilter is { Key: not "all" } f && Enum.TryParse<Resource>(f.Key, out var r) ? r : null;
        var rows = only is { } res ? _all.Where(x => ResourcesOf(x).Contains(res)) : _all;
        var available = rows.OrderBy(x => x.IsLocked);
        var sorted = Sort?.Key switch
        {
            "shortest" => available.ThenBy(x => x.Seconds),
            "cheapest" => available.ThenBy(x => only is null ? MainResource(x) : Resource.None).ThenBy(x => Amount(x, only)),
            "priciest" => available.ThenBy(x => only is null ? MainResource(x) : Resource.None).ThenByDescending(x => Amount(x, only)),
            _ => available.ThenByDescending(x => x.Seconds),
        };
        Rows = sorted.ThenBy(x => x.Name, StringComparer.Ordinal).ThenBy(x => x.FromLevel).ToList();
        OnPropertyChanged(nameof(EmptyText));
        SelectedRow = Rows.FirstOrDefault(x => x.Item.Id == keepItem) ?? Rows.FirstOrDefault();
    }

    /// <summary>The resources one copy costs; walls count for both resources they take.</summary>
    private IEnumerable<Resource> ResourcesOf(UpgradeRowVm row)
    {
        foreach (var kv in row.CostTotals.NonZero)
        {
            yield return kv.Key;
        }

        if (row.CostTotals.GoldOrElixir > 0)
        {
            yield return _wallPair.A;
            yield return _wallPair.B;
        }
    }

    private Resource MainResource(UpgradeRowVm row) =>
        row.CostTotals.NonZero.Select(kv => kv.Key).FirstOrDefault(row.CostTotals.GoldOrElixir > 0 ? _wallPair.A : Resource.None);

    /// <summary>What one copy costs in <paramref name="only"/>, or in its main resource.</summary>
    private long Amount(UpgradeRowVm row, Resource? only)
    {
        var r = only ?? MainResource(row);
        var either = r == _wallPair.A || r == _wallPair.B ? row.CostTotals.GoldOrElixir : 0;
        return row.CostTotals[r] + either;
    }

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

        var (first, last) = StripWindow(item.TopLevel, owned.Count > 0 ? owned.Keys.Min() : 0, hallMax);
        Strip = Enumerable.Range(first, last - first + 1)
            .Select(l => new LevelCell(_ctx.Img(item, l, 72), l, owned.GetValueOrDefault(l), l > hallMax, l == hallMax, item.Name))
            .ToList();
        StripTitle = item.Name;
        var have = item.IsStructure
            ? string.Join(", ", owned.OrderByDescending(kv => kv.Key).Select(kv => kv.Value > 1 ? $"{kv.Value}× level {kv.Key}" : $"level {kv.Key}"))
            : owned.Count > 0 ? $"level {owned.Keys.First()}" : "not unlocked";
        var shown = first > 1 || last < item.TopLevel ? $" · showing levels {first}–{last}" : "";
        StripDetail = $"You have {(have.Length == 0 ? "none built" : have)} · max {hallMax} at {_ctx.HallShort}{_ctx.Hall} · {item.TopLevel} in the game{shown}";
    }

    /// <summary>
    /// Which levels the strip shows. Items with many levels (heroes have over 90) get a window that starts just below
    /// the lowest level the player has and reaches the hall's max where it fits.
    /// </summary>
    internal static (int First, int Last) StripWindow(int topLevel, int lowestOwned, int hallMax)
    {
        if (topLevel <= StripCells)
        {
            return (1, Math.Max(1, topLevel));
        }

        var first = Math.Min(Math.Max(1, lowestOwned - 2), Math.Max(1, hallMax - StripCells + 3));
        first = Math.Clamp(first, 1, topLevel - StripCells + 1);
        return (first, first + StripCells - 1);
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
            _all.Add(new UpgradeRowVm(ctx, wall, g.Key, g.Count(), max, 0, cost));
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
