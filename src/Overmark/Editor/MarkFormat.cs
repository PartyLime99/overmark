using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Overmark.Editor;

/// <summary>
/// A mark as stored in an editable copy: one flat shape for every kind of mark, so the JSON stays simple.
/// Points are x, y pairs and bounds are x, y, width, height, all in image pixels.
/// </summary>
internal sealed class MarkData
{
    public string Type { get; set; } = "";

    /// <summary>#AARRGGBB.</summary>
    public string Colour { get; set; } = "";

    public double Width { get; set; }

    public double[]? Points { get; set; }

    public double[]? Bounds { get; set; }

    public string? Text { get; set; }

    public bool Boxed { get; set; }

    public bool Arrowhead { get; set; }

    /// <summary>For a dimension: Horizontal, Vertical or Aligned.</summary>
    public string? Kind { get; set; }

    /// <summary>For a dimension whose text was moved: where it was put.</summary>
    public double[]? TextAt { get; set; }

    public double TextAngle { get; set; }
}

/// <summary>Turns marks into <see cref="MarkData"/> and back, so they can be kept editable between editor sessions.</summary>
internal static class MarkFormat
{
    public static MarkData ToData(Mark mark)
    {
        var data = new MarkData
        {
            Colour = $"#{mark.Style.Color.A:X2}{mark.Style.Color.R:X2}{mark.Style.Color.G:X2}{mark.Style.Color.B:X2}",
            Width = mark.Style.Width,
        };

        switch (mark)
        {
            case FreehandMark m:
                data.Type = "freehand";
                data.Points = Flatten(m.Points);
                break;
            case HighlighterMark m:
                data.Type = "highlighter";
                data.Points = Flatten(m.Points);
                break;
            case LineMark m:
                data.Type = "line";
                data.Points = Flatten([m.Start, m.End]);
                break;
            case ArrowMark m:
                data.Type = "arrow";
                data.Points = Flatten([m.Start, m.End]);
                break;
            case CurveMark m:
                data.Type = "curve";
                data.Points = Flatten([m.Start, m.Through, m.End]);
                data.Arrowhead = m.Arrowhead;
                break;
            case RectangleMark m:
                data.Type = "rectangle";
                data.Bounds = [m.Bounds.X, m.Bounds.Y, m.Bounds.Width, m.Bounds.Height];
                break;
            case EllipseMark m:
                data.Type = "ellipse";
                data.Bounds = [m.Bounds.X, m.Bounds.Y, m.Bounds.Width, m.Bounds.Height];
                break;
            case TextMark m:
                data.Type = "text";
                data.Points = Flatten([m.Position]);
                data.Text = m.Text;
                data.Boxed = m.Boxed;
                break;
            case DimensionMark m:
                data.Type = "dimension";
                data.Points = Flatten([m.A, m.B, m.Through]);
                data.Kind = m.Kind.ToString();
                data.Text = m.Text;
                data.TextAt = m.TextAt is { } at ? [at.X, at.Y] : null;
                data.TextAngle = m.TextAngle;
                break;
            default:
                throw new NotSupportedException($"Can't keep a {mark.GetType().Name}");
        }

        return data;
    }

    /// <summary>The mark <paramref name="data"/> describes, or null if it's incomplete or of a kind this version doesn't know.</summary>
    public static Mark? FromData(MarkData data)
    {
        if (ParseColour(data.Colour) is not { } colour || !double.IsFinite(data.Width) || data.Width <= 0)
        {
            return null;
        }

        var style = new StrokeStyle(colour, data.Width);
        var points = Points(data.Points);
        return data.Type switch
        {
            "freehand" when points.Count > 0 => new FreehandMark(style, points),
            "highlighter" when points.Count > 0 => new HighlighterMark(style, points),
            "line" when points.Count == 2 => new LineMark(style, points[0], points[1]),
            "arrow" when points.Count == 2 => new ArrowMark(style, points[0], points[1]),
            "curve" when points.Count == 3 => new CurveMark(style, points[0], points[1], points[2], data.Arrowhead),
            "rectangle" when Bounds(data.Bounds) is { } bounds => new RectangleMark(style, bounds),
            "ellipse" when Bounds(data.Bounds) is { } bounds => new EllipseMark(style, bounds),
            "text" when points.Count == 1 && data.Text is { } text => new TextMark(style, points[0], text, data.Boxed),
            "dimension" when points.Count == 3 && Enum.TryParse<DimensionKind>(data.Kind, out var kind) && Enum.IsDefined(kind) =>
                new DimensionMark(style, points[0], points[1], points[2], kind, data.Text ?? "", Points(data.TextAt) is [var at] ? at : null, data.TextAngle),
            _ => null,
        };
    }

    private static double[] Flatten(IEnumerable<Point> points) => [.. points.SelectMany(p => new[] { p.X, p.Y })];

    private static List<Point> Points(double[]? values)
    {
        var points = new List<Point>();
        if (values is null || values.Length % 2 != 0 || !values.All(double.IsFinite))
        {
            return points;
        }

        for (var i = 0; i < values.Length; i += 2)
        {
            points.Add(new Point(values[i], values[i + 1]));
        }

        return points;
    }

    private static Rect? Bounds(double[]? values) =>
        values is { Length: 4 } && values.All(double.IsFinite) && values[2] >= 0 && values[3] >= 0
            ? new Rect(values[0], values[1], values[2], values[3])
            : null;

    private static Color? ParseColour(string? text)
    {
        if (text is not { Length: 9 } || text[0] != '#' || !uint.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
        {
            return null;
        }

        return Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
    }
}
