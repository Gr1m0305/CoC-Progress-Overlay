using ClashOverlay.Core.Export;
using ClashOverlay.Tests.Support;

namespace ClashOverlay.Tests.Export;

public class ExportParserTests
{
    [Fact]
    public void Parses_entries_counts_timers_and_extras()
    {
        var export = ExportParser.Parse(Fixtures.Read("exports", "edge_cases.json"));

        Assert.Equal("#P0LYJ2Q", export.Tag); // upper-cased; O read as zero is only for typed tags, 0 stays 0
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), export.Timestamp);

        var walls = export.In(ExportSection.Buildings).Where(e => e.Id == 1000010).ToList();
        Assert.Equal(250, walls.Sum(w => w.Count));

        var cc = export.In(ExportSection.Buildings).Single(e => e.Id == 1000014);
        Assert.Equal(12, cc.Level);
        Assert.Equal(86400, cc.Timer);
        Assert.True(cc.IsUpgrading);

        var geared = export.In(ExportSection.Buildings).Where(e => e.Id == 1000013).ToList();
        Assert.Equal(1, geared.Single(e => e.Count == 1).GearUp);

        var underConstruction = export.In(ExportSection.Buildings).Single(e => e.Id == 1000012);
        Assert.Equal(0, underConstruction.Level);
        Assert.Equal(600, underConstruction.Timer);

        // "lvl": "12" as a string is tolerated
        Assert.Equal(12, export.In(ExportSection.Buildings).Single(e => e.Id == 1000000).Level);

        var station = export.In(ExportSection.Buildings).Single(e => e.Id == 1000097);
        Assert.Single(station.Types);
        Assert.Equal(2, station.Types[0].Modules.Count);

        var unit = export.In(ExportSection.Units).Single(e => e.Id == 4000023);
        Assert.True(unit.HelperRecurrent);
        Assert.Equal(72981, export.In(ExportSection.Helpers).First().HelperCooldown);

        Assert.True(export.HasBuilderBase);
        Assert.Equal(2, export.In(ExportSection.Buildings2).Single(e => e.Id == 1000041).Count);
    }

    [Fact]
    public void Skips_malformed_entries_and_unknown_sections()
    {
        var export = ExportParser.Parse(Fixtures.Read("exports", "edge_cases.json"));

        Assert.DoesNotContain(export.Entries, e => e.Id <= 0);
        Assert.DoesNotContain(export.Entries, e => e.Id == 18000000); // decos are not read
        Assert.DoesNotContain(export.Entries, e => e.Id == 1); // some_future_section
    }

    [Fact]
    public void Accepts_code_fences_and_byte_order_mark()
    {
        var json = Fixtures.Read("exports", "edge_cases.json");
        var fenced = "﻿```json\n" + json + "\n```";

        var export = ExportParser.Parse(fenced);

        Assert.Equal("#P0LYJ2Q", export.Tag);
        Assert.True(ExportParser.LooksLikeExport(fenced));
        Assert.False(ExportParser.LooksLikeExport("hello"));
    }

    [Fact]
    public void Reads_bytes_and_rejects_binary()
    {
        var bytes = File.ReadAllBytes(Fixtures.Path("exports", "edge_cases.json"));
        Assert.Equal("#P0LYJ2Q", ExportParser.Parse(bytes).Tag);

        var ex = Assert.Throws<ExportFormatException>(() => ExportParser.Parse(new byte[] { 0xFF, 0xFE, 0x00, 0xC3 }));
        Assert.Contains("isn't text", ex.Message);
    }

    [Fact]
    public void Truncated_export_explains_the_problem()
    {
        var ex = Assert.Throws<ExportFormatException>(() => ExportParser.Parse(Fixtures.Read("exports", "truncated.json")));

        Assert.Contains("valid JSON", ex.Message);
        Assert.Contains("cut off", ex.Message);
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("[1,2,3]", "isn't a village export")]
    [InlineData("{\"buildings\": []}", "No player tag")]
    [InlineData("{\"tag\": \"#ABCDEF\", \"buildings\": [{\"data\": 1000001, \"lvl\": 9}]}", "isn't a valid player tag")]
    [InlineData("{\"tag\": \"#2PQ8LV9CY\", \"decos\": []}", "doesn't look like a village export")]
    public void Rejects_things_that_are_not_exports(string text, string expected)
    {
        var ex = Assert.Throws<ExportFormatException>(() => ExportParser.Parse(text));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Millisecond_timestamps_are_understood()
    {
        var export = ExportParser.Parse("{\"tag\": \"#2PQ8LV9CY\", \"timestamp\": 1795564800000, \"buildings\": [{\"data\": 1000001, \"lvl\": 19}]}");
        Assert.Equal(new DateTimeOffset(2026, 11, 25, 0, 0, 0, TimeSpan.Zero), export.Timestamp);
    }

    [Fact]
    public void Missing_timestamp_is_allowed()
    {
        var export = ExportParser.Parse("{\"tag\": \"#2PQ8LV9CY\", \"buildings\": [{\"data\": 1000001, \"lvl\": 19}]}");
        Assert.Null(export.Timestamp);
        Assert.Null(export.AgeAt(DateTimeOffset.UtcNow));
    }
}

public class PlayerTagTests
{
    [Theory]
    [InlineData("#2pq8lv9cy", "#2PQ8LV9CY")]
    [InlineData(" 2PQ8 LV9CY ", "#2PQ8LV9CY")]
    [InlineData("#9OJRG2UQ", "#90JRG2UQ")]
    [InlineData("##2PQ", "#2PQ")]
    public void Normalizes(string raw, string expected) => Assert.Equal(expected, Core.PlayerTag.Normalize(raw));

    [Theory]
    [InlineData("#2PQ8LV9CY", true)]
    [InlineData("#2P", false)]
    [InlineData("#2PQ", true)]
    [InlineData("#2PQA", false)]
    [InlineData("2PQ8LV9CY", false)]
    public void Validates(string tag, bool valid) => Assert.Equal(valid, Core.PlayerTag.IsValid(tag));

    [Fact]
    public void Url_encodes_the_hash() => Assert.Equal("%232PQ8LV9CY", Core.PlayerTag.ForUrl("#2pq8lv9cy"));
}
