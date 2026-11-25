using ClashOverlay.Core.Calc;
using ClashOverlay.Core.Data;
using ClashOverlay.Core.State;
using ClashOverlay.Tests.Support;
using static ClashOverlay.Tests.Support.TestGameData;

namespace ClashOverlay.Tests.Calc;

/// <summary>Exact numbers against the hand-made <see cref="TestGameData"/> set.</summary>
public class CalculationTests
{
    private readonly GameData _data = Create();

    /// <summary>TH3, Double Cannon not built yet, nothing rushed.</summary>
    private VillageState ScenarioA() => Home(_data, 3,
        [
            (Cannon, 4, null), (Cannon, 4, null), (Cannon, 3, null), (Cannon, 2, null),
            (ArcherTower, 2, null), (AirBomb, 3, null), (AirBomb, 2, null),
            .. Many(GameData.WallId, 2, 15), .. Many(GameData.WallId, 3, 5),
            .. Many(GameData.BuildersHutId, 1, 5),
            (GameData.LaboratoryId, 2, null), (GameData.HeroHallId, 1, null),
        ],
        [(Barbarian, 2, null), (Archer, 2, null), (King, 3, null), (Puppet, 2, null), (EventTroop, 1, null)]);

    [Fact]
    public void Max_level_is_the_highest_level_the_hall_allows()
    {
        var cannon = _data.Get(Cannon)!;
        Assert.Equal(0, cannon.MaxLevelFor(0));
        Assert.Equal(2, cannon.MaxLevelFor(1));
        Assert.Equal(3, cannon.MaxLevelFor(2));
        Assert.Equal(4, cannon.MaxLevelFor(3));
        Assert.Equal(0, _data.Get(DoubleCannon)!.MaxLevelFor(2));
    }

    [Fact]
    public void Unbuilt_merge_keeps_its_sources_as_merge_feed()
    {
        var goals = GoalPlanner.Plan(_data, ScenarioA(), 3);
        var cannons = goals.Where(g => g.Item.Id == Cannon).ToList();

        Assert.Equal(4, cannons.Count);
        Assert.Equal(2, cannons.Count(g => g.IsMergeFeed));
        Assert.All(cannons, g => Assert.Equal(4, g.Target));
        Assert.Equal(2, cannons.Count(g => g.IsNewAtHall)); // TH3 adds two cannons

        var dc = Assert.Single(goals, g => g.Item.Id == DoubleCannon);
        Assert.True(dc.IsUnbuilt);
        Assert.True(dc.IsNewAtHall);
    }

    [Fact]
    public void Built_merge_removes_its_sources()
    {
        var state = Home(_data, 3, [(Cannon, 4, null), (Cannon, 4, null), (DoubleCannon, 1, null)]);
        Assert.Equal(2, GoalPlanner.SlotCount(_data, state, _data.Get(Cannon)!, 3));
        Assert.Equal(0, GoalPlanner.SlotCount(_data, state, _data.Get(Cannon)!, 2)); // so the merged pair is not "rushed"
    }

    [Fact]
    public void Merge_under_construction_already_consumed_its_sources()
    {
        var state = Home(_data, 3, [(Cannon, 4, null), (Cannon, 4, null), (DoubleCannon, 0, 3000)]);
        Assert.Equal(2, GoalPlanner.SlotCount(_data, state, _data.Get(Cannon)!, 3));
    }

    [Fact]
    public void Builders_huts_count_what_the_player_has()
    {
        var state = Home(_data, 3, Many(GameData.BuildersHutId, 1, 3));
        Assert.Equal(3, GoalPlanner.SlotCount(_data, state, _data.Get(GameData.BuildersHutId)!, 3));
    }

    [Fact]
    public void Progress_counts_levels_per_category()
    {
        var report = VillageAnalyzer.Analyze(_data, ScenarioA());
        var defenses = report.Progress.For(Category.Defenses)!;

        // Cannons 13/16, Archer Tower 2/2, Double Cannon 0/2, huts 5/10.
        Assert.Equal(20, defenses.LevelsDone);
        Assert.Equal(30, defenses.LevelsMax);
        Assert.Equal(20 / 30.0, defenses.LevelFraction, 6);
        Assert.Equal(3, defenses.ItemsMaxed); // two cannons at 4 and the Archer Tower
    }

    [Fact]
    public void Overall_excludes_walls_and_seasonal_troops()
    {
        var report = VillageAnalyzer.Analyze(_data, ScenarioA());

        Assert.DoesNotContain(report.Goals, g => g.Item.Id == EventTroop);
        var nonWall = report.Progress.Categories.Where(c => c.Category != Category.Walls).Sum(c => c.LevelsMax);
        Assert.Equal(nonWall, report.Progress.Overall.LevelsMax);

        var walls = report.Progress.Walls;
        Assert.Equal(20, walls.Total);
        Assert.Equal(5, walls.AtMax);
        Assert.Equal([new WallLevelCount(3, 5), new WallLevelCount(2, 15)], walls.ByLevel);
        Assert.Equal(15 * 40, walls.GoldOrElixirLeft); // 15 walls 2 → 3, gold or elixir
    }

    [Fact]
    public void Cost_and_time_left()
    {
        var report = VillageAnalyzer.Analyze(_data, ScenarioA());
        var p = report.Progress;

        // Gold: cannon 3→4 (400) + 2→4 (300+400) + Double Cannon 0→2 (3000) + huts 5×500 + air bomb 2→3 (100).
        Assert.Equal(400 + 700 + 3000 + 2500 + 100, p.CostLeft[Resource.Gold]);
        // Elixir: lab 2→3 (900), hero hall 1→2 (600), barbarian 2→3 (100).
        Assert.Equal(900 + 600 + 100, p.CostLeft[Resource.Elixir]);
        Assert.Equal(3000, p.CostLeft[Resource.DarkElixir]);
        Assert.Equal(240, p.CostLeft[Resource.ShinyOre]);

        // Builder time: cannons 240 + 180 + 240, DC 3600 + 7200, huts 5×900, AB 60, lab 1200, HH 600, King 10800.
        Assert.Equal(240 + 420 + 10800 + 4500 + 60 + 1200 + 600 + 10800, p.BuilderTime);
        Assert.Equal(5, p.Builders);
        Assert.Equal((long)Math.Ceiling(p.BuilderTime / 5.0), p.BuilderTimePerBuilder);
        Assert.Equal(7200, p.LabTime);
        Assert.Equal(_data.Get(King), p.LongestHero);
        Assert.Equal(10800, p.LongestHeroTime);
    }

    [Fact]
    public void Builders_override_divides_the_time()
    {
        var p = VillageAnalyzer.Analyze(_data, ScenarioA(), new AnalyzerOptions(BuildersOverride: 2)).Progress;
        Assert.Equal(2, p.Builders);
        Assert.Equal((long)Math.Ceiling(p.BuilderTime / 2.0), p.BuilderTimePerBuilder);
    }

    [Fact]
    public void Gold_pass_boosts_shorten_the_times_left()
    {
        var plain = VillageAnalyzer.Analyze(_data, ScenarioA());
        var boosted = VillageAnalyzer.Analyze(_data, ScenarioA(), new AnalyzerOptions { Boosts = new TimeBoosts(Builder: 20, Research: 10) });
        var p = boosted.Progress;

        Assert.Equal(plain.Progress.BuilderTime * 8 / 10, p.BuilderTime);
        Assert.Equal(plain.Progress.LabTime * 9 / 10, p.LabTime);
        Assert.Equal(8640, p.LongestHeroTime); // 10800 less 20%
        Assert.Equal(plain.Progress.PetHouseTime, p.PetHouseTime);
        Assert.Equal(plain.Progress.Overall.TimeFraction, p.Overall.TimeFraction, 9); // progress by time uses the game's times

        var king = Assert.Single(boosted.Upgrades.Builder, r => r.Item.Id == King);
        Assert.Equal(_data.Get(King)!.TimeToReach(king.ToLevel) * 8 / 10, king.Time);
        var barbarian = Assert.Single(boosted.Upgrades.Laboratory, r => r.Item.Id == Barbarian);
        Assert.Equal(_data.Get(Barbarian)!.TimeToReach(barbarian.ToLevel) * 9 / 10, barbarian.Time);
    }

    [Fact]
    public void A_running_upgrade_keeps_its_real_timer_under_a_boost()
    {
        var state = Home(_data, 3, [(Cannon, 3, 100)]);
        var goal = Assert.Single(VillageAnalyzer.Analyze(_data, state).Goals, g => g.Item.Id == Cannon && g.IsUpgrading);
        Assert.Equal(100, goal.TimeLeftWith(new TimeBoosts(20, 20)));
        Assert.Equal(goal.TimeLeft, goal.TimeLeftWith(TimeBoosts.None));
    }

    [Fact]
    public void Boosts_only_touch_main_village_builders_and_the_laboratory()
    {
        var b = new TimeBoosts(Builder: 20, Research: 15);
        Assert.Equal(20, b.PercentFor(UpgradeTrack.Builder));
        Assert.Equal(15, b.PercentFor(UpgradeTrack.Laboratory));
        Assert.Equal(0, b.PercentFor(UpgradeTrack.PetHouse));
        Assert.Equal(0, b.PercentFor(UpgradeTrack.BuilderBaseBuilder));
        Assert.Equal(0, b.PercentFor(UpgradeTrack.StarLaboratory));
        Assert.Equal(1000, b.Apply(_data.Get(BbCannon)!, 1000));
        Assert.Equal(TimeBoosts.MaxPercent, new TimeBoosts(Builder: 90).PercentFor(UpgradeTrack.Builder));
        Assert.Equal(81, new TimeBoosts(Builder: 20).Apply(_data.Get(Cannon)!, 101)); // rounded up
    }

    [Fact]
    public void Time_weighted_progress()
    {
        var state = Home(_data, 3, [(ArcherTower, 1, null)]);
        var at = VillageAnalyzer.Analyze(_data, state).Goals.Single(g => g.Item.Id == ArcherTower);

        Assert.Equal(60 + 600, at.TimeToTarget);
        Assert.Equal(60, at.TimeDone);
        Assert.Equal(0.5, ProgressCalculator.Summarise(Category.Defenses, [at]).LevelFraction);
        Assert.Equal(60 / 660.0, ProgressCalculator.Summarise(Category.Defenses, [at]).TimeFraction, 6);
    }

    [Fact]
    public void Running_upgrade_uses_its_timer_and_is_already_paid()
    {
        var state = Home(_data, 3, [(Cannon, 3, 100)]);
        var goal = GoalPlanner.Plan(_data, state, 3).First(g => g.Item.Id == Cannon);

        Assert.True(goal.IsUpgrading);
        Assert.Equal(100, goal.TimeLeft);
        Assert.Equal(0, goal.CostLeft[Resource.Gold]);
        Assert.Equal(3, goal.LevelsDone);
        Assert.Equal(60 + 120 + 180 + 140, goal.TimeDone);
    }

    [Fact]
    public void Epic_equipment_counts_only_when_owned()
    {
        Assert.DoesNotContain(GoalPlanner.Plan(_data, ScenarioA(), 3), g => g.Item.Id == EpicGear);

        var owned = Home(_data, 3, [], [(EpicGear, 1, null)]);
        Assert.Contains(GoalPlanner.Plan(_data, owned, 3), g => g.Item.Id == EpicGear);
    }

    [Fact]
    public void Nothing_rushed_when_everything_is_at_the_previous_max()
    {
        var rush = VillageAnalyzer.Analyze(_data, ScenarioA()).Rush;
        Assert.Equal(2, rush.ComparedWith);
        Assert.False(rush.IsRushed);
        Assert.Empty(rush.Rows);
    }

    [Fact]
    public void Rushed_groups_identical_copies_and_skips_new_ones()
    {
        // TH3 with cannons at 1, 1 (TH2 max 3), one air bomb (TH2 allows two at level 2), and the king at 1.
        var state = Home(_data, 3,
            [(Cannon, 1, null), (Cannon, 1, null), (AirBomb, 2, null), .. Many(GameData.WallId, 1, 20), (GameData.LaboratoryId, 2, null), (GameData.HeroHallId, 1, null)],
            [(Barbarian, 2, null), (King, 1, null)]);

        var rush = RushCalculator.Calculate(_data, state);

        var cannons = Assert.Single(rush.Rows, r => r.Item.Id == Cannon);
        Assert.Equal(2, cannons.Count);
        Assert.Equal(2, cannons.BehindEach);
        var missingBomb = Assert.Single(rush.Rows, r => r.Item.Id == AirBomb);
        Assert.True(missingBomb.IsUnbuilt);
        Assert.Equal(2, missingBomb.BehindEach);
        Assert.Equal(1, Assert.Single(rush.Rows, r => r.Item.Id == King).BehindEach);
        Assert.DoesNotContain(rush.Rows, r => r.Item.Id == DoubleCannon); // new at TH3
        Assert.DoesNotContain(rush.Rows, r => r.Item.Id == GameData.WallId); // walls are not rows

        // Score: cannons 2×2, missing air bomb 2, Archer Tower not built 2, Archer not unlocked 2, king 1.
        Assert.Equal(4 + 2 + 2 + 2 + 1, rush.Score);
        Assert.Equal(2, Assert.Single(rush.Rows, r => r.Item.Id == Archer).BehindEach);
        Assert.Equal(15, rush.WallsBelow);
        Assert.Contains(rush.Categories, c => c.Category == Category.Walls && !c.Scored);
        Assert.True(rush.Fraction is > 0 and < 1);
    }

    [Fact]
    public void Town_hall_1_has_nothing_to_be_rushed_against()
    {
        var rush = RushCalculator.Calculate(_data, Home(_data, 1, [(Cannon, 1, null)]));
        Assert.Empty(rush.Rows);
        Assert.Equal(0, rush.Score);
    }

    [Fact]
    public void Builder_upgrades_longest_first_with_locks_and_build_rows()
    {
        var plan = VillageAnalyzer.Analyze(_data, ScenarioA()).Upgrades;
        var names = plan.Builder.Select(r => $"{r.Item.Name} {r.FromLevel}").ToList();

        Assert.Equal(["Barbarian King 3", "Double Cannon 0", "Laboratory 2", "Builder's Hut 1", "Hero Hall 1", "Cannon 3", "Cannon 2", "Air Bomb 2"], names);
        Assert.Equal("Needs Hero Hall 2", plan.Builder[0].LockedReason);
        Assert.True(plan.Builder[1].IsBuild);
        Assert.Equal(5, plan.Builder[3].Count);
        Assert.All(plan.Builder.Zip(plan.Builder.Skip(1)), p => Assert.True(p.First.Time >= p.Second.Time));
        Assert.DoesNotContain(plan.Builder, r => r.Item.Category == Category.Walls);
    }

    [Fact]
    public void Lab_research_is_greyed_when_the_lab_is_too_low()
    {
        var plan = VillageAnalyzer.Analyze(_data, ScenarioA()).Upgrades;
        var barb = Assert.Single(plan.Laboratory);
        Assert.Equal(Barbarian, barb.Item.Id);
        Assert.Equal("Needs Lab 3", barb.LockedReason);
        Assert.Equal(7200, barb.Time);
        Assert.Equal(100, barb.Cost[Resource.Elixir]);
    }

    [Fact]
    public void Active_upgrades_shortest_first()
    {
        var state = Home(_data, 3, [(Cannon, 3, 500), (GameData.LaboratoryId, 2, 100)], [(Barbarian, 2, 50)]);
        var plan = UpgradePlanner.Plan(GoalPlanner.Plan(_data, state, 3), state);

        Assert.Equal([Barbarian, GameData.LaboratoryId, Cannon], plan.Active.Select(a => a.Item.Id));
        Assert.Equal(3, plan.Active[0].ToLevel);
        Assert.DoesNotContain(plan.Builder, r => r.Item.Id == Cannon && r.FromLevel == 3); // running, not offered
    }

    [Fact]
    public void Hall_news_lists_new_items_and_copies()
    {
        var news = VillageAnalyzer.News(_data, Village.Home, 3);

        var dc = Assert.Single(news.Items, i => i.Item.Id == DoubleCannon);
        Assert.True(dc.IsNewItem);
        Assert.Equal(1, dc.Added);
        Assert.Equal(2, Assert.Single(news.Items, i => i.Item.Id == Cannon).Added);
        Assert.Equal(5, Assert.Single(news.Items, i => i.Item.Id == GameData.WallId).Added);
        Assert.Equal(DoubleCannon, news.Items[0].Item.Id); // brand-new first
    }

    [Fact]
    public void Builder_base_uses_its_own_hall_and_tracks()
    {
        var state = new VillageState
        {
            Village = Village.BuilderBase,
            HallLevel = 2,
            Structures = [new StructureInstance { Item = _data.Get(BbCannon)!, Level = 1 }, new StructureInstance { Item = _data.Get(GameData.StarLaboratoryId)!, Level = 1 }],
            Units = new Dictionary<int, UnitState> { [BbTroop] = new() { Item = _data.Get(BbTroop)!, Level = 1 } },
            AsOf = DateTimeOffset.UnixEpoch,
            HasStructures = true,
            Builders = 1,
        };

        var report = VillageAnalyzer.Analyze(_data, state);

        Assert.Equal(3, report.Goals.Count(g => g.Item.Id == BbCannon)); // 2 + 1
        Assert.Equal(1, report.Rush.Rows.Single(r => r.Item.Id == BbCannon).Count); // the second BH1 cannon is missing
        Assert.Equal(900, report.Progress.LabTime);
        Assert.Equal(ProgressCalculator.BuilderOrder.Count, report.Progress.Categories.Count);
        Assert.Contains(report.Upgrades.Laboratory, r => r.Item.Id == BbTroop && r.LockedReason == null);
    }
}
