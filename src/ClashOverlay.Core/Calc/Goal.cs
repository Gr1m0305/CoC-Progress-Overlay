using ClashOverlay.Core.Data;

namespace ClashOverlay.Core.Calc;

/// <summary>
/// One thing to max: a building/trap/wall instance (a "slot" the hall allows) or a unit, with the player's level and the
/// level it should reach at the hall level being evaluated.
/// </summary>
public sealed class Goal
{
    public required ItemDef Item { get; init; }

    public required int Level { get; init; }

    public required int Target { get; init; }

    /// <summary>Seconds left on a running upgrade to <c>Level + 1</c>.</summary>
    public long? TimerLeft { get; init; }

    /// <summary>Structures: which copy (0-based, best first). Units: -1.</summary>
    public int SlotIndex { get; init; } = -1;

    /// <summary>A building that is only needed to be merged into another one (e.g. an Archer Tower for a Multi-Archer Tower).</summary>
    public bool IsMergeFeed { get; init; }

    /// <summary>A copy the current hall level added (not there at the previous hall level).</summary>
    public bool IsNewAtHall { get; init; }

    public bool IsUpgrading => TimerLeft is > 0;

    public bool IsUnbuilt => Level == 0 && !IsUpgrading;

    public bool IsMaxed => Level >= Target;

    public int LevelsDone => Math.Min(Level, Target);

    public int LevelsLeft => Math.Max(0, Target - Level);

    /// <summary>Total time from nothing to <see cref="Target"/>.</summary>
    public long TimeToTarget => Item.TimeBetween(0, Target);

    /// <summary>Time still needed: the running upgrade's timer counts instead of its full time.</summary>
    public long TimeLeft
    {
        get
        {
            if (Level >= Target)
            {
                return 0;
            }

            var first = IsUpgrading ? TimerLeft!.Value : Item.TimeToReach(Level + 1);
            return first + Item.TimeBetween(Level + 1, Target);
        }
    }

    public long TimeDone => Math.Max(0, TimeToTarget - TimeLeft);

    /// <summary><see cref="TimeLeft"/> with Gold Pass boosts on the levels not started yet.</summary>
    public long TimeLeftWith(TimeBoosts? boosts)
    {
        if (boosts is null || boosts.IsNone)
        {
            return TimeLeft;
        }

        if (Level >= Target)
        {
            return 0;
        }

        var first = IsUpgrading ? TimerLeft!.Value : boosts.Apply(Item, Item.TimeToReach(Level + 1));
        return first + boosts.Apply(Item, Item.TimeBetween(Level + 1, Target));
    }

    /// <summary>Cost still to pay: a running upgrade is already paid for.</summary>
    public ResourceTotals CostLeft
    {
        get
        {
            var totals = new ResourceTotals();
            for (var l = Level + 1; l <= Target; l++)
            {
                if (l == Level + 1 && IsUpgrading)
                {
                    continue;
                }

                totals.Add(Item.GetLevel(l));
            }

            return totals;
        }
    }

    public override string ToString() => $"{Item.Name} #{SlotIndex} {Level}/{Target}";
}

/// <summary>
/// Amounts per resource. Costs that can be paid with either of two resources (walls: gold or elixir, builder gold or
/// builder elixir) are kept apart in <see cref="GoldOrElixir"/>.
/// </summary>
public sealed class ResourceTotals
{
    private readonly Dictionary<Resource, long> _amounts = new();

    public long GoldOrElixir { get; private set; }

    public long this[Resource r] => _amounts.GetValueOrDefault(r);

    public IEnumerable<KeyValuePair<Resource, long>> NonZero => _amounts.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key);

    public bool IsEmpty => GoldOrElixir == 0 && _amounts.Values.All(v => v == 0);

    public void Add(Resource r, long amount)
    {
        if (amount > 0 && r != Resource.None)
        {
            _amounts[r] = _amounts.GetValueOrDefault(r) + amount;
        }
    }

    public void Add(LevelDef? level)
    {
        if (level is null)
        {
            return;
        }

        foreach (var c in level.Costs)
        {
            if (level.AltResource != Resource.None)
            {
                GoldOrElixir += c.Amount;
            }
            else
            {
                Add(c.Resource, c.Amount);
            }
        }
    }

    public void Add(ResourceTotals other)
    {
        foreach (var kv in other._amounts)
        {
            Add(kv.Key, kv.Value);
        }

        GoldOrElixir += other.GoldOrElixir;
    }
}
