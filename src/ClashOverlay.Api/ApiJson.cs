using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClashOverlay.Api;

public static class ApiJson
{
    /// <summary>camelCase, case-insensitive, and the API's <c>yyyyMMddTHHmmss.fffZ</c> times.</summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new ApiTimeConverter() },
    };

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    /// <summary>Parses <c>20261125T080000.000Z</c> (and ISO 8601 as a fallback).</summary>
    public static DateTimeOffset? ParseTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string[] formats = ["yyyyMMdd'T'HHmmss.fff'Z'", "yyyyMMdd'T'HHmmss'Z'"];
        if (DateTimeOffset.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t))
        {
            return t;
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out t) ? t : null;
    }

    public static string FormatTime(DateTimeOffset time) =>
        time.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss.fff'Z'", CultureInfo.InvariantCulture);

    private sealed class ApiTimeConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            ParseTime(reader.GetString()) ?? throw new JsonException("Not an API time: " + reader.GetString());

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(FormatTime(value));
    }
}
