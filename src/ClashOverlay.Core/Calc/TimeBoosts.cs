using ClashOverlay.Core.Data;

namespace ClashOverlay.Core.Calc;

/// <summary>
/// Gold Pass boosts that shorten upgrades: <see cref="Builder"/> percent off the Main Village's builder upgrades
/// (buildings, traps, heroes) and <see cref="Research"/> percent off its Laboratory research. The game does not put
/// them in the village export or the API, so the player sets them. Running upgrades already have their real timers and
/// are not changed; only work that has not started is shortened.
/// </summary>
public sealed record TimeBoosts(int Builder = 0, int Research = 0)
{
    /// <summary>The largest boost accepted; the Gold Pass gives up to 20%.</summary>
    public const int MaxPercent = 50;

    public static readonly TimeBoosts None = new();

    public bool IsNone => Builder <= 0 && Research <= 0;

    /// <summary>The percent taken off <paramref name="track"/>'s upgrades.</summary>
    public int PercentFor(UpgradeTrack track) => track switch
    {
        UpgradeTrack.Builder => Math.Clamp(Builder, 0, MaxPercent),
        UpgradeTrack.Laboratory => Math.Clamp(Research, 0, MaxPercent),
        _ => 0,
    };

    /// <summary><paramref name="seconds"/> of <paramref name="item"/>'s upgrade time with the boost applied, rounded up.</summary>
    public long Apply(ItemDef item, long seconds)
    {
        var percent = PercentFor(item.Track);
        return percent == 0 || seconds <= 0 ? seconds : (long)Math.Ceiling(seconds * (100 - percent) / 100.0);
    }
}
