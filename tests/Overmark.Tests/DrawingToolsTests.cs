using System.Windows;
using System.Windows.Media;
using Overmark.Editor;

namespace Overmark.Tests;

public class DrawingToolsTests
{
    private const double Tolerance = 8;

    private readonly DrawingTools _tools = new();
    private readonly List<Mark> _committed = [];

    public DrawingToolsTests() => _tools.Committed += _committed.Add;

    private void Drag(double x1, double y1, double x2, double y2, bool shift = false)
    {
        _tools.PointerDown(new Point(x1, y1), shift, Tolerance);
        _tools.PointerMove(new Point((x1 + x2) / 2, (y1 + y2) / 2), shift);
        _tools.PointerUp(new Point(x2, y2), shift);
    }

    [Fact]
    public void Dragging_with_the_line_tool_adds_a_line()
    {
        _tools.Tool = ToolKind.Line;

        Drag(10, 10, 110, 60);

        var line = Assert.IsType<LineMark>(Assert.Single(_committed));
        Assert.Equal(new Point(10, 10), line.Start);
        Assert.Equal(new Point(110, 60), line.End);
        Assert.Null(_tools.Preview);
    }

    [Fact]
    public void Colour_and_thickness_are_fixed_when_the_mark_starts()
    {
        _tools.Tool = ToolKind.Arrow;
        _tools.Colour = Palette.Colours[4];
        _tools.Width = 12;

        _tools.PointerDown(new Point(0, 0), false, Tolerance);
        _tools.Colour = Palette.Colours[0];
        _tools.Width = 2;
        _tools.PointerUp(new Point(50, 50), false);

        var arrow = Assert.Single(_committed);
        Assert.Equal(new StrokeStyle(Palette.Colours[4], 12), arrow.Style);
    }

    [Fact]
    public void A_click_with_the_line_tool_adds_nothing()
    {
        _tools.Tool = ToolKind.Line;

        Drag(10, 10, 10, 10);

        Assert.Empty(_committed);
    }

    [Fact]
    public void A_click_with_the_pen_adds_a_dot()
    {
        _tools.Tool = ToolKind.Freehand;

        Drag(10, 10, 10, 10);

        Assert.Single(Assert.IsType<FreehandMark>(Assert.Single(_committed)).Points);
    }

    [Fact]
    public void Freehand_keeps_the_points_it_passes_through()
    {
        _tools.Tool = ToolKind.Freehand;

        _tools.PointerDown(new Point(0, 0), false, Tolerance);
        _tools.PointerMove(new Point(0.2, 0), false); // too close to the last point to keep
        _tools.PointerMove(new Point(5, 5), false);
        _tools.PointerUp(new Point(10, 0), false);

        Assert.Equal([new Point(0, 0), new Point(5, 5), new Point(10, 0)], Assert.IsType<FreehandMark>(Assert.Single(_committed)).Points);
    }

    [Fact]
    public void Shift_straightens_lines()
    {
        _tools.Tool = ToolKind.Line;

        Drag(0, 0, 100, 4, shift: true);

        Assert.Equal(new Point(100, 0), Assert.IsType<LineMark>(Assert.Single(_committed)).End);
    }

    [Fact]
    public void Shift_makes_rectangles_square()
    {
        _tools.Tool = ToolKind.Rectangle;

        Drag(0, 0, 40, 25, shift: true);

        Assert.Equal(new Rect(0, 0, 40, 40), Assert.IsType<RectangleMark>(Assert.Single(_committed)).Bounds);
    }

    [Fact]
    public void Shift_makes_ellipses_circles()
    {
        _tools.Tool = ToolKind.Ellipse;

        Drag(10, 10, 60, 30, shift: true);

        Assert.Equal(new Rect(10, 10, 50, 50), Assert.IsType<EllipseMark>(Assert.Single(_committed)).Bounds);
    }

    [Fact]
    public void Shift_makes_the_highlighter_a_straight_line()
    {
        _tools.Tool = ToolKind.Highlighter;

        _tools.PointerDown(new Point(0, 0), true, Tolerance);
        _tools.PointerMove(new Point(30, 9), true);
        _tools.PointerUp(new Point(60, 3), true);

        var highlight = Assert.IsType<HighlighterMark>(Assert.Single(_committed));
        Assert.Equal([new Point(0, 0), new Point(60, 0)], highlight.Points);
        Assert.Equal(Palette.StrokeWidth(ToolKind.Highlighter, Palette.DefaultThickness), highlight.Style.Width);
    }

    [Fact]
    public void Pressing_shift_mid_drag_updates_the_preview()
    {
        _tools.Tool = ToolKind.Line;
        _tools.PointerDown(new Point(0, 0), false, Tolerance);
        _tools.PointerMove(new Point(100, 4), false);

        _tools.ShiftChanged(true);

        Assert.Equal(new Point(100, 0), Assert.IsType<LineMark>(_tools.Preview).End);
    }

    [Fact]
    public void A_curve_waits_to_be_bent_then_enter_finishes_it()
    {
        _tools.Tool = ToolKind.Curve;

        Drag(0, 0, 100, 0);
        Assert.Empty(_committed);
        Assert.True(_tools.IsBending);
        Assert.Equal(new Point(50, 0), _tools.BendHandle);

        _tools.PointerDown(new Point(52, 2), false, Tolerance); // grabs the handle
        _tools.PointerMove(new Point(50, 40), false);
        _tools.PointerUp(new Point(50, 40), false);
        Assert.Empty(_committed);

        _tools.Finish();
        var curve = Assert.IsType<CurveMark>(Assert.Single(_committed));
        Assert.Equal(new Point(50, 40), curve.Through);
        Assert.False(_tools.IsBending);
    }

    [Fact]
    public void Clicking_away_from_a_curve_finishes_it_and_starts_the_next()
    {
        _tools.Tool = ToolKind.Curve;
        Drag(0, 0, 100, 0);

        Drag(200, 200, 300, 200);

        Assert.Single(_committed);
        Assert.True(_tools.IsBending);
    }

    [Fact]
    public void Switching_tools_finishes_a_curve()
    {
        _tools.Tool = ToolKind.Curve;
        Drag(0, 0, 100, 0);

        _tools.Tool = ToolKind.Line;

        Assert.IsType<CurveMark>(Assert.Single(_committed));
    }

    [Fact]
    public void Escape_throws_a_curve_away()
    {
        _tools.Tool = ToolKind.Curve;
        Drag(0, 0, 100, 0);

        _tools.Cancel();
        _tools.Finish();

        Assert.Empty(_committed);
        Assert.Null(_tools.Preview);
    }

    [Fact]
    public void The_eraser_makes_no_marks()
    {
        _tools.Tool = ToolKind.Eraser;

        Drag(0, 0, 100, 100);

        Assert.Empty(_committed);
        Assert.Null(_tools.Preview);
    }

    [Fact]
    public void A_crop_box_waits_for_enter()
    {
        Rect? applied = null;
        _tools.CropApplied += box => applied = box;
        _tools.Tool = ToolKind.Crop;

        Drag(10, 20, 110, 70);
        Assert.Equal(new Rect(10, 20, 100, 50), _tools.CropBox);
        Assert.Null(applied);

        _tools.Finish();
        Assert.Equal(new Rect(10, 20, 100, 50), applied);
        Assert.Null(_tools.CropBox);
        Assert.Empty(_committed);
    }

    [Fact]
    public void A_tiny_crop_box_is_ignored()
    {
        _tools.Tool = ToolKind.Crop;

        Drag(10, 10, 12, 30);

        Assert.Null(_tools.CropBox);
    }

    [Fact]
    public void Escape_or_switching_tools_drops_a_crop_box()
    {
        Rect? applied = null;
        _tools.CropApplied += box => applied = box;
        _tools.Tool = ToolKind.Crop;

        Drag(0, 0, 50, 50);
        _tools.Cancel();
        Assert.Null(_tools.CropBox);

        Drag(0, 0, 50, 50);
        _tools.Tool = ToolKind.Line;
        Assert.Null(_tools.CropBox);
        Assert.Null(applied);
    }

    [Fact]
    public void The_highlighter_is_four_times_as_wide_as_the_slider()
    {
        Assert.Equal(20, Palette.StrokeWidth(ToolKind.Highlighter, 5));
        Assert.Equal(8, Palette.StrokeWidth(ToolKind.Highlighter, 1));
        Assert.Equal(5, Palette.StrokeWidth(ToolKind.Line, 5));
    }

    [Fact]
    public void The_arrowhead_toggle_applies_to_a_curve_being_bent()
    {
        _tools.Tool = ToolKind.Curve;
        Drag(0, 0, 100, 0);

        _tools.CurveArrowhead = true;
        _tools.Finish();

        Assert.True(Assert.IsType<CurveMark>(Assert.Single(_committed)).Arrowhead);
    }
}
