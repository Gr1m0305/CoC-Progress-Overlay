using ClashOverlay.Core.Calc;
using ClashOverlay.Core.Data;
using ClashOverlay.Core.Export;
using ClashOverlay.Core.State;
using ClashOverlay.Tests.Support;
using static ClashOverlay.Tests.Support.TestGameData;

namespace ClashOverlay.Tests.Calc;

/// <summary>Levels raised by hand in the overlay between exports.</summary>
public sealed class LevelEditsTests : IDisposable
{
    private static readonly DateTimeOffset ExportTime = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly GameData _data = Create();
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private static VillageExport Export(DateTimeOffset? at = null, params ExportEntry[] entries) => new()
    {
        Tag = "#2PQ",
        Timestamp = at ?? ExportTime,
        Entries = [new ExportEntry(ExportSection.Buildings, GameData.TownHallId, 3), .. entries],
        RawJson = "{}",
    };

    private AccountState State(VillageExport? export, ApiPlayerSnapshot? api = null) =>
        VillageStateBuilder.Build(_data, export, api, ExportTime.AddHours(2));

    private static LevelEdit Edit(int id, int from, int to, int count = 1, double hoursAfterExport = 1) =>
        new(id, from, to, count, ExportTime.AddHours(hoursAfterExport));

    [Fact]
    public void Raises_one_copy_of_a_building()
    {
        var state = State(Export(null, new ExportEntry(ExportSection.Buildings, Cannon, 2, Count: 3)));

        var result = LevelEdits.Apply(_data, state, [Edit(Cannon, 2, 3)]);

        var levels = result.State.Home.InstancesOf(Cannon).Select(s => s.Level).OrderBy(l => l).ToList();
        Assert.Equal([2, 2, 3], levels);
        Assert.Single(result.State.Home.InstancesOf(Cannon), s => s.Edited);
        Assert.Single(result.Applied);
        Assert.Empty(result.Obsolete);
    }

    [Fact]
    public void Raises_every_copy_at_once()
    {
        var state = State(Export(null, new ExportEntry(ExportSection.Buildings, GameData.WallId, 1, Count: 10)));
        var result = LevelEdits.Apply(_data, state, [Edit(GameData.WallId, 1, 2, count: 10)]);
        Assert.All(result.State.Home.InstancesOf(GameData.WallId), w => Assert.Equal(2, w.Level));
    }

    [Fact]
    public void A_running_upgrade_is_the_copy_that_finished()
    {
        var state = State(Export(null,
            new ExportEntry(ExportSection.Buildings, Cannon, 2),
            new ExportEntry(ExportSection.Buildings, Cannon, 2, Timer: 86_400)));
        Assert.Single(state.Home.InstancesOf(Cannon), s => s.IsUpgrading);

        var result = LevelEdits.Apply(_data, state, [Edit(Cannon, 2, 3)]);

        Assert.DoesNotContain(result.State.Home.InstancesOf(Cannon), s => s.IsUpgrading);
        Assert.Equal([2, 3], result.State.Home.InstancesOf(Cannon).Select(s => s.Level).OrderBy(l => l));
    }

    [Fact]
    public void Edits_change_progress()
    {
        var state = State(Export(null, new ExportEntry(ExportSection.Buildings, Cannon, 2, Count: 2)));
        var before = VillageAnalyzer.Analyze(_data, state.Home);
        var after = VillageAnalyzer.Analyze(_data, LevelEdits.Apply(_data, state, [Edit(Cannon, 2, 3), Edit(Cannon, 3, 4, hoursAfterExport: 1.5)]).State.Home);

        Assert.Equal(before.Progress.For(Category.Defenses)!.LevelsDone + 2, after.Progress.For(Category.Defenses)!.LevelsDone);
    }

    [Fact]
    public void A_newer_export_replaces_building_edits()
    {
        var edit = Edit(Cannon, 2, 3);
        var newer = State(Export(ExportTime.AddHours(3), new ExportEntry(ExportSection.Buildings, Cannon, 2)));

        var result = LevelEdits.Apply(_data, newer, [edit]);

        Assert.Equal(2, result.State.Home.InstancesOf(Cannon).Single().Level);
        Assert.Equal([edit], result.Obsolete);
    }

    [Fact]
    public void An_edit_whose_copy_is_gone_is_obsolete()
    {
        var state = State(Export(null, new ExportEntry(ExportSection.Buildings, Cannon, 4)));
        Assert.Single(LevelEdits.Apply(_data, state, [Edit(Cannon, 2, 3)]).Obsolete);
    }

    [Fact]
    public void Raises_a_unit_and_marks_it_manual()
    {
        var state = State(Export(null, new ExportEntry(ExportSection.Units, Barbarian, 1, Timer: 86_400)));

        var result = LevelEdits.Apply(_data, state, [Edit(Barbarian, 1, 2)]);

        var barb = result.State.Home.Units[Barbarian];
        Assert.Equal(2, barb.Level);
        Assert.Equal(LevelSource.Manual, barb.Source);
        Assert.Null(barb.TimerLeft);
    }

    [Fact]
    public void A_later_api_sync_overrules_unit_edits_after_a_grace_period()
    {
        var edit = Edit(Barbarian, 1, 2);
        var export = Export(null, new ExportEntry(ExportSection.Units, Barbarian, 1));
        ApiPlayerSnapshot Api(TimeSpan after) => new()
        {
            FetchedAt = edit.At + after,
            TownHallLevel = 3,
            Units = [new ApiUnitLevel("Barbarian", 1, 3, "home")],
        };

        var soon = LevelEdits.Apply(_data, State(export, Api(TimeSpan.FromMinutes(2))), [edit]);
        Assert.Equal(2, soon.State.Home.Units[Barbarian].Level);

        var later = LevelEdits.Apply(_data, State(export, Api(TimeSpan.FromMinutes(30))), [edit]);
        Assert.Equal(1, later.State.Home.Units[Barbarian].Level);
        Assert.Equal([edit], later.Obsolete);
    }

    [Fact]
    public void An_edit_the_data_already_shows_is_obsolete()
    {
        var api = new ApiPlayerSnapshot { FetchedAt = ExportTime.AddMinutes(30), TownHallLevel = 3, Units = [new ApiUnitLevel("Barbarian", 3, 3, "home")] };
        var result = LevelEdits.Apply(_data, State(Export(), api), [Edit(Barbarian, 1, 2)]);
        Assert.Equal(3, result.State.Home.Units[Barbarian].Level);
        Assert.Single(result.Obsolete);
    }

    [Fact]
    public void Edits_never_pass_the_top_level_and_never_lower()
    {
        var state = State(Export(null, new ExportEntry(ExportSection.Buildings, Cannon, 4)));
        Assert.Null(LevelEdits.Raise(_data.Get(Cannon)!, 4, 1, ExportTime));
        var bad = LevelEdits.Apply(_data, state, [new LevelEdit(Cannon, 4, 5, 1, ExportTime.AddHours(1)), new LevelEdit(Cannon, 4, 3, 1, ExportTime.AddHours(1))]);
        Assert.Equal(2, bad.Obsolete.Count);
        Assert.Equal(4, bad.State.Home.InstancesOf(Cannon).Single().Level);
    }

    [Fact]
    public void Builder_base_edits_go_to_the_builder_base()
    {
        var export = Export(null,
            new ExportEntry(ExportSection.Buildings2, GameData.BuilderHallId, 2),
            new ExportEntry(ExportSection.Buildings2, BbCannon, 1));
        var result = LevelEdits.Apply(_data, State(export), [Edit(BbCannon, 1, 2)]);
        Assert.Equal(2, result.State.BuilderBase.InstancesOf(BbCannon).Single().Level);
        Assert.Equal(1, result.State.BuilderBase.Builders);
    }

    [Fact]
    public void Undo_and_storage()
    {
        var a = Edit(Cannon, 2, 3);
        var b = Edit(Cannon, 3, 4, hoursAfterExport: 2);
        var c = Edit(Barbarian, 1, 2);
        var path = _dir.File("edits.json");

        LevelEdits.Save(path, [a, b, c]);
        var loaded = LevelEdits.Load(path);
        Assert.Equal([a, b, c], loaded);

        var undone = LevelEdits.UndoLast(loaded, Cannon);
        Assert.Equal([a, c], undone);

        LevelEdits.Save(path, []);
        Assert.False(File.Exists(path));
        Assert.Empty(LevelEdits.Load(path));
    }
}
