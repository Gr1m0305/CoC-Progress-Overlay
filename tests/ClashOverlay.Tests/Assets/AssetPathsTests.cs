using ClashOverlay.Core.Assets;
using ClashOverlay.Core.Data;
using ClashOverlay.Tests.Support;
using static ClashOverlay.Tests.Support.TestGameData;

namespace ClashOverlay.Tests.Assets;

public class AssetPathsTests
{
    private readonly GameData _data = Create();

    [Theory]
    [InlineData("P.E.K.K.A", "pekka")]
    [InlineData("L.A.S.S.I", "lassi")]
    [InlineData("B.O.T.O’s Shack", "botos_shack")]
    [InlineData("Builder's Hut", "builder's_hut")]
    [InlineData("O.T.T.O's Outpost", "otto's_outpost")]
    [InlineData("X-Bow", "x-bow")]
    [InlineData("Multi-Archer Tower", "multi-archer_tower")]
    [InlineData("Santa's Surprise", "santa's_surprise")]
    [InlineData("Get those Goblins?", "get_those_goblins")]
    public void Clean(string name, string expected) => Assert.Equal(expected, AssetPaths.Clean(name));

    [Theory]
    [InlineData("Legend League I", "legend_league_1")]
    [InlineData("Legend League III", "legend_league_3")]
    [InlineData("Electro League 33", "electro_league_33")]
    [InlineData("Wood League V", "wood_league_5")]
    [InlineData("Titan League II", "titan_league_2")]
    [InlineData("Diamond League", "diamond_league")]
    [InlineData("Unranked", "unranked")]
    public void League_names(string name, string expected) => Assert.Equal(expected, AssetPaths.CleanLeague(name));

    [Fact]
    public void Paths_per_kind()
    {
        Assert.Equal("buildings/home-village/cannon/level_3.webp", AssetPaths.ForItem(_data.Get(Cannon)!, 3));
        Assert.Equal("buildings/home-village/cannon/level_1.webp", AssetPaths.ForItem(_data.Get(Cannon)!, 0));
        Assert.Equal("traps/home-village/air_bomb/level_2.webp", AssetPaths.ForItem(_data.Get(AirBomb)!, 2));
        Assert.Equal("buildings/home-village/wall/level_2.webp", AssetPaths.ForItem(_data.Get(GameData.WallId)!, 2));
        Assert.Equal("buildings/home-village/town_hall/level_3.webp", AssetPaths.ForItem(_data.Get(GameData.TownHallId)!, 3));
        Assert.Equal("buildings/builder-base/cannon/level_1.webp", AssetPaths.ForItem(_data.Get(BbCannon)!, 1));
        Assert.Equal("troops/barbarian/icon.webp", AssetPaths.ForItem(_data.Get(Barbarian)!, 7));
        Assert.Equal("heroes/barbarian_king/icon.webp", AssetPaths.ForItem(_data.Get(King)!, 1));
        Assert.Equal("heroes/barbarian_king/portrait.webp", AssetPaths.HeroPortrait(_data.Get(King)!));
        Assert.Equal("equipment/giant_gauntlet.webp", AssetPaths.ForItem(_data.Get(EpicGear)!, 1));
        Assert.Equal("resources/dark_elixir.webp", AssetPaths.ForResource(Resource.DarkElixir));
        Assert.Equal("leagues/cwl/champion_league_2.png", AssetPaths.WarLeague("Champion League II"));
        Assert.Equal("leagues/league-tier/electro_league_33.png", AssetPaths.LeagueTier("Electro League 33"));
        Assert.Equal("leagues/builder-base/emerald_league_1.png", AssetPaths.BuilderLeague("Emerald League I"));
        Assert.Equal("player_labels/clan_wars.webp", AssetPaths.PlayerLabel("Clan Wars"));
    }

    [Fact]
    public void Local_copies_are_png()
    {
        Assert.Equal(Path.Combine("spells", "rage_spell.png"), AssetPaths.LocalRelative("spells/rage_spell.webp"));
        Assert.Equal(Path.Combine("leagues", "cwl", "unranked.png"), AssetPaths.LocalRelative("leagues/cwl/unranked.png"));
    }

    [Fact]
    public void Wanted_has_every_building_level_and_unit_icon()
    {
        var wanted = AssetPaths.Wanted(_data);
        Assert.Contains("buildings/home-village/cannon/level_4.webp", wanted);
        Assert.Contains("buildings/home-village/cannon/level_1.webp", wanted);
        Assert.Contains("troops/party_troop/icon.webp", wanted);
        Assert.Contains("heroes/barbarian_king/portrait.webp", wanted);
        Assert.Contains("resources/starry_ore.webp", wanted);
        Assert.Contains("icons/Icon_HV_Trophy.png", wanted);
    }

    [Fact]
    public void Resolver_falls_back_to_the_nearest_lower_level()
    {
        var root = Path.Combine("C:", "assets");
        var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(root, "buildings", "home-village", "cannon", "level_2.png"),
            Path.Combine(root, "troops", "barbarian", "icon.png"),
        };
        var resolver = new AssetResolver(root, have.Contains);

        Assert.EndsWith(Path.Combine("cannon", "level_2.png"), resolver.Item(_data.Get(Cannon)!, 4));
        Assert.EndsWith(Path.Combine("cannon", "level_2.png"), resolver.Item(_data.Get(Cannon)!, 2));
        Assert.Null(resolver.Item(_data.Get(Cannon)!, 1)); // nothing at or below level 1
        Assert.NotNull(resolver.Item(_data.Get(Barbarian)!, 9));
        Assert.Null(resolver.Item(_data.Get(Archer)!, 1));
    }

    [Theory]
    [InlineData("Barbarian King", "BK")]
    [InlineData("P.E.K.K.A", "PE")]
    [InlineData("Wall", "WA")]
    [InlineData("X-Bow", "XB")]
    [InlineData("", "?")]
    public void Initials(string name, string expected) => Assert.Equal(expected, AssetResolver.Initials(name));
}
