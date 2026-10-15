using Overmark.Settings;

namespace Overmark.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "OvermarkTests", Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_folder, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Missing_file_is_created_with_defaults()
    {
        var settings = new SettingsStore(SettingsPath).Load(out var problem);

        Assert.Null(problem);
        Assert.True(File.Exists(SettingsPath));
        Assert.Equal("PrintScreen", settings.Hotkeys.RegionCapture);
    }

    [Fact]
    public void Saved_settings_round_trip()
    {
        var store = new SettingsStore(SettingsPath);
        var settings = new AppSettings { SnippingKeyPromptDismissed = true };
        settings.Hotkeys.LastRegion = "Ctrl+Alt+R";
        store.Save(settings);

        var loaded = store.Load(out _);

        Assert.True(loaded.SnippingKeyPromptDismissed);
        Assert.Equal("Ctrl+Alt+R", loaded.Hotkeys.LastRegion);
    }

    [Fact]
    public void A_copy_can_be_changed_without_touching_the_original()
    {
        var original = new AppSettings();
        var copy = SettingsStore.Copy(original);

        copy.Hotkeys.Set(Overmark.Hotkeys.HotkeyAction.FullScreen, "Ctrl+Alt+F");
        copy.PopupSeconds = 9;

        Assert.Equal("Ctrl+PrintScreen", original.Hotkeys.FullScreen);
        Assert.Equal(5, original.PopupSeconds);
        Assert.Equal("Ctrl+Alt+F", copy.Hotkeys.Get(Overmark.Hotkeys.HotkeyAction.FullScreen));
    }

    [Fact]
    public void Older_settings_files_get_the_new_defaults()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, """{ "hotkeys": { "regionCapture": "PrintScreen" }, "adjustBeforeCapture": true }""");

        Assert.True(new SettingsStore(SettingsPath).TryRead(out var settings, out _));
        Assert.True(settings.AdjustBeforeCapture);
        Assert.True(settings.CheckForUpdates);
        Assert.Equal(5, settings.PopupSeconds);
        Assert.Equal(5, settings.Editor.StrokeWidth);
    }

    [Fact]
    public void Plus_signs_are_written_plainly_for_hand_editing()
    {
        new SettingsStore(SettingsPath).Load(out _);

        Assert.Contains("\"Shift+PrintScreen\"", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Hand_edits_with_comments_and_trailing_commas_are_accepted()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, """
            {
              // my keys
              "hotkeys": { "fullScreen": "Win+F", },
            }
            """);

        Assert.True(new SettingsStore(SettingsPath).TryRead(out var settings, out _));
        Assert.Equal("Win+F", settings.Hotkeys.FullScreen);
        Assert.Equal("PrintScreen", settings.Hotkeys.RegionCapture);
    }

    [Fact]
    public void Broken_file_is_set_aside_at_startup()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, "{ not json");

        var settings = new SettingsStore(SettingsPath).Load(out var problem);

        Assert.NotNull(problem);
        Assert.Equal("PrintScreen", settings.Hotkeys.RegionCapture);
        Assert.Single(Directory.GetFiles(_folder, "settings.json.invalid-*"));
    }

    [Fact]
    public void Broken_file_is_left_alone_when_reloading()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, "{ not json");

        Assert.False(new SettingsStore(SettingsPath).TryRead(out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
        Assert.Equal("{ not json", File.ReadAllText(SettingsPath));
    }
}
