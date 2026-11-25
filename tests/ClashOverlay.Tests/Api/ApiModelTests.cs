using ClashOverlay.Api;
using ClashOverlay.Api.Models;
using ClashOverlay.Core.Data;
using ClashOverlay.Core.Export;
using ClashOverlay.Core.State;
using ClashOverlay.Tests.Support;

namespace ClashOverlay.Tests.Api;

/// <summary>The hand-made API fixtures parse into the models, and the player merges with the export on the real data.</summary>
[Collection(GameDataCollection.Name)]
public class ApiModelTests(GameDataFixture fixture)
{
    private static T Read<T>(string file) => ApiJson.Deserialize<T>(Fixtures.Read("api", file))!;

    [Theory]
    [InlineData("20261125T080000.000Z", 2026, 11, 25, 8)]
    [InlineData("20261125T080000Z", 2026, 11, 25, 8)]
    [InlineData("2026-11-25T08:00:00Z", 2026, 11, 25, 8)]
    public void Api_times(string text, int y, int m, int d, int h) =>
        Assert.Equal(new DateTimeOffset(y, m, d, h, 0, 0, TimeSpan.Zero), ApiJson.ParseTime(text));

    [Fact]
    public void Player()
    {
        var p = Read<PlayerDto>("demo_player.json");
        Assert.Equal("#2PQ8LV9CY", p.Tag);
        Assert.Equal(19, p.TownHallLevel);
        Assert.Equal("Electro League 33", p.LeagueTier!.Name);
        Assert.Equal("Demo Clan", p.Clan!.Name);
        Assert.NotEmpty(p.Achievements);
        Assert.Contains(p.Heroes, h => h.Name == "Barbarian King" && h.Equipment!.Count == 2);
        Assert.Contains(p.Troops, t => t.SuperTroopIsActive);
        Assert.Contains(p.Troops, t => t.Village == "builderBase");
    }

    [Fact]
    public void War_and_friends()
    {
        var war = Read<WarDto>("demo_currentwar.json");
        Assert.Equal("inWar", war.State);
        Assert.Equal(15, war.TeamSize);
        Assert.Equal(new DateTimeOffset(2026, 11, 25, 18, 0, 0, TimeSpan.Zero), war.EndTime);
        Assert.Equal(15, war.Clan!.Members.Count);
        Assert.Single(war.Clan.Members.Single(m => m.Tag == "#2PQ8LV9CY").Attacks);

        Assert.Equal(5, Read<ItemList<WarLogEntryDto>>("demo_warlog.json").Items.Count);
        var group = Read<LeagueGroupDto>("demo_leaguegroup.json");
        Assert.Equal(8, group.Clans.Count);
        Assert.Equal(7, group.Rounds.Count);
        var wars = Read<Dictionary<string, WarDto>>("demo_cwlwars.json");
        Assert.Equal(28, wars.Count);
        Assert.All(group.Rounds.SelectMany(r => r.WarTags), t => Assert.True(wars.ContainsKey(t)));
        Assert.Equal(7, wars.Values.Count(w => w.Clan!.Tag == "#2YQJ0PLCR" || w.Opponent!.Tag == "#2YQJ0PLCR"));

        var raid = Assert.Single(Read<ItemList<RaidSeasonDto>>("demo_raids.json").Items);
        Assert.Equal("ended", raid.State);
        Assert.Contains(raid.Members, m => m.Tag == "#2PQ8LV9CY");
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.Zero), Read<GoldPassDto>("demo_goldpass.json").EndTime);
        Assert.Equal(20, Read<ItemList<ClanMemberDto>>("demo_members.json").Items.Count);
        Assert.Equal("Champion League II", Read<ClanDto>("demo_clan.json").WarLeague!.Name);
    }

    [Fact]
    public void Every_api_name_is_known_to_the_game_data()
    {
        var snapshot = ApiPlayerMapper.ToSnapshot(Read<PlayerDto>("demo_player.json"), DateTimeOffset.UnixEpoch);
        Assert.Empty(ApiPlayerMapper.UnknownNames(fixture.Data, snapshot));
        Assert.Contains(snapshot.Units, u => u.Name == "Battle Machine" && u.Village == "builderBase");
    }

    [Fact]
    public void Newer_api_levels_win_over_the_export()
    {
        var export = ExportParser.Parse(Fixtures.Read("exports", "demo_th19.json"));
        var player = Read<PlayerDto>("demo_player.json");
        var archer = player.Troops.First(t => t.Name == "Archer" && t.Village == "home");
        var troops = player.Troops.Select(t => t == archer ? new UnitDto { Name = t.Name, Level = t.Level + 1, MaxLevel = t.MaxLevel, Village = t.Village } : t).ToList();
        var newer = new PlayerDto { Tag = player.Tag, TownHallLevel = 19, BuilderHallLevel = 10, Troops = troops, Heroes = player.Heroes, Spells = player.Spells, HeroEquipment = player.HeroEquipment };
        var at = export.Timestamp!.Value.AddHours(2);

        var state = VillageStateBuilder.Build(fixture.Data, export, ApiPlayerMapper.ToSnapshot(newer, at), at).Home;
        var archerId = fixture.Data.FindUnit("Archer", Village.Home)!.Id;
        var dragonRider = fixture.Data.FindUnit("Dragon Rider", Village.Home)!.Id;

        Assert.Equal(archer.Level + 1, state.UnitLevel(archerId));
        Assert.Equal(LevelSource.Api, state.Units[archerId].Source);
        Assert.True(state.Units[dragonRider].IsUpgrading); // same level in both: the export's lab timer stays
        Assert.DoesNotContain(state.Units.Values, u => u.Item.IsSuperTroop && u.Item.CountsTowardsProgress);
    }

    [Fact]
    public void Older_api_does_not_undo_the_export()
    {
        var export = ExportParser.Parse(Fixtures.Read("exports", "demo_th19.json"));
        var player = Read<PlayerDto>("demo_player.json");
        var older = new PlayerDto
        {
            Tag = player.Tag,
            TownHallLevel = 19,
            Troops = player.Troops.Select(t => new UnitDto { Name = t.Name, Level = Math.Max(1, t.Level - 1), MaxLevel = t.MaxLevel, Village = t.Village }).ToList(),
        };

        var withApi = VillageStateBuilder.Build(fixture.Data, export, ApiPlayerMapper.ToSnapshot(older, export.Timestamp!.Value.AddDays(-1)), export.Timestamp.Value).Home;
        var exportOnly = VillageStateBuilder.Build(fixture.Data, export, null, export.Timestamp.Value).Home;

        Assert.All(exportOnly.Units, kv => Assert.Equal(kv.Value.Level, withApi.UnitLevel(kv.Key)));
    }
}
