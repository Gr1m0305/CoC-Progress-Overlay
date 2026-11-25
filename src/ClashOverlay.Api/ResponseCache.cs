using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace ClashOverlay.Api;

/// <param name="MaxAge">How long the entry counts as fresh (decided when it was written).</param>
public sealed record CacheEntry(string Body, DateTimeOffset FetchedAt, TimeSpan MaxAge)
{
    public bool IsFreshAt(DateTimeOffset now) => now - FetchedAt < MaxAge;
}

/// <summary>
/// API responses on disk (<c>cache\&lt;scope&gt;\&lt;key&gt;.json</c>) with the time they were fetched, plus an in-memory copy. The
/// app shows the last good copy when the API is unreachable.
/// </summary>
public sealed class ResponseCache
{
    private readonly string _root;
    private readonly ConcurrentDictionary<string, CacheEntry> _memory = new(StringComparer.Ordinal);

    public ResponseCache(string root)
    {
        _root = root;
    }

    public CacheEntry? Read(string scope, string key)
    {
        var id = Id(scope, key);
        if (_memory.TryGetValue(id, out var hit))
        {
            return hit;
        }

        var path = PathFor(scope, key);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = doc.RootElement;
            var entry = new CacheEntry(
                root.GetProperty("body").GetRawText(),
                root.GetProperty("fetchedAt").GetDateTimeOffset(),
                TimeSpan.FromSeconds(root.TryGetProperty("maxAgeSeconds", out var age) ? age.GetDouble() : 0));
            _memory[id] = entry;
            return entry;
        }
        catch (Exception e) when (e is JsonException or IOException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null; // a damaged cache file is the same as none
        }
    }

    public void Write(string scope, string key, string body, DateTimeOffset fetchedAt, TimeSpan maxAge)
    {
        var entry = new CacheEntry(body, fetchedAt, maxAge);
        _memory[Id(scope, key)] = entry;

        var path = PathFor(scope, key);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms))
            {
                w.WriteStartObject();
                w.WriteString("fetchedAt", fetchedAt);
                w.WriteNumber("maxAgeSeconds", Math.Min(maxAge.TotalSeconds, int.MaxValue));
                w.WritePropertyName("body");
                w.WriteRawValue(body);
                w.WriteEndObject();
            }

            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, ms.ToArray());
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            // memory copy still works; the disk copy is a convenience
        }
    }

    public void Clear(string scope)
    {
        foreach (var id in _memory.Keys.Where(k => k.StartsWith(scope + "/", StringComparison.Ordinal)))
        {
            _memory.TryRemove(id, out _);
        }

        var dir = Path.Combine(_root, Safe(scope));
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private string PathFor(string scope, string key) => Path.Combine(_root, Safe(scope), Safe(key) + ".json");

    private static string Id(string scope, string key) => scope + "/" + key;

    private static string Safe(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            sb.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_');
        }

        return sb.ToString();
    }
}
