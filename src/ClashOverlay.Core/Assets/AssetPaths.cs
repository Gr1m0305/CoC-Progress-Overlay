using System.Text;
using ClashOverlay.Core.Data;

namespace ClashOverlay.Core.Assets;

/// <summary>
/// Where ClashKing keeps each image (paths under <c>https://assets.clashk.ing/</c>, see DATA_NOTES §4) and where the app
/// keeps its PNG copy under <c>%LOCALAPPDATA%\ClashOverlay\assets\</c>. Nothing here is downloaded or committed.
/// </summary>
public static class AssetPaths
{
    public const string CdnBase = "https://assets.clashk.ing/";
    public const string ManifestUrl = CdnBase + "manifest.json";

    /// <summary>UI icons the overlay uses from ClashKing's <c>icons/</c> folder.</summary>
    public static readonly IReadOnlyList<string> UiIcons =
    [
        "icons/Icon_HV_Trophy.png", "icons/Icon_HV_Trophy_Best.png", "icons/Icon_HV_Clan_War.png", "icons/Icon_HV_Gold_Pass.png",
        "icons/Icon_DC_CWL.png", "icons/Icon_HV_Raid_Attack.png", "icons/Icon_HV_XP.png", "icons/Icon_HV_Attack_Star.png",
        "icons/Icon_HV_Shield.png", "icons/Icon_HV_Sword.png", "icons/Icon_CC_Resource_Capital_Gold_small.png",
        "icons/Icon_HV_Clan_Games_Medal.png", "icons/Icon_BB_Star.png", "icons/Icon_BB_Empty_Star.png", "icons/Icon_Unknown_Troop.png",
    ];

    /// <summary>ClashKing's name clean-up: lower case, spaces to <c>_</c>, no <c>.</c>, <c>?</c> or <c>’</c>; straight apostrophes and hyphens stay.</summary>
    public static string Clean(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            switch (ch)
            {
                case ' ':
                    sb.Append('_');
                    break;
                case '.' or '?' or '’':
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>League names with a trailing Roman numeral use a digit: "Legend League I" is <c>legend_league_1</c>.</summary>
    public static string CleanLeague(string name)
    {
        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 1)
        {
            parts[^1] = parts[^1] switch
            {
                "I" => "1",
                "II" => "2",
                "III" => "3",
                "IV" => "4",
                "V" => "5",
                var other => other,
            };
        }

        return Clean(string.Join(' ', parts));
    }

    private static string VillageFolder(Village v) => v == Village.Home ? "home-village" : "builder-base";

    /// <summary>The image for an item at a level (buildings and traps have one per level; units one icon).</summary>
    public static string ForItem(ItemDef item, int level)
    {
        var name = Clean(item.Name);
        return item.Kind switch
        {
            ItemKind.Trap => $"traps/{VillageFolder(item.Village)}/{name}/level_{Math.Max(1, level)}.webp",
            ItemKind.Building or ItemKind.Wall or ItemKind.TownHall or ItemKind.BuilderHall =>
                $"buildings/{VillageFolder(item.Village)}/{name}/level_{Math.Max(1, level)}.webp",
            ItemKind.Troop or ItemKind.Siege => $"troops/{name}/icon.webp",
            ItemKind.Hero => $"heroes/{name}/icon.webp",
            ItemKind.Pet => $"pets/{name}/icon.webp",
            ItemKind.Spell => $"spells/{name}.webp",
            ItemKind.Equipment => $"equipment/{name}.webp",
            ItemKind.Guardian => $"guardians/{name}/icon.webp",
            ItemKind.Helper => $"helpers/{name}.webp",
            _ => $"unknown/{name}.webp",
        };
    }

    public static string HeroPortrait(ItemDef hero) => $"heroes/{Clean(hero.Name)}/portrait.webp";

    public static string? ForResource(Resource r) => r switch
    {
        Resource.Gold => "resources/gold.webp",
        Resource.Elixir => "resources/elixir.webp",
        Resource.DarkElixir => "resources/dark_elixir.webp",
        Resource.BuilderGold => "resources/builder_gold.webp",
        Resource.BuilderElixir => "resources/builder_elixir.webp",
        Resource.Gems => "resources/gems.webp",
        Resource.ShinyOre => "resources/shiny_ore.webp",
        Resource.GlowyOre => "resources/glowy_ore.webp",
        Resource.StarryOre => "resources/starry_ore.webp",
        _ => null,
    };

    public static string LeagueTier(string name) => $"leagues/league-tier/{CleanLeague(name)}.png";

    public static string BuilderLeague(string name) => $"leagues/builder-base/{CleanLeague(name)}.png";

    public static string WarLeague(string name) => $"leagues/cwl/{CleanLeague(name)}.png";

    public static string CapitalLeague(string name) => $"leagues/capital-leagues/{CleanLeague(name)}.png";

    public static string PlayerLabel(string name) => $"player_labels/{Clean(name)}.webp";

    /// <summary>The local copy is always a PNG: <c>spells/rage_spell.webp</c> is kept as <c>spells\rage_spell.png</c>.</summary>
    public static string LocalRelative(string remotePath)
    {
        var dot = remotePath.LastIndexOf('.');
        var stem = dot > remotePath.LastIndexOf('/') ? remotePath[..dot] : remotePath;
        return (stem + ".png").Replace('/', Path.DirectorySeparatorChar);
    }

    /// <summary>Every image the static data implies, plus resources, leagues and the UI icons.</summary>
    public static IReadOnlyCollection<string> Wanted(GameData data)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in data.Items)
        {
            if (item.Kind is ItemKind.Building or ItemKind.Trap or ItemKind.Wall or ItemKind.TownHall or ItemKind.BuilderHall)
            {
                foreach (var level in item.Levels)
                {
                    set.Add(ForItem(item, level.Level));
                }
            }
            else
            {
                set.Add(ForItem(item, 1));
                if (item.Kind == ItemKind.Hero)
                {
                    set.Add(HeroPortrait(item));
                }
            }
        }

        foreach (var r in Enum.GetValues<Resource>())
        {
            if (ForResource(r) is { } p)
            {
                set.Add(p);
            }
        }

        foreach (var l in data.LeagueTiers)
        {
            set.Add(LeagueTier(l.Name));
        }

        foreach (var l in data.BuilderLeagues)
        {
            set.Add(BuilderLeague(l.Name));
        }

        foreach (var l in data.WarLeagues)
        {
            set.Add(WarLeague(l.Name));
        }

        set.Add(WarLeague("Unranked"));
        set.UnionWith(UiIcons);
        return set;
    }
}

/// <summary>Finds the local PNG for an item, falling back to the nearest lower level that exists.</summary>
public sealed class AssetResolver
{
    private readonly string _root;
    private readonly Func<string, bool> _exists;

    public AssetResolver(string assetsRoot, Func<string, bool>? exists = null)
    {
        _root = assetsRoot;
        _exists = exists ?? File.Exists;
    }

    public string Root => _root;

    /// <summary>Full path of the best local image, or null (the UI then draws a placeholder with the initials).</summary>
    public string? Item(ItemDef item, int level)
    {
        if (item.Kind is ItemKind.Building or ItemKind.Trap or ItemKind.Wall or ItemKind.TownHall or ItemKind.BuilderHall)
        {
            // a building not built yet (level 0) shows its first level
            for (var l = Math.Max(1, level); l >= 1; l--)
            {
                if (Local(AssetPaths.ForItem(item, l)) is { } hit)
                {
                    return hit;
                }
            }

            return null;
        }

        return Local(AssetPaths.ForItem(item, level));
    }

    public string? Path(string remotePath) => Local(remotePath);

    private string? Local(string remotePath)
    {
        var full = System.IO.Path.Combine(_root, AssetPaths.LocalRelative(remotePath));
        return _exists(full) ? full : null;
    }

    /// <summary>"Barbarian King" becomes "BK"; used on placeholder tiles.</summary>
    public static string Initials(string name)
    {
        var words = name.Split([' ', '-', '.'], StringSplitOptions.RemoveEmptyEntries);
        return words.Length switch
        {
            0 => "?",
            1 => words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant(),
            _ => string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0]))),
        };
    }
}
