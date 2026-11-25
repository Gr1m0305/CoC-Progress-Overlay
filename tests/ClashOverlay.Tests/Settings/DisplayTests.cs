using ClashOverlay.Core;
using ClashOverlay.Core.Display;
using ClashOverlay.Core.Settings;

namespace ClashOverlay.Tests.Settings;

public sealed class DisplayTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, "0s")]
    [InlineData(42, "42s")]
    [InlineData(150, "2m 30s")]
    [InlineData(600, "10m")]
    [InlineData(3600, "1h")]
    [InlineData(12000, "3h 20m")]
    [InlineData(86400, "1d")]
    [InlineData(1231200, "14d 6h")]
    public void Durations(long seconds, string text) => Assert.Equal(text, Fmt.Duration(seconds));

    [Theory]
    [InlineData(20, "just now")]
    [InlineData(4 * 60 + 50, "4 min ago")]
    [InlineData(3 * 3600 + 100, "3 h ago")]
    [InlineData(47 * 3600, "47 h ago")]
    [InlineData(5 * 86400, "5 d ago")]
    public void Ago(int seconds, string text) => Assert.Equal(text, Fmt.Ago(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(0, NumberFormat.Compact, "0")]
    [InlineData(9876, NumberFormat.Compact, "9,876")]
    [InlineData(12345, NumberFormat.Compact, "12.3K")]
    [InlineData(999_999, NumberFormat.Compact, "999K")]
    [InlineData(1_000_000, NumberFormat.Compact, "1M")]
    [InlineData(1_234_567, NumberFormat.Compact, "1.23M")]
    [InlineData(123_456_789, NumberFormat.Compact, "123M")]
    [InlineData(4_500_000_000, NumberFormat.Compact, "4.5B")]
    [InlineData(-25_000, NumberFormat.Compact, "-25K")]
    [InlineData(12_345_678, NumberFormat.Full, "12,345,678")]
    public void Numbers(long value, NumberFormat format, string text) => Assert.Equal(text, Fmt.Number(value, format));

    [Fact]
    public void Numbers_ignore_the_windows_language()
    {
        var old = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal("1,234,567", Fmt.Number(1_234_567));
            Assert.Equal("1.23M", Fmt.Number(1_234_567, NumberFormat.Compact));
            Assert.Equal("96.7%", Fmt.Percent(0.9675));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = old;
        }
    }

    [Theory]
    [InlineData(0.0, "0.0%")]
    [InlineData(0.9675, "96.7%")]
    [InlineData(0.99999, "99.9%")]
    [InlineData(1.0, "100.0%")]
    [InlineData(0.5, "50.0%")]
    public void Percentages(double f, string text) => Assert.Equal(text, Fmt.Percent(f));

    [Fact]
    public void Sync_line_when_both_are_recent()
    {
        var s = SyncStatus.Compute(Now, Now.AddMinutes(-3), false, Now.AddHours(-2), 19, 19, Now.AddDays(-30));
        Assert.Equal("API synced 3 min ago · Export 2 h ago", s.Text);
        Assert.False(s.ExportWarning);
    }

    [Fact]
    public void Export_older_than_three_days_warns()
    {
        var s = SyncStatus.Compute(Now, Now, false, Now.AddDays(-3).AddMinutes(-1), 19, 19, null);
        Assert.True(s.ExportWarning);
        Assert.Contains("3 days", s.WarningReason);
        Assert.False(SyncStatus.Compute(Now, Now, false, Now.AddDays(-3).AddMinutes(1), 19, 19, null).ExportWarning);
    }

    [Fact]
    public void Export_from_before_the_last_hall_change_warns()
    {
        var s = SyncStatus.Compute(Now, Now, false, Now.AddHours(-5), 19, 19, Now.AddHours(-1));
        Assert.True(s.ExportWarning);
        Assert.Contains("Town Hall changed", s.WarningReason);

        var lower = SyncStatus.Compute(Now, Now, false, Now.AddHours(-1), 18, 19, null);
        Assert.True(lower.ExportWarning);
        Assert.Contains("Town Hall 19", lower.WarningReason);
    }

    [Fact]
    public void Missing_sources()
    {
        var s = SyncStatus.Compute(Now, null, false, null, 0, 0, null);
        Assert.Equal("API not synced · No export", s.Text);
        Assert.True(s.ExportWarning);
    }

    [Fact]
    public void Hidden_overlay_takes_only_the_toggle_key_and_only_over_the_game()
    {
        var keys = new HotkeySettings();
        Assert.Empty(HotkeyPlan.Wanted(keys, gameForeground: false, overlayVisible: false, accounts: 3));
        var hidden = HotkeyPlan.Wanted(keys, gameForeground: true, overlayVisible: false, accounts: 3);
        Assert.Equal([HotkeyAction.Toggle], hidden.Keys);
        Assert.Equal("Ctrl+R", hidden[HotkeyAction.Toggle].ToString());
    }

    [Fact]
    public void Visible_overlay_adds_village_account_and_close_keys()
    {
        var wanted = HotkeyPlan.Wanted(new HotkeySettings(), gameForeground: false, overlayVisible: true, accounts: 3);
        Assert.Equal("Ctrl+B", wanted[HotkeyAction.SwitchVillage].ToString());
        Assert.Equal("Ctrl+Tab", wanted[HotkeyAction.NextAccount].ToString());
        Assert.Equal("Ctrl+3", wanted[HotkeyAction.Account3].ToString());
        Assert.False(wanted.ContainsKey(HotkeyAction.Account4));
        Assert.Equal("Esc", wanted[HotkeyAction.Close].ToString());

        var single = HotkeyPlan.Wanted(new HotkeySettings(), true, true, 1);
        Assert.False(single.ContainsKey(HotkeyAction.NextAccount));
        Assert.False(single.ContainsKey(HotkeyAction.Account1));
    }

    [Fact]
    public void Hotkey_problems()
    {
        Assert.Empty(HotkeyPlan.Problems(new HotkeySettings()));
        var bad = new HotkeySettings { Toggle = "Ctrl+B", SwitchVillage = "Ctrl+B", Close = "Banana", AccountModifier = "Q" };
        var problems = HotkeyPlan.Problems(bad);
        Assert.Contains(problems, p => p.Contains("already used by Show / hide"));
        Assert.Contains(problems, p => p.Contains("Banana"));
        Assert.Contains(problems, p => p.Contains("modifiers only"));
        Assert.Contains(HotkeyPlan.Problems(new HotkeySettings { Toggle = "" }), p => p.Contains("no key set"));
    }

    [Fact]
    public void Overlay_centres_on_the_game_monitor()
    {
        var work = new PixelRect(1920, 0, 2560, 1400);
        var (rect, fit) = OverlayGeometry.Place(work, new PixelRect(0, 0, 800, 600), OverlayPlacement.CenterOnGameMonitor, 1260, 760);
        Assert.Equal(1.0, fit);
        Assert.Equal(new PixelRect(1920 + 650, 320, 1260, 760), rect);
    }

    [Fact]
    public void Overlay_over_the_game_window_stays_inside_the_work_area()
    {
        var work = new PixelRect(0, 0, 1920, 1040);
        var (centred, _) = OverlayGeometry.Place(work, new PixelRect(400, 100, 1400, 900), OverlayPlacement.OverGameWindow, 1260, 760);
        Assert.Equal(new PixelRect(470, 170, 1260, 760), centred);

        var (clamped, _) = OverlayGeometry.Place(work, new PixelRect(1500, 800, 400, 200), OverlayPlacement.OverGameWindow, 1260, 760);
        Assert.Equal(1920 - 1260, clamped.Left);
        Assert.Equal(1040 - 760, clamped.Top);
    }

    [Fact]
    public void Overlay_shrinks_to_fit_a_small_screen()
    {
        var work = new PixelRect(0, 0, 1366, 728);
        var (rect, fit) = OverlayGeometry.Place(work, null, OverlayPlacement.CenterOnGameMonitor, 1890, 1140);
        Assert.True(fit < 1);
        Assert.True(rect.Width <= 1366 - 2 * OverlayGeometry.Margin);
        Assert.True(rect.Height <= 728 - 2 * OverlayGeometry.Margin);
        Assert.True(rect.Left >= 0 && rect.Top >= 0);
    }

    [Fact]
    public void Dragged_overlay_stays_where_it_was_put()
    {
        var work = new PixelRect(0, 0, 1920, 1040);
        var (rect, fit) = OverlayGeometry.PlaceAt(work, 300, 120, 1260, 760);
        Assert.Equal(new PixelRect(300, 120, 1260, 760), rect);
        Assert.Equal(1.0, fit);
    }

    [Fact]
    public void Dragged_overlay_is_brought_back_on_screen()
    {
        var work = new PixelRect(0, 0, 1920, 1040);
        var (off, _) = OverlayGeometry.PlaceAt(work, 1500, 900, 1260, 760);
        Assert.Equal(new PixelRect(660, 280, 1260, 760), off);
        var (left, _) = OverlayGeometry.PlaceAt(work, -2000, -50, 1260, 760);
        Assert.Equal(0, left.Left);
        Assert.Equal(0, left.Top);
        var (tooBig, fit) = OverlayGeometry.PlaceAt(work, 0, 0, 2520, 1520);
        Assert.True(fit < 1);
        Assert.True(tooBig.Width <= 1920 && tooBig.Height <= 1040);
    }

    [Theory]
    [InlineData(ResizeCorner.BottomRight, 126, 76, 100, 50, 1386, 836)]
    [InlineData(ResizeCorner.BottomRight, -126, -76, 100, 50, 1134, 684)]
    [InlineData(ResizeCorner.TopLeft, -126, -76, -26, -26, 1386, 836)]
    [InlineData(ResizeCorner.TopRight, 126, -76, 100, -26, 1386, 836)]
    [InlineData(ResizeCorner.BottomLeft, -126, 76, -26, 50, 1386, 836)]
    public void Corners_resize_and_keep_the_shape(ResizeCorner corner, int dx, int dy, int left, int top, int width, int height)
    {
        var start = new PixelRect(100, 50, 1260, 760);
        var r = OverlayGeometry.Resize(start, corner, dx, dy, 630, 2520);
        Assert.Equal(new PixelRect(left, top, width, height), r);
        Assert.Equal(OverlayGeometry.BaseWidth / OverlayGeometry.BaseHeight, r.Width / (double)r.Height, 2);
    }

    [Fact]
    public void Resizing_stops_at_the_limits()
    {
        var start = new PixelRect(0, 0, 1260, 760);
        Assert.Equal(630, OverlayGeometry.Resize(start, ResizeCorner.BottomRight, -5000, -5000, 630, 1900).Width);
        Assert.Equal(1900, OverlayGeometry.Resize(start, ResizeCorner.BottomRight, 5000, 5000, 630, 1900).Width);

        // Dragging the top-left corner in keeps the bottom-right corner where it was.
        var small = OverlayGeometry.Resize(start, ResizeCorner.TopLeft, 5000, 5000, 630, 1900);
        Assert.Equal(1260, small.Right);
        Assert.Equal(760, small.Bottom);
    }

    [Fact]
    public void Scrubber_hides_keys()
    {
        var jwt = "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzUxMiJ9.eyJpc3MiOiJzdXBlcmNlbGwiLCJhdWQiOiJzdXBlcmNlbGw6Z2FtZWFwaSJ9.c2lnbmF0dXJlc2lnbmF0dXJl";
        Assert.Equal("key [redacted] end", LogScrubber.Scrub($"key {jwt} end"));
        Assert.Equal("Authorization: Bearer [redacted]", LogScrubber.Scrub("Authorization: Bearer abc.def"));
        Assert.Equal("{\"password\":\"[redacted]\"}", LogScrubber.Scrub("{\"password\":\"hunter2\"}"));
        Assert.Equal("Player #2PQ8LV9CY loaded", LogScrubber.Scrub("Player #2PQ8LV9CY loaded"));
    }
}
