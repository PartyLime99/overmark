using System.Diagnostics;
using Microsoft.Win32;

namespace Overmark.Platform;

/// <summary>
/// Windows' "Use the Print screen key to open screen capture" setting, which hands Print Screen to Snipping Tool.
/// </summary>
internal static class SnippingKeySetting
{
    private const string KeyPath = @"Control Panel\Keyboard";
    private const string ValueName = "PrintScreenKeyForSnippingEnabled";

    /// <summary>True or false when Windows has stored the setting; null when it hasn't.</summary>
    public static bool? IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(ValueName) is int value ? value != 0 : null;
    }

    /// <summary>Opens Settings > Accessibility > Keyboard, where the user can switch it off.</summary>
    public static void OpenWindowsSetting() =>
        Process.Start(new ProcessStartInfo("ms-settings:easeofaccess-keyboard") { UseShellExecute = true });
}
