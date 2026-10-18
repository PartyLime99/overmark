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
        Assert.Equal("PrintScreen", Assert.Single(bindings[HotkeyAction.RegionCapture]).ToString());
        Assert.Equal("Shift+PrintScreen", Assert.Single(bindings[HotkeyAction.LastRegion]).ToString());
        Assert.Equal("Ctrl+PrintScreen", Assert.Single(bindings[HotkeyAction.FullScreen]).ToString());
    }

    [Fact]
    public void Invalid_entry_falls_back_to_default()
    {
        var settings = new HotkeySettings { LastRegion = "Shift+Nonsense" };

        var bindings = HotkeyBindings.FromSettings(settings, out var problems);

        Assert.Equal("Shift+PrintScreen", Assert.Single(bindings[HotkeyAction.LastRegion]).ToString());
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

    [Fact]
    public void An_action_can_have_several_hotkeys()
    {
        var settings = new HotkeySettings { RegionCapture = "PrintScreen, win+shift+s" };

        var bindings = HotkeyBindings.FromSettings(settings, out var problems);

        Assert.Empty(problems);
        Assert.Equal(new[] { "PrintScreen", "Shift+Win+S" }, bindings[HotkeyAction.RegionCapture].Select(h => h.ToString()).ToArray());
    }

    [Fact]
    public void Invalid_entry_beside_a_valid_one_is_dropped()
    {
        var settings = new HotkeySettings { RegionCapture = "Win+Shift+S, Nonsense" };

        var bindings = HotkeyBindings.FromSettings(settings, out var problems);

        Assert.Equal("Shift+Win+S", Assert.Single(bindings[HotkeyAction.RegionCapture]).ToString());
        Assert.Contains("Nonsense", Assert.Single(problems));
    }

    [Fact]
    public void Clash_drops_only_the_clashing_hotkey()
    {
        var settings = new HotkeySettings { FullScreen = "Ctrl+PrintScreen, PrintScreen" };

        var bindings = HotkeyBindings.FromSettings(settings, out var problems);

        Assert.Equal("PrintScreen", Assert.Single(bindings[HotkeyAction.RegionCapture]).ToString());
        Assert.Equal("Ctrl+PrintScreen", Assert.Single(bindings[HotkeyAction.FullScreen]).ToString());
        Assert.Contains("Region capture", Assert.Single(problems));
    }

    [Fact]
    public void Repeated_hotkey_in_one_action_is_kept_once()
    {
        var settings = new HotkeySettings { RegionCapture = "PrintScreen, PrtScn" };

        var bindings = HotkeyBindings.FromSettings(settings, out var problems);

        Assert.Empty(problems);
        Assert.Single(bindings[HotkeyAction.RegionCapture]);
    }

    [Fact]
    public void Join_writes_a_list_that_reads_back_the_same()
    {
        Hotkey[] hotkeys = [Hotkey.Parse("PrintScreen"), Hotkey.Parse("Win+Shift+S")];

        var text = HotkeyBindings.Join(hotkeys);
        var bindings = HotkeyBindings.FromSettings(new HotkeySettings { RegionCapture = text }, out _);

        Assert.Equal("PrintScreen, Shift+Win+S", text);
        Assert.Equal(hotkeys, bindings[HotkeyAction.RegionCapture].ToArray());
        Assert.Equal("None", HotkeyBindings.Join([]));
        Assert.Equal("PrtScn or Shift+Win+S", HotkeyBindings.Describe(hotkeys));
    }
}
