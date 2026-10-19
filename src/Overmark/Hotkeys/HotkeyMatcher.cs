using System.Windows.Input;

namespace Overmark.Hotkeys;

/// <summary>What the keyboard hook should do with one key event.</summary>
internal readonly record struct KeyDecision(bool Swallow, HotkeyAction? Fire)
{
    public static KeyDecision PassThrough { get; } = new(false, null);
}

/// <summary>
/// Decides which key events are Overmark hotkeys. Free of Win32 calls so it can be unit tested.
/// Not thread-safe: the caller serialises access.
/// </summary>
internal sealed class HotkeyMatcher
{
    private readonly Dictionary<Hotkey, HotkeyAction> _bindings = [];

    // Keys whose press we swallowed. Their auto-repeats and release are swallowed too, so other apps never
    // see half a keystroke.
    private readonly HashSet<int> _held = [];

    public void SetBindings(IReadOnlyDictionary<HotkeyAction, IReadOnlyList<Hotkey>> bindings)
    {
        _bindings.Clear();
        foreach (var (action, hotkeys) in bindings)
        {
            foreach (var hotkey in hotkeys)
            {
                _bindings[hotkey] = action;
            }
        }
    }

    public KeyDecision KeyDown(int virtualKey, ModifierKeys modifiers)
    {
        if (_held.Contains(virtualKey))
        {
            return new KeyDecision(true, null);
        }

        if (!_bindings.TryGetValue(new Hotkey(modifiers, virtualKey), out var action))
        {
            return KeyDecision.PassThrough;
        }

        _held.Add(virtualKey);
        return new KeyDecision(true, action);
    }

    public KeyDecision KeyUp(int virtualKey, ModifierKeys modifiers)
    {
        if (_held.Remove(virtualKey))
        {
            return new KeyDecision(true, null);
        }

        // The press never reached us (e.g. the hook started while the key was held): act on the release instead.
        return _bindings.TryGetValue(new Hotkey(modifiers, virtualKey), out var action)
            ? new KeyDecision(true, action)
            : KeyDecision.PassThrough;
    }
}
