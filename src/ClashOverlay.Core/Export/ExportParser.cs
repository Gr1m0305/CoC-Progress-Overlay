using System.Text;
using System.Text.Json;
using ClashOverlay.Core.Data;

namespace ClashOverlay.Core.Export;

/// <summary>
/// Parses the JSON the player copies from the game (Settings → More Settings → Data Export). Syntax only: ids are
/// checked against the game data by <see cref="Validate"/>, where unknown ones become warnings, never errors.
/// </summary>
public static class ExportParser
{
    public const int MaxBytes = 2_000_000;

    private static readonly Dictionary<string, ExportSection> Sections = new(StringComparer.Ordinal)
    {
        ["buildings"] = ExportSection.Buildings,
        ["traps"] = ExportSection.Traps,
        ["units"] = ExportSection.Units,
        ["siege_machines"] = ExportSection.SiegeMachines,
        ["spells"] = ExportSection.Spells,
        ["heroes"] = ExportSection.Heroes,
        ["pets"] = ExportSection.Pets,
        ["equipment"] = ExportSection.Equipment,
        ["guardians"] = ExportSection.Guardians,
        ["helpers"] = ExportSection.Helpers,
        ["buildings2"] = ExportSection.Buildings2,
        ["traps2"] = ExportSection.Traps2,
        ["units2"] = ExportSection.Units2,
        ["heroes2"] = ExportSection.Heroes2,
    };

    public static VillageExport Parse(byte[] bytes)
    {
        if (bytes.Length > MaxBytes)
        {
            throw new ExportFormatException("That file is too big to be a village export.");
        }

        string text;
        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new ExportFormatException("That file isn't text. Use the JSON copied from the game (a .json or .txt file).");
        }

        return Parse(text);
    }

    public static VillageExport Parse(string raw)
    {
        if (raw.Length > MaxBytes)
        {
            throw new ExportFormatException("That text is too long to be a village export.");
        }

        var text = StripWrapping(raw);
        if (text.Length == 0)
        {
            throw new ExportFormatException("The export is empty. Copy it in the game (Settings → More Settings → Data Export) and paste it here.");
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException ex)
        {
            var cutOff = !text.EndsWith('}') ? " It looks cut off: copy it again and paste the whole thing." : string.Empty;
            throw new ExportFormatException($"That isn't valid JSON (problem near line {ex.LineNumber + 1}).{cutOff}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new ExportFormatException("That JSON isn't a village export (expected an object with a player tag).");
            }

            if (!root.TryGetProperty("tag", out var tagEl) || tagEl.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(tagEl.GetString()))
            {
                throw new ExportFormatException("No player tag found. Make sure you copied the whole village export.");
            }

            var tag = PlayerTag.Normalize(tagEl.GetString());
            if (!PlayerTag.IsValid(tag))
            {
                throw new ExportFormatException($"\"{Truncate(tagEl.GetString()!, 20)}\" isn't a valid player tag.");
            }

            var entries = new List<ExportEntry>();
            foreach (var prop in root.EnumerateObject())
            {
                if (!Sections.TryGetValue(prop.Name, out var section) || prop.Value.ValueKind != JsonValueKind.Array)
                {
                    continue; // decos, obstacles, skins, sceneries, emotes, boosts...: not used
                }

                foreach (var item in prop.Value.EnumerateArray())
                {
                    if (ParseEntry(section, item) is { } entry)
                    {
                        entries.Add(entry);
                    }
                }
            }

            if (!entries.Any(e => e.Section is ExportSection.Buildings or ExportSection.Units or ExportSection.Heroes))
            {
                throw new ExportFormatException("This doesn't look like a village export (no buildings, troops or heroes).");
            }

            DateTimeOffset? timestamp = null;
            if (root.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.Number && ts.TryGetInt64(out var secs) && secs > 0)
            {
                // Seconds; tolerate milliseconds too.
                timestamp = secs > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(secs) : DateTimeOffset.FromUnixTimeSeconds(secs);
            }

            return new VillageExport { Tag = tag, Timestamp = timestamp, Entries = entries, RawJson = text };
        }
    }

    /// <summary>Ids the game data doesn't know (a newer game update): logged and skipped by the caller.</summary>
    public static IReadOnlyList<ExportWarning> Validate(VillageExport export, GameData data)
    {
        var warnings = new List<ExportWarning>();
        var seen = new HashSet<int>();
        foreach (var e in export.Entries)
        {
            if (!data.Contains(e.Id) && seen.Add(e.Id))
            {
                warnings.Add(new ExportWarning(e.Section, e.Id, $"Unknown id {e.Id} in \"{SectionName(e.Section)}\" (newer than the game data?)"));
            }
        }

        return warnings;
    }

    /// <summary>True for text that is probably an export (used to decide whether to try parsing a paste).</summary>
    public static bool LooksLikeExport(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var t = StripWrapping(text);
        return t.StartsWith('{') && t.Contains("\"tag\"", StringComparison.Ordinal);
    }

    public static string SectionName(ExportSection s) => Sections.First(kv => kv.Value == s).Key;

    private static ExportEntry? ParseEntry(ExportSection section, JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var id = Int(item, "data");
        if (id <= 0)
        {
            return null;
        }

        var types = new List<CraftedType>();
        if (item.TryGetProperty("types", out var t) && t.ValueKind == JsonValueKind.Array)
        {
            foreach (var type in t.EnumerateArray())
            {
                var modules = new List<CraftedModule>();
                if (type.TryGetProperty("modules", out var m) && m.ValueKind == JsonValueKind.Array)
                {
                    modules.AddRange(m.EnumerateArray().Select(x => new CraftedModule(Int(x, "data"), Math.Max(0, Int(x, "lvl")))));
                }

                types.Add(new CraftedType(Int(type, "data"), modules));
            }
        }

        long? timer = item.TryGetProperty("timer", out var tm) && tm.ValueKind == JsonValueKind.Number && tm.TryGetInt64(out var tv) && tv > 0 ? tv : null;
        long? cooldown = item.TryGetProperty("helper_cooldown", out var hc) && hc.ValueKind == JsonValueKind.Number && hc.TryGetInt64(out var hv) ? hv : null;
        return new ExportEntry(section, id, Math.Max(0, Int(item, "lvl")), Math.Max(1, Int(item, "cnt", 1)), timer)
        {
            GearUp = Int(item, "gear_up"),
            Supercharge = Int(item, "supercharge"),
            HelperCooldown = cooldown,
            HelperRecurrent = item.TryGetProperty("helper_recurrent", out var hr) && hr.ValueKind == JsonValueKind.True,
            Types = types,
        };
    }

    private static int Int(JsonElement e, string name, int fallback = 0)
    {
        if (!e.TryGetProperty(name, out var v))
        {
            return fallback;
        }

        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt64(out var n) => (int)Math.Clamp(n, int.MinValue, int.MaxValue),
            JsonValueKind.String when int.TryParse(v.GetString(), out var s) => s,
            JsonValueKind.True => 1,
            _ => fallback,
        };
    }

    /// <summary>Removes a byte order mark, surrounding whitespace and a Markdown code fence.</summary>
    internal static string StripWrapping(string text)
    {
        var t = text.Trim().TrimStart('﻿').Trim();
        if (t.StartsWith("```", StringComparison.Ordinal))
        {
            t = t[3..];
            if (t.StartsWith("json", StringComparison.OrdinalIgnoreCase))
            {
                t = t[4..];
            }

            if (t.EndsWith("```", StringComparison.Ordinal))
            {
                t = t[..^3];
            }

            t = t.Trim();
        }

        return t;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
