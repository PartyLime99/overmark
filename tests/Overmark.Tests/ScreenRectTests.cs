using Overmark.Platform;

namespace Overmark.Tests;

public class ScreenRectTests
{
    [Fact]
    public void Corners_are_both_inside_whichever_way_round()
    {
        var expected = new ScreenRect(10, 20, 91, 31);

        Assert.Equal(expected, ScreenRect.FromCorners(new PixelPoint(10, 20), new PixelPoint(100, 50)));
        Assert.Equal(expected, ScreenRect.FromCorners(new PixelPoint(100, 50), new PixelPoint(10, 20)));
    }

    [Fact]
    public void Same_corner_twice_is_one_pixel()
    {
        Assert.Equal(new ScreenRect(5, 5, 1, 1), ScreenRect.FromCorners(new PixelPoint(5, 5), new PixelPoint(5, 5)));
    }

    [Fact]
    public void Contains_excludes_right_and_bottom()
    {
        var rect = new ScreenRect(0, 0, 10, 10);

        Assert.True(rect.Contains(new PixelPoint(9, 9)));
        Assert.False(rect.Contains(new PixelPoint(10, 9)));
        Assert.False(rect.Contains(new PixelPoint(9, 10)));
    }

    [Fact]
    public void Intersect_handles_negative_coordinates_and_no_overlap()
    {
        var leftMonitor = new ScreenRect(-1920, 0, 1920, 1080);

        Assert.Equal(new ScreenRect(-100, 0, 100, 50), leftMonitor.Intersect(new ScreenRect(-100, -10, 300, 60)));
        Assert.Null(leftMonitor.Intersect(new ScreenRect(0, 0, 100, 100)));
    }
}
