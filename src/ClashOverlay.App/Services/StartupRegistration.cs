using Microsoft.Win32;

namespace ClashOverlay.App.Services;

/// <summary>
/// "Start with Windows": the per-user Run key. The installer's "Start with Windows" box writes the same value, so the
/// two stay in step.
/// </summary>
internal static class StartupRegistration
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "ClashOverlay";

    public static string Command => $"\"{Environment.ProcessPath}\" --startup";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    /// <summary>True when the stored command points at another copy of the app (e.g. after moving the portable build).</summary>
    public static bool PointsElsewhere
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string s && !string.Equals(ExePath(s), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>The program part of a Run command, without quotes or arguments.</summary>
    private static string ExePath(string command)
    {
        var c = command.Trim();
        if (c.StartsWith('"'))
        {
            var end = c.IndexOf('"', 1);
            return end > 0 ? c[1..end] : c.Trim('"');
        }

        var exe = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? c[..(exe + 4)] : c;
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(ValueName, Command);
        }
        else if (key.GetValue(ValueName) is not null)
        {
            key.DeleteValue(ValueName);
        }
    }
}
