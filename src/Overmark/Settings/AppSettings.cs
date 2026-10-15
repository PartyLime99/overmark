using Overmark.Hotkeys;
using Overmark.Platform;

namespace Overmark.Settings;

/// <summary>Everything stored in settings.json.</summary>
internal sealed class AppSettings
{
    public HotkeySettings Hotkeys { get; set; } = new();

    /// <summary>Where captures are saved. Empty means Pictures\Screenshots.</summary>
    public string? SaveFolder { get; set; } = "";

    /// <summary>File names as a .NET date/time format, without the .png extension.</summary>
    public string? FileNamePattern { get; set; } = "yyyy-MM-dd_HH-mm-ss";

    /// <summary>
    /// False (default): capture as soon as the mouse button is released.
    /// True: after selecting, adjust the selection and press Enter (or double-click) to capture.
    /// </summary>
    public bool AdjustBeforeCapture { get; set; }

    /// <summary>How many seconds the preview popup stays up after a capture. 0 shows a plain notification instead.</summary>
    public double PopupSeconds { get; set; } = 5;

    /// <summary>Whether an installed Overmark checks GitHub for new versions and installs them on the next restart.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>The most recent region or window capture, repeated by Last region.</summary>
    public SavedRegion? LastRegion { get; set; }

    /// <summary>The editor's last-used tool, colour and thickness.</summary>
    public EditorSettings Editor { get; set; } = new();

    /// <summary>True once the user has answered the prompt about Windows' own Print Screen setting.</summary>
    public bool SnippingKeyPromptDismissed { get; set; }
}

/// <summary>Hotkeys as text, e.g. "Shift+PrintScreen". An empty value or "None" turns a hotkey off.</summary>
internal sealed class HotkeySettings
{
    public string? RegionCapture { get; set; } = "PrintScreen";

    public string? LastRegion { get; set; } = "Shift+PrintScreen";

    public string? FullScreen { get; set; } = "Ctrl+PrintScreen";

    public string? Get(HotkeyAction action) => action switch
    {
        HotkeyAction.RegionCapture => RegionCapture,
        HotkeyAction.LastRegion => LastRegion,
        HotkeyAction.FullScreen => FullScreen,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    public void Set(HotkeyAction action, string? hotkey)
    {
        switch (action)
        {
            case HotkeyAction.RegionCapture:
                RegionCapture = hotkey;
                break;
            case HotkeyAction.LastRegion:
                LastRegion = hotkey;
                break;
            case HotkeyAction.FullScreen:
                FullScreen = hotkey;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }
}

/// <summary>The editor's choices, remembered between captures.</summary>
internal sealed class EditorSettings
{
    /// <summary>Freehand, Line, Arrow, Curve, Rectangle or Highlighter.</summary>
    public string? Tool { get; set; } = "Arrow";

    /// <summary>A colour like "#E53935".</summary>
    public string? Colour { get; set; } = "#E53935";

    /// <summary>Line thickness in image pixels, from 1 to 30.</summary>
    public double StrokeWidth { get; set; } = 5;

    public bool CurveArrowhead { get; set; }

    /// <summary>The colour last picked with the custom colour button, if any.</summary>
    public string? CustomColour { get; set; }
}

/// <summary>A desktop rectangle in physical pixels.</summary>
internal sealed class SavedRegion
{
    public int Left { get; set; }

    public int Top { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public static SavedRegion From(ScreenRect rect) => new() { Left = rect.Left, Top = rect.Top, Width = rect.Width, Height = rect.Height };

    public ScreenRect ToScreenRect() => new(Left, Top, Width, Height);
}
