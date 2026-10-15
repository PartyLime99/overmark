using System.Windows.Media;

namespace Overmark.Editor;

internal enum ToolKind
{
    Freehand,
    Line,
    Arrow,
    Curve,
    Rectangle,
    Ellipse,
    Highlighter,
    Eraser,
    Crop,
}

/// <summary>The fixed colours and the thickness range offered in the editor.</summary>
internal static class Palette
{
    public const double MinimumThickness = 1;
    public const double MaximumThickness = 30;
    public const double DefaultThickness = 5;

    /// <summary>Highlighter strokes are this opaque (out of 255), so text underneath stays readable.</summary>
    public const byte HighlighterAlpha = 0x66;

    public static IReadOnlyList<Color> Colours { get; } =
    [
        Color.FromRgb(0xE5, 0x39, 0x35), // red
        Color.FromRgb(0xFB, 0x8C, 0x00), // orange
        Color.FromRgb(0xFD, 0xD8, 0x35), // yellow
        Color.FromRgb(0x43, 0xA0, 0x47), // green
        Color.FromRgb(0x1E, 0x88, 0xE5), // blue
        Color.FromRgb(0x8E, 0x24, 0xAA), // purple
        Color.FromRgb(0x00, 0x00, 0x00), // black
        Color.FromRgb(0xFF, 0xFF, 0xFF), // white
    ];

    public static IReadOnlyList<string> ColourNames { get; } = ["Red", "Orange", "Yellow", "Green", "Blue", "Purple", "Black", "White"];

    /// <summary>
    /// The stroke width, in image pixels, that <paramref name="tool"/> draws with at the slider's thickness.
    /// The highlighter is a broad marker, so it draws four times as wide.
    /// </summary>
    public static double StrokeWidth(ToolKind tool, double thickness) =>
        tool == ToolKind.Highlighter ? Math.Max(8, thickness * 4) : thickness;
}
