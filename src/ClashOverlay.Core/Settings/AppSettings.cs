using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ClashOverlay.Core.Calc;
using ClashOverlay.Core.Data;

namespace ClashOverlay.Core.Settings;

public enum NumberFormat
{
    /// <summary>12.3M, 456K.</summary>
    Compact,

    /// <summary>12,345,678.</summary>
    Full,
}

public enum OverlayPlacement
{
    /// <summary>Centred in the work area of the monitor the game is on.</summary>
    CenterOnGameMonitor,

    /// <summary>Centred over the game window itself.</summary>
    OverGameWindow,
}

/// <summary>
/// How to recognise the game's window: a process name and a title pattern (<c>*</c> and <c>?</c> wildcards). Either
/// may be empty to match anything. Only the window's title, process name and rectangle are ever read.
/// </summary>
public sealed class GameWindowRule
{
    public string Name { get; set; } = "";

    /// <summary>Without <c>.exe</c>, case-insensitive.</summary>
    public string? ProcessName { get; set; }

    public string? TitlePattern { get; set; }

    public bool Enabled { get; set; } = true;

    public bool Matches(string processName, string title)
    {
        if (!Enabled || (string.IsNullOrWhiteSpace(ProcessName) && string.IsNullOrWhiteSpace(TitlePattern)))
        {
            return false;
        }

        var exe = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? processName[..^4] : processName;
        var wanted = ProcessName?.Trim() ?? "";
        if (wanted.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            wanted = wanted[..^4];
        }

        if (wanted.Length > 0 && !string.Equals(exe, wanted, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(TitlePattern) || Wildcard(TitlePattern.Trim(), title);
    }

    public static bool Wildcard(string pattern, string text)
    {
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(text, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    }

    /// <summary>
    /// Google Play Games for PC runs the game in <c>crosvm.exe</c> with the window titled "Clash of Clans". Emulators
    /// title their window after the emulator, so those rules match the emulator window.
    /// </summary>
    public static List<GameWindowRule> Defaults() =>
    [
        new() { Name = "Google Play Games", ProcessName = "crosvm", TitlePattern = "Clash of Clans" },
        new() { Name = "BlueStacks", ProcessName = "HD-Player", TitlePattern = "*" },
        new() { Name = "LDPlayer", ProcessName = "dnplayer", TitlePattern = "*" },
        new() { Name = "MEmu", ProcessName = "MEmu", TitlePattern = "*" },
        new() { Name = "NoxPlayer", ProcessName = "Nox", TitlePattern = "*" },
        new() { Name = "MuMu Player", ProcessName = "MuMuPlayer", TitlePattern = "*" },
        new() { Name = "MuMu Player 12", ProcessName = "MuMuNxDevice", TitlePattern = "*" },
        new() { Name = "Any window titled Clash of Clans", ProcessName = null, TitlePattern = "Clash of Clans" },
    ];
}

public sealed class HotkeySettings
{
    public string Toggle { get; set; } = "Ctrl+R";

    public string SwitchVillage { get; set; } = "Ctrl+B";

    public string NextAccount { get; set; } = "Ctrl+Tab";

    /// <summary>Held with 1–9 to jump to an account.</summary>
    public string AccountModifier { get; set; } = "Ctrl";

    public string Close { get; set; } = "Esc";
}

public sealed class AccountSettings
{
    public string Tag { get; set; } = "";

    public string? Nickname { get; set; }

    /// <summary>The name the API last reported, for menus while offline.</summary>
    public string? Name { get; set; }

    public int TownHall { get; set; }

    /// <summary>When the API first showed the current Town Hall level; an export older than this is out of date.</summary>
    public DateTimeOffset? TownHallChangedAt { get; set; }
}

/// <summary><c>%LOCALAPPDATA%\ClashOverlay\settings.json</c>. Never holds the API key or the developer login.</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public int Version { get; set; } = 1;

    public List<AccountSettings> Accounts { get; set; } = [];

    public string? SelectedAccount { get; set; }

    public Village SelectedVillage { get; set; } = Village.Home;

    public HotkeySettings Hotkeys { get; set; } = new();

    public List<GameWindowRule> GameRules { get; set; } = GameWindowRule.Defaults();

    public OverlayPlacement Placement { get; set; } = OverlayPlacement.CenterOnGameMonitor;

    /// <summary>0.75–1.5.</summary>
    public double Scale { get; set; } = 1.0;

    /// <summary>Opacity of the glass panel behind the text, 0.5–1.</summary>
    public double PanelOpacity { get; set; } = 0.86;

    public ProgressMode ProgressMode { get; set; } = ProgressMode.Levels;

    /// <summary>Numbers are always formatted the same way, whatever the Windows language.</summary>
    public NumberFormat NumberFormat { get; set; } = NumberFormat.Compact;

    /// <summary>Builders to divide builder time by; null uses the huts in the export.</summary>
    public int? BuildersOverride { get; set; }

    public bool HideWhenGameMinimised { get; set; } = true;

    public bool ShowDetectionToast { get; set; } = true;

    public bool CheckForUpdates { get; set; } = true;

    public DateTimeOffset? LastUpdateCheck { get; set; }

    /// <summary>Off by default: create and rotate keys on the developer site with the saved login.</summary>
    public bool DeveloperSiteAutoKey { get; set; }

    /// <summary>The public IP the key was last known to work from.</summary>
    public string? LastKnownIp { get; set; }

    public bool SetupCompleted { get; set; }

    public AccountSettings? Selected => Accounts.FirstOrDefault(a => a.Tag == SelectedAccount) ?? Accounts.FirstOrDefault();

    public AppSettings Normalize()
    {
        Scale = double.IsFinite(Scale) ? Math.Clamp(Scale, 0.75, 1.5) : 1.0;
        PanelOpacity = double.IsFinite(PanelOpacity) ? Math.Clamp(PanelOpacity, 0.5, 1.0) : 0.86;
        if (BuildersOverride is < 1 or > 12)
        {
            BuildersOverride = null;
        }

        Hotkeys ??= new HotkeySettings();
        GameRules ??= GameWindowRule.Defaults();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Accounts = (Accounts ?? [])
            .Select(a =>
            {
                a.Tag = PlayerTag.Normalize(a.Tag);
                return a;
            })
            .Where(a => PlayerTag.IsValid(a.Tag) && seen.Add(a.Tag))
            .ToList();
        if (SelectedAccount is not null)
        {
            SelectedAccount = PlayerTag.Normalize(SelectedAccount);
        }

        if (Accounts.All(a => a.Tag != SelectedAccount))
        {
            SelectedAccount = Accounts.FirstOrDefault()?.Tag;
        }

        return this;
    }

    /// <summary>Missing file: defaults. Damaged file: kept as <c>settings.damaged.json</c>, then defaults.</summary>
    public static AppSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            return new AppSettings().Normalize();
        }

        try
        {
            return (JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new AppSettings()).Normalize();
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            try
            {
                File.Copy(path, Path.Combine(Path.GetDirectoryName(path)!, "settings.damaged.json"), overwrite: true);
            }
            catch (IOException)
            {
            }

            return new AppSettings().Normalize();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Normalize(), Json));
        File.Move(tmp, path, overwrite: true);
    }

    public AppSettings Clone() => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, Json), Json)!.Normalize();
}
