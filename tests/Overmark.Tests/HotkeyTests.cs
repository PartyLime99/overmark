using System.Windows.Input;
using Overmark.Hotkeys;

namespace Overmark.Tests;

public class HotkeyTests
{
    private const int VkSnapshot = 0x2C;

    [Theory]
    [InlineData("PrintScreen", ModifierKeys.None, VkSnapshot)]
    [InlineData("Shift+PrintScreen", ModifierKeys.Shift, VkSnapshot)]
    [InlineData("ctrl + prtscn", ModifierKeys.Control, VkSnapshot)]
    [InlineData("Ctrl+Alt+F5", ModifierKeys.Control | ModifierKeys.Alt, 0x74)]
    [InlineData("Win+1", ModifierKeys.Windows, 0x31)]
    [InlineData("Shift+a", ModifierKeys.Shift, 0x41)]
    public void Parses_valid_hotkeys(string text, ModifierKeys modifiers, int virtualKey)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.Equal(new Hotkey(modifiers, virtualKey), hotkey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Ctrl+A")]
    [InlineData("Ctrl+A+B")]
    [InlineData("44")]
    [InlineData("A,B")]
    [InlineData("Ctrl+LeftShift")]
    [InlineData("Ctrl+NotAKey")]
    public void Rejects_invalid_hotkeys(string? text)
    {
        Assert.False(Hotkey.TryParse(text, out _));
    }

    [Theory]
    [InlineData("shift+ctrl+prtsc", "Ctrl+Shift+PrintScreen")]
    [InlineData("Win+Alt+7", "Alt+Win+7")]
    [InlineData("F12", "F12")]
    public void Formats_in_canonical_order(string text, string expected)
    {
        Assert.Equal(expected, Hotkey.Parse(text).ToString());
    }

    [Fact]
    public void Display_text_uses_short_print_screen_name()
    {
        Assert.Equal("Shift+PrtScn", Hotkey.Parse("Shift+PrintScreen").DisplayText);
    }
}
