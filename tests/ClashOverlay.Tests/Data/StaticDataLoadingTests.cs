using ClashOverlay.Core.Data;
using ClashOverlay.Core.Export;
using ClashOverlay.Tests.Support;

namespace ClashOverlay.Tests.Data;

[Collection(GameDataCollection.Name)]
public class StaticDataLoadingTests(GameDataFixture fixture)
{
    private readonly GameData _data = fixture.Data;

    private ItemDef Home(string name) => _data.Items.Single(i => i.Village == Village.Home && i.Name == name && !i.IsSeasonal);

    [Fact]
    public void Loads_every_group()
    {
        foreach (var kind in Enum.GetValues<ItemKind>())
        {
            Assert.Contains(_data.Items, i => i.Kind == kind);
        }

        Assert.Equal(10, _data.MaxBuilderHall);
        Assert.NotEmpty(_data.LeagueTiers);
        Assert.NotEmpty(_data.BuilderLeagues);
        Assert.NotEmpty(_data.WarLeagues);
    }

    [Fact]
    public void Manifest_names_the_source()
    {
        var manifest = StaticDataManifest.TryRead(fixture.DataDir);
        Assert.NotNull(manifest);
        Assert.Equal(ClashKingDataSource.RequiredFiles.Count, manifest!.Files.Count);
        Assert.All(manifest.Files, f => Assert.Equal(64, f.Sha256.Length));
        Assert.True(manifest.Commit is not null || manifest.Origin == "cdn");
    }

    /// <summary>
    /// Unit levels are normalised to "cost/time to reach level N". In game, Barbarian level 11 → 12 costs 6,000,000
    /// Elixir and takes 3 days since the Town Hall 19 update.
    /// </summary>
    [Fact]
    public void Unit_upgrade_time_is_the_time_to_reach_the_next_level()
    {
        var barbarian = Home("Barbarian");
        var twelve = barbarian.GetLevel(12)!;

        Assert.Equal(6_000_000, twelve.CostOf(Resource.Elixir));
        Assert.Equal(3 * 86400, twelve.TimeSeconds);
        Assert.Equal(14, twelve.RequiredLab);
        Assert.Equal(16, twelve.RequiredHall);

        // Level 1 is free (it comes with the Barracks).
        Assert.Empty(barbarian.GetLevel(1)!.Costs);
        Assert.Equal(0, barbarian.GetLevel(1)!.TimeSeconds);
        // Level 14 needs Lab 17 and Town Hall 19.
        Assert.Equal(17, barbarian.GetLevel(14)!.RequiredLab);
        Assert.Equal(19, barbarian.GetLevel(14)!.RequiredHall);
    }

    [Fact]
    public void Building_build_cost_is_the_cost_to_reach_that_level()
    {
        // Wall 20 is 10,000,000 gold or elixir, no time.
        var wall20 = Home("Wall").GetLevel(20)!;
        Assert.Equal(10_000_000, wall20.CostOf(Resource.Gold));
        Assert.Equal(Resource.Elixir, wall20.AltResource);
        Assert.Equal(0, wall20.TimeSeconds);

        // Cannon 21: 3,000,000 gold, 1 day 12 hours, Town Hall 15.
        var cannon21 = Home("Cannon").GetLevel(21)!;
        Assert.Equal(3_000_000, cannon21.CostOf(Resource.Gold));
        Assert.Equal(36 * 3600, cannon21.TimeSeconds);
        Assert.Equal(15, cannon21.RequiredHall);
    }

    [Fact]
    public void Equipment_costs_ore()
    {
        var puppet = Home("Barbarian Puppet");
        Assert.Equal(120, puppet.GetLevel(2)!.CostOf(Resource.ShinyOre));
        Assert.Equal(20, puppet.GetLevel(3)!.CostOf(Resource.GlowyOre));
        Assert.Equal(18, puppet.TopLevel);
        Assert.Equal(27, Home("Giant Gauntlet").TopLevel);
        Assert.True(Home("Giant Gauntlet").IsEpic);
        Assert.Equal("Barbarian King", puppet.HeroName);
    }

    [Fact]
    public void Firecracker_Tower_levels_without_a_hall_get_TH19()
    {
        var tower = Home("Firecracker Tower");
        Assert.All(tower.Levels, l => Assert.Equal(19, l.RequiredHall));
    }

    [Fact]
    public void Merges_are_read_with_their_sources()
    {
        var mat = Home("Multi-Archer Tower").GetLevel(1)!;
        var src = Assert.Single(mat.MergeSources);
        Assert.Equal(Home("Archer Tower").Id, src.SourceId);
        Assert.Equal(2, src.Count);
        Assert.Equal(21, src.Level);

        var mgt = Home("Multi-Gear Tower").GetLevel(1)!;
        Assert.All(mgt.MergeSources, s => Assert.True(s.GearedUp));

        var th17 = _data.Get(GameData.TownHallId)!.GetLevel(17)!;
        Assert.Equal(Home("Eagle Artillery").Id, Assert.Single(th17.MergeSources).SourceId);
    }

    [Fact]
    public void Excluded_items_do_not_count()
    {
        Assert.False(_data.Get(GameData.TownHallId)!.CountsTowardsProgress);
        Assert.False(_data.Get(GameData.CraftingStationId)!.CountsTowardsProgress);
        Assert.False(_data.Get(GameData.BobsHutId)!.CountsTowardsProgress);
        Assert.All(_data.Items.Where(i => i.IsSeasonal), i => Assert.False(i.CountsTowardsProgress));
        Assert.All(_data.Items.Where(i => i.Kind == ItemKind.Helper), i => Assert.False(i.CountsTowardsProgress));
        Assert.True(Home("Cannon").CountsTowardsProgress);
    }

    [Fact]
    public void Categories_and_tracks()
    {
        Assert.Equal(Category.SiegeMachines, Home("Wall Wrecker").Category);
        Assert.Equal(UpgradeTrack.Laboratory, Home("Wall Wrecker").Track);
        Assert.Equal(UpgradeTrack.Builder, Home("Barbarian King").Track);
        Assert.Equal(UpgradeTrack.PetHouse, Home("L.A.S.S.I").Track);
        Assert.Equal(Category.Defenses, Home("Builder's Hut").Category);
        var bm = _data.Items.Single(i => i.Kind == ItemKind.Hero && i.Name == "Battle Machine");
        Assert.Equal(Village.BuilderBase, bm.Village);
        Assert.Equal(UpgradeTrack.BuilderBaseBuilder, bm.Track);
        var raged = _data.Items.Single(i => i.Name == "Raged Barbarian");
        Assert.Equal(UpgradeTrack.StarLaboratory, raged.Track);
    }

    [Fact]
    public void Api_names_resolve()
    {
        Assert.Equal("P.E.K.K.A", _data.FindUnit("P.E.K.K.A", Village.Home)!.Name);
        Assert.Equal(Village.BuilderBase, _data.FindUnit("Baby Dragon", Village.BuilderBase)!.Village);
        Assert.Equal(ItemKind.Troop, _data.FindUnit("Baby Dragon", Village.Home)!.Kind);
        Assert.Equal(ItemKind.Pet, _data.FindUnit("L.A.S.S.I", Village.Home)!.Kind);
        Assert.Equal(ItemKind.Equipment, _data.FindUnit("Barbarian Puppet", Village.Home)!.Kind);
        Assert.Null(_data.FindUnit("No Such Troop", Village.Home));
    }

    [Theory]
    [InlineData("demo_th19.json")]
    [InlineData("rushed_th13.json")]
    public void Fixtures_have_no_unknown_ids(string file)
    {
        var export = ExportParser.Parse(Fixtures.Read("exports", file));
        Assert.Empty(ExportParser.Validate(export, _data));
    }

    [Fact]
    public void Unknown_ids_are_warnings()
    {
        var export = ExportParser.Parse(Fixtures.Read("exports", "edge_cases.json"));
        var warning = Assert.Single(ExportParser.Validate(export, _data));
        Assert.Equal(9999999, warning.Id);
        Assert.Equal(ExportSection.Buildings, warning.Section);
    }
}
