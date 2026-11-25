using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClashOverlay.AssetFetch;

/// <summary>ClashKing's <c>manifest.json</c>: every asset path with the SHA-256 of the file.</summary>
public sealed class CdnManifest
{
    public CdnManifest(IReadOnlyDictionary<string, string> shaByPath)
    {
        ShaByPath = shaByPath;
    }

    public IReadOnlyDictionary<string, string> ShaByPath { get; }

    /// <summary>Paths in one group, e.g. <c>player_labels</c>.</summary>
    public IEnumerable<string> InFolder(string folder) => ShaByPath.Keys.Where(p => p.StartsWith(folder + "/", StringComparison.Ordinal));

    /// <summary>Reads <c>{"assets": {"&lt;group&gt;": [{"path", "sha"}]}}</c>; unknown fields are ignored.</summary>
    public static CdnManifest Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (doc.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Object)
        {
            foreach (var group in assets.EnumerateObject())
            {
                if (group.Value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var a in group.Value.EnumerateArray())
                {
                    if (a.TryGetProperty("path", out var p) && p.GetString() is { Length: > 0 } path)
                    {
                        map[path] = a.TryGetProperty("sha", out var s) ? s.GetString() ?? "" : "";
                    }
                }
            }
        }

        if (map.Count == 0)
        {
            throw new JsonException("The asset manifest lists no files.");
        }

        return new CdnManifest(map);
    }
}

/// <summary>What is on disk: <c>assets\manifest.json</c>, one entry per downloaded file.</summary>
public sealed class LocalAssetManifest
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string Source { get; set; } = "assets.clashk.ing";

    public DateTimeOffset? UpdatedAt { get; set; }

    public Dictionary<string, LocalAsset> Files { get; set; } = new(StringComparer.Ordinal);

    public static LocalAssetManifest Read(string dir)
    {
        var path = Path.Combine(dir, "manifest.json");
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<LocalAssetManifest>(File.ReadAllText(path), Json) is { } m)
            {
                m.Files = new Dictionary<string, LocalAsset>(m.Files, StringComparer.Ordinal);
                return m;
            }
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            // start over; files on disk are re-checked against the CDN
        }

        return new LocalAssetManifest();
    }

    public void Write(string dir)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "manifest.json");
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, overwrite: true);
    }
}

public sealed class LocalAsset
{
    public string? Sha { get; set; }

    public DateTimeOffset? LastModified { get; set; }

    /// <summary>Path of the PNG relative to the assets folder.</summary>
    public string Local { get; set; } = "";

    [JsonIgnore]
    public bool HasVersion => !string.IsNullOrEmpty(Sha) || LastModified is not null;
}
