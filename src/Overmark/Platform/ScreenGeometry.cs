namespace Overmark.Platform;

/// <summary>A position on the desktop in physical pixels.</summary>
internal readonly record struct PixelPoint(int X, int Y);

/// <summary>
/// A rectangle in physical screen pixels. Overmark is per-monitor DPI aware, so Windows never scales these.
/// <see cref="Right"/> and <see cref="Bottom"/> are exclusive: the last pixel column is Right - 1.
/// </summary>
internal readonly record struct ScreenRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;

    /// <summary>The rectangle with <paramref name="a"/> and <paramref name="b"/> as opposite corner pixels (both inside it).</summary>
    public static ScreenRect FromCorners(PixelPoint a, PixelPoint b) =>
        FromEdges(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    /// <summary>From inclusive edge pixels: columns left..right and rows top..bottom are all inside.</summary>
    public static ScreenRect FromEdges(int left, int top, int right, int bottom) =>
        new(left, top, right - left + 1, bottom - top + 1);

    public static ScreenRect FromRect(NativeMethods.RECT rect) =>
        new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    public bool Contains(PixelPoint point) =>
        point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;

    /// <summary>The overlap with <paramref name="other"/>, or null if they don't overlap.</summary>
    public ScreenRect? Intersect(ScreenRect other)
    {
        var left = Math.Max(Left, other.Left);
        var top = Math.Max(Top, other.Top);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        return right > left && bottom > top ? new ScreenRect(left, top, right - left, bottom - top) : null;
    }

    public override string ToString() => $"{Width} x {Height} at ({Left}, {Top})";
}
