using System.Text.Json.Serialization;

namespace ClashOverlay.Api.Models;

// Typed views of the official API's JSON. Only the fields the app uses; everything is nullable or has a default so a
// field Supercell drops or renames never breaks parsing.

public sealed class IconUrls
{
    public string? Tiny { get; init; }

    public string? Small { get; init; }

    public string? Medium { get; init; }

    public string? Large { get; init; }

    [JsonIgnore]
    public string? Best => Large ?? Medium ?? Small ?? Tiny;
}

public sealed class LeagueRef
{
    public int Id { get; init; }

    public string Name { get; init; } = "";

    public IconUrls? IconUrls { get; init; }
}

public sealed class LabelDto
{
    public int Id { get; init; }

    public string Name { get; init; } = "";

    public IconUrls? IconUrls { get; init; }
}

public sealed class ClanRef
{
    public string Tag { get; init; } = "";

    public string Name { get; init; } = "";

    public int ClanLevel { get; init; }

    public IconUrls? BadgeUrls { get; init; }
}

public sealed class AchievementDto
{
    public string Name { get; init; } = "";

    public int Stars { get; init; }

    public long Value { get; init; }

    public long Target { get; init; }

    public string? Info { get; init; }

    public string? CompletionInfo { get; init; }

    public string? Village { get; init; }
}

public sealed class UnitDto
{
    public string Name { get; init; } = "";

    public int Level { get; init; }

    public int MaxLevel { get; init; }

    public string? Village { get; init; }

    public bool SuperTroopIsActive { get; init; }

    /// <summary>On heroes: the equipment worn right now.</summary>
    public List<UnitDto>? Equipment { get; init; }
}

public sealed class PlayerDto
{
    public string Tag { get; init; } = "";

    public string Name { get; init; } = "";

    public int TownHallLevel { get; init; }

    public int? TownHallWeaponLevel { get; init; }

    public int ExpLevel { get; init; }

    public int Trophies { get; init; }

    public int BestTrophies { get; init; }

    public int WarStars { get; init; }

    public int AttackWins { get; init; }

    public int DefenseWins { get; init; }

    public int BuilderHallLevel { get; init; }

    public int BuilderBaseTrophies { get; init; }

    public int BestBuilderBaseTrophies { get; init; }

    public string? Role { get; init; }

    public string? WarPreference { get; init; }

    public int Donations { get; init; }

    public int DonationsReceived { get; init; }

    public long ClanCapitalContributions { get; init; }

    public ClanRef? Clan { get; init; }

    public LeagueRef? LeagueTier { get; init; }

    public LeagueRef? BuilderBaseLeague { get; init; }

    public List<AchievementDto> Achievements { get; init; } = [];

    public List<LabelDto> Labels { get; init; } = [];

    public List<UnitDto> Troops { get; init; } = [];

    public List<UnitDto> Heroes { get; init; } = [];

    public List<UnitDto> HeroEquipment { get; init; } = [];

    public List<UnitDto> Spells { get; init; } = [];
}

public sealed class LocationDto
{
    public int Id { get; init; }

    public string Name { get; init; } = "";

    public bool IsCountry { get; init; }

    public string? CountryCode { get; init; }
}

public sealed class ClanCapitalDto
{
    public int CapitalHallLevel { get; init; }
}

public sealed class ClanDto
{
    public string Tag { get; init; } = "";

    public string Name { get; init; } = "";

    public string? Type { get; init; }

    public string? Description { get; init; }

    public LocationDto? Location { get; init; }

    public IconUrls? BadgeUrls { get; init; }

    public int ClanLevel { get; init; }

    public int ClanPoints { get; init; }

    public int ClanBuilderBasePoints { get; init; }

    public int ClanCapitalPoints { get; init; }

    public LeagueRef? CapitalLeague { get; init; }

    public LeagueRef? WarLeague { get; init; }

    public int RequiredTrophies { get; init; }

    public int RequiredTownhallLevel { get; init; }

    public string? WarFrequency { get; init; }

    public int WarWinStreak { get; init; }

    public int WarWins { get; init; }

    public int? WarTies { get; init; }

    public int? WarLosses { get; init; }

    public bool IsWarLogPublic { get; init; }

    public int Members { get; init; }

    public ClanCapitalDto? ClanCapital { get; init; }
}

public sealed class ClanMemberDto
{
    public string Tag { get; init; } = "";

    public string Name { get; init; } = "";

    public string? Role { get; init; }

    public int TownHallLevel { get; init; }

    public int ExpLevel { get; init; }

    public LeagueRef? LeagueTier { get; init; }

    public int Trophies { get; init; }

    public int BuilderBaseTrophies { get; init; }

    public int ClanRank { get; init; }

    public int PreviousClanRank { get; init; }

    public int Donations { get; init; }

    public int DonationsReceived { get; init; }
}

public sealed class ItemList<T>
{
    public List<T> Items { get; init; } = [];
}

public sealed class WarAttackDto
{
    public string AttackerTag { get; init; } = "";

    public string DefenderTag { get; init; } = "";

    public int Stars { get; init; }

    public double DestructionPercentage { get; init; }

    public int Order { get; init; }

    public int Duration { get; init; }
}

public sealed class WarMemberDto
{
    public string Tag { get; init; } = "";

    public string Name { get; init; } = "";

    public int TownhallLevel { get; init; }

    public int MapPosition { get; init; }

    public int OpponentAttacks { get; init; }

    public List<WarAttackDto> Attacks { get; init; } = [];
}

public sealed class WarClanDto
{
    public string Tag { get; init; } = "";

    public string Name { get; init; } = "";

    public IconUrls? BadgeUrls { get; init; }

    public int ClanLevel { get; init; }

    public int Attacks { get; init; }

    public int Stars { get; init; }

    public double DestructionPercentage { get; init; }

    public int? ExpEarned { get; init; }

    public List<WarMemberDto> Members { get; init; } = [];
}

/// <summary><c>/clans/{tag}/currentwar</c> and <c>/clanwarleagues/wars/{warTag}</c>.</summary>
public sealed class WarDto
{
    /// <summary><c>notInWar</c>, <c>preparation</c>, <c>inWar</c> or <c>warEnded</c>.</summary>
    public string State { get; init; } = "notInWar";

    public int TeamSize { get; init; }

    public int AttacksPerMember { get; init; }

    public string? BattleModifier { get; init; }

    public DateTimeOffset? PreparationStartTime { get; init; }

    public DateTimeOffset? StartTime { get; init; }

    public DateTimeOffset? EndTime { get; init; }

    public DateTimeOffset? WarStartTime { get; init; }

    public WarClanDto? Clan { get; init; }

    public WarClanDto? Opponent { get; init; }

    [JsonIgnore]
    public bool IsEnded => State == "warEnded";
}

public sealed class WarLogEntryDto
{
    /// <summary><c>win</c>, <c>lose</c>, <c>tie</c>; null for CWL entries.</summary>
    public string? Result { get; init; }

    public DateTimeOffset? EndTime { get; init; }

    public int TeamSize { get; init; }

    public int AttacksPerMember { get; init; }

    public WarClanDto? Clan { get; init; }

    public WarClanDto? Opponent { get; init; }
}

public sealed class LeagueGroupMemberDto
{
    public string Tag { get; init; } = "";

    public string Name { get; init; } = "";

    public int TownHallLevel { get; init; }
}

public sealed class LeagueGroupClanDto
{
    public string Tag { get; init; } = "";

    public string Name { get; init; } = "";

    public int ClanLevel { get; init; }

    public IconUrls? BadgeUrls { get; init; }

    public List<LeagueGroupMemberDto> Members { get; init; } = [];
}

public sealed class LeagueRoundDto
{
    /// <summary><c>#0</c> while the round has not been drawn.</summary>
    public List<string> WarTags { get; init; } = [];
}

/// <summary><c>/clans/{tag}/currentwar/leaguegroup</c>.</summary>
public sealed class LeagueGroupDto
{
    /// <summary><c>preparation</c>, <c>inWar</c> or <c>ended</c>.</summary>
    public string State { get; init; } = "";

    public string Season { get; init; } = "";

    public List<LeagueGroupClanDto> Clans { get; init; } = [];

    public List<LeagueRoundDto> Rounds { get; init; } = [];
}

public sealed class RaidMemberDto
{
    public string Tag { get; init; } = "";

    public string Name { get; init; } = "";

    public int Attacks { get; init; }

    public int AttackLimit { get; init; }

    public int BonusAttackLimit { get; init; }

    public long CapitalResourcesLooted { get; init; }
}

public sealed class RaidSeasonDto
{
    /// <summary><c>ongoing</c> or <c>ended</c>.</summary>
    public string State { get; init; } = "";

    public DateTimeOffset? StartTime { get; init; }

    public DateTimeOffset? EndTime { get; init; }

    public long CapitalTotalLoot { get; init; }

    public int RaidsCompleted { get; init; }

    public int TotalAttacks { get; init; }

    public int EnemyDistrictsDestroyed { get; init; }

    public int OffensiveReward { get; init; }

    public int DefensiveReward { get; init; }

    public List<RaidMemberDto> Members { get; init; } = [];
}

public sealed class GoldPassDto
{
    public DateTimeOffset? StartTime { get; init; }

    public DateTimeOffset? EndTime { get; init; }
}

public sealed class ApiErrorDto
{
    public string? Reason { get; init; }

    public string? Message { get; init; }
}
