using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Overmark.Editor;

/// <summary>
/// Hides what's under it, such as names, email addresses or figures: a solid box in the mark's colour, or the
/// picture pixelated into blocks (<see cref="Pixelate"/>). The style's width is the thickness slider's value, which
/// sets the block size (see <see cref="Palette.BlockSize"/>). It covers whole pixels, so nothing shows at its edges,
/// and it's drawn from the picture alone: marks under it are covered, not pixelated.
/// </summary>
internal sealed record RedactMark(StrokeStyle Style, Rect Bounds, bool Pixelate) : Mark(Style)
{
    /// <summary>A pixelated box drawn with no picture to hand (which the editor never does) is a plain grey.</summary>
    private static readonly Brush NoPicture = Frozen(new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)));

    /// <summary>The whole pixels it covers.</summary>
    public Int32Rect Area => WholePixels(Bounds);

    public int BlockSize => Palette.BlockSize(Style.Width);

    public override Rect Extent => AreaRect;

    private Rect AreaRect => new(Area.X, Area.Y, Area.Width, Area.Height);

    /// <summary>The whole pixels <paramref name="bounds"/> touches, at least one each way.</summary>
    public static Int32Rect WholePixels(Rect bounds)
    {
        var left = (int)Math.Floor(bounds.Left);
        var top = (int)Math.Floor(bounds.Top);
        var right = Math.Max(left + 1, (int)Math.Ceiling(bounds.Right));
        var bottom = Math.Max(top + 1, (int)Math.Ceiling(bounds.Bottom));
        return new Int32Rect(left, top, right - left, bottom - top);
    }

    public override void Draw(DrawingContext dc) => Draw(dc, null);

    public override void Draw(DrawingContext dc, BitmapSource? picture)
    {
        var area = AreaRect;
        if (!Pixelate)
        {
            dc.DrawRectangle(Fill(), null, area);
            return;
        }

        if (picture is null || Pixelation.Mosaic(picture, Area, BlockSize) is not { } mosaic)
        {
            dc.DrawRectangle(NoPicture, null, area);
            return;
        }

        // One pixel of the mosaic per block, stretched without smoothing so the blocks keep sharp edges at any zoom.
        // The last row and column of blocks may be cut short by the box's edge.
        var block = BlockSize;
        var blocks = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(blocks, BitmapScalingMode.NearestNeighbor);
        using (var context = blocks.Open())
        {
            context.DrawImage(mosaic, new Rect(area.X, area.Y, mosaic.PixelWidth * block, mosaic.PixelHeight * block));
        }

        dc.PushClip(new RectangleGeometry(area));
        dc.DrawDrawing(blocks);
        dc.Pop();
    }

    /// <summary>The thickness only sets the block size, so a solid box stays as it is.</summary>
    public override Mark WithThickness(double thickness) => Pixelate ? base.WithThickness(thickness) : this;

    /// <summary>Moves in whole pixels, so the box keeps covering whole pixels.</summary>
    public override Mark MovedBy(Vector offset) =>
        this with { Bounds = Rect.Offset(Bounds, new Vector(Math.Round(offset.X), Math.Round(offset.Y))) };

    public override IReadOnlyList<(ShapeHandle Handle, Point At)> Handles => MarkGeometry.BoxHandles(AreaRect);

    public override Mark Reshaped(ShapeHandle handle, Point to, bool shift)
    {
        var resized = MarkGeometry.Resized(AreaRect, handle, new Point(Math.Round(to.X), Math.Round(to.Y)), shift);
        var area = WholePixels(resized);
        return this with { Bounds = new Rect(area.X, area.Y, area.Width, area.Height) };
    }

    // Anywhere inside counts, not just the outline: it's solid.
    public override bool IsTouchedBy(Point point, double radius)
    {
        var area = AreaRect;
        area.Inflate(radius, radius);
        return area.Contains(point);
    }

    protected override Geometry Centreline() => new RectangleGeometry(AreaRect);

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

/// <summary>Turns part of a picture into blocks of its average colours, for the redact tool.</summary>
internal static class Pixelation
{
    private const int CacheSize = 32;

    // Mosaics made recently from each picture, so redrawing (on every pointer move while a box is dragged, and every
    // time the marks change) doesn't work them out again. Dropped along with the picture.
    private static readonly ConditionalWeakTable<BitmapSource, Dictionary<(Int32Rect Area, int Block), BitmapSource?>> Made = new();

    /// <summary>
    /// The pixels of <paramref name="picture"/> inside <paramref name="area"/>, as one pixel for each block of
    /// <paramref name="block"/> × <paramref name="block"/> pixels (counted from the area's top-left), in the average
    /// colour of the pixels the block covers. Blocks reaching past the picture's edge average the part that's in it.
    /// Null if the area is entirely outside the picture.
    /// </summary>
    public static BitmapSource? Mosaic(BitmapSource picture, Int32Rect area, int block)
    {
        var made = Made.GetOrCreateValue(picture);
        if (made.TryGetValue((area, block), out var known))
        {
            return known;
        }

        if (made.Count >= CacheSize)
        {
            made.Clear();
        }

        var mosaic = Make(picture, area, block);
        made[(area, block)] = mosaic;
        return mosaic;
    }

    private static BitmapSource? Make(BitmapSource picture, Int32Rect area, int block)
    {
        // The part of the area that's in the picture.
        var left = Math.Max(0, area.X);
        var top = Math.Max(0, area.Y);
        var right = Math.Min(picture.PixelWidth, area.X + area.Width);
        var bottom = Math.Min(picture.PixelHeight, area.Y + area.Height);
        if (right <= left || bottom <= top)
        {
            return null;
        }

        // Premultiplied, so see-through pixels don't tint the average.
        var width = right - left;
        var height = bottom - top;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        var source = picture.Format == PixelFormats.Pbgra32 ? picture : new FormatConvertedBitmap(picture, PixelFormats.Pbgra32, null, 0);
        source.CopyPixels(new Int32Rect(left, top, width, height), pixels, stride, 0);

        var columns = (area.Width + block - 1) / block;
        var rows = (area.Height + block - 1) / block;
        var sums = new long[columns * rows * 4];
        var counts = new int[columns * rows];
        for (var y = 0; y < height; y++)
        {
            var row = (top + y - area.Y) / block * columns;
            for (var x = 0; x < width; x++)
            {
                var cell = row + (left + x - area.X) / block;
                var at = y * stride + x * 4;
                for (var c = 0; c < 4; c++)
                {
                    sums[cell * 4 + c] += pixels[at + c];
                }

                counts[cell]++;
            }
        }

        var blocks = new byte[columns * rows * 4];
        for (var cell = 0; cell < counts.Length; cell++)
        {
            if (counts[cell] == 0)
            {
                continue; // past the picture's edge: left see-through
            }

            for (var c = 0; c < 4; c++)
            {
                blocks[cell * 4 + c] = (byte)((sums[cell * 4 + c] + counts[cell] / 2) / counts[cell]);
            }
        }

        var mosaic = BitmapSource.Create(columns, rows, 96, 96, PixelFormats.Pbgra32, null, blocks, columns * 4);
        mosaic.Freeze();
        return mosaic;
    }
}
