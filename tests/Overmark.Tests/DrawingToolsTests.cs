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
    public void The_text_tool_types_where_it_is_clicked_and_enter_keeps_it()
    {
        _tools.Tool = ToolKind.Text;
        _tools.Width = 5;

        _tools.PointerDown(new Point(100, 50), false, Tolerance);
        _tools.PointerUp(new Point(100, 50), false);
        Assert.True(_tools.HasPending);
        _tools.SetText("Hello");
        Assert.Empty(_committed);

        _tools.Finish();
        var text = Assert.IsType<TextMark>(Assert.Single(_committed));
        Assert.Equal("Hello", text.Text);
        Assert.Equal(20, text.FontSize);
        Assert.Equal(100, text.Position.X);
        Assert.Equal(50, text.Position.Y + TextMark.LineHeight(20) / 2, 6); // first line centred on the click
        Assert.Null(_tools.Typing);
    }

    [Fact]
    public void Blank_text_is_not_kept_and_trailing_space_is_dropped()
    {
        _tools.Tool = ToolKind.Text;

        _tools.PointerDown(new Point(10, 10), false, Tolerance);
        _tools.SetText("  \r\n ");
        _tools.Finish();
        Assert.Empty(_committed);

        _tools.PointerDown(new Point(10, 10), false, Tolerance);
        _tools.SetText("Two\r\nlines \r\n");
        _tools.Finish();
        Assert.Equal("Two\r\nlines", Assert.IsType<TextMark>(Assert.Single(_committed)).Text);
    }

    [Fact]
    public void Clicking_elsewhere_finishes_text_and_starts_the_next()
    {
        _tools.Tool = ToolKind.Text;
        _tools.PointerDown(new Point(10, 10), false, Tolerance);
        _tools.SetText("First");

        _tools.PointerDown(new Point(200, 100), false, Tolerance);

        Assert.Equal("First", Assert.IsType<TextMark>(Assert.Single(_committed)).Text);
        Assert.Equal("", _tools.Typing?.Text);
        Assert.Equal(200, _tools.Typing?.Origin.X);
    }

    [Fact]
    public void Colour_size_and_box_apply_to_text_still_being_typed()
    {
        _tools.Tool = ToolKind.Text;
        _tools.PointerDown(new Point(10, 10), false, Tolerance);
        _tools.SetText("Note");

        _tools.Colour = Palette.Colours[4];
        _tools.Width = 10;
        _tools.TextBoxed = true;
        _tools.Finish();

        var text = Assert.IsType<TextMark>(Assert.Single(_committed));
        Assert.Equal(new StrokeStyle(Palette.Colours[4], 40), text.Style);
        Assert.True(text.Boxed);
    }

    [Fact]
    public void Switching_tools_keeps_text_and_escape_drops_it()
    {
        _tools.Tool = ToolKind.Text;
        _tools.PointerDown(new Point(10, 10), false, Tolerance);
        _tools.SetText("Kept");
        _tools.Tool = ToolKind.Arrow;
        Assert.Single(_committed);

        _tools.Tool = ToolKind.Text;
        _tools.PointerDown(new Point(10, 10), false, Tolerance);
        _tools.SetText("Dropped");
        _tools.Cancel();
        _tools.Finish();
        Assert.Single(_committed);
        Assert.Null(_tools.Typing);
    }

    private void Click(double x, double y, bool shift = false)
    {
        _tools.PointerDown(new Point(x, y), shift, Tolerance);
        _tools.PointerUp(new Point(x, y), shift);
    }

    [Fact]
    public void Three_clicks_place_a_dimension_then_its_text_is_typed()
    {
        _tools.Tool = ToolKind.Dimension;
        _tools.Width = 4;

        Click(100, 100);
        Click(300, 100);
        Assert.Equal([new Point(100, 100), new Point(300, 100)], _tools.DimensionPoints);
        _tools.PointerMove(new Point(200, 60), false);
        Assert.Equal(DimensionKind.Horizontal, Assert.IsType<DimensionMark>(_tools.Preview).Kind);
        Assert.StartsWith("Horizontal", _tools.DimensionHint?.Text);

        Click(200, 60);
        Assert.Equal("200 px", _tools.Typing?.Text); // starts with the measured length
        Assert.Empty(_tools.DimensionPoints);
        Assert.Empty(_committed);

        _tools.SetText("50.0 mm");
        _tools.Finish();
        var dimension = Assert.IsType<DimensionMark>(Assert.Single(_committed));
        Assert.Equal("50.0 mm", dimension.Text);
        Assert.Equal((new Point(100, 100), new Point(300, 100), new Point(200, 60)), (dimension.A, dimension.B, dimension.Through));
        Assert.Equal(new StrokeStyle(Palette.Colours[0], 4), dimension.Style);
        Assert.Null(_tools.Typing);
        Assert.False(_tools.HasPending);
    }

    [Fact]
    public void Dragging_from_the_first_point_picks_the_second()
    {
        _tools.Tool = ToolKind.Dimension;

        Drag(10, 10, 10, 150);

        Assert.Equal([new Point(10, 10), new Point(10, 150)], _tools.DimensionPoints);
    }

    [Fact]
    public void Shift_snaps_a_dimensions_second_point_but_not_where_it_is_placed()
    {
        _tools.Tool = ToolKind.Dimension;

        Click(100, 100, shift: true);
        Click(300, 108, shift: true);
        Assert.Equal([new Point(100, 100), new Point(300, 100)], _tools.DimensionPoints);

        Click(230, 60, shift: true);
        _tools.Finish();
        var dimension = Assert.IsType<DimensionMark>(Assert.Single(_committed));
        Assert.Equal((new Point(300, 100), new Point(230, 60), DimensionKind.Horizontal), (dimension.B, dimension.Through, dimension.Kind));
        Assert.Equal("200 px", dimension.Text);
    }

    [Fact]
    public void Shift_snaps_a_dimension_dragged_between_its_points()
    {
        _tools.Tool = ToolKind.Dimension;

        Drag(10, 10, 14, 150, shift: true);
        Assert.Equal([new Point(10, 10), new Point(10, 150)], _tools.DimensionPoints);

        _tools.Cancel();
        Drag(0, 0, 100, 92, shift: true); // 45°
        var second = _tools.DimensionPoints[1];
        Assert.Equal(96, second.X, 6);
        Assert.Equal(96, second.Y, 6);
    }

    [Fact]
    public void The_dimension_guide_line_follows_shift()
    {
        _tools.Tool = ToolKind.Dimension;
        Click(0, 0);
        _tools.PointerMove(new Point(100, 6), true);
        Assert.Equal(new Point(100, 0), _tools.DimensionGuide);

        _tools.ShiftChanged(false);
        Assert.Equal(new Point(100, 6), _tools.DimensionGuide);

        _tools.ShiftChanged(true);
        Click(100, 6); // Shift up by the time it's clicked: the click's own Shift decides
        Assert.Equal(new Point(100, 6), _tools.DimensionPoints[1]);
    }

    [Fact]
    public void Clicking_away_from_dimension_text_finishes_it_without_starting_another()
    {
        _tools.Tool = ToolKind.Dimension;
        Click(0, 0);
        Click(100, 0);
        Click(50, 30);
        _tools.SetText("");

        Click(400, 400);

        Assert.Equal("", Assert.IsType<DimensionMark>(Assert.Single(_committed)).Text); // kept, with no text
        Assert.Empty(_tools.DimensionPoints);
        Assert.False(_tools.HasPending);
    }

    [Fact]
    public void Escape_drops_a_dimension_at_any_stage()
    {
        _tools.Tool = ToolKind.Dimension;
        Click(0, 0);
        _tools.Cancel();
        Assert.Empty(_tools.DimensionPoints);

        Click(0, 0);
        Click(100, 0);
        Click(50, 30);
        _tools.Cancel();
        _tools.Finish();

        Assert.Empty(_committed);
        Assert.Null(_tools.Preview);
    }

    [Fact]
    public void The_move_tool_draws_nothing()
    {
        _tools.Tool = ToolKind.Move;

        Drag(0, 0, 100, 100);

        Assert.Empty(_committed);
        Assert.Null(_tools.Preview);
    }

    [Fact]
    public void Text_is_four_times_the_thickness_with_a_minimum()
    {
        Assert.Equal(20, Palette.StrokeWidth(ToolKind.Text, 5));
        Assert.Equal(10, Palette.StrokeWidth(ToolKind.Text, 1));
    }

    [Fact]
    public void Boxed_text_gets_a_contrasting_backdrop()
    {
        Assert.Equal(Colors.White, Palette.Backdrop(Palette.Colours[0])); // red
        Assert.Equal(Colors.White, Palette.Backdrop(Palette.Colours[6])); // black
        Assert.NotEqual(Colors.White, Palette.Backdrop(Palette.Colours[2])); // yellow
        Assert.NotEqual(Colors.White, Palette.Backdrop(Palette.Colours[7])); // white
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
