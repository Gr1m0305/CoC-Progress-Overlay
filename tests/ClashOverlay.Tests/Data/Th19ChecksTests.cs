using ClashOverlay.Core.Data;
using ClashOverlay.Tests.Support;

namespace ClashOverlay.Tests.Data;

/// <summary>
/// Town Hall 19 ("Clash of Titans", 16 Nov 2026) checks against the downloaded data set, confirmed against Supercell's
/// release notes. A data update that breaks any of these fails the build.
/// </summary>
[Collection(GameDataCollection.Name)]
public class Th19ChecksTests(GameDataFixture fixture)
{
    private readonly GameData _data = fixture.Data;

    private ItemDef Home(string name) =>
        _data.Items.Single(i => i.Village == Village.Home && i.Name == name && !i.IsSeasonal);

    [Fact]
    public void Town_Hall_19_is_the_top_level() => Assert.Equal(19, _data.MaxTownHall);

    [Theory]
    [InlineData("Air Defense", 17)]
    [InlineData("Hidden Tesla", 18)]
    [InlineData("X-Bow", 14)]
    [InlineData("Multi-Archer Tower", 5)]
    [InlineData("Multi-Gear Tower", 4)]
    [InlineData("Ricochet Cannon", 5)]
    [InlineData("Builder's Hut", 9)]
    [InlineData("Clan Castle", 15)]
    [InlineData("Laboratory", 17)]
    [InlineData("Hero Hall", 13)]
    [InlineData("Squad Barracks", 12)]
    [InlineData("Gold Storage", 20)]
    [InlineData("Elixir Storage", 20)]
    [InlineData("Dark Elixir Storage", 14)]
    [InlineData("Wall", 20)]
    [InlineData("Air Bomb", 14)]
    [InlineData("Spring Trap", 14)]
    [InlineData("Skeleton Trap", 6)]
    [InlineData("Spiky Tower", 2)]
    [InlineData("Firecracker Tower", 2)]
    [InlineData("Guardian Shrine", 1)]
    public void Building_and_trap_max_at_TH19(string name, int max) => Assert.Equal(max, Home(name).MaxLevelFor(19));

    [Theory]
    [InlineData("Barbarian King", 115)]
    [InlineData("Archer Queen", 115)]
    [InlineData("Minion Prince", 100)]
    [InlineData("Grand Warden", 90)]
    [InlineData("Royal Champion", 60)]
    [InlineData("Dragon Duke", 30)]
    [InlineData("Smasher", 10)]
    [InlineData("Angry Jelly", 15)]
    [InlineData("Flame Flinger", 6)]
    [InlineData("Heroic Spell", 4)]
    public void Unit_max_at_TH19(string name, int max) => Assert.Equal(max, Home(name).MaxLevelFor(19));

    [Theory]
    [InlineData("Spiky Tower")]
    [InlineData("Firecracker Tower")]
    [InlineData("Guardian Shrine")]
    public void New_at_TH19(string name)
    {
        var item = Home(name);
        Assert.Equal(0, item.MaxLevelFor(18));
        Assert.Equal(0, _data.GrossCount(item.Id, 18));
        Assert.True(_data.GrossCount(item.Id, 19) > 0);
    }

    [Theory]
    [InlineData("Spiky Tower", 2)]
    [InlineData("Firecracker Tower", 2)]
    [InlineData("Guardian Shrine", 1)]
    [InlineData("Air Defense", 5)]
    [InlineData("Spring Trap", 10)]
    [InlineData("Wall", 350)]
    public void Count_at_TH19(string name, int count) => Assert.Equal(count, _data.GrossCount(Home(name).Id, 19));

    [Fact]
    public void TH19_adds_one_Air_Defense_one_Spring_Trap_and_25_walls()
    {
        Assert.Equal(1, _data.GrossCount(Home("Air Defense").Id, 19) - _data.GrossCount(Home("Air Defense").Id, 18));
        Assert.Equal(1, _data.GrossCount(Home("Spring Trap").Id, 19) - _data.GrossCount(Home("Spring Trap").Id, 18));
        Assert.Equal(25, _data.GrossCount(GameData.WallId, 19) - _data.GrossCount(GameData.WallId, 18));
    }

    [Fact]
    public void Spiky_Tower_costs_Dark_Elixir() => Assert.Equal(Resource.DarkElixir, Home("Spiky Tower").UpgradeResource);

    [Fact]
    public void Baby_Dragon_Squad_exists_and_is_a_squad()
    {
        var squad = Home("Baby Dragon Squad");
        Assert.True(squad.IsSquad);
        Assert.False(squad.CountsTowardsProgress);
    }

    [Fact]
    public void Guardians_live_in_the_Guardian_Shrine_from_TH19()
    {
        var shrine = Home("Guardian Shrine");
        Assert.Equal(Category.ArmyBuildings, shrine.Category);
        Assert.Equal(3, _data.Items.Count(i => i.Kind == ItemKind.Guardian));
    }

    [Fact]
    public void Super_troops_are_excluded_but_their_caps_come_from_the_data()
    {
        // Sources disagree on several super troop caps after TH19: the data set is the authority, so just check that
        // every super troop has levels and is kept out of the totals.
        var supers = _data.Items.Where(i => i.IsSuperTroop).ToList();
        Assert.NotEmpty(supers);
        Assert.All(supers, s =>
        {
            Assert.False(s.CountsTowardsProgress);
            Assert.True(s.TopLevel > 0);
        });
    }
}
