using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Overmark.Editor;

/// <summary>The colour and width a mark was drawn with. Width is in image pixels.</summary>
internal readonly record struct StrokeStyle(Color Color, double Width);

/// <summary>
/// Something drawn on a capture. Marks stay separate objects until the image is saved, so each one can be
/// undone (and, later, erased) on its own. Coordinates are image pixels.
/// </summary>
internal abstract record Mark(StrokeStyle Style)
{
    public abstract void Draw(DrawingContext dc);

    /// <summary>True if a circle of <paramref name="radius"/> at <paramref name="point"/> touches the drawn stroke.</summary>
    public virtual bool IsTouchedBy(Point point, double radius)
    {
        var reach = new Pen(Brushes.Black, Style.Width + 2 * radius)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        return Centreline().StrokeContains(reach, point);
    }

    /// <summary>The line the stroke is drawn along (the stroke's width is added when testing for a touch).</summary>
    protected abstract Geometry Centreline();

    protected Brush Fill(byte alpha = 0xFF)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, Style.Color.R, Style.Color.G, Style.Color.B));
        brush.Freeze();
        return brush;
    }

    protected Pen RoundPen(byte alpha = 0xFF)
    {
        var pen = new Pen(Fill(alpha), Style.Width)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();
        return pen;
    }
}

internal sealed record FreehandMark(StrokeStyle Style, IReadOnlyList<Point> Points) : Mark(Style)
{
    public override void Draw(DrawingContext dc) => DrawStroke(dc, Points, Style, Fill(), RoundPen());

    protected override Geometry Centreline() => StrokeCentreline(Points);

    internal static Geometry StrokeCentreline(IReadOnlyList<Point> points) =>
        points.Count == 1 ? new EllipseGeometry(points[0], 0.01, 0.01) : MarkGeometry.SmoothPath(points);

    /// <summary>A single point (a click) is drawn as a dot.</summary>
    internal static void DrawStroke(DrawingContext dc, IReadOnlyList<Point> points, StrokeStyle style, Brush fill, Pen pen)
    {
        if (points.Count == 1)
        {
            dc.DrawEllipse(fill, null, points[0], style.Width / 2, style.Width / 2);
        }
        else
        {
            dc.DrawGeometry(null, pen, MarkGeometry.SmoothPath(points));
        }
    }
}

/// <summary>A translucent stroke. Drawn as one path, so where it crosses itself it doesn't get darker.</summary>
internal sealed record HighlighterMark(StrokeStyle Style, IReadOnlyList<Point> Points) : Mark(Style)
{
    public override void Draw(DrawingContext dc) =>
        FreehandMark.DrawStroke(dc, Points, Style, Fill(Palette.HighlighterAlpha), RoundPen(Palette.HighlighterAlpha));

    protected override Geometry Centreline() => FreehandMark.StrokeCentreline(Points);
}

internal sealed record LineMark(StrokeStyle Style, Point Start, Point End) : Mark(Style)
{
    public override void Draw(DrawingContext dc) => dc.DrawLine(RoundPen(), Start, End);

    protected override Geometry Centreline() => new LineGeometry(Start, End);
}

internal sealed record ArrowMark(StrokeStyle Style, Point Start, Point End) : Mark(Style)
{
    public override void Draw(DrawingContext dc)
    {
        var direction = End - Start;
        var headLength = Math.Min(MarkGeometry.HeadLength(Style.Width), direction.Length);
        var shaftEnd = End - direction / direction.Length * (headLength * 0.8);
        dc.DrawLine(RoundPen(), Start, shaftEnd);
        dc.DrawGeometry(Fill(), null, MarkGeometry.ArrowHead(End, direction, headLength));
    }

    protected override Geometry Centreline() => new LineGeometry(Start, End);
}

/// <summary>
/// A quadratic curve from <see cref="Start"/> to <see cref="End"/> that passes through <see cref="Through"/>
/// halfway along, optionally with an arrowhead at the end.
/// </summary>
internal sealed record CurveMark(StrokeStyle Style, Point Start, Point Through, Point End, bool Arrowhead) : Mark(Style)
{
    public override void Draw(DrawingContext dc)
    {
        var control = MarkGeometry.ControlPoint(Start, Through, End);
        if (!Arrowhead)
        {
            dc.DrawGeometry(null, RoundPen(), Curve(Start, control, End));
            return;
        }

        // Point the head along the curve's final direction, and stop the curve under the head.
        var direction = End - control;
        if (direction.LengthSquared < 0.0001)
        {
            direction = End - Start;
        }

        var headLength = Math.Min(MarkGeometry.HeadLength(Style.Width), (End - Start).Length);
        var stop = ParameterAtDistanceFromEnd(control, headLength * 0.8);
        var (partControl, partEnd) = MarkGeometry.SplitAt(Start, control, End, stop);
        dc.DrawGeometry(null, RoundPen(), Curve(Start, partControl, partEnd));
        dc.DrawGeometry(Fill(), null, MarkGeometry.ArrowHead(End, direction, headLength));
    }

    protected override Geometry Centreline() => Curve(Start, MarkGeometry.ControlPoint(Start, Through, End), End);

    private static Geometry Curve(Point start, Point control, Point end)
    {
        var curve = new StreamGeometry();
        using (var context = curve.Open())
        {
            context.BeginFigure(start, isFilled: false, isClosed: false);
            context.QuadraticBezierTo(control, end, isStroked: true, isSmoothJoin: true);
        }

        curve.Freeze();
        return curve;
    }

    /// <summary>The curve parameter about <paramref name="distance"/> (straight-line) back from the end.</summary>
    private double ParameterAtDistanceFromEnd(Point control, double distance)
    {
        double low = 0, high = 1;
        for (var i = 0; i < 30; i++)
        {
            var middle = (low + high) / 2;
            if ((End - MarkGeometry.Quadratic(Start, control, End, middle)).Length > distance)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }
}

/// <summary>An ellipse (a circle when its bounds are square), outline only.</summary>
internal sealed record EllipseMark(StrokeStyle Style, Rect Bounds) : Mark(Style)
{
    public override void Draw(DrawingContext dc)
    {
        var pen = new Pen(Fill(), Style.Width);
        pen.Freeze();
        dc.DrawGeometry(null, pen, Centreline());
    }

    // Only the outline counts, as with rectangles.
    protected override Geometry Centreline() => new EllipseGeometry(Bounds);
}

internal sealed record RectangleMark(StrokeStyle Style, Rect Bounds) : Mark(Style)
{
    public override void Draw(DrawingContext dc)
    {
        var pen = new Pen(Fill(), Style.Width) { LineJoin = PenLineJoin.Miter };
        pen.Freeze();
        dc.DrawRectangle(null, pen, Bounds);
    }

    // Only the outline counts: erasing inside an empty rectangle leaves it alone.
    protected override Geometry Centreline() => new RectangleGeometry(Bounds);
}

/// <summary>
/// Typed text, one or more lines. <see cref="Position"/> is the top-left of the first line, and the style's width is
/// the font size. <see cref="Boxed"/> puts it on a box of a contrasting colour, outlined in the text's colour, so it
/// stays readable on a busy background.
/// </summary>
internal sealed record TextMark(StrokeStyle Style, Point Position, string Text, bool Boxed) : Mark(Style)
{
    public static Typeface Typeface { get; } = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public double FontSize => Style.Width;

    /// <summary>Where the text is: at least one line high, and a little wide even when nothing has been typed yet.</summary>
    public Rect TextArea
    {
        get
        {
            var text = Format(Brushes.Black);
            return new Rect(Position, new Size(Math.Max(text.WidthIncludingTrailingWhitespace, FontSize * 0.3), Math.Max(text.Height, LineHeight(FontSize))));
        }
    }

    /// <summary>Everything that's drawn, the box included.</summary>
    public Rect Bounds
    {
        get
        {
            var area = TextArea;
            if (Boxed)
            {
                area.Inflate(FontSize * 0.35, FontSize * 0.1);
            }

            return area;
        }
    }

    /// <summary>The height of a line of text at <paramref name="fontSize"/>.</summary>
    public static double LineHeight(double fontSize) => Typeface.FontFamily.LineSpacing * fontSize;

    public override void Draw(DrawingContext dc)
    {
        if (Boxed)
        {
            var backdrop = Palette.Backdrop(Style.Color);
            var fill = new SolidColorBrush(Color.FromArgb(0xE6, backdrop.R, backdrop.G, backdrop.B));
            fill.Freeze();
            var outline = new Pen(Fill(), Math.Max(1, FontSize / 12));
            outline.Freeze();
            dc.DrawRoundedRectangle(fill, outline, Bounds, FontSize * 0.2, FontSize * 0.2);
        }

        if (Text.Length > 0)
        {
            dc.DrawText(Format(Fill()), Position);
        }
    }

    // Touching anywhere on the text (or its box) erases it, not just the letters' strokes.
    public override bool IsTouchedBy(Point point, double radius)
    {
        var area = Bounds;
        area.Inflate(radius, radius);
        return area.Contains(point);
    }

    protected override Geometry Centreline() => new RectangleGeometry(Bounds);

    // Image pixels are the unit everywhere, on screen (under the zoom) and when saving, so one pixel per DIP.
    private FormattedText Format(Brush brush) =>
        new(Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface, FontSize, brush, pixelsPerDip: 1);
}
