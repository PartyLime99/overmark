using System.Windows.Input;

namespace Overmark.Hotkeys;

/// <summary>A key plus modifiers, such as Shift+PrintScreen. <see cref="VirtualKey"/> is a Win32 virtual-key code.</summary>
/// <remarks>
/// <see cref="ModifierKeys"/> flag values match Win32's MOD_ALT, MOD_CONTROL, MOD_SHIFT and MOD_WIN,
/// so they can be passed to RegisterHotKey as they are.
/// </remarks>
internal readonly record struct Hotkey(ModifierKeys Modifiers, int VirtualKey)
{
    private static readonly Dictionary<string, ModifierKeys> ModifierNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl"] = ModifierKeys.Control,
        ["Control"] = ModifierKeys.Control,
        ["Shift"] = ModifierKeys.Shift,
        ["Alt"] = ModifierKeys.Alt,
        ["Win"] = ModifierKeys.Windows,
        ["Windows"] = ModifierKeys.Windows,
    };

    private static readonly Dictionary<string, Key> KeyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PrtScn"] = Key.PrintScreen,
        ["PrtSc"] = Key.PrintScreen,
        ["Esc"] = Key.Escape,
    };

    // Key enum members that share a value with another member; ToString() could return either name.
    private static readonly Dictionary<Key, string> KeyNames = new()
    {
        [Key.PrintScreen] = "PrintScreen",
        [Key.Enter] = "Enter",
        [Key.CapsLock] = "CapsLock",
        [Key.PageUp] = "PageUp",
        [Key.PageDown] = "PageDown",
    };

    /// <summary>Text for menus, e.g. "Shift+PrtScn".</summary>
    public string DisplayText => ToString().Replace("PrintScreen", "PrtScn");

    public static Hotkey Parse(string text) =>
        TryParse(text, out var hotkey) ? hotkey : throw new FormatException($"\"{text}\" isn't a valid hotkey.");

    /// <summary>Parses text such as "Ctrl+Shift+PrintScreen". Names are case-insensitive.</summary>
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = ModifierKeys.None;
        Key? key = null;
        foreach (var rawPart in text.Split('+'))
        {
            var part = rawPart.Trim();
            if (ModifierNames.TryGetValue(part, out var modifier))
            {
                if (modifiers.HasFlag(modifier))
                {
                    return false;
                }

                modifiers |= modifier;
            }
            else if (key is null && TryParseKey(part, out var parsed))
            {
                key = parsed;
            }
            else
            {
                return false;
            }
        }

        if (key is null)
        {
            return false;
        }

        hotkey = new Hotkey(modifiers, KeyInterop.VirtualKeyFromKey(key.Value));
        return true;
    }

    /// <summary>The canonical settings-file form, e.g. "Ctrl+Shift+PrintScreen".</summary>
    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(KeyName(KeyInterop.KeyFromVirtualKey(VirtualKey)));
        return string.Join("+", parts);
    }

    private static bool TryParseKey(string text, out Key key)
    {
        if (KeyAliases.TryGetValue(text, out key))
        {
            return true;
        }

        // A lone digit means that number key, not the Key enum member with that numeric value.
        if (text.Length == 1 && char.IsAsciiDigit(text[0]))
        {
            return Enum.TryParse($"D{text}", out key);
        }

        // Names only: Enum.TryParse would otherwise accept raw numbers and comma-separated lists.
        if (text.Length == 0 || !char.IsAsciiLetter(text[0]) || !text.All(char.IsAsciiLetterOrDigit))
        {
            return false;
        }

        return Enum.TryParse(text, ignoreCase: true, out key) && key != Key.None && !IsModifierKey(key);
    }

    private static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift
        or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System;

    private static string KeyName(Key key) =>
        KeyNames.TryGetValue(key, out var name) ? name
        : key is >= Key.D0 and <= Key.D9 ? ((char)('0' + (key - Key.D0))).ToString()
        : key.ToString();
}
