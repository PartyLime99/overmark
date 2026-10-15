using System.Globalization;
using Overmark.Settings;

namespace Overmark.Capture;

internal static class FileNamer
{
    public const string DefaultPattern = "yyyy-MM-dd_HH-mm-ss";

    public static string DefaultFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");

    /// <summary>Where captures and recordings are saved.</summary>
    public static string FolderFor(AppSettings settings) => string.IsNullOrWhiteSpace(settings.SaveFolder)
        ? DefaultFolder
        : Environment.ExpandEnvironmentVariables(settings.SaveFolder);

    /// <summary>
    /// A path in <paramref name="folder"/> for a new capture, named from <paramref name="pattern"/> (a .NET
    /// date/time format string). Adds _2, _3, ... when the name is taken.
    /// </summary>
    public static string NextPath(string folder, string? pattern, DateTime time, Func<string, bool> exists, string extension = ".png")
    {
        var name = Sanitize(Format(time, pattern));
        var path = Path.Combine(folder, name + extension);
        for (var n = 2; exists(path); n++)
        {
            path = Path.Combine(folder, $"{name}_{n}{extension}");
        }

        return path;
    }

    private static string Format(DateTime time, string? pattern)
    {
        if (!string.IsNullOrWhiteSpace(pattern))
        {
            try
            {
                return time.ToString(pattern, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                // Fall back to the default below.
            }
        }

        return time.ToString(DefaultPattern, CultureInfo.InvariantCulture);
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return clean.Length > 0 ? clean : "Screenshot";
    }
}
