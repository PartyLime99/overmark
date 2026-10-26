using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace Overmark.Editor;

/// <summary>A mark copied to the clipboard. <see cref="Measures"/> is true for a dimension that said what it measured.</summary>
internal sealed record CopiedMark(Mark Mark, bool Measures);

/// <summary>
/// Puts marks on the clipboard and reads them back, so they can be pasted onto this capture, another one, or one in
/// another editor. They're kept in Overmark's own clipboard format, as JSON in the shape editable copies keep marks in
/// (see <see cref="MarkFormat"/>); other apps see nothing to paste.
/// </summary>
internal static class MarkClipboard
{
    public const string Format = "Overmark.Marks";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    /// <summary>What a mark is called, for saying what was copied.</summary>
    public static string NameOf(Mark mark) => mark switch
    {
        FreehandMark => "Freehand line",
        HighlighterMark => "Highlight",
        LineMark => "Line",
        ArrowMark => "Arrow",
        CurveMark { Arrowhead: true } => "Curved arrow",
        CurveMark => "Curve",
        RectangleMark => "Rectangle",
        EllipseMark => "Circle",
        RedactMark { Pixelate: true } => "Pixelated box",
        RedactMark => "Solid box",
        TextMark => "Text",
        DimensionMark => "Dimension",
        _ => "Mark",
    };

    public static string ToText(IReadOnlyList<CopiedMark> copied) =>
        JsonSerializer.Serialize(new Stored { Marks = [.. copied.Select(c => new StoredMark { Mark = MarkFormat.ToData(c.Mark), Measures = c.Measures })] }, JsonOptions);

    /// <summary>
    /// The marks <paramref name="text"/> holds, in order, leaving out any of a kind this version can't read; null if
    /// there are none.
    /// </summary>
    public static IReadOnlyList<CopiedMark>? FromText(string text)
    {
        try
        {
            var marks = JsonSerializer.Deserialize<Stored>(text, JsonOptions)?.Marks?
                .Select(stored => stored.Mark is { } data && MarkFormat.FromData(data) is { } mark ? new CopiedMark(mark, stored.Measures) : null)
                .OfType<CopiedMark>()
                .ToList();
            return marks is { Count: > 0 } ? marks : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Puts <paramref name="copied"/> on the clipboard, in place of whatever was there. Returns false (saying why) if it couldn't.</summary>
    public static bool TryCopy(IReadOnlyList<CopiedMark> copied, out string? problem)
    {
        var data = new DataObject();
        data.SetData(Format, ToText(copied), autoConvert: false);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, copy: true);
                problem = null;
                return true;
            }
            catch (ExternalException) when (attempt < 5)
            {
                // Another app has the clipboard open; it's usually free again within moments.
                Thread.Sleep(50);
            }
            catch (ExternalException ex)
            {
                problem = $"Couldn't copy to the clipboard: {ex.Message}";
                return false;
            }
        }
    }

    /// <summary>The marks on the clipboard, as they were put there by <see cref="TryCopy"/>, or null if there aren't any.</summary>
    public static string? Read()
    {
        try
        {
            return Clipboard.ContainsData(Format) ? Clipboard.GetData(Format) as string : null;
        }
        catch (ExternalException ex)
        {
            // Another app has the clipboard open.
            Log.Warn($"Couldn't read the clipboard: {ex.Message}");
            return null;
        }
    }

    private sealed class Stored
    {
        public List<StoredMark>? Marks { get; set; }
    }

    private sealed class StoredMark
    {
        public MarkData? Mark { get; set; }

        public bool Measures { get; set; }
    }
}
