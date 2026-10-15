namespace Overmark;

internal static class CommandLine
{
    /// <summary>
    /// The file to open in the editor, as a full path: from <c>--edit &lt;file&gt;</c>, or a lone file path (which is
    /// how Windows passes a file to "Open with"). Null when the arguments don't ask for that.
    /// </summary>
    public static string? EditPath(IReadOnlyList<string> args)
    {
        var path = args switch
        {
            [var flag, var file] when flag.Equals("--edit", StringComparison.OrdinalIgnoreCase) => file,
            [var file] when !file.StartsWith('-') => file,
            _ => null,
        };

        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
