using Overmark.Settings;

namespace Overmark.Hotkeys;

internal static class HotkeyBindings
{
    /// <summary>
    /// Turns the hotkey text from settings into bindings. An invalid entry falls back to its default, and an
    /// entry that clashes with an earlier one is dropped; each fix-up is described in <paramref name="problems"/>.
    /// </summary>
    public static Dictionary<HotkeyAction, Hotkey> FromSettings(HotkeySettings settings, out List<string> problems)
    {
        var defaults = new HotkeySettings();
        var bindings = new Dictionary<HotkeyAction, Hotkey>();
        problems = [];

        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            var text = settings.Get(action);
            if (IsTurnedOff(text))
            {
                continue;
            }

            if (!Hotkey.TryParse(text, out var hotkey))
            {
                var fallback = defaults.Get(action)!;
                problems.Add($"\"{text}\" isn't a valid hotkey for {action.DisplayName()}, so {fallback} is used instead.");
                hotkey = Hotkey.Parse(fallback);
            }

            var clash = bindings.FirstOrDefault(b => b.Value == hotkey);
            if (bindings.ContainsValue(hotkey))
            {
                problems.Add($"{hotkey} is set for both {clash.Key.DisplayName()} and {action.DisplayName()}, " +
                             $"so {action.DisplayName()} has no hotkey.");
                continue;
            }

            bindings[action] = hotkey;
        }

        return bindings;
    }

    private static bool IsTurnedOff(string? text) =>
        string.IsNullOrWhiteSpace(text) || text.Trim().Equals("None", StringComparison.OrdinalIgnoreCase);
}
