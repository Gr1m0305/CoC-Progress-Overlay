using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClashOverlay.Core.Data;

/// <summary><c>data\manifest.json</c>: where the downloaded static data came from.</summary>
public sealed class StaticDataManifest
{
    public const string FileName = "manifest.json";

    public string Source { get; set; } = "ClashKing";

    public string Repository { get; set; } = ClashKingDataSource.Repository;

    /// <summary>Git commit of the data (null when it came from the CDN fallback).</summary>
    public string? Commit { get; set; }

    public DateTimeOffset? CommitDate { get; set; }

    /// <summary>"github" or "cdn".</summary>
    public string Origin { get; set; } = "github";

    public DateTimeOffset DownloadedAt { get; set; }

    public List<StaticDataFile> Files { get; set; } = [];

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static StaticDataManifest? TryRead(string dataDir)
    {
        var path = Path.Combine(dataDir, FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<StaticDataManifest>(File.ReadAllText(path), Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Write(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        var path = Path.Combine(dataDir, FileName);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Options));
        File.Move(tmp, path, overwrite: true);
    }
}

public sealed class StaticDataFile
{
    public string Name { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;

    public long Size { get; set; }
}
