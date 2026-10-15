using System.Windows;
using Overmark.Editor;

namespace Overmark.Tests;

public class MarkGeometryTests
{
    [Theory]
    [InlineData(100, 3, 100, 0)]     // nearly flat -> horizontal
    [InlineData(-4, 80, 0, 80)]      // nearly vertical -> vertical
    [InlineData(50, 54, 52, 52)]     // nearly diagonal -> 45°, length projected onto the diagonal
    [InlineData(-60, -58, -59, -59)] // works in every direction
    public void Shift_snaps_to_the_nearest_45_degrees(double dx, double dy, double expectedX, double expectedY)
    {
        var snapped = MarkGeometry.SnapAngle(new Point(0, 0), new Point(dx, dy));

        Assert.Equal(expectedX, snapped.X, precision: 6);
        Assert.Equal(expectedY, snapped.Y, precision: 6);
    }

    [Fact]
    public void Shift_makes_a_square_in_the_drag_direction()
    {
        Assert.Equal(new Rect(new Point(10, 10), new Point(-30, 50)), MarkGeometry.RectangleFrom(new Point(10, 10), new Point(-30, 20), square: true));
        Assert.Equal(new Rect(10, 10, 30, 0), MarkGeometry.RectangleFrom(new Point(10, 10), new Point(40, 10), square: false));
    }

    [Fact]
    public void A_curve_passes_through_its_bend_handle_halfway()
    {
        var start = new Point(0, 0);
        var through = new Point(30, 60);
        var end = new Point(100, 0);

        var halfway = MarkGeometry.Quadratic(start, MarkGeometry.ControlPoint(start, through, end), end, 0.5);

        Assert.Equal(through, halfway);
    }

    [Fact]
    public void Splitting_a_curve_ends_on_the_curve()
    {
        var (p0, p1, p2) = (new Point(0, 0), new Point(50, 100), new Point(100, 0));

        var (_, end) = MarkGeometry.SplitAt(p0, p1, p2, 0.3);

        Assert.Equal(MarkGeometry.Quadratic(p0, p1, p2, 0.3), end);
    }

    [Fact]
    public void Arrowheads_grow_with_the_stroke_but_never_get_tiny()
    {
        Assert.Equal(10, MarkGeometry.HeadLength(1));
        Assert.Equal(36, MarkGeometry.HeadLength(9));
    }
}
