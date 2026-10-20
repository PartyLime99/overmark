using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Overmark.Editor;

namespace Overmark.Tests;

public class RedactTests
{
    private const double Tolerance = 8;

    private static readonly StrokeStyle Black = new(Palette.Colours[6], 4); // 12 px blocks

    private readonly DrawingTools _tools = new();
    private readonly List<Mark> _committed = [];

    public RedactTests() => _tools.Committed += _committed.Add;

    /// <summary>A picture whose left 12 columns are black and white stripes, one pixel each, and the rest solid red.</summary>
    private static BitmapSource Striped(int width = 24, int height = 12)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var at = (y * width + x) * 4;
                (pixels[at], pixels[at + 1], pixels[at + 2]) = x < 12 ? x % 2 == 0 ? ((byte)0, (byte)0, (byte)0) : ((byte)255, (byte)255, (byte)255) : ((byte)0, (byte)0, (byte)255);
                pixels[at + 3] = 255;
            }
        }

        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        image.Freeze();
        return image;
    }

    private static byte[] Pixels(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
        converted.CopyPixels(pixels, image.PixelWidth * 4, 0);
        return pixels;
    }

    private static (byte B, byte G, byte R) At(byte[] pixels, int width, int x, int y)
    {
        var at = (y * width + x) * 4;
        return (pixels[at], pixels[at + 1], pixels[at + 2]);
    }

    private void Drag(double x1, double y1, double x2, double y2, bool shift = false)
    {
        _tools.PointerDown(new Point(x1, y1), shift, Tolerance);
        _tools.PointerMove(new Point((x1 + x2) / 2, (y1 + y2) / 2), shift);
        _tools.PointerUp(new Point(x2, y2), shift);
    }

    [Fact]
    public void Dragging_with_the_solid_box_tool_covers_every_pixel_it_touches()
    {
        _tools.Tool = ToolKind.Redact;
        _tools.Colour = Palette.Colours[6];

        Drag(10.4, 10.6, 30.2, 20.1);

        var redact = Assert.IsType<RedactMark>(Assert.Single(_committed));
        Assert.Equal(new Rect(10, 10, 21, 11), redact.Bounds);
        Assert.False(redact.Pixelate);
        Assert.Equal(Palette.Colours[6], redact.Style.Color);
    }

    [Fact]
    public void The_pixelate_tool_makes_a_pixelated_box_and_shift_a_square_one()
    {
        _tools.Tool = ToolKind.Pixelate;

        Drag(0, 0, 40, 10, shift: true);

        var redact = Assert.IsType<RedactMark>(Assert.Single(_committed));
        Assert.True(redact.Pixelate);
        Assert.Equal(new Rect(0, 0, 40, 40), redact.Bounds); // a square
        Assert.Equal(new StrokeStyle(Palette.Colours[0], Palette.DefaultThickness), redact.Style);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_click_with_a_redact_tool_adds_nothing(bool pixelate)
    {
        _tools.Tool = pixelate ? ToolKind.Pixelate : ToolKind.Redact;

        Drag(10, 10, 10, 10);
        Drag(10, 10, 60, 10); // no height

        Assert.Empty(_committed);
    }

    [Fact]
    public void The_block_size_follows_the_thickness_but_never_gets_small()
    {
        Assert.Equal(15, Palette.BlockSize(Palette.DefaultThickness));
        Assert.Equal(90, Palette.BlockSize(30));
        Assert.Equal(6, Palette.BlockSize(1));
        Assert.Equal(12, new RedactMark(Black, new Rect(0, 0, 10, 10), true).BlockSize);
    }

    [Fact]
    public void Pixelating_averages_each_block()
    {
        var mosaic = Assert.IsAssignableFrom<BitmapSource>(Pixelation.Mosaic(Striped(), new Int32Rect(0, 0, 24, 12), 12));

        Assert.Equal((2, 1), (mosaic.PixelWidth, mosaic.PixelHeight));
        var pixels = Pixels(mosaic);
        Assert.Equal(((byte)128, (byte)128, (byte)128), At(pixels, 2, 0, 0)); // half black, half white
        Assert.Equal(((byte)0, (byte)0, (byte)255), At(pixels, 2, 1, 0)); // red
    }

    [Fact]
    public void Blocks_are_counted_from_the_box_and_cut_short_at_its_edge()
    {
        // From x = 6 the first block is stripes and red; the second, only 6 wide, is all red.
        var mosaic = Assert.IsAssignableFrom<BitmapSource>(Pixelation.Mosaic(Striped(), new Int32Rect(6, 0, 18, 12), 12));

        Assert.Equal((2, 1), (mosaic.PixelWidth, mosaic.PixelHeight));
        var pixels = Pixels(mosaic);
        Assert.Equal(((byte)64, (byte)64, (byte)191), At(pixels, 2, 0, 0));
        Assert.Equal(((byte)0, (byte)0, (byte)255), At(pixels, 2, 1, 0));
        Assert.Null(Pixelation.Mosaic(Striped(), new Int32Rect(30, 0, 10, 10), 12)); // off the picture
    }

    [Fact]
    public void Saving_flattens_a_pixelated_box_into_its_blocks()
    {
        var document = new EditorDocument(Striped());
        document.Add(new RedactMark(Black, new Rect(0, 0, 24, 12), Pixelate: true));

        var pixels = Pixels(document.Render());

        Assert.All(Enumerable.Range(0, 12), x => Assert.Equal(((byte)128, (byte)128, (byte)128), At(pixels, 24, x, 5)));
        Assert.All(Enumerable.Range(12, 12), x => Assert.Equal(((byte)0, (byte)0, (byte)255), At(pixels, 24, x, 5)));
    }

    [Fact]
    public void A_solid_box_covers_exactly_its_pixels()
    {
        var document = new EditorDocument(Striped());
        document.Add(new RedactMark(Black, new Rect(13, 2, 4, 4), Pixelate: false));

        var pixels = Pixels(document.Render());

        Assert.Equal(((byte)0, (byte)0, (byte)0), At(pixels, 24, 13, 2));
        Assert.Equal(((byte)0, (byte)0, (byte)0), At(pixels, 24, 16, 5));
        Assert.Equal(((byte)0, (byte)0, (byte)255), At(pixels, 24, 12, 2)); // just outside, untouched
        Assert.Equal(((byte)0, (byte)0, (byte)255), At(pixels, 24, 17, 6));
    }

    [Fact]
    public void Anywhere_inside_a_box_erases_or_grabs_it()
    {
        var redact = new RedactMark(Black, new Rect(10, 10, 40, 20), Pixelate: true);

        Assert.True(redact.IsTouchedBy(new Point(30, 20), 1));
        Assert.True(redact.IsTouchedBy(new Point(52, 20), 3));
        Assert.False(redact.IsTouchedBy(new Point(60, 20), 3));
    }

    [Fact]
    public void A_box_moves_and_resizes_in_whole_pixels()
    {
        var redact = new RedactMark(Black, new Rect(10, 10, 40, 20), Pixelate: true);

        Assert.Equal(new Rect(12, 8, 40, 20), ((RedactMark)redact.MovedBy(new Vector(2.4, -1.6))).Bounds);
        Assert.Equal(new Rect(10, 10, 51, 20), ((RedactMark)redact.Reshaped(ShapeHandle.BottomRight, new Point(60.6, 30.2), false)).Bounds);
        Assert.Equal(new Rect(10, 5, 40, 25), ((RedactMark)redact.Reshaped(ShapeHandle.Top, new Point(0, 4.8), false)).Bounds);
    }

    [Fact]
    public void Only_what_shows_can_be_changed_on_a_selected_box()
    {
        var document = new EditorDocument(Striped(200, 100));
        var move = new MoveTool(document);
        document.Add(new RedactMark(Black, new Rect(10, 10, 40, 20), Pixelate: true));
        document.Add(new RedactMark(Black, new Rect(100, 10, 40, 20), Pixelate: false));
        document.MarkSaved();

        move.PointerDown(new Point(30, 20));
        move.PointerUp();
        move.Recolour(Palette.Colours[0]); // a pixelated box has the picture's colours
        Assert.False(document.HasUnsavedChanges);
        move.SetThickness(10); // but its blocks can be bigger
        Assert.Equal(30, ((RedactMark)document.Marks[0]).BlockSize);

        move.PointerDown(new Point(120, 20));
        move.PointerUp();
        document.MarkSaved();
        move.SetThickness(2); // a solid box has no thickness
        Assert.False(document.HasUnsavedChanges);
        move.Recolour(Palette.Colours[0]);
        Assert.Equal(Palette.Colours[0], document.Marks[1].Style.Color);
    }

    [Fact]
    public void A_box_survives_being_kept_editable()
    {
        var redact = new RedactMark(Black, new Rect(10, 10, 40, 20), Pixelate: true);

        Assert.Equal(redact, MarkFormat.FromData(MarkFormat.ToData(redact)));
        Assert.Equal(redact with { Pixelate = false }, MarkFormat.FromData(MarkFormat.ToData(redact with { Pixelate = false })));
    }
}
