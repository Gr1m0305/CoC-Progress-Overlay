using ClashOverlay.Api.Models;
using ClashOverlay.Core.Data;
using ClashOverlay.Core.State;

namespace ClashOverlay.Api;

/// <summary>Turns the API's player into the levels the merge rule needs (troops, spells, heroes, pets, equipment).</summary>
public static class ApiPlayerMapper
{
    public static ApiPlayerSnapshot ToSnapshot(PlayerDto player, DateTimeOffset fetchedAt)
    {
        var units = new List<ApiUnitLevel>();
        foreach (var u in player.Troops.Concat(player.Spells).Concat(player.Heroes).Concat(player.HeroEquipment))
        {
            if (!string.IsNullOrWhiteSpace(u.Name))
            {
                units.Add(new ApiUnitLevel(u.Name, u.Level, u.MaxLevel, string.IsNullOrEmpty(u.Village) ? "home" : u.Village));
            }
        }

        return new ApiPlayerSnapshot
        {
            FetchedAt = fetchedAt,
            TownHallLevel = player.TownHallLevel,
            BuilderHallLevel = player.BuilderHallLevel,
            Units = units,
        };
    }

    /// <summary>API names the game data does not know (logged so a renamed unit is noticed).</summary>
    public static IReadOnlyList<string> UnknownNames(GameData data, ApiPlayerSnapshot snapshot) =>
        snapshot.Units
            .Where(u => data.FindUnit(u.Name, string.Equals(u.Village, "builderBase", StringComparison.OrdinalIgnoreCase) ? Village.BuilderBase : Village.Home) is null)
            .Select(u => $"{u.Name} ({u.Village})")
            .ToList();
}
