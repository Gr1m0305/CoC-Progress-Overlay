using System.Text.RegularExpressions;

namespace ClashOverlay.Core;

/// <summary>
/// A safety net for log text: the API key is never passed to a logger, but anything that looks like a key (a JWT, a
/// bearer header, a password field) is replaced before a line is written.
/// </summary>
public static partial class LogScrubber
{
    public const string Redacted = "[redacted]";

    [GeneratedRegex(@"eyJ[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}")]
    private static partial Regex Jwt();

    [GeneratedRegex(@"(?i)(bearer\s+)[^\s""']+")]
    private static partial Regex Bearer();

    [GeneratedRegex(@"(?i)(""?(?:password|apikey|api_key|token|temporaryAPIToken)""?\s*[:=]\s*""?)[^""\s,}]+")]
    private static partial Regex Field();

    public static string Scrub(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        text = Jwt().Replace(text, Redacted);
        text = Bearer().Replace(text, "$1" + Redacted);
        return Field().Replace(text, "$1" + Redacted);
    }

    public static bool NeedsScrub(string text) => !string.IsNullOrEmpty(text) && Scrub(text) != text;
}
