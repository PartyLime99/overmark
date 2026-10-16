using Overmark.Platform;

namespace Overmark.Capture;

internal enum SelectionHandle
{
    None,
    Move,
    Left,
    Top,
    Right,
    Bottom,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>
/// Editing a selection in physical pixels. Edge pixels are inclusive (the last column is Right - 1), and every
/// result stays inside the bounds it's given.
/// </summary>
internal static class SelectionMath
{
    /// <summary>Which part of <paramref name="selection"/> is at <paramref name="point"/>, within <paramref name="tolerance"/> pixels of an edge.</summary>
    public static SelectionHandle HitTest(ScreenRect selection, PixelPoint point, int tolerance)
    {
        int left = selection.Left, top = selection.Top, right = selection.Right - 1, bottom = selection.Bottom - 1;
        if (point.X < left - tolerance || point.X > right + tolerance || point.Y < top - tolerance || point.Y > bottom + tolerance)
        {
            return SelectionHandle.None;
        }

        var nearLeft = Math.Abs(point.X - left) <= tolerance;
        var nearRight = Math.Abs(point.X - right) <= tolerance;
        var nearTop = Math.Abs(point.Y - top) <= tolerance;
        var nearBottom = Math.Abs(point.Y - bottom) <= tolerance;

        return (nearTop, nearBottom, nearLeft, nearRight) switch
        {
            (true, _, true, _) => SelectionHandle.TopLeft,
            (true, _, _, true) => SelectionHandle.TopRight,
            (_, true, true, _) => SelectionHandle.BottomLeft,
            (_, true, _, true) => SelectionHandle.BottomRight,
            (_, _, true, _) => SelectionHandle.Left,
            (_, _, _, true) => SelectionHandle.Right,
            (true, _, _, _) => SelectionHandle.Top,
            (_, true, _, _) => SelectionHandle.Bottom,
            _ => selection.Contains(point) ? SelectionHandle.Move : SelectionHandle.None,
        };
    }

    /// <summary>The selection after dragging <paramref name="handle"/> by (dx, dy) from where it was at <paramref name="anchor"/>.</summary>
    public static ScreenRect Drag(ScreenRect anchor, SelectionHandle handle, int dx, int dy, ScreenRect bounds)
    {
        if (handle == SelectionHandle.Move)
        {
            return MoveBy(anchor, dx, dy, bounds);
        }

        int left = anchor.Left, top = anchor.Top, right = anchor.Right - 1, bottom = anchor.Bottom - 1;
        if (handle is SelectionHandle.Left or SelectionHandle.TopLeft or SelectionHandle.BottomLeft)
        {
            left = Math.Clamp(left + dx, bounds.Left, right);
        }

        if (handle is SelectionHandle.Right or SelectionHandle.TopRight or SelectionHandle.BottomRight)
        {
            right = Math.Clamp(right + dx, left, bounds.Right - 1);
        }

        if (handle is SelectionHandle.Top or SelectionHandle.TopLeft or SelectionHandle.TopRight)
        {
            top = Math.Clamp(top + dy, bounds.Top, bottom);
        }

        if (handle is SelectionHandle.Bottom or SelectionHandle.BottomLeft or SelectionHandle.BottomRight)
        {
            bottom = Math.Clamp(bottom + dy, top, bounds.Bottom - 1);
        }

        return ScreenRect.FromEdges(left, top, right, bottom);
    }

    /// <summary>
    /// The selection with <paramref name="centre"/> as its middle pixel and <paramref name="corner"/> as one corner.
    /// The opposite corner stops at the edge of <paramref name="bounds"/>, so near an edge it's no longer centred.
    /// </summary>
    public static ScreenRect FromCentre(PixelPoint centre, PixelPoint corner, ScreenRect bounds)
    {
        var opposite = new PixelPoint(
            Math.Clamp(2 * centre.X - corner.X, bounds.Left, bounds.Right - 1),
            Math.Clamp(2 * centre.Y - corner.Y, bounds.Top, bounds.Bottom - 1));
        return ScreenRect.FromCorners(opposite, corner);
    }

    public static ScreenRect MoveBy(ScreenRect selection, int dx, int dy, ScreenRect bounds) => selection with
    {
        Left = Math.Clamp(selection.Left + dx, bounds.Left, bounds.Right - selection.Width),
        Top = Math.Clamp(selection.Top + dy, bounds.Top, bounds.Bottom - selection.Height),
    };

    /// <summary>Grows or shrinks the right and bottom edges, keeping at least one pixel.</summary>
    public static ScreenRect ResizeBy(ScreenRect selection, int dx, int dy, ScreenRect bounds) => selection with
    {
        Width = Math.Clamp(selection.Width + dx, 1, bounds.Right - selection.Left),
        Height = Math.Clamp(selection.Height + dy, 1, bounds.Bottom - selection.Top),
    };
}
