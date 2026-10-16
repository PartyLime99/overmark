using Overmark.Capture;
using Overmark.Platform;

namespace Overmark.Tests;

public class SelectionMathTests
{
    private static readonly ScreenRect Desktop = new(0, 0, 1000, 800);
    private static readonly ScreenRect Selection = new(100, 100, 200, 100); // columns 100..299, rows 100..199

    [Theory]
    [InlineData(100, 100, "TopLeft")]
    [InlineData(299, 199, "BottomRight")]
    [InlineData(303, 150, "Right")]
    [InlineData(200, 96, "Top")]
    [InlineData(200, 150, "Move")]
    [InlineData(320, 150, "None")]
    public void Hit_test_finds_handles_within_tolerance(int x, int y, string expected)
    {
        Assert.Equal(Enum.Parse<SelectionHandle>(expected), SelectionMath.HitTest(Selection, new PixelPoint(x, y), tolerance: 5));
    }

    [Fact]
    public void Dragging_right_edge_widens()
    {
        var result = SelectionMath.Drag(Selection, SelectionHandle.Right, 20, 999, Desktop);

        Assert.Equal(new ScreenRect(100, 100, 220, 100), result);
    }

    [Fact]
    public void Dragging_an_edge_past_the_opposite_one_stops_at_one_pixel()
    {
        var result = SelectionMath.Drag(Selection, SelectionHandle.Left, 500, 0, Desktop);

        Assert.Equal(new ScreenRect(299, 100, 1, 100), result);
    }

    [Fact]
    public void Dragging_a_corner_moves_two_edges()
    {
        var result = SelectionMath.Drag(Selection, SelectionHandle.TopLeft, -10, -20, Desktop);

        Assert.Equal(new ScreenRect(90, 80, 210, 120), result);
    }

    [Fact]
    public void Moving_stays_on_the_desktop()
    {
        Assert.Equal(new ScreenRect(800, 0, 200, 100), SelectionMath.MoveBy(Selection, 5000, -5000, Desktop));
    }

    [Fact]
    public void Resizing_keeps_at_least_one_pixel_and_stays_on_the_desktop()
    {
        Assert.Equal(new ScreenRect(100, 100, 1, 1), SelectionMath.ResizeBy(Selection, -500, -500, Desktop));
        Assert.Equal(new ScreenRect(100, 100, 900, 700), SelectionMath.ResizeBy(Selection, 5000, 5000, Desktop));
    }

    [Theory]
    [InlineData(599, 449)]
    [InlineData(401, 351)]
    [InlineData(401, 449)]
    public void From_centre_mirrors_the_corner_around_the_centre_pixel(int x, int y)
    {
        var result = SelectionMath.FromCentre(new PixelPoint(500, 400), new PixelPoint(x, y), Desktop);

        Assert.Equal(new ScreenRect(401, 351, 199, 99), result); // 99 pixels either side of column 500 and 49 of row 400
    }

    [Fact]
    public void From_centre_stops_the_far_side_at_the_desktop_edge()
    {
        var result = SelectionMath.FromCentre(new PixelPoint(50, 780), new PixelPoint(150, 700), Desktop);

        Assert.Equal(new ScreenRect(0, 700, 151, 100), result);
    }
}
