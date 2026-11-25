using ClashOverlay.Core.Data;
using ClashOverlay.Core.Export;
using ClashOverlay.Core.State;
using ClashOverlay.Tests.Support;
using static ClashOverlay.Tests.Support.TestGameData;

namespace ClashOverlay.Tests.Calc;

/// <summary>The merge rule between the village export and the API.</summary>
public class VillageStateBuilderTests
{
    private static readonly DateTimeOffset ExportTime = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly GameData _data = Create();

    private static VillageExport Export(params ExportEntry[] entries) => new()
    {
        Tag = "#2PQ",
        Timestamp = ExportTime,
        Entries = [new ExportEntry(ExportSection.Buildings, GameData.TownHallId, 2), .. entries],
        RawJson = "{}",
    };

    private static ApiPlayerSnapshot Api(DateTimeOffset at, int th, params ApiUnitLevel[] units) =>
        new() { FetchedAt = at, TownHallLevel = th, Units = units };

    [Fact]
    public void Structures_come_from_the_export_one_per_copy()
    {
        var export = Export(
            new ExportEntry(ExportSection.Buildings, Cannon, 2, Count: 3),
            new ExportEntry(ExportSection.Buildings, GameData.WallId, 1, Count: 10),
            new ExportEntry(ExportSection.Traps, AirBomb, 1));

        var home = VillageStateBuilder.Build(_data, export, null, ExportTime).Home;

        Assert.Equal(2, home.HallLevel);
        Assert.Equal(3, home.InstancesOf(Cannon).Count());
        Assert.Equal(10, home.InstancesOf(GameData.WallId).Count());
        Assert.Single(home.InstancesOf(AirBomb));
        Assert.DoesNotContain(home.Structures, s => s.Item.Id == GameData.TownHallId);
        Assert.True(home.HasStructures);
    }

    [Fact]
    public void Running_timers_move_forward_and_finished_ones_gain_their_level()
    {
        var export = Export(
            new ExportEntry(ExportSection.Buildings, Cannon, 2, Timer: 7200),
            new ExportEntry(ExportSection.Buildings, ArcherTower, 1, Timer: 1800),
            new ExportEntry(ExportSection.Units, Barbarian, 1, Timer: 600));

        var home = VillageStateBuilder.Build(_data, export, null, ExportTime.AddHours(1)).Home;

        var cannon = Assert.Single(home.InstancesOf(Cannon));
        Assert.Equal(2, cannon.Level);
        Assert.Equal(3600, cannon.TimerLeft);
        var tower = Assert.Single(home.InstancesOf(ArcherTower));
        Assert.Equal(2, tower.Level);
        Assert.Null(tower.TimerLeft);
        Assert.Equal(2, home.UnitLevel(Barbarian));
        Assert.False(home.Units[Barbarian].IsUpgrading);
    }

    [Fact]
    public void Hall_level_comes_from_the_api_when_it_has_one()
    {
        var state = VillageStateBuilder.Build(_data, Export(), Api(ExportTime.AddDays(1), 3), ExportTime.AddDays(1));
        Assert.Equal(3, state.Home.HallLevel);

        state = VillageStateBuilder.Build(_data, Export(), Api(ExportTime.AddDays(1), 0), ExportTime.AddDays(1));
        Assert.Equal(2, state.Home.HallLevel);
    }

    [Fact]
    public void Newer_api_levels_win()
    {
        var export = Export(new ExportEntry(ExportSection.Units, Barbarian, 1), new ExportEntry(ExportSection.Heroes, King, 1));
        var api = Api(ExportTime.AddDays(2), 2, new ApiUnitLevel("Barbarian", 2, 3, "home"), new ApiUnitLevel("Barbarian King", 3, 4, "home"));

        var home = VillageStateBuilder.Build(_data, export, api, ExportTime.AddDays(2)).Home;

        Assert.Equal(2, home.UnitLevel(Barbarian));
        Assert.Equal(LevelSource.Api, home.Units[Barbarian].Source);
        Assert.Equal(3, home.UnitLevel(King));
    }

    [Fact]
    public void Newer_export_levels_win()
    {
        var export = Export(new ExportEntry(ExportSection.Units, Barbarian, 3));
        var api = Api(ExportTime.AddDays(-1), 2, new ApiUnitLevel("Barbarian", 2, 3, "home"));

        var home = VillageStateBuilder.Build(_data, export, api, ExportTime.AddDays(1)).Home;

        Assert.Equal(3, home.UnitLevel(Barbarian));
        Assert.Equal(LevelSource.Export, home.Units[Barbarian].Source);
    }

    [Fact]
    public void Agreeing_levels_keep_the_export_timer()
    {
        var export = Export(new ExportEntry(ExportSection.Units, Barbarian, 2, Timer: 7200));
        var api = Api(ExportTime.AddHours(1), 2, new ApiUnitLevel("Barbarian", 2, 3, "home"));

        var home = VillageStateBuilder.Build(_data, export, api, ExportTime.AddHours(1)).Home;

        Assert.Equal(2, home.UnitLevel(Barbarian));
        Assert.Equal(3600, home.Units[Barbarian].TimerLeft);
    }

    [Fact]
    public void Api_still_showing_the_start_level_means_the_upgrade_is_nearly_done()
    {
        // The export's timer has run out by now, but the newer API has not seen the new level yet.
        var export = Export(new ExportEntry(ExportSection.Units, Barbarian, 2, Timer: 600));
        var api = Api(ExportTime.AddHours(1), 2, new ApiUnitLevel("Barbarian", 2, 3, "home"));

        var home = VillageStateBuilder.Build(_data, export, api, ExportTime.AddHours(1)).Home;

        Assert.Equal(2, home.UnitLevel(Barbarian));
        Assert.True(home.Units[Barbarian].IsUpgrading);
    }

    [Fact]
    public void Units_only_the_api_knows_are_added()
    {
        var api = Api(ExportTime, 2, new ApiUnitLevel("Archer", 2, 2, "home"), new ApiUnitLevel("Raged Barbarian", 2, 2, "builderBase"));

        var state = VillageStateBuilder.Build(_data, Export(), api, ExportTime);

        Assert.Equal(2, state.Home.UnitLevel(Archer));
        Assert.Equal(LevelSource.Api, state.Home.Units[Archer].Source);
        Assert.Equal(2, state.BuilderBase.UnitLevel(BbTroop));
        Assert.False(state.Home.Units.ContainsKey(BbTroop));
    }

    [Fact]
    public void Without_an_export_there_are_no_structures()
    {
        var state = VillageStateBuilder.Build(_data, null, Api(ExportTime, 3, new ApiUnitLevel("Barbarian", 2, 3, "home")), ExportTime);

        Assert.Equal(3, state.Home.HallLevel);
        Assert.False(state.Home.HasStructures);
        Assert.Empty(state.Home.Structures);
        Assert.Equal(2, state.Home.UnitLevel(Barbarian));
    }

    [Fact]
    public void Builders_are_the_huts_in_the_export()
    {
        var export = Export(new ExportEntry(ExportSection.Buildings, GameData.BuildersHutId, 1, Count: 4));
        Assert.Equal(4, VillageStateBuilder.Build(_data, export, null, ExportTime).Home.Builders);
    }

    [Fact]
    public void Builder_base_sections_build_the_builder_base()
    {
        var export = Export(
            new ExportEntry(ExportSection.Buildings2, GameData.BuilderHallId, 2),
            new ExportEntry(ExportSection.Buildings2, BbCannon, 1, Count: 2),
            new ExportEntry(ExportSection.Units2, BbTroop, 1));

        var bb = VillageStateBuilder.Build(_data, export, null, ExportTime).BuilderBase;

        Assert.Equal(Village.BuilderBase, bb.Village);
        Assert.Equal(2, bb.HallLevel);
        Assert.Equal(2, bb.InstancesOf(BbCannon).Count());
        Assert.Equal(1, bb.UnitLevel(BbTroop));
        Assert.Equal(1, bb.Builders);
    }

    [Fact]
    public void Unknown_ids_become_warnings_not_errors()
    {
        var export = Export(new ExportEntry(ExportSection.Buildings, 9999999, 1));
        var state = VillageStateBuilder.Build(_data, export, null, ExportTime);
        Assert.Contains(state.Warnings, w => w.Id == 9999999);
    }

    [Theory]
    [InlineData(3, null, 100, 3, null)]
    [InlineData(3, 500L, 100, 3, 400L)]
    [InlineData(3, 500L, 500, 4, null)]
    [InlineData(0, 60L, 3600, 1, null)]
    public void Advance(int level, long? timer, long elapsed, int expectedLevel, long? expectedLeft) =>
        Assert.Equal((expectedLevel, expectedLeft), VillageStateBuilder.Advance(level, timer, elapsed));
}
