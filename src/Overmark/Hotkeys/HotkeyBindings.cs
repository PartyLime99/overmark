using Overmark.Settings;

namespace Overmark.Hotkeys;

internal static class HotkeyBindings
{
    /// <summary>
    /// Turns the hotkey text from settings into bindings: each action's hotkeys, in the order written. An action can
    /// have several, separated by commas ("PrintScreen, Win+Shift+S"). Invalid entries are dropped, and an action
    /// left with none falls back to its default; a hotkey already used by an earlier action is dropped. Each fix-up
    /// is described in <paramref name="problems"/>. Actions that are turned off are left out.
    /// </summary>
    public static Dictionary<HotkeyAction, IReadOnlyList<Hotkey>> FromSettings(HotkeySettings settings, out List<string> problems)
    {
        var defaults = new HotkeySettings();
        var bindings = new Dictionary<HotkeyAction, IReadOnlyList<Hotkey>>();
        var owners = new Dictionary<Hotkey, HotkeyAction>();
        problems = [];

        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            var hotkeys = new List<Hotkey>();
            var invalid = new List<string>();
            foreach (var entry in Split(settings.Get(action)))
            {
                if (!Hotkey.TryParse(entry, out var hotkey))
                {
                    invalid.Add(entry);
                }
                else if (!hotkeys.Contains(hotkey))
                {
                    hotkeys.Add(hotkey);
                }
            }

            if (invalid.Count > 0)
            {
                var names = string.Join(", ", invalid.Select(entry => $"\"{entry}\""));
                if (hotkeys.Count > 0)
                {
                    problems.Add($"{names} isn't a valid hotkey for {action.DisplayName()}, so it's ignored.");
                }
                else
                {
                    var fallback = defaults.Get(action)!;
                    problems.Add($"{names} isn't a valid hotkey for {action.DisplayName()}, so {fallback} is used instead.");
                    hotkeys.Add(Hotkey.Parse(fallback));
                }
            }

            foreach (var hotkey in hotkeys.ToList())
            {
                if (owners.TryGetValue(hotkey, out var owner))
                {
                    problems.Add($"{hotkey} is set for both {owner.DisplayName()} and {action.DisplayName()}, " +
                                 $"so only {owner.DisplayName()} uses it.");
                    hotkeys.Remove(hotkey);
                }
                else
                {
                    owners[hotkey] = action;
                }
            }

            if (hotkeys.Count > 0)
            {
                bindings[action] = hotkeys;
            }
        }

        return bindings;
    }

    /// <summary>The entries in one action's hotkey text. Empty for an empty value or "None".</summary>
    public static IEnumerable<string> Split(string? text) =>
        (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(entry => !entry.Equals("None", StringComparison.OrdinalIgnoreCase));

    /// <summary>The settings-file form of a list of hotkeys, e.g. "PrintScreen, Win+Shift+S", or "None" for an empty list.</summary>
    public static string Join(IEnumerable<Hotkey> hotkeys)
    {
        var text = string.Join(", ", hotkeys);
        return text.Length > 0 ? text : "None";
    }

    /// <summary>Text for menus and messages, e.g. "PrtScn or Win+Shift+S".</summary>
    public static string Describe(IEnumerable<Hotkey> hotkeys) => string.Join(" or ", hotkeys.Select(h => h.DisplayText));
}
