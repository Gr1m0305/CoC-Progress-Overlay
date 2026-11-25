using ClashOverlay.Core.Calc;
using ClashOverlay.Core.Settings;
using ClashOverlay.Tests.Support;

namespace ClashOverlay.Tests.Settings;

public sealed class SettingsTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Theory]
    [InlineData("Ctrl+R", KeyModifiers.Ctrl, 0x52, "Ctrl+R")]
    [InlineData("ctrl + shift + f5", KeyModifiers.Ctrl | KeyModifiers.Shift, 0x74, "Ctrl+Shift+F5")]
    [InlineData("Ctrl+Tab", KeyModifiers.Ctrl, 0x09, "Ctrl+Tab")]
    [InlineData("Esc", KeyModifiers.None, 0x1B, "Esc")]
    [InlineData("Escape", KeyModifiers.None, 0x1B, "Esc")]
    [InlineData("Alt+1", KeyModifiers.Alt, 0x31, "Alt+1")]
    [InlineData("Win+Num7", KeyModifiers.Win, 0x67, "Win+Num7")]
    [InlineData("Control+`", KeyModifiers.Ctrl, 0xC0, "Ctrl+`")]
    public void Hotkeys_parse_and_format(string text, KeyModifiers mods, int key, string formatted)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var g));
        Assert.Equal(mods, g.Modifiers);
        Assert.Equal(key, g.Key);
        Assert.Equal(formatted, g.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+R+T")]
    [InlineData("Ctrl+Banana")]
    [InlineData("F25")]
    public void Bad_hotkeys(string text) => Assert.False(HotkeyGesture.TryParse(text, out _));

    [Theory]
    [InlineData("crosvm", "Clash of Clans")]
    [InlineData("crosvm.exe", "clash of clans")]
    [InlineData("crosvm", "Clash of Clans - Chief")]
    public void The_default_rule_is_google_play_games(string process, string title)
    {
        var rule = Assert.Single(GameWindowRule.Defaults());
        Assert.Equal("Google Play Games", rule.Name);
        Assert.True(rule.Matches(process, title));
    }

    [Fact]
    public void Built_in_emulator_rules_from_older_versions_are_removed()
    {
        List<GameWindowRule> rules =
        [
            new() { Name = "Google Play Games", ProcessName = "crosvm", TitlePattern = "Clash of Clans*" },
            new() { Name = "BlueStacks", ProcessName = "HD-Player", TitlePattern = "*" },
            new() { Name = "LDPlayer", ProcessName = "dnplayer", TitlePattern = "*", Enabled = false },
            new() { Name = "MuMu Player 12", ProcessName = "MuMuNxDevice", TitlePattern = "*" },
            new() { Name = "Any window titled Clash of Clans", ProcessName = null, TitlePattern = "Clash of Clans" },
            new() { Name = "BlueStacks", ProcessName = "HD-Player", TitlePattern = "Clash of Clans*" }, // edited: kept
            new() { Name = "Picked: My emulator", ProcessName = "emu", TitlePattern = "Game" },
        ];

        Assert.True(GameWindowRule.Upgrade(rules));

        Assert.Equal(["Google Play Games", "BlueStacks", "Picked: My emulator"], rules.Select(r => r.Name));
        Assert.Equal("Clash of Clans*", rules[1].TitlePattern);
        Assert.False(GameWindowRule.Upgrade(rules));
    }

    [Theory]
    [InlineData("crosvm", "Google Play Games")]
    [InlineData("chrome", "Clash of Clans - Wiki - Google Chrome")]
    [InlineData("explorer", "Clash of Clans tips")]
    public void Default_rules_ignore_other_windows(string process, string title) =>
        Assert.DoesNotContain(GameWindowRule.Defaults(), r => r.Matches(process, title));

    [Fact]
    public void Empty_or_disabled_rules_match_nothing()
    {
        Assert.False(new GameWindowRule().Matches("crosvm", "Clash of Clans"));
        Assert.False(new GameWindowRule { ProcessName = "crosvm", Enabled = false }.Matches("crosvm", "x"));
    }

    [Theory]
    [InlineData("Clash*", "Clash of Clans", true)]
    [InlineData("Clash?of?Clans", "Clash of Clans", true)]
    [InlineData("Clash (of) [Clans]", "Clash (of) [Clans]", true)]
    [InlineData("*Clans", "Clash of Clans x", false)]
    public void Wildcards(string pattern, string text, bool expected) => Assert.Equal(expected, GameWindowRule.Wildcard(pattern, text));

    [Fact]
    public void Round_trip_and_normalise()
    {
        var path = _dir.File("settings.json");
        var s = new AppSettings
        {
            Accounts = [new AccountSettings { Tag = "2pq8lv9cy", Nickname = "Main", BuilderBoost = 20, ResearchBoost = 300 }, new AccountSettings { Tag = "#2PQ8LV9CY" }, new AccountSettings { Tag = "bad!" }],
            SelectedAccount = "#NOPE",
            Scale = 3,
            PanelOpacity = 0.1,
            BuildersOverride = 40,
            ProgressMode = ProgressMode.Time,
        };
        s.Hotkeys.Toggle = "Ctrl+Shift+R";
        s.Save(path);

        var loaded = AppSettings.Load(path);

        Assert.Equal("#2PQ8LV9CY", Assert.Single(loaded.Accounts).Tag);
        Assert.Equal("Main", loaded.Accounts[0].Nickname);
        Assert.Equal(new TimeBoosts(20, TimeBoosts.MaxPercent), loaded.Accounts[0].Boosts);
        Assert.DoesNotContain("\"boosts\"", File.ReadAllText(path));
        Assert.Equal("#2PQ8LV9CY", loaded.SelectedAccount);
        Assert.Equal(AppSettings.MaxScale, loaded.Scale);
        Assert.Equal(0.5, loaded.PanelOpacity);
        Assert.Null(loaded.BuildersOverride);
        Assert.Equal(ProgressMode.Time, loaded.ProgressMode);
        Assert.Equal("Ctrl+Shift+R", loaded.Hotkeys.Toggle);
        Assert.Contains("\"Time\"", File.ReadAllText(path)); // enums as names
    }

    [Theory]
    [InlineData("crosvm", "Clash of Clans", true)]
    [InlineData("crosvm", "Clash of Clans - FlamingArrow48", true)]
    [InlineData("crosvm.exe", "Clash of Clans - Chief", true)]
    [InlineData("crosvm", "Brawl Stars", false)]
    [InlineData("chrome", "Clash of Clans - Wikipedia - Google Chrome", false)]
    [InlineData("chrome", "Clash of Clans", false)]
    [InlineData("HD-Player", "BlueStacks App Player", false)]
    public void Default_rules_find_google_play_games(string process, string title, bool found) =>
        Assert.Equal(found, GameWindowRule.Defaults().Any(r => r.Matches(process, title)));

    [Fact]
    public void Rules_saved_by_1_0_are_widened()
    {
        List<GameWindowRule> rules =
        [
            new() { Name = "Picked: Clash of Clans - FlamingArrow48", ProcessName = "crosvm", TitlePattern = "Clash of Clans - FlamingArrow48" },
            new() { Name = "Picked: Clash of Clans - Alt", ProcessName = "crosvm", TitlePattern = "Clash of Clans - Alt" },
            new() { Name = "Google Play Games", ProcessName = "crosvm", TitlePattern = "Clash of Clans" },
            new() { Name = "Picked: BlueStacks App Player", ProcessName = "HD-Player", TitlePattern = "BlueStacks App Player" },
            new() { Name = "My rule", ProcessName = null, TitlePattern = "Clash of Clans - Exact" },
        ];

        Assert.True(GameWindowRule.Upgrade(rules));

        Assert.Equal(4, rules.Count); // the two picks became one
        Assert.Equal("Picked: Clash of Clans", rules[0].Name);
        Assert.Equal("Clash of Clans*", rules[0].TitlePattern);
        Assert.Equal("Clash of Clans*", rules[1].TitlePattern);
        Assert.Equal("BlueStacks App Player", rules[2].TitlePattern);
        Assert.Equal("Clash of Clans - Exact", rules[3].TitlePattern);
        Assert.True(rules[0].Matches("crosvm", "Clash of Clans - SomeoneElse"));
        Assert.False(GameWindowRule.Upgrade(rules)); // only once
    }

    [Fact]
    public void Old_settings_files_are_upgraded_when_loaded()
    {
        var path = _dir.File("settings.json");
        File.WriteAllText(path, """
            { "gameRules": [ { "name": "Google Play Games", "processName": "crosvm", "titlePattern": "Clash of Clans", "enabled": true } ],
              "showDetectionToast": true, "checkForUpdates": true, "updateNotifiedTag": "v1.0.0" }
            """);
        var s = AppSettings.Load(path);
        Assert.Equal("Clash of Clans*", Assert.Single(s.GameRules).TitlePattern);
        Assert.True(s.InstallUpdatesAutomatically);
    }

    [Theory]
    [InlineData("Clash of Clans - FlamingArrow48", "Clash of Clans*")]
    [InlineData("Clash of Clans", "Clash of Clans*")]
    [InlineData("BlueStacks App Player", "BlueStacks App Player")]
    public void Picked_windows_are_saved_without_the_player_name(string title, string pattern) =>
        Assert.Equal(pattern, GameWindowRule.PatternForPicked(title));

    [Fact]
    public void A_half_saved_position_is_dropped()
    {
        var s = new AppSettings { OverlayLeft = 100, OverlayTop = null }.Normalize();
        Assert.Null(s.OverlayLeft);
        var t = new AppSettings { OverlayLeft = -1500, OverlayTop = 20 }.Normalize();
        Assert.Equal(-1500, t.OverlayLeft);
        Assert.Equal(20, t.OverlayTop);
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var s = AppSettings.Load(_dir.File("nope.json"));
        Assert.Equal("Ctrl+R", s.Hotkeys.Toggle);
        Assert.Equal(0.86, s.PanelOpacity);
        Assert.True(s.InstallUpdatesAutomatically);
        Assert.False(s.DeveloperSiteAutoKey);
        Assert.NotEmpty(s.GameRules);
    }

    [Fact]
    public void Damaged_file_is_kept_aside()
    {
        var path = _dir.File("settings.json");
        File.WriteAllText(path, "{ \"scale\": ");
        var s = AppSettings.Load(path);
        Assert.Equal(1.0, s.Scale);
        Assert.True(File.Exists(_dir.File("settings.damaged.json")));
    }

    [Fact]
    public void Settings_never_hold_secrets()
    {
        var path = _dir.File("settings.json");
        new AppSettings { DeveloperSiteAutoKey = true }.Save(path);
        var text = File.ReadAllText(path);
        Assert.DoesNotContain("apiKey", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
    }
}
