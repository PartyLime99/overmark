namespace Overmark;

internal static class AppInfo
{
    public static string Version { get; } = typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Where releases (and updates) are published.</summary>
    public const string RepositoryUrl = "https://github.com/PartyLime99/overmark";

    /// <summary>Roams with the Windows profile, like other app settings.</summary>
    public static string SettingsFile { get; } = Path.Combine(DataFolder, "settings.json");

    /// <summary>
    /// Next to the settings rather than in Local app data, because the installer owns %LocalAppData%\Overmark.
    /// </summary>
    public static string LogFile { get; } = Path.Combine(DataFolder, "logs", "overmark.log");

    /// <summary>The captures saved recently, for the editor's list of today's captures.</summary>
    public static string HistoryFile { get; } = Path.Combine(DataFolder, "history.json");

    /// <summary>Editable copies of recently edited captures (original pixels and marks), kept for a day.</summary>
    public static string EditsFolder { get; } = Path.Combine(DataFolder, "edits");

    private static string DataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Overmark");
}
