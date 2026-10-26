using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Overmark.Editor;

/// <summary>
/// Makes a capture's white background see-through, e.g. to lift a CAD model off its white background. Usually only pure
/// white that touches the edges goes, so white inside the picture (labels, enclosed areas) stays; it can take all pure
/// white instead, holes and all. The pixels right next to it are softened: anti-aliased edges are part white, and would
/// otherwise leave a white fringe on a dark background.
/// </summary>
internal static class Transparency
{
    /// <summary>
    /// A copy of <paramref name="image"/> with the white background inside <paramref name="area"/> (the kept part of
    /// the image) made see-through, or null if no white touches the area's edges. With <paramref name="everywhere"/>,
    /// all pure white in the area goes, not just the background, and it's null only if there's none.
    /// </summary>
    public static BitmapSource? RemoveWhite(BitmapSource image, Int32Rect area, bool everywhere = false)
    {
        var (pixels, stride) = Bgra32(image);
        if (RemoveWhite(pixels, image.PixelWidth, image.PixelHeight, area, everywhere) == 0)
        {
            return null;
        }

        var result = BitmapSource.Create(image.PixelWidth, image.PixelHeight, image.DpiX, image.DpiY, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }

    /// <summary>
    /// The work behind <see cref="RemoveWhite(BitmapSource, Int32Rect, bool)"/>, in place on Bgra32 pixels (alpha not
    /// premultiplied). Returns how many white pixels it made see-through; if none, nothing is changed.
    /// </summary>
    public static int RemoveWhite(byte[] pixels, int width, int height, Int32Rect area, bool everywhere = false)
    {
        var left = area.X;
        var top = area.Y;
        var right = area.X + area.Width - 1;
        var bottom = area.Y + area.Height - 1;
        var background = new bool[width * height];
        var queue = new int[width * height];
        var head = 0;
        var tail = 0;

        for (var x = left; x <= right; x++)
        {
            Reach(x, top);
            Reach(x, bottom);
        }

        for (var y = top; y <= bottom; y++)
        {
            Reach(left, y);
            Reach(right, y);
            if (everywhere)
            {
                // Every white pixel is a start, so the flood has nowhere left to go: it just clears them.
                for (var x = left + 1; x < right; x++)
                {
                    Reach(x, y);
                }
            }
        }

        // Flood across the background, side to side only: a diagonal step would leak through a one-pixel outline.
        var cleared = 0;
        while (head < tail)
        {
            var i = queue[head++];
            if (pixels[i * 4 + 3] != 0)
            {
                pixels[i * 4 + 3] = 0; // it keeps its white, for anything that ignores transparency
                cleared++;
            }

            var x = i % width;
            var y = i / width;
            if (x > left)
            {
                Reach(x - 1, y);
            }

            if (x < right)
            {
                Reach(x + 1, y);
            }

            if (y > top)
            {
                Reach(x, y - 1);
            }

            if (y < bottom)
            {
                Reach(x, y + 1);
            }
        }

        if (cleared == 0)
        {
            return 0;
        }

        // The fringe: solid pixels touching the background, even at a corner. Each is taken as the darkest colour that,
        // blended with white, gives the pixel, at the opacity that takes: the same on white as before, without the white
        // on anything else.
        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++)
            {
                var p = (y * width + x) * 4;
                if (!background[y * width + x] && pixels[p + 3] == 255 && TouchesBackground(x, y))
                {
                    var least = Math.Min(pixels[p], Math.Min(pixels[p + 1], pixels[p + 2]));
                    var alpha = 255 - least;
                    for (var c = 0; c < 3; c++)
                    {
                        pixels[p + c] = alpha == 0 ? (byte)255 : (byte)Math.Round((pixels[p + c] - least) * 255.0 / alpha);
                    }

                    pixels[p + 3] = (byte)alpha;
                }
            }
        }

        return cleared;

        // Already see-through pixels count as background too, so doing it again (or on a reopened capture) carries on
        // from where the background already is.
        void Reach(int x, int y)
        {
            var i = y * width + x;
            var p = i * 4;
            if (!background[i] && (pixels[p + 3] == 0 || (pixels[p] == 255 && pixels[p + 1] == 255 && pixels[p + 2] == 255 && pixels[p + 3] == 255)))
            {
                background[i] = true;
                queue[tail++] = i;
            }
        }

        bool TouchesBackground(int x, int y)
        {
            for (var ny = Math.Max(top, y - 1); ny <= Math.Min(bottom, y + 1); ny++)
            {
                for (var nx = Math.Max(left, x - 1); nx <= Math.Min(right, x + 1); nx++)
                {
                    if (background[ny * width + nx])
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    /// <summary>Whether any of the picture is see-through (screen captures never are; a reopened PNG may be).</summary>
    public static bool IsSeeThrough(BitmapSource image)
    {
        if (image.Format == PixelFormats.Bgr32 || image.Format == PixelFormats.Bgr24 || image.Format == PixelFormats.Rgb24)
        {
            return false;
        }

        var (pixels, _) = Bgra32(image);
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 255)
            {
                return true;
            }
        }

        return false;
    }

    private static (byte[] Pixels, int Stride) Bgra32(BitmapSource image)
    {
        var stride = image.PixelWidth * 4;
        var pixels = new byte[stride * image.PixelHeight];
        var source = image.Format == PixelFormats.Bgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        source.CopyPixels(pixels, stride, 0);
        return (pixels, stride);
    }
}
