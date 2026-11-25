using ClashOverlay.Core.Calc;
using ClashOverlay.Core.Data;
using ClashOverlay.Core.Export;
using ClashOverlay.Core.State;
using ClashOverlay.Tests.Support;

namespace ClashOverlay.Tests.Calc;

/// <summary>The calculations on the anonymised TH19 demo village and the real downloaded data.</summary>
[Collection(GameDataCollection.Name)]
public class DemoVillageTests
{
    private readonly GameData _data;
    private readonly VillageReport _home;
    private readonly VillageReport _builderBase;

    public DemoVillageTests(GameDataFixture fixture)
    {
        _data = fixture.Data;
        var export = ExportParser.Parse(Fixtures.Read("exports", "demo_th19.json"));
        var account = VillageStateBuilder.Build(_data, export, null, export.Timestamp!.Value.AddHours(1));
        _home = VillageAnalyzer.Analyze(_data, account.Home);
        _builderBase = VillageAnalyzer.Analyze(_data, account.BuilderBase);
    }

    private ItemDef Home(string name) => _data.Items.Single(i => i.Village == Village.Home && i.Name == name && !i.IsSeasonal);

    [Fact]
    public void Halls_and_builders()
    {
        Assert.Equal(19, _home.State.HallLevel);
        Assert.Equal(6, _home.Progress.Builders); // five huts and B.O.B
        Assert.Equal(10, _builderBase.State.HallLevel);
        Assert.Equal(2, _builderBase.Progress.Builders); // the Master Builder and O.T.T.O.
    }

    [Fact]
    public void Mostly_maxed()
    {
        Assert.InRange(_home.Progress.Fraction(ProgressMode.Levels), 0.85, 0.99);
        Assert.InRange(_home.Progress.Fraction(ProgressMode.Time), 0.80, 0.95);
        Assert.InRange(_builderBase.OverallFraction, 0.85, 0.995);
    }

    [Fact]
    public void Only_the_planted_items_are_rushed()
    {
        var scored = _home.Rush.Rows.Where(r => r.IsScored).Select(r => $"{r.Item.Name} x{r.Count} -{r.BehindEach}").ToList();
        Assert.Equal(["Inferno Tower x1 -2", "Giant Bomb x3 -1", "Bomb x1 -1", "Phoenix x1 -1"], scored);
        Assert.True(_home.Rush.Rows.Single(r => r.Item.Name == "Phoenix").IsUpgrading);
        Assert.Equal(2 + 3 + 1 + 1, _home.Rush.Score);
        Assert.Equal(18, _home.Rush.ComparedWith);

        // Equipment is listed after the scored rows.
        var firstUnscored = _home.Rush.Rows.ToList().FindIndex(r => !r.IsScored);
        Assert.True(firstUnscored >= scored.Count);
        Assert.Equal(0, _builderBase.Rush.Score);
    }

    [Fact]
    public void New_at_town_hall_19_is_not_rushed()
    {
        Assert.DoesNotContain(_home.Rush.Rows, r => r.Item.Name is "Spiky Tower" or "Firecracker Tower" or "Guardian Shrine");
    }

    [Fact]
    public void Remaining_copies_feed_the_unbuilt_merge()
    {
        var feed = _home.Goals.Where(g => g.IsMergeFeed).ToList();
        var tower = Assert.Single(feed, g => g.Item.Name == "Archer Tower");
        Assert.Equal(21, tower.Target);
        Assert.True(tower.IsMaxed);
        Assert.Equal(7, Assert.Single(feed, g => g.Item.Name == "Scattershot").Target);
        Assert.Contains(_home.Upgrades.Builder, r => r.Item.Name == "Firecracker Tower" && r.IsBuild);
        Assert.DoesNotContain(_home.Goals, g => g.Item.Name == "Eagle Artillery"); // merged into the Town Hall
    }

    [Theory]
    [InlineData(15, 8, 1)]
    [InlineData(16, 4, 1)]
    [InlineData(17, 2, 0)]
    [InlineData(18, 2, 0)]
    [InlineData(19, 0, 0)]
    public void Archer_towers_left_when_every_merge_is_built(int hall, int archerTowers, int eagles)
    {
        var merges = _data.ItemsIn(Village.Home)
            .Where(i => i.Kind != ItemKind.TownHall && i.Levels.Any(l => l.MergeSources.Count > 0))
            .SelectMany(i => Enumerable.Repeat(new StructureInstance { Item = i, Level = 1 }, _data.GrossCount(i.Id, hall)))
            .ToList();
        var state = new VillageState
        {
            Village = Village.Home,
            HallLevel = hall,
            Structures = merges,
            Units = new Dictionary<int, UnitState>(),
            AsOf = DateTimeOffset.UnixEpoch,
            HasStructures = true,
        };

        Assert.Equal(archerTowers, GoalPlanner.SlotCount(_data, state, Home("Archer Tower"), hall));
        Assert.Equal(eagles, GoalPlanner.SlotCount(_data, state, Home("Eagle Artillery"), hall));
    }

    [Fact]
    public void New_at_town_hall_19()
    {
        var news = _home.News;
        Assert.Equal(19, news.HallLevel);
        Assert.True(news.Items.Single(i => i.Item.Name == "Spiky Tower").IsNewItem);
        Assert.True(news.Items.Single(i => i.Item.Name == "Firecracker Tower").IsNewItem);
        Assert.True(news.Items.Single(i => i.Item.Name == "Guardian Shrine").IsNewItem);
        Assert.Equal(1, news.Items.Single(i => i.Item.Name == "Air Defense").Added);
        Assert.Equal(1, news.Items.Single(i => i.Item.Name == "Spring Trap").Added);
        Assert.Equal(25, news.Items.Single(i => i.Item.Name == "Wall").Added);
        Assert.True(news.ItemsWithNewLevels > 20);
    }

    [Fact]
    public void Research_waits_for_laboratory_17()
    {
        Assert.Equal(16, _home.State.LabLevel);
        Assert.Contains(_home.Upgrades.Laboratory, r => r.LockedReason == "Needs Lab 17");
        Assert.All(_home.Upgrades.Laboratory.Where(r => !r.IsLocked), r => Assert.True(r.Item.GetLevel(r.ToLevel)!.RequiredLab <= 16));
    }

    [Fact]
    public void Longest_upgrades_first()
    {
        foreach (var rows in new[] { _home.Upgrades.Builder, _home.Upgrades.Laboratory, _builderBase.Upgrades.Builder })
        {
            Assert.NotEmpty(rows);
            Assert.All(rows.Zip(rows.Skip(1)), p => Assert.True(p.First.Time >= p.Second.Time));
        }
    }

    [Fact]
    public void Active_upgrades_count_down_from_the_export()
    {
        var active = _home.Upgrades.Active;
        Assert.Equal(6, active.Count);
        Assert.Equal("Spiky Tower", active[0].Item.Name);
        Assert.Equal(0, active[0].FromLevel);
        Assert.Equal(151200 - 3600, active[0].SecondsLeft); // one hour after the export
        Assert.All(active.Zip(active.Skip(1)), p => Assert.True(p.First.SecondsLeft <= p.Second.SecondsLeft));
        Assert.Equal("Giant Cannon", Assert.Single(_builderBase.Upgrades.Active).Item.Name);
    }

    [Fact]
    public void Seasonal_super_and_squad_troops_are_left_out()
    {
        Assert.DoesNotContain(_home.Goals, g => g.Item.IsSeasonal || g.Item.IsSuperTroop || g.Item.IsSquad);
        Assert.Contains(_home.State.Units.Values, u => u.Item.IsSquad); // they are in the export, just not counted
    }

    [Fact]
    public void Costs_include_walls_and_ores()
    {
        var p = _home.Progress;
        Assert.Equal(350, p.Walls.Total);
        Assert.Equal(40, p.Walls.AtMax);
        Assert.True(p.Walls.GoldOrElixirLeft > 0);
        Assert.Equal(0, p.CostLeft.GoldOrElixir);
        Assert.Equal(p.Walls.GoldOrElixirLeft, p.TotalCostLeft.GoldOrElixir);
        Assert.True(p.CostLeft[Resource.Gold] > 0 && p.CostLeft[Resource.Elixir] > 0 && p.CostLeft[Resource.DarkElixir] > 0);
        Assert.True(p.CostLeft[Resource.ShinyOre] > 0 && p.CostLeft[Resource.GlowyOre] > 0 && p.CostLeft[Resource.StarryOre] > 0);
        Assert.True(_builderBase.Progress.CostLeft[Resource.BuilderGold] > 0);
    }

    [Fact]
    public void Times()
    {
        var p = _home.Progress;
        Assert.True(p.BuilderTime > p.LabTime);
        Assert.Equal((long)Math.Ceiling(p.BuilderTime / 6.0), p.BuilderTimePerBuilder);
        Assert.Equal("Barbarian King", p.LongestHero?.Name);
        Assert.True(p.PetHouseTime > 0);
        Assert.Equal("Battle Machine", _builderBase.Progress.LongestHero?.Name);
    }

    [Fact]
    public void Rushed_town_hall_13()
    {
        var export = ExportParser.Parse(Fixtures.Read("exports", "rushed_th13.json"));
        var account = VillageStateBuilder.Build(_data, export, null, export.Timestamp!.Value);
        var rush = VillageAnalyzer.Analyze(_data, account.Home).Rush;

        Assert.Equal(13, account.Home.HallLevel);
        Assert.True(rush.Score > _home.Rush.Score);
        foreach (var id in new[] { 1000012, 1000027, 1000019, 1000021, 12000002 })
        {
            Assert.Contains(rush.Rows, r => r.Item.Id == id);
        }
    }
}
