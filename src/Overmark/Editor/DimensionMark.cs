using System.Windows;
using System.Windows.Media;

namespace Overmark.Editor;

/// <summary>What a dimension measures between its two points.</summary>
internal enum DimensionKind
{
    /// <summary>The distance across (X), with a horizontal dimension line above or below the points.</summary>
    Horizontal,

    /// <summary>The distance up or down (Y), with a vertical dimension line to one side.</summary>
    Vertical,

    /// <summary>The straight-line distance, with the dimension line parallel to the line between the points.</summary>
    Aligned,
}

/// <summary>The parts of a dimension the move tool can grab.</summary>
internal enum DimensionPart
{
    None,
    Text,
    Line,
    Extension,
}

/// <summary>
/// A drafting-style dimension, like SolidWorks' smart dimension: extension lines from the two measured points
/// <see cref="A"/> and <see cref="B"/>, and a dimension line with arrowheads that passes through <see cref="Through"/>.
/// The style's width is the thickness slider's value; the lines are drawn half as thick and the text four times as big.
/// </summary>
/// <remarks>
/// With no <see cref="TextAt"/>, the text sits in a gap in the middle of the dimension line, or just outside the line if
/// it's too long to fit between the arrows. <see cref="TextAt"/> pins its centre somewhere else: on the line (or the
/// line carried on past the arrows), or anywhere off it, with a leader line back to the dimension line.
/// <see cref="TextAngle"/> turns the text, in degrees clockwise from level.
/// </remarks>
internal sealed record DimensionMark(
    StrokeStyle Style, Point A, Point B, Point Through, DimensionKind Kind, string Text, Point? TextAt = null, double TextAngle = 0) : Mark(Style)
{
    public double LineWidth => Math.Max(1, Style.Width / 2);

    public double FontSize => Palette.StrokeWidth(ToolKind.Text, Style.Width);

    public double HeadLength => Math.Max(8, LineWidth * 5);

    /// <summary>Where everything goes, worked out from the points, text and placement.</summary>
    public DimensionLayout Layout => new(this);

    public override Rect Extent
    {
        get
        {
            var layout = Layout;
            var area = new Rect(A, B);
            foreach (var point in new[] { layout.P1, layout.P2, layout.LineFrom, layout.LineTo })
            {
                area.Union(point);
            }

            if (Text.Length > 0)
            {
                foreach (var corner in layout.TextCorners(0))
                {
                    area.Union(corner);
                }
            }

            return Inflated(area, HeadLength * 0.35);
        }
    }

    /// <summary>
    /// Which of the three kinds a dimension between <paramref name="a"/> and <paramref name="b"/> becomes when it's
    /// placed at <paramref name="pointer"/>: above or below the points measures across, to the left or right
    /// measures up or down, and anywhere else measures the true length.
    /// </summary>
    public static DimensionKind KindFor(Point a, Point b, Point pointer)
    {
        var withinX = pointer.X >= Math.Min(a.X, b.X) && pointer.X <= Math.Max(a.X, b.X);
        var withinY = pointer.Y >= Math.Min(a.Y, b.Y) && pointer.Y <= Math.Max(a.Y, b.Y);
        return (withinX, withinY) switch
        {
            (true, false) => DimensionKind.Horizontal,
            (false, true) => DimensionKind.Vertical,
            _ => DimensionKind.Aligned,
        };
    }

    /// <summary>Turns an angle (degrees) the shortest way to read the right way up: between -90° and 90°.</summary>
    public static double Upright(double degrees)
    {
        degrees = ((degrees % 360) + 360) % 360;
        return degrees > 270 ? degrees - 360 : degrees > 90 ? degrees - 180 : degrees;
    }

    /// <summary>Snaps a text angle to level, upright, along the dimension line or across it, when it's within 8°.</summary>
    public static double SnapAngle(double degrees, double lineAngle)
    {
        foreach (var target in new[] { 0, 90, Upright(lineAngle), Upright(lineAngle + 90) })
        {
            var difference = Math.Abs(degrees - target) % 180;
            if (Math.Min(difference, 180 - difference) < 8)
            {
                return target;
            }
        }

        return degrees;
    }

    public override Mark MovedBy(Vector offset) =>
        this with { A = A + offset, B = B + offset, Through = Through + offset, TextAt = TextAt + offset };

    public override bool IsTouchedBy(Point point, double radius) => Layout.PartAt(point, radius) != DimensionPart.None;

    public override void Draw(DrawingContext dc)
    {
        var layout = Layout;
        var brush = Fill();
        var line = new Pen(brush, LineWidth);
        line.Freeze();
        var thin = new Pen(brush, LineWidth * 0.7);
        thin.Freeze();

        foreach (var (from, to) in layout.ExtensionLines)
        {
            dc.DrawLine(thin, from, to);
        }

        if (layout.Leader is { } leader)
        {
            dc.DrawLine(thin, leader.From, leader.To);
        }

        // The line breaks where the text sits on it.
        var gap = layout.HasText && layout.TextOnLine ? layout.OutsideText() : null;
        if (gap is not null)
        {
            dc.PushClip(gap);
        }

        var (lineFrom, lineTo) = layout.VisibleLine;
        if ((lineTo - lineFrom) * layout.Along > 0)
        {
            dc.DrawLine(line, lineFrom, lineTo);
        }

        if (gap is not null)
        {
            dc.Pop();
        }

        // Inside the extension lines, the arrows point out to them; when there's no room, they sit outside pointing in.
        var inward = layout.ArrowsInside ? -1 : 1;
        dc.DrawGeometry(brush, null, MarkGeometry.ArrowHead(layout.P1, layout.Along * inward, HeadLength, 0.33));
        dc.DrawGeometry(brush, null, MarkGeometry.ArrowHead(layout.P2, -layout.Along * inward, HeadLength, 0.33));

        if (layout.HasText)
        {
            var text = TextMark.Format(Text, FontSize, brush);
            var centre = layout.TextCentre;
            dc.PushTransform(new RotateTransform(layout.TextAngle, centre.X, centre.Y));
            dc.DrawText(text, new Point(centre.X - text.WidthIncludingTrailingWhitespace / 2, centre.Y - text.Height / 2));
            dc.Pop();
        }
    }

    protected override Geometry Centreline()
    {
        var layout = Layout;
        var lines = new GeometryGroup();
        lines.Children.Add(new LineGeometry(layout.LineFrom, layout.LineTo));
        foreach (var (from, to) in layout.ExtensionLines)
        {
            lines.Children.Add(new LineGeometry(from, to));
        }

        return lines;
    }
}

/// <summary>Where each part of a <see cref="DimensionMark"/> goes. All points are image pixels.</summary>
internal sealed class DimensionLayout
{
    private readonly DimensionMark _mark;

    public DimensionLayout(DimensionMark mark)
    {
        _mark = mark;
        var (a, b, through) = (mark.A, mark.B, mark.Through);
        switch (mark.Kind)
        {
            case DimensionKind.Horizontal:
                P1 = new Point(a.X, through.Y);
                P2 = new Point(b.X, through.Y);
                Measured = Math.Abs(b.X - a.X);
                Outward = new Vector(0, through.Y < (a.Y + b.Y) / 2 ? -1 : 1);
                break;
            case DimensionKind.Vertical:
                P1 = new Point(through.X, a.Y);
                P2 = new Point(through.X, b.Y);
                Measured = Math.Abs(b.Y - a.Y);
                Outward = new Vector(through.X < (a.X + b.X) / 2 ? -1 : 1, 0);
                break;
            default:
                var span = b - a;
                Measured = span.Length;
                var normal = Measured > 0 ? new Vector(-span.Y, span.X) / Measured : new Vector(0, -1);
                var offset = (through - a) * normal;
                P1 = a + normal * offset;
                P2 = b + normal * offset;
                Outward = offset < 0 ? -normal : normal;
                break;
        }

        Length = (P2 - P1).Length;
        Along = Length > 0.01 ? (P2 - P1) / Length : new Vector(-Outward.Y, Outward.X);
        ArrowsInside = Length >= mark.HeadLength * 2.6;

        // The text's size, padded a little, measured with a space when there's no text yet (while typing).
        var fontSize = mark.FontSize;
        var text = TextMark.Format(mark.Text.Length > 0 ? mark.Text : " ", fontSize, Brushes.Black);
        TextSize = new Size(text.WidthIncludingTrailingWhitespace + fontSize * 0.4, Math.Max(text.Height, TextMark.LineHeight(fontSize)));
        TextAngle = mark.TextAngle;
        var radians = TextAngle * Math.PI / 180;
        TextX = new Vector(Math.Cos(radians), Math.Sin(radians));
        TextY = new Vector(-Math.Sin(radians), Math.Cos(radians));

        var middle = P1 + Along * (Length / 2);
        if (mark.TextAt is { } at)
        {
            TextCentre = at;
            TextOnLine = Math.Abs((at - P1) * Outward) < 0.5;
        }
        else
        {
            // In the middle of the line if it fits between the arrowheads, otherwise just outside the line.
            TextOnLine = 2 * TextReach(Along) + 2.4 * mark.HeadLength <= Length;
            TextCentre = TextOnLine ? middle : middle + Outward * (TextReach(Outward) + mark.LineWidth * 2 + 2);
        }

        TextAlong = (TextCentre - P1) * Along;

        // The line runs between the arrowheads, on past them when they're outside, and out to text placed beyond them.
        var start = ArrowsInside ? 0 : -2 * mark.HeadLength;
        var end = ArrowsInside ? Length : Length + 2 * mark.HeadLength;
        if (HasText && TextOnLine)
        {
            start = Math.Min(start, TextAlong);
            end = Math.Max(end, TextAlong);
        }

        LineFrom = P1 + Along * start;
        LineTo = P1 + Along * end;

        // Hidden under the arrowheads, so their tips stay sharp.
        var trim = mark.HeadLength * 0.8;
        VisibleLine = (
            ArrowsInside && start == 0 ? P1 + Along * trim : LineFrom,
            ArrowsInside && end == Length ? P1 + Along * (Length - trim) : LineTo);

        var gap = Math.Max(2, mark.LineWidth * 1.5);
        var overshoot = mark.HeadLength * 0.6;
        ExtensionLines = [.. new[] { (a, P1), (b, P2) }
            .Where(pair => (pair.Item2 - pair.Item1).Length > gap)
            .Select(pair =>
            {
                var direction = pair.Item2 - pair.Item1;
                direction.Normalize();
                return (pair.Item1 + direction * gap, pair.Item2 + direction * overshoot);
            })];

        if (HasText && !TextOnLine)
        {
            Leader = LeaderLine();
        }
    }

    /// <summary>Where the dimension line meets the extension lines (and the arrowheads' tips).</summary>
    public Point P1 { get; }

    public Point P2 { get; }

    /// <summary>The distance between the extension lines.</summary>
    public double Length { get; }

    /// <summary>What the dimension measures, in image pixels.</summary>
    public double Measured { get; }

    /// <summary>Halfway along the dimension line.</summary>
    public Point Middle => P1 + Along * (Length / 2);

    /// <summary>A unit vector from <see cref="P1"/> towards <see cref="P2"/>.</summary>
    public Vector Along { get; }

    /// <summary>A unit vector across the line, pointing away from the measured points.</summary>
    public Vector Outward { get; }

    public bool ArrowsInside { get; }

    public bool HasText => _mark.Text.Length > 0;

    public Point TextCentre { get; }

    /// <summary>The space the text takes, a little padded, before it's turned.</summary>
    public Size TextSize { get; }

    public double TextAngle { get; }

    /// <summary>Unit vectors along and down the text, turned by its angle.</summary>
    public Vector TextX { get; }

    public Vector TextY { get; }

    /// <summary>True if the text sits on the dimension line (or its continuation), which then breaks round it.</summary>
    public bool TextOnLine { get; }

    /// <summary>How far along the line the text's centre is, from <see cref="P1"/>.</summary>
    public double TextAlong { get; }

    /// <summary>The whole dimension line, including any part hidden under the arrowheads or the text.</summary>
    public Point LineFrom { get; }

    public Point LineTo { get; }

    /// <summary>The dimension line as drawn: stopping under arrowheads that point outwards.</summary>
    public (Point From, Point To) VisibleLine { get; }

    public IReadOnlyList<(Point From, Point To)> ExtensionLines { get; }

    /// <summary>The line from the dimension line to text that's been moved off it.</summary>
    public (Point From, Point To)? Leader { get; }

    /// <summary>The angle of the dimension line in degrees, for snapping the text to it.</summary>
    public double LineAngle => Math.Atan2(Along.Y, Along.X) * 180 / Math.PI;

    /// <summary>The corners of the text's box, grown by <paramref name="padding"/>.</summary>
    public Point[] TextCorners(double padding)
    {
        var x = TextX * (TextSize.Width / 2 + padding);
        var y = TextY * (TextSize.Height / 2 + padding);
        return [TextCentre - x - y, TextCentre + x - y, TextCentre + x + y, TextCentre - x + y];
    }

    public bool TextContains(Point point, double padding)
    {
        var offset = point - TextCentre;
        return Math.Abs(offset * TextX) <= TextSize.Width / 2 + padding && Math.Abs(offset * TextY) <= TextSize.Height / 2 + padding;
    }

    /// <summary>
    /// Where text dragged to <paramref name="centre"/> should go: onto the dimension line (or its continuation) if
    /// it's within <paramref name="tolerance"/> of it, otherwise exactly there.
    /// </summary>
    public Point SnapText(Point centre, double tolerance)
    {
        var across = (centre - P1) * Outward;
        return Math.Abs(across) < tolerance ? centre - Outward * across : centre;
    }

    /// <summary>The part of the dimension at <paramref name="point"/>, if any, within <paramref name="tolerance"/>.</summary>
    public DimensionPart PartAt(Point point, double tolerance)
    {
        var reach = tolerance + _mark.LineWidth / 2;
        if (HasText && (TextContains(point, tolerance) || (Leader is { } leader && Distance(point, leader.From, leader.To) <= reach)))
        {
            return DimensionPart.Text;
        }

        if (Distance(point, LineFrom, LineTo) <= reach || Distance(point, P1, P1 - Along * _mark.HeadLength) <= reach
            || Distance(point, P2, P2 + Along * _mark.HeadLength) <= reach)
        {
            return DimensionPart.Line;
        }

        return ExtensionLines.Any(line => Distance(point, line.From, line.To) <= reach) ? DimensionPart.Extension : DimensionPart.None;
    }

    /// <summary>Everywhere except the text (and a little round it), so the dimension line can be clipped to stop at it.</summary>
    public Geometry OutsideText()
    {
        var area = new Rect(LineFrom, LineTo);
        area.Union(TextCentre);
        area.Inflate(TextSize.Width + TextSize.Height, TextSize.Width + TextSize.Height);

        var text = new StreamGeometry();
        using (var context = text.Open())
        {
            var corners = TextCorners(_mark.LineWidth);
            context.BeginFigure(corners[0], isFilled: true, isClosed: true);
            context.PolyLineTo(corners[1..], isStroked: false, isSmoothJoin: false);
        }

        var outside = new GeometryGroup { FillRule = FillRule.EvenOdd };
        outside.Children.Add(new RectangleGeometry(area));
        outside.Children.Add(text);
        outside.Freeze();
        return outside;
    }

    private static double Distance(Point point, Point from, Point to)
    {
        var span = to - from;
        var along = span.LengthSquared > 0 ? Math.Clamp((point - from) * span / span.LengthSquared, 0, 1) : 0;
        return (point - (from + span * along)).Length;
    }

    /// <summary>How far the text reaches from its centre in <paramref name="direction"/> (a unit vector).</summary>
    private double TextReach(Vector direction) =>
        Math.Abs(TextSize.Width / 2 * (TextX * direction)) + Math.Abs(TextSize.Height / 2 * (TextY * direction));

    /// <summary>From the nearest point of the dimension line to the edge of the text, or null if they're touching.</summary>
    private (Point From, Point To)? LeaderLine()
    {
        var from = P1 + Along * Math.Clamp(TextAlong, 0, Length);
        var towards = from - TextCentre;
        var x = Math.Abs(towards * TextX);
        var y = Math.Abs(towards * TextY);
        var scale = Math.Min(x > 1e-6 ? (TextSize.Width / 2 + 2) / x : double.MaxValue, y > 1e-6 ? (TextSize.Height / 2 + 2) / y : double.MaxValue);
        if (scale >= 1)
        {
            return null;
        }

        var to = TextCentre + towards * scale;
        return (to - from).Length > 4 ? (from, to) : null;
    }
}
