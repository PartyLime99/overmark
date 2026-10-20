using System.Windows.Media;

namespace Overmark.Editor;

internal enum ToolKind
{
    Move,
    Freehand,
    Line,
    Arrow,
    Curve,
    Rectangle,
    Ellipse,
    Highlighter,

    /// <summary>The redact tool's solid box.</summary>
    Redact,

    /// <summary>The redact tool's pixelated box.</summary>
    Pixelate,
    Text,
    Dimension,
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
    /// The highlighter is a broad marker, so it draws four times as wide. For text it's the font size, also four
    /// times the thickness, so the default thickness of 5 gives 20 px text.
    /// </summary>
    public static double StrokeWidth(ToolKind tool, double thickness) => tool switch
    {
        ToolKind.Highlighter => Math.Max(8, thickness * 4),
        ToolKind.Text => Math.Max(10, thickness * 4),
        _ => thickness,
    };

    /// <summary>
    /// How big the blocks are, in image pixels, when the redact tool pixelates at the slider's thickness: three times
    /// the thickness, so the default of 5 gives 15 px blocks, about the height of a line of text on screen. Never
    /// smaller than 6 px, as small blocks can leave text readable.
    /// </summary>
    public static int BlockSize(double thickness) => (int)Math.Max(6, Math.Round(thickness * 3));

    /// <summary>What goes behind boxed text: white behind dark colours, near-black behind light ones (yellow, white).</summary>
    public static Color Backdrop(Color colour) =>
        0.299 * colour.R + 0.587 * colour.G + 0.114 * colour.B > 160 ? Color.FromRgb(0x20, 0x20, 0x20) : Colors.White;
}
