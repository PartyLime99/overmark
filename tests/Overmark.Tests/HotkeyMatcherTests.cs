using System.Windows.Input;
using Overmark.Hotkeys;

namespace Overmark.Tests;

public class HotkeyMatcherTests
{
    private const int PrintScreen = 0x2C;
    private const int LetterA = 0x41;
    private const int LetterS = 0x53;

    private static HotkeyMatcher CreateMatcher()
    {
        var matcher = new HotkeyMatcher();
        matcher.SetBindings(new Dictionary<HotkeyAction, IReadOnlyList<Hotkey>>
        {
            [HotkeyAction.RegionCapture] = [new(ModifierKeys.None, PrintScreen), new(ModifierKeys.Windows | ModifierKeys.Shift, LetterS)],
            [HotkeyAction.LastRegion] = [new(ModifierKeys.Shift, PrintScreen)],
        });
        return matcher;
    }

    [Fact]
    public void Press_fires_once_and_release_is_swallowed()
    {
        var matcher = CreateMatcher();

        Assert.Equal(new KeyDecision(true, HotkeyAction.RegionCapture), matcher.KeyDown(PrintScreen, ModifierKeys.None));
        Assert.Equal(new KeyDecision(true, null), matcher.KeyUp(PrintScreen, ModifierKeys.None));
    }

    [Fact]
    public void Each_of_an_actions_hotkeys_fires_it()
    {
        var matcher = CreateMatcher();

        Assert.Equal(HotkeyAction.RegionCapture, matcher.KeyDown(PrintScreen, ModifierKeys.None).Fire);
        matcher.KeyUp(PrintScreen, ModifierKeys.None);
        Assert.Equal(HotkeyAction.RegionCapture, matcher.KeyDown(LetterS, ModifierKeys.Windows | ModifierKeys.Shift).Fire);
        Assert.Equal(new KeyDecision(true, null), matcher.KeyUp(LetterS, ModifierKeys.Windows | ModifierKeys.Shift));
        Assert.Equal(KeyDecision.PassThrough, matcher.KeyDown(LetterS, ModifierKeys.None));
    }

    [Fact]
    public void Auto_repeat_is_swallowed_without_firing_again()
    {
        var matcher = CreateMatcher();
        matcher.KeyDown(PrintScreen, ModifierKeys.None);

        Assert.Equal(new KeyDecision(true, null), matcher.KeyDown(PrintScreen, ModifierKeys.None));
        Assert.Equal(new KeyDecision(true, null), matcher.KeyDown(PrintScreen, ModifierKeys.None));
    }

    [Fact]
    public void Modifiers_must_match_exactly()
    {
        var matcher = CreateMatcher();

        Assert.Equal(HotkeyAction.LastRegion, matcher.KeyDown(PrintScreen, ModifierKeys.Shift).Fire);
        matcher.KeyUp(PrintScreen, ModifierKeys.Shift);
        Assert.Equal(KeyDecision.PassThrough, matcher.KeyDown(PrintScreen, ModifierKeys.Control | ModifierKeys.Shift));
    }

    [Fact]
    public void Other_keys_pass_through()
    {
        var matcher = CreateMatcher();

        Assert.Equal(KeyDecision.PassThrough, matcher.KeyDown(LetterA, ModifierKeys.None));
        Assert.Equal(KeyDecision.PassThrough, matcher.KeyUp(LetterA, ModifierKeys.None));
    }

    [Fact]
    public void Release_still_swallowed_if_modifier_let_go_first()
    {
        var matcher = CreateMatcher();
        matcher.KeyDown(PrintScreen, ModifierKeys.Shift);

        Assert.Equal(new KeyDecision(true, null), matcher.KeyUp(PrintScreen, ModifierKeys.None));
    }

    [Fact]
    public void Release_without_a_seen_press_fires()
    {
        var matcher = CreateMatcher();

        Assert.Equal(new KeyDecision(true, HotkeyAction.RegionCapture), matcher.KeyUp(PrintScreen, ModifierKeys.None));
    }
}
