using Microsoft.Win32;

namespace Overmark.Platform;

/// <summary>
/// Whether Overmark starts when the user signs in to Windows. It's the standard per-user Run registry entry, so
/// it also shows (and can be switched off) in Task Manager's Startup apps.
/// </summary>
internal static class StartWithWindows
{
    /// <summary>Passed on the command line when started at sign-in, so Overmark starts quietly.</summary>
    public const string StartupArgument = "--startup";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Overmark";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" {StartupArgument}");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
