using System.Windows;
using Overmark.Editor;

namespace Overmark.Tests;

public class TransparencyTests
{
    /// <summary>Bgra32 pixels, all opaque white.</summary>
    private static byte[] White(int width, int height) => Enumerable.Repeat((byte)255, width * height * 4).ToArray();

    private static void Set(byte[] pixels, int width, int x, int y, byte b, byte g, byte r)
    {
        var p = (y * width + x) * 4;
        (pixels[p], pixels[p + 1], pixels[p + 2], pixels[p + 3]) = (b, g, r, 255);
    }

    private static byte Alpha(byte[] pixels, int width, int x, int y) => pixels[(y * width + x) * 4 + 3];

    private static Int32Rect All(int width, int height) => new(0, 0, width, height);

    [Fact]
    public void White_touching_the_edges_goes_and_the_picture_stays()
    {
        var pixels = White(6, 6);
        for (var y = 2; y <= 3; y++)
        {
            for (var x = 2; x <= 3; x++)
            {
                Set(pixels, 6, x, y, 0, 0, 0);
            }
        }

        Assert.Equal(32, Transparency.RemoveWhite(pixels, 6, 6, All(6, 6)));
        Assert.Equal(0, Alpha(pixels, 6, 0, 0));
        Assert.Equal(0, Alpha(pixels, 6, 5, 2));
        Assert.Equal(255, Alpha(pixels, 6, 2, 2));
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, pixels[84..88]);
    }

    [Fact]
    public void White_enclosed_by_the_picture_stays()
    {
        var pixels = White(7, 7);
        for (var i = 1; i <= 5; i++)
        {
            Set(pixels, 7, i, 1, 0, 0, 0);
            Set(pixels, 7, i, 5, 0, 0, 0);
            Set(pixels, 7, 1, i, 0, 0, 0);
            Set(pixels, 7, 5, i, 0, 0, 0);
        }

        Assert.Equal(24, Transparency.RemoveWhite(pixels, 7, 7, All(7, 7)));
        Assert.Equal(255, Alpha(pixels, 7, 3, 3));
        Assert.Equal(255, Alpha(pixels, 7, 2, 4));
    }

    [Fact]
    public void Everywhere_takes_the_enclosed_white_too()
    {
        // A ring (as above) with a grey pixel in the white inside it, which gets softened like any other edge.
        var pixels = White(7, 7);
        for (var i = 1; i <= 5; i++)
        {
            Set(pixels, 7, i, 1, 0, 0, 0);
            Set(pixels, 7, i, 5, 0, 0, 0);
            Set(pixels, 7, 1, i, 0, 0, 0);
            Set(pixels, 7, 5, i, 0, 0, 0);
        }

        Set(pixels, 7, 3, 3, 128, 128, 128);

        Assert.Equal(24 + 8, Transparency.RemoveWhite(pixels, 7, 7, All(7, 7), everywhere: true));
        Assert.Equal(0, Alpha(pixels, 7, 0, 0));
        Assert.Equal(0, Alpha(pixels, 7, 2, 2));
        Assert.Equal(0, Alpha(pixels, 7, 4, 3));
        Assert.Equal(255, Alpha(pixels, 7, 1, 1)); // the black ring stays solid
        Assert.Equal(127, Alpha(pixels, 7, 3, 3));
    }

    [Fact]
    public void Everywhere_keeps_to_the_kept_area()
    {
        var pixels = White(6, 6);
        Set(pixels, 6, 2, 2, 0, 0, 0);

        Assert.Equal(15, Transparency.RemoveWhite(pixels, 6, 6, new Int32Rect(1, 1, 4, 4), everywhere: true));
        Assert.Equal(255, Alpha(pixels, 6, 0, 0));
        Assert.Equal(255, Alpha(pixels, 6, 5, 5));
        Assert.Equal(0, Alpha(pixels, 6, 3, 3));
    }

    [Fact]
    public void Everywhere_with_no_white_changes_nothing()
    {
        var pixels = White(4, 4);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i + 1] = 200;
        }

        var before = pixels.ToArray();

        Assert.Equal(0, Transparency.RemoveWhite(pixels, 4, 4, All(4, 4), everywhere: true));
        Assert.Equal(before, pixels);
    }

    [Fact]
    public void The_background_does_not_leak_through_a_diagonal_gap()
    {
        // A ring with its top-left corner missing: the white inside only meets the outside at a corner.
        var pixels = White(7, 7);
        for (var i = 1; i <= 5; i++)
        {
            Set(pixels, 7, i, 5, 0, 0, 0);
            Set(pixels, 7, 5, i, 0, 0, 0);
            if (i > 1)
            {
                Set(pixels, 7, i, 1, 0, 0, 0);
                Set(pixels, 7, 1, i, 0, 0, 0);
            }
        }

        Transparency.RemoveWhite(pixels, 7, 7, All(7, 7));

        Assert.Equal(0, Alpha(pixels, 7, 1, 1));
        Assert.Equal(0, Alpha(pixels, 7, 2, 2)); // touches the background at its corner, so it's part of the fringe
        Assert.Equal(255, Alpha(pixels, 7, 3, 3));
        Assert.Equal(255, Alpha(pixels, 7, 4, 4));
    }

    [Theory]
    [InlineData(128, 128, 128)]
    [InlineData(100, 150, 200)]
    [InlineData(230, 235, 250)]
    public void An_edge_pixel_looks_the_same_on_white_without_its_white(byte b, byte g, byte r)
    {
        var pixels = White(3, 3);
        Set(pixels, 3, 1, 1, b, g, r);

        Transparency.RemoveWhite(pixels, 3, 3, All(3, 3));

        var p = (1 * 3 + 1) * 4;
        var alpha = pixels[p + 3];
        Assert.Equal(255 - Math.Min(b, Math.Min(g, r)), alpha);
        Assert.Equal(0, Math.Min(pixels[p], Math.Min(pixels[p + 1], pixels[p + 2]))); // its darkest channel goes to black
        byte[] original = [b, g, r];
        for (var c = 0; c < 3; c++)
        {
            var onWhite = (pixels[p + c] * alpha + 255 * (255 - alpha)) / 255.0;
            Assert.InRange(onWhite, original[c] - 1, original[c] + 1);
        }
    }

    [Fact]
    public void A_dark_edge_stays_solid()
    {
        var pixels = White(3, 3);
        Set(pixels, 3, 1, 1, 0, 40, 90);

        Transparency.RemoveWhite(pixels, 3, 3, All(3, 3));

        Assert.Equal(new byte[] { 0, 40, 90, 255 }, pixels[16..20]);
    }

    [Fact]
    public void Only_the_kept_area_changes()
    {
        var pixels = White(6, 6);

        Assert.Equal(16, Transparency.RemoveWhite(pixels, 6, 6, new Int32Rect(1, 1, 4, 4)));
        Assert.Equal(255, Alpha(pixels, 6, 0, 0));
        Assert.Equal(255, Alpha(pixels, 6, 5, 3));
        Assert.Equal(0, Alpha(pixels, 6, 1, 1));
        Assert.Equal(0, Alpha(pixels, 6, 4, 4));
    }

    [Fact]
    public void Nearly_white_is_not_background()
    {
        var pixels = White(4, 4);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 254;
        }

        var before = pixels.ToArray();

        Assert.Equal(0, Transparency.RemoveWhite(pixels, 4, 4, All(4, 4)));
        Assert.Equal(before, pixels);
    }

    [Fact]
    public void Doing_it_again_changes_nothing()
    {
        var pixels = White(6, 6);
        Set(pixels, 6, 2, 2, 120, 125, 145);
        Set(pixels, 6, 3, 2, 30, 30, 30);
        Transparency.RemoveWhite(pixels, 6, 6, All(6, 6));
        var once = pixels.ToArray();

        Assert.Equal(0, Transparency.RemoveWhite(pixels, 6, 6, All(6, 6)));
        Assert.Equal(once, pixels);
    }
}
