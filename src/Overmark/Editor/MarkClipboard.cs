using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace Overmark.Editor;

/// <summary>A mark copied to the clipboard. <see cref="Measures"/> is true for a dimension that said what it measured.</summary>
internal sealed record CopiedMark(Mark Mark, bool Measures);

/// <summary>
/// Puts a mark on the clipboard and reads it back, so it can be pasted onto this capture, another one, or one in
/// another editor. It's kept in Overmark's own clipboard format, as JSON in the shape editable copies keep marks in
/// (see <see cref="MarkFormat"/>); other apps see nothing to paste.
/// </summary>
internal static class MarkClipboard
{
    public const string Format = "Overmark.Mark";

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

    public static string ToText(CopiedMark copied) =>
        JsonSerializer.Serialize(new Stored { Mark = MarkFormat.ToData(copied.Mark), Measures = copied.Measures }, JsonOptions);

    /// <summary>The mark <paramref name="text"/> holds, or null if it isn't one this version can read.</summary>
    public static CopiedMark? FromText(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<Stored>(text, JsonOptions) is { Mark: { } data } stored && MarkFormat.FromData(data) is { } mark
                ? new CopiedMark(mark, stored.Measures)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Puts <paramref name="copied"/> on the clipboard, in place of whatever was there. Returns false (saying why) if it couldn't.</summary>
    public static bool TryCopy(CopiedMark copied, out string? problem)
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

    /// <summary>The mark on the clipboard, as it was put there by <see cref="TryCopy"/>, or null if there isn't one.</summary>
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
        public MarkData? Mark { get; set; }

        public bool Measures { get; set; }
    }
}
