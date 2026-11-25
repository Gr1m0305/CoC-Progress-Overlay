namespace ClashOverlay.Core.Settings;

public enum HotkeyAction
{
    Toggle,
    SwitchVillage,
    NextAccount,
    Account1,
    Account2,
    Account3,
    Account4,
    Account5,
    Account6,
    Account7,
    Account8,
    Account9,
    Close,
}

/// <summary>
/// Which hotkeys are registered when. Nothing is registered unless the game is the foreground window or the overlay is
/// showing. With the overlay hidden only the toggle key is taken, so the game keeps every other key; while it shows,
/// the village, account and close keys are added.
/// </summary>
public static class HotkeyPlan
{
    public static IReadOnlyDictionary<HotkeyAction, HotkeyGesture> Wanted(HotkeySettings keys, bool gameForeground, bool overlayVisible, int accounts)
    {
        var wanted = new Dictionary<HotkeyAction, HotkeyGesture>();
        if (!gameForeground && !overlayVisible)
        {
            return wanted;
        }

        Add(wanted, HotkeyAction.Toggle, keys.Toggle);
        if (!overlayVisible)
        {
            return wanted;
        }

        Add(wanted, HotkeyAction.SwitchVillage, keys.SwitchVillage);
        if (accounts > 1)
        {
            Add(wanted, HotkeyAction.NextAccount, keys.NextAccount);
            for (var i = 1; i <= Math.Min(9, accounts); i++)
            {
                Add(wanted, HotkeyAction.Account1 + (i - 1), AccountGesture(keys, i));
            }
        }

        Add(wanted, HotkeyAction.Close, keys.Close);
        return wanted;
    }

    public static string AccountGesture(HotkeySettings keys, int n) =>
        string.IsNullOrWhiteSpace(keys.AccountModifier) ? n.ToString(System.Globalization.CultureInfo.InvariantCulture) : $"{keys.AccountModifier}+{n}";

    /// <summary>Problems to show in settings: keys that do not parse, or two actions on the same key.</summary>
    public static IReadOnlyList<string> Problems(HotkeySettings keys)
    {
        var problems = new List<string>();
        var seen = new Dictionary<HotkeyGesture, string>();

        void Check(string label, string? text, bool required)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                if (required)
                {
                    problems.Add($"{label}: no key set.");
                }

                return;
            }

            if (!HotkeyGesture.TryParse(text, out var g))
            {
                problems.Add($"{label}: \"{text}\" is not a key combination.");
                return;
            }

            if (seen.TryGetValue(g, out var other))
            {
                problems.Add($"{label} uses {g}, which is already used by {other}.");
            }
            else
            {
                seen[g] = label;
            }
        }

        Check("Show / hide", keys.Toggle, required: true);
        Check("Switch village", keys.SwitchVillage, required: false);
        Check("Next account", keys.NextAccount, required: false);
        Check("Close", keys.Close, required: false);
        if (!string.IsNullOrWhiteSpace(keys.AccountModifier))
        {
            var mods = keys.AccountModifier.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (mods.Any(m => m.ToUpperInvariant() is not ("CTRL" or "CONTROL" or "ALT" or "SHIFT" or "WIN" or "WINDOWS")))
            {
                problems.Add($"Account keys: \"{keys.AccountModifier}\" should be modifiers only, such as Ctrl or Ctrl+Shift.");
            }
            else
            {
                for (var i = 1; i <= 9; i++)
                {
                    Check($"Account {i}", AccountGesture(keys, i), required: false);
                }
            }
        }

        return problems;
    }

    private static void Add(Dictionary<HotkeyAction, HotkeyGesture> wanted, HotkeyAction action, string? text)
    {
        if (HotkeyGesture.TryParse(text, out var g) && !wanted.ContainsValue(g))
        {
            wanted[action] = g;
        }
    }
}
