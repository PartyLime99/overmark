using System.Diagnostics.CodeAnalysis;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Overmark.Settings;

/// <summary>
/// Reads and writes settings.json. The file is meant to be hand-editable, so comments and trailing commas are allowed.
/// </summary>
internal sealed class SettingsStore(string filePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // Write "+" as itself rather than as a Unicode escape, so the file is easy to edit by hand.
        // The escaping only matters when JSON is embedded in HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private string? _lastWritten;

    public string FilePath { get; } = filePath;

    /// <summary>True if the file still holds exactly what <see cref="Save"/> last wrote, i.e. a change notification was our own write.</summary>
    public bool IsOurLastWrite()
    {
        try
        {
            return _lastWritten is not null && File.ReadAllText(FilePath) == _lastWritten;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Loads settings at startup, creating the file with defaults if it's missing. An unreadable file is renamed
    /// (so nothing is lost) and replaced with defaults, and <paramref name="problem"/> says what happened.
    /// </summary>
    public AppSettings Load(out string? problem)
    {
        problem = null;
        if (File.Exists(FilePath))
        {
            if (TryRead(out var settings, out var error))
            {
                return settings;
            }

            var backup = $"{FilePath}.invalid-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(FilePath, backup, overwrite: true);
            problem = $"settings.json couldn't be read ({error}). It was renamed to {Path.GetFileName(backup)} " +
                      "and the default settings were restored.";
        }

        var defaults = new AppSettings();
        Save(defaults);
        return defaults;
    }

    /// <summary>Reads settings without changing the file. Returns false, with the reason, if the JSON is invalid.</summary>
    public bool TryRead([NotNullWhen(true)] out AppSettings? settings, [NotNullWhen(false)] out string? error)
    {
        try
        {
            settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
            settings.Hotkeys ??= new HotkeySettings();
            settings.Editor ??= new EditorSettings();
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            settings = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>An independent copy, e.g. for the settings window to edit until Save is clicked.</summary>
    public static AppSettings Copy(AppSettings settings) =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, JsonOptions), JsonOptions)!;

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(temp, json);
        File.Move(temp, FilePath, overwrite: true);
        _lastWritten = json;
    }
}
