using System.Text;

namespace ClashOverlay.Core;

/// <summary>Player and clan tags: <c>#</c> followed by characters from <c>0289PYLQGRJCUV</c>.</summary>
public static class PlayerTag
{
    private const string Alphabet = "0289PYLQGRJCUV";

    /// <summary>Upper case, no spaces, a leading <c>#</c>, and the letter O read as zero.</summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var sb = new StringBuilder("#");
        foreach (var ch in raw.Trim().ToUpperInvariant())
        {
            if (ch is '#' or ' ' or '\t' or ' ')
            {
                continue;
            }

            sb.Append(ch == 'O' ? '0' : ch);
        }

        return sb.ToString();
    }

    public static bool IsValid(string? tag)
    {
        if (tag is null || tag.Length < 4 || tag.Length > 15 || tag[0] != '#')
        {
            return false;
        }

        for (var i = 1; i < tag.Length; i++)
        {
            if (!Alphabet.Contains(tag[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The tag as it goes into an API path: <c>#</c> becomes <c>%23</c>.</summary>
    public static string ForUrl(string tag) => Uri.EscapeDataString(Normalize(tag));

    /// <summary>Safe as a folder name (no <c>#</c>).</summary>
    public static string ForPath(string tag) => Normalize(tag).TrimStart('#');
}
