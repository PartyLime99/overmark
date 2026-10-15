using static Overmark.Platform.NativeMethods;

namespace Overmark.Platform;

/// <summary>The mouse pointer, in physical desktop pixels.</summary>
internal static class Pointer
{
    public static PixelPoint Position => GetCursorPos(out var point) ? new PixelPoint(point.X, point.Y) : default;

    public static void MoveTo(PixelPoint point) => SetCursorPos(point.X, point.Y);
}
