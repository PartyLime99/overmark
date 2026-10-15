using Overmark.Hotkeys;
using Overmark.Settings;

namespace Overmark.Tests;

public class HotkeyBindingsTests
{
    [Fact]
    public void Defaults_bind_every_action()
    {
        var bindings = HotkeyBindings.FromSettings(new HotkeySettings(), out var problems);

        Assert.Empty(problems);
        Assert.Equal("PrintScreen", bindings[HotkeyAction.RegionCapture].ToString());
        Assert.Equal("Shift+PrintScreen", bindings[HotkeyAction.LastRegion].ToString());
        Assert.Equal("Ctrl+PrintScreen", bindings[HotkeyAction.FullScreen].ToString());
    }

    [Fact]
    public void Invalid_entry_falls_back_to_default()
    {
        var settings = new HotkeySettings { LastRegion = "Shift+Nonsense" };

        var bindings = HotkeyBindings.FromSettings(settings, out var problems);

        Assert.Equal("Shift+PrintScreen", bindings[HotkeyAction.LastRegion].ToString());
        Assert.Single(problems);
    }

    [Fact]
    public void Clashing_entry_is_dropped()
    {
        var settings = new HotkeySettings { FullScreen = "printscreen" };

        var bindings = HotkeyBindings.FromSettings(settings, out var problems);

        Assert.False(bindings.ContainsKey(HotkeyAction.FullScreen));
        Assert.Contains("Region capture", Assert.Single(problems));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("none")]
    public void Empty_or_none_turns_a_hotkey_off(string? text)
    {
        var settings = new HotkeySettings { FullScreen = text };

        var bindings = HotkeyBindings.FromSettings(settings, out var problems);

        Assert.False(bindings.ContainsKey(HotkeyAction.FullScreen));
        Assert.Empty(problems);
    }
}
