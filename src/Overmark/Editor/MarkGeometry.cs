using System.Windows;
using System.Windows.Media;

namespace Overmark.Editor;

/// <summary>
/// The handles the move tool shows on a selected mark, for reshaping it: the ends of a line, arrow, curve or
/// dimension (and a curve's bend in the middle), and the corners and sides of a box.
/// </summary>
internal enum ShapeHandle
{
    Start,
    Through,
    End,
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
}

/// <summary>Shape maths for marks. All coordinates are image pixels.</summary>
internal static class MarkGeometry
{
    /// <summary>The handles of a box: its four corners and the middle of each side.</summary>
    public static IReadOnlyList<(ShapeHandle Handle, Point At)> BoxHandles(Rect box)
    {
        var middle = Midpoint(box.TopLeft, box.BottomRight);
        return
        [
            (ShapeHandle.TopLeft, box.TopLeft),
            (ShapeHandle.Top, new Point(middle.X, box.Top)),
            (ShapeHandle.TopRight, box.TopRight),
            (ShapeHandle.Right, new Point(box.Right, middle.Y)),
            (ShapeHandle.BottomRight, box.BottomRight),
            (ShapeHandle.Bottom, new Point(middle.X, box.Bottom)),
            (ShapeHandle.BottomLeft, box.BottomLeft),
            (ShapeHandle.Left, new Point(box.Left, middle.Y)),
        ];
    }

    /// <summary>
    /// <paramref name="box"/> with the corner or side <paramref name="handle"/> dragged to <paramref name="to"/>, the
    /// opposite corner or side staying put. A corner dragged with <paramref name="square"/> makes a square. Dragged
    /// past the other side, it turns inside out, as when drawing.
    /// </summary>
    public static Rect Resized(Rect box, ShapeHandle handle, Point to, bool square) => handle switch
    {
        ShapeHandle.TopLeft => RectangleFrom(box.BottomRight, to, square),
        ShapeHandle.TopRight => RectangleFrom(box.BottomLeft, to, square),
        ShapeHandle.BottomRight => RectangleFrom(box.TopLeft, to, square),
        ShapeHandle.BottomLeft => RectangleFrom(box.TopRight, to, square),
        ShapeHandle.Top => new Rect(new Point(box.Left, to.Y), box.BottomRight),
        ShapeHandle.Bottom => new Rect(box.TopLeft, new Point(box.Right, to.Y)),
        ShapeHandle.Left => new Rect(new Point(to.X, box.Top), box.BottomRight),
        ShapeHandle.Right => new Rect(box.TopLeft, new Point(to.X, box.Bottom)),
        _ => box,
    };

    /// <summary>
    /// Moves <paramref name="end"/> so the line from <paramref name="start"/> runs at the nearest multiple of 45°,
    /// keeping as much of its length along that direction as possible.
    /// </summary>
    public static Point SnapAngle(Point start, Point end)
    {
        var offset = end - start;
        if (offset.LengthSquared == 0)
        {
            return end;
        }

        var step = Math.PI / 4;
        var angle = Math.Round(Math.Atan2(offset.Y, offset.X) / step) * step;

        // Rounding removes the tiny error in cos(90°) etc., so straight lines stay exactly straight.
        var direction = new Vector(Math.Round(Math.Cos(angle), 12), Math.Round(Math.Sin(angle), 12));
        return start + direction * (offset * direction);
    }

    /// <summary>The rectangle dragged from <paramref name="start"/> to <paramref name="end"/>, made square (in the drag direction) if asked.</summary>
    public static Rect RectangleFrom(Point start, Point end, bool square)
    {
        if (square)
        {
            var side = Math.Max(Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
            end = new Point(start.X + side * Sign(end.X - start.X), start.Y + side * Sign(end.Y - start.Y));
        }

        return new Rect(start, end);
    }

    public static Point Midpoint(Point a, Point b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    /// <summary>
    /// The control point of the quadratic curve from <paramref name="start"/> to <paramref name="end"/> that passes
    /// through <paramref name="through"/> halfway along. Dragging that point is how the user bends a curve.
    /// </summary>
    public static Point ControlPoint(Point start, Point through, Point end) =>
        new(2 * through.X - (start.X + end.X) / 2, 2 * through.Y - (start.Y + end.Y) / 2);

    public static Point Quadratic(Point p0, Point p1, Point p2, double t)
    {
        var u = 1 - t;
        return new Point(
            u * u * p0.X + 2 * u * t * p1.X + t * t * p2.X,
            u * u * p0.Y + 2 * u * t * p1.Y + t * t * p2.Y);
    }

    /// <summary>The first part of a quadratic curve, from its start to parameter <paramref name="t"/>.</summary>
    public static (Point Control, Point End) SplitAt(Point p0, Point p1, Point p2, double t) =>
        (p0 + (p1 - p0) * t, Quadratic(p0, p1, p2, t));

    /// <summary>How long an arrowhead is for a stroke of the given width.</summary>
    public static double HeadLength(double strokeWidth) => Math.Max(10, strokeWidth * 4);

    /// <summary>
    /// A filled arrowhead with its point at <paramref name="tip"/>, facing along <paramref name="direction"/>.
    /// A stroke ending <c>0.8 × length</c> back from the tip is hidden under it, so the tip stays sharp.
    /// <paramref name="spread"/> is half its width as a fraction of its length.
    /// </summary>
    public static Geometry ArrowHead(Point tip, Vector direction, double length, double spread = 0.45)
    {
        direction.Normalize();
        var normal = new Vector(-direction.Y, direction.X);
        var back = tip - direction * length;
        var halfWidth = length * spread;

        var head = new StreamGeometry();
        using (var context = head.Open())
        {
            context.BeginFigure(tip, isFilled: true, isClosed: true);
            context.LineTo(back + normal * halfWidth, isStroked: true, isSmoothJoin: false);
            context.LineTo(back - normal * halfWidth, isStroked: true, isSmoothJoin: false);
        }

        head.Freeze();
        return head;
    }

    /// <summary>A smooth path through freehand points, using each point as a control point between midpoints.</summary>
    public static Geometry SmoothPath(IReadOnlyList<Point> points)
    {
        var path = new StreamGeometry();
        using (var context = path.Open())
        {
            context.BeginFigure(points[0], isFilled: false, isClosed: false);
            if (points.Count == 2)
            {
                context.LineTo(points[1], isStroked: true, isSmoothJoin: true);
            }
            else
            {
                for (var i = 1; i < points.Count - 1; i++)
                {
                    context.QuadraticBezierTo(points[i], Midpoint(points[i], points[i + 1]), isStroked: true, isSmoothJoin: true);
                }

                context.LineTo(points[^1], isStroked: true, isSmoothJoin: true);
            }
        }

        path.Freeze();
        return path;
    }

    private static int Sign(double value) => value < 0 ? -1 : 1;
}
