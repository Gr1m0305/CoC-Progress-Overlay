using System.Globalization;
using ClashOverlay.Core.Settings;

namespace ClashOverlay.Core.Display;

/// <summary>Text for numbers, times and percentages. Always invariant, whatever the Windows language.</summary>
public static class Fmt
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Upgrade times and countdowns: <c>14d 6h</c>, <c>3h 20m</c>, <c>45m</c>, <c>30s</c>.</summary>
    public static string Duration(long seconds)
    {
        if (seconds <= 0)
        {
            return "0s";
        }

        var d = seconds / 86400;
        var h = seconds % 86400 / 3600;
        var m = seconds % 3600 / 60;
        var s = seconds % 60;
        if (d > 0)
        {
            return h > 0 ? $"{d}d {h}h" : $"{d}d";
        }

        if (h > 0)
        {
            return m > 0 ? $"{h}h {m}m" : $"{h}h";
        }

        if (m > 0)
        {
            return m < 10 && s > 0 ? $"{m}m {s}s" : $"{m}m";
        }

        return $"{s}s";
    }

    public static string Duration(TimeSpan span) => Duration((long)Math.Ceiling(span.TotalSeconds));

    /// <summary>"just now", "4 min ago", "3 h ago", "5 d ago".</summary>
    public static string Ago(TimeSpan span)
    {
        if (span < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (span < TimeSpan.FromHours(1))
        {
            return $"{(int)span.TotalMinutes} min ago";
        }

        if (span < TimeSpan.FromHours(48))
        {
            return $"{(int)span.TotalHours} h ago";
        }

        return $"{(int)span.TotalDays} d ago";
    }

    public static string Number(long value, NumberFormat format = NumberFormat.Full)
    {
        if (format == NumberFormat.Full || Math.Abs(value) < 10_000)
        {
            return value.ToString("#,0", Inv);
        }

        var abs = Math.Abs((double)value);
        var (div, suffix) = abs switch
        {
            >= 1e9 => (1e9, "B"),
            >= 1e6 => (1e6, "M"),
            _ => (1e3, "K"),
        };
        var scaled = abs / div;
        // Three significant figures, rounded down so 999,999 never reads as 1000K.
        var decimals = scaled >= 100 ? 0 : scaled >= 10 ? 1 : 2;
        var factor = Math.Pow(10, decimals);
        var shown = Math.Floor(scaled * factor) / factor;
        return (value < 0 ? "-" : "") + shown.ToString("0." + new string('#', decimals), Inv).TrimEnd('.') + suffix;
    }

    public static string Number(int value, NumberFormat format = NumberFormat.Full) => Number((long)value, format);

    /// <summary>One decimal, rounded down, so 99.96% never shows as 100.0%.</summary>
    public static string Percent(double fraction)
    {
        if (double.IsNaN(fraction))
        {
            return "–";
        }

        var tenths = Math.Floor(Math.Clamp(fraction, 0, 1) * 1000 + 1e-9) / 10;
        return tenths.ToString("0.0", Inv) + "%";
    }

    public static string Plural(int n, string one, string? many = null) => n == 1 ? $"{n} {one}" : $"{n} {many ?? one + "s"}";
}
