using System.Text;

namespace ClashOverlay.Core.Data;

/// <summary>Where a loaded data set came from (shown in About and Settings).</summary>
public sealed record DataSourceInfo(string SourceName, string? Commit, DateTimeOffset? CommitDate, DateTimeOffset? DownloadedAt, string? Origin);

public sealed record LeagueDef(int Id, string Name, int TrophyStart);

/// <summary>
/// The static game data: every item keyed by the numeric id the village export uses, per-hall unlock counts and
/// league lists. Built by an <see cref="IGameDataSource"/>.
/// </summary>
public sealed class GameData
{
    public const int TownHallId = 1000001;
    public const int BuilderHallId = 1000034;
    public const int WallId = 1000010;
    public const int BuilderWallId = 1000033;
    public const int BuildersHutId = 1000015;
    public const int BobsHutId = 1000064;
    public const int LaboratoryId = 1000007;
    public const int StarLaboratoryId = 1000046;
    public const int PetHouseId = 1000068;
    public const int HeroHallId = 1000071;
    public const int BlacksmithId = 1000070;
    public const int CraftingStationId = 1000097;
    public const int OttosOutpostId = 1000078;

    private readonly Dictionary<int, ItemDef> _items;
    private readonly Dictionary<(Village, string), ItemDef> _unitsByName;
    private readonly Dictionary<int, int[]> _grossCounts; // item id -> cumulative count per hall level (index 0 = hall 1)
    private readonly Dictionary<int, List<(ItemDef Target, LevelDef Level, MergeSource Source)>> _mergeUses;

    public GameData(IEnumerable<ItemDef> items, IReadOnlyList<LeagueDef> leagueTiers, IReadOnlyList<LeagueDef> builderLeagues,
        IReadOnlyList<LeagueDef> warLeagues, DataSourceInfo source)
    {
        _items = new Dictionary<int, ItemDef>();
        foreach (var item in items)
        {
            _items[item.Id] = item;
        }

        LeagueTiers = leagueTiers;
        BuilderLeagues = builderLeagues;
        WarLeagues = warLeagues;
        Source = source;

        _unitsByName = new Dictionary<(Village, string), ItemDef>();
        foreach (var item in _items.Values.Where(i => i.IsUnit).OrderBy(i => i.IsSeasonal).ThenBy(i => i.Id))
        {
            _unitsByName.TryAdd((item.Village, NameKey(item.Name)), item);
        }

        MaxTownHall = _items.TryGetValue(TownHallId, out var th) ? th.TopLevel : 0;
        MaxBuilderHall = _items.TryGetValue(BuilderHallId, out var bh) ? bh.TopLevel : 0;
        _grossCounts = BuildGrossCounts();

        _mergeUses = new Dictionary<int, List<(ItemDef, LevelDef, MergeSource)>>();
        foreach (var item in _items.Values)
        {
            foreach (var level in item.Levels)
            {
                foreach (var merge in level.MergeSources)
                {
                    if (!_mergeUses.TryGetValue(merge.SourceId, out var list))
                    {
                        _mergeUses[merge.SourceId] = list = [];
                    }

                    list.Add((item, level, merge));
                }
            }
        }
    }

    public IReadOnlyCollection<ItemDef> Items => _items.Values;

    public int MaxTownHall { get; }

    public int MaxBuilderHall { get; }

    public IReadOnlyList<LeagueDef> LeagueTiers { get; }

    public IReadOnlyList<LeagueDef> BuilderLeagues { get; }

    public IReadOnlyList<LeagueDef> WarLeagues { get; }

    public DataSourceInfo Source { get; }

    public ItemDef? Get(int id) => _items.GetValueOrDefault(id);

    public bool Contains(int id) => _items.ContainsKey(id);

    public int MaxHall(Village village) => village == Village.Home ? MaxTownHall : MaxBuilderHall;

    public ItemDef? Hall(Village village) => Get(village == Village.Home ? TownHallId : BuilderHallId);

    /// <summary>Finds a troop, spell, hero, pet or equipment by the name the API uses.</summary>
    public ItemDef? FindUnit(string apiName, Village village) =>
        _unitsByName.GetValueOrDefault((village, NameKey(apiName)));

    /// <summary>How many of a building or trap the hall levels up to <paramref name="hallLevel"/> add (before merges).</summary>
    public int GrossCount(int itemId, int hallLevel)
    {
        if (hallLevel <= 0 || !_grossCounts.TryGetValue(itemId, out var counts) || counts.Length == 0)
        {
            return 0;
        }

        return counts[Math.Min(hallLevel, counts.Length) - 1];
    }

    /// <summary>True when the hall tables list this item at all.</summary>
    public bool HasCountData(int itemId) => _grossCounts.ContainsKey(itemId);

    /// <summary>The merged buildings (and hall levels) that consume <paramref name="sourceId"/>.</summary>
    public IReadOnlyList<(ItemDef Target, LevelDef Level, MergeSource Source)> MergeUsesOf(int sourceId) =>
        _mergeUses.TryGetValue(sourceId, out var list) ? list : [];

    public IEnumerable<ItemDef> ItemsIn(Village village) => _items.Values.Where(i => i.Village == village);

    /// <summary>Normalised name for matching: lower case letters and digits only.</summary>
    public static string NameKey(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
        }

        return sb.ToString();
    }

    private Dictionary<int, int[]> BuildGrossCounts()
    {
        var result = new Dictionary<int, int[]>();
        foreach (var hallId in new[] { TownHallId, BuilderHallId })
        {
            if (!_items.TryGetValue(hallId, out var hall))
            {
                continue;
            }

            var top = hall.TopLevel;
            var running = new Dictionary<int, int>();
            var perLevel = new List<Dictionary<int, int>>();
            foreach (var level in hall.Levels.OrderBy(l => l.Level))
            {
                foreach (var u in level.Unlocks)
                {
                    running[u.ItemId] = running.GetValueOrDefault(u.ItemId) + u.Quantity;
                }

                perLevel.Add(new Dictionary<int, int>(running));
            }

            foreach (var id in running.Keys)
            {
                var arr = new int[top];
                for (var i = 0; i < top && i < perLevel.Count; i++)
                {
                    arr[i] = perLevel[i].GetValueOrDefault(id);
                }

                result[id] = arr;
            }
        }

        return result;
    }
}
