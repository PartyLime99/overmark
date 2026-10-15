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
    public bool IsTouchedBy(Point point, double radius)
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
