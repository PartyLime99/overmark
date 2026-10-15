namespace Overmark.Hotkeys;

internal enum HotkeyAction
{
    RegionCapture,
    LastRegion,
    FullScreen,
}

internal static class HotkeyActionExtensions
{
    public static string DisplayName(this HotkeyAction action) => action switch
    {
        HotkeyAction.RegionCapture => "Region capture",
        HotkeyAction.LastRegion => "Last region",
        HotkeyAction.FullScreen => "Full screen",
        _ => action.ToString(),
    };
}
