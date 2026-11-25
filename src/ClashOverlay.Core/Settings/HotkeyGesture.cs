using System.Globalization;

namespace ClashOverlay.Core.Settings;

/// <summary>Same values as the Win32 <c>MOD_*</c> flags.</summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>A key combination such as <c>Ctrl+R</c>; <see cref="Key"/> is a Win32 virtual-key code.</summary>
public readonly record struct HotkeyGesture(KeyModifiers Modifiers, int Key)
{
    private static readonly Dictionary<string, int> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Tab"] = 0x09,
        ["Enter"] = 0x0D,
        ["Return"] = 0x0D,
        ["Esc"] = 0x1B,
        ["Escape"] = 0x1B,
        ["Space"] = 0x20,
        ["PageUp"] = 0x21,
        ["PageDown"] = 0x22,
        ["End"] = 0x23,
        ["Home"] = 0x24,
        ["Left"] = 0x25,
        ["Up"] = 0x26,
        ["Right"] = 0x27,
        ["Down"] = 0x28,
        ["Insert"] = 0x2D,
        ["Delete"] = 0x2E,
        ["`"] = 0xC0,
        ["Backtick"] = 0xC0,
    };

    public bool IsEmpty => Key == 0;

    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var mods = KeyModifiers.None;
        int? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL":
                    mods |= KeyModifiers.Ctrl;
                    continue;
                case "ALT":
                    mods |= KeyModifiers.Alt;
                    continue;
                case "SHIFT":
                    mods |= KeyModifiers.Shift;
                    continue;
                case "WIN" or "WINDOWS":
                    mods |= KeyModifiers.Win;
                    continue;
            }

            if (key is not null)
            {
                return false; // two keys
            }

            key = KeyCode(raw);
            if (key is null)
            {
                return false;
            }
        }

        if (key is null)
        {
            return false;
        }

        gesture = new HotkeyGesture(mods, key.Value);
        return true;
    }

    public static HotkeyGesture Parse(string text) =>
        TryParse(text, out var g) ? g : throw new FormatException($"Not a key combination: {text}");

    private static int? KeyCode(string name)
    {
        if (Named.TryGetValue(name, out var code))
        {
            return code;
        }

        if (name.Length == 1 && char.IsAsciiLetterOrDigit(name[0]))
        {
            return char.ToUpperInvariant(name[0]);
        }

        if (name.Length is 2 or 3 && (name[0] is 'F' or 'f') && int.TryParse(name[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var f) && f is >= 1 and <= 24)
        {
            return 0x70 + f - 1;
        }

        if (name.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && name.Length == 4 && char.IsAsciiDigit(name[3]))
        {
            return 0x60 + (name[3] - '0');
        }

        return null;
    }

    public override string ToString()
    {
        if (IsEmpty)
        {
            return "";
        }

        var parts = new List<string>();
        if (Modifiers.HasFlag(KeyModifiers.Ctrl))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(KeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(KeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(KeyModifiers.Win))
        {
            parts.Add("Win");
        }

        parts.Add(KeyName(Key));
        return string.Join('+', parts);
    }

    public static string KeyName(int key) => key switch
    {
        0x09 => "Tab",
        0x0D => "Enter",
        0x1B => "Esc",
        0x20 => "Space",
        0x21 => "PageUp",
        0x22 => "PageDown",
        0x23 => "End",
        0x24 => "Home",
        0x25 => "Left",
        0x26 => "Up",
        0x27 => "Right",
        0x28 => "Down",
        0x2D => "Insert",
        0x2E => "Delete",
        0xC0 => "`",
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)key).ToString(),
        >= 0x60 and <= 0x69 => "Num" + (key - 0x60),
        >= 0x70 and <= 0x87 => "F" + (key - 0x6F),
        _ => "0x" + key.ToString("X2", CultureInfo.InvariantCulture),
    };
}
