using System.Windows;
using System.Windows.Media;
using Overmark.Editor;

namespace Overmark.Tests;

public class DimensionMarkTests
{
    private static readonly StrokeStyle Red = new(Palette.Colours[0], 4); // 2 px lines, 16 px text

    private static DimensionMark Horizontal(string text, Point? textAt = null) =>
        new(Red, new Point(100, 200), new Point(300, 200), new Point(200, 150), DimensionKind.Horizontal, text, textAt);

    [Fact]
    public void Where_it_is_placed_picks_what_it_measures()
    {
        Point a = new(100, 100), b = new(200, 160);

        Assert.Equal(DimensionKind.Horizontal, DimensionMark.KindFor(a, b, new Point(150, 40))); // above
        Assert.Equal(DimensionKind.Horizontal, DimensionMark.KindFor(a, b, new Point(150, 220))); // below
        Assert.Equal(DimensionKind.Vertical, DimensionMark.KindFor(a, b, new Point(20, 130))); // left
        Assert.Equal(DimensionKind.Vertical, DimensionMark.KindFor(a, b, new Point(300, 130))); // right
        Assert.Equal(DimensionKind.Aligned, DimensionMark.KindFor(a, b, new Point(250, 60))); // off a corner
        Assert.Equal(DimensionKind.Aligned, DimensionMark.KindFor(a, b, new Point(150, 130))); // between them
    }

    [Fact]
    public void Each_kind_measures_its_own_distance()
    {
        Point a = new(0, 0), b = new(30, 40);

        Assert.Equal(30, new DimensionMark(Red, a, b, new Point(15, -20), DimensionKind.Horizontal, "").Layout.Measured);
        Assert.Equal(40, new DimensionMark(Red, a, b, new Point(-20, 20), DimensionKind.Vertical, "").Layout.Measured);
        Assert.Equal(50, new DimensionMark(Red, a, b, new Point(-40, 30), DimensionKind.Aligned, "").Layout.Measured, 6);
    }

    [Fact]
    public void The_dimension_line_goes_through_the_placing_click()
    {
        var horizontal = Horizontal("").Layout;
        Assert.Equal(new Point(100, 150), horizontal.P1);
        Assert.Equal(new Point(300, 150), horizontal.P2);
        Assert.Equal(new Vector(0, -1), horizontal.Outward); // above the points

        // Aligned: offset across the line from (0,0) to (100,0), to y = 30 (the point's x doesn't matter).
        var aligned = new DimensionMark(Red, new Point(0, 0), new Point(100, 0), new Point(500, 30), DimensionKind.Aligned, "").Layout;
        Assert.Equal(0, aligned.P1.X, 6);
        Assert.Equal(30, aligned.P1.Y, 6);
        Assert.Equal(100, aligned.P2.X, 6);
        Assert.Equal(30, aligned.P2.Y, 6);
    }

    [Fact]
    public void Short_text_sits_in_the_middle_of_the_line()
    {
        var layout = Horizontal("50").Layout;

        Assert.True(layout.TextOnLine);
        Assert.Equal(new Point(200, 150), layout.TextCentre);
        Assert.True(layout.ArrowsInside);
        Assert.Null(layout.Leader);
    }

    [Fact]
    public void Text_too_long_for_the_gap_moves_just_outside_the_line()
    {
        var layout = Horizontal("A very long note that will never fit between these arrows").Layout;

        Assert.False(layout.TextOnLine);
        Assert.Equal(200, layout.TextCentre.X, 6);
        Assert.True(layout.TextCentre.Y < 150 - layout.TextSize.Height / 2); // above the line, clear of it
        Assert.Null(layout.Leader); // right beside the line, so no leader needed
    }

    [Fact]
    public void Text_moved_off_the_line_gets_a_leader_back_to_it()
    {
        var layout = Horizontal("50", textAt: new Point(400, 60)).Layout;

        Assert.False(layout.TextOnLine);
        var leader = Assert.NotNull(layout.Leader);
        Assert.Equal(new Point(300, 150), leader.From); // the nearest end of the line
        Assert.True((leader.To - layout.TextCentre).Length < (leader.From - layout.TextCentre).Length);
    }

    [Fact]
    public void Text_dragged_near_the_line_snaps_onto_it_and_the_line_reaches_it()
    {
        var layout = Horizontal("50").Layout;
        var snapped = layout.SnapText(new Point(380, 154), tolerance: 8);
        Assert.Equal(new Point(380, 150), snapped);
        Assert.Equal(new Point(380, 140), layout.SnapText(new Point(380, 140), tolerance: 8));

        var beyond = Horizontal("50", textAt: snapped).Layout;
        Assert.True(beyond.TextOnLine);
        Assert.Equal(380, beyond.LineTo.X, 6); // the line carries on out to the text
    }

    [Fact]
    public void Text_angles_stay_readable_and_snap()
    {
        Assert.Equal(0, DimensionMark.Upright(180));
        Assert.Equal(-30, DimensionMark.Upright(150));
        Assert.Equal(90, DimensionMark.Upright(-90));
        Assert.Equal(30, DimensionMark.Upright(30));

        Assert.Equal(0, DimensionMark.SnapAngle(5, lineAngle: 37));
        Assert.Equal(90, DimensionMark.SnapAngle(-87, lineAngle: 37));
        Assert.Equal(37, DimensionMark.SnapAngle(40, lineAngle: 37));
        Assert.Equal(-53, DimensionMark.SnapAngle(-50, lineAngle: 37)); // across the line
        Assert.Equal(20, DimensionMark.SnapAngle(20, lineAngle: 37));
    }

    [Fact]
    public void Parts_can_be_told_apart()
    {
        var layout = Horizontal("50").Layout;

        Assert.Equal(DimensionPart.Text, layout.PartAt(new Point(200, 150), 3));
        Assert.Equal(DimensionPart.Line, layout.PartAt(new Point(140, 151), 3));
        Assert.Equal(DimensionPart.Extension, layout.PartAt(new Point(100, 180), 3));
        Assert.Equal(DimensionPart.None, layout.PartAt(new Point(200, 180), 3));
    }

    [Fact]
    public void Moving_a_dimension_moves_every_part()
    {
        var moved = (DimensionMark)Horizontal("50", textAt: new Point(400, 60)).MovedBy(new Vector(10, -5));

        Assert.Equal((new Point(110, 195), new Point(310, 195), new Point(210, 145), new Point(410, 55)), (moved.A, moved.B, moved.Through, moved.TextAt!.Value));
    }

    [Fact]
    public void Every_kind_of_mark_can_be_moved()
    {
        var by = new Vector(7, 3);
        Mark[] marks =
        [
            new FreehandMark(Red, [new Point(0, 0), new Point(10, 5), new Point(20, 0)]),
            new HighlighterMark(Red, [new Point(0, 0), new Point(30, 0)]),
            new LineMark(Red, new Point(0, 0), new Point(40, 20)),
            new ArrowMark(Red, new Point(0, 0), new Point(40, 20)),
            new CurveMark(Red, new Point(0, 0), new Point(20, 15), new Point(40, 0), Arrowhead: true),
            new RectangleMark(Red, new Rect(5, 5, 30, 20)),
            new EllipseMark(Red, new Rect(5, 5, 30, 20)),
            new TextMark(new StrokeStyle(Colors.Black, 20), new Point(5, 5), "Note", Boxed: true),
            Horizontal("50"),
        ];

        foreach (var mark in marks)
        {
            var before = mark.Extent;
            var after = mark.MovedBy(by).Extent;
            // WPF works out curve bounds in single precision, hence the loose tolerance.
            Assert.Equal(before.X + by.X, after.X, 3);
            Assert.Equal(before.Y + by.Y, after.Y, 3);
            Assert.Equal(before.Width, after.Width, 3);
            Assert.Equal(before.Height, after.Height, 3);
        }
    }
}
