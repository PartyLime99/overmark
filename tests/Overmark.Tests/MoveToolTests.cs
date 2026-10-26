using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Overmark.Editor;

namespace Overmark.Tests;

public class MoveToolTests
{
    private static readonly StrokeStyle Red = new(Palette.Colours[0], 4);

    private readonly EditorDocument _document = new(BlankImage());
    private readonly MoveTool _move;

    public MoveToolTests() => _move = new MoveTool(_document);

    private static BitmapSource BlankImage()
    {
        var image = BitmapSource.Create(500, 400, 96, 96, PixelFormats.Bgr32, null, new byte[500 * 400 * 4], 500 * 4);
        image.Freeze();
        return image;
    }

    private void Drag(Point from, Point to, bool shift = false)
    {
        _move.PointerDown(from);
        _move.PointerMove(from + (to - from) / 2, shift);
        _move.PointerMove(to, shift);
        _move.PointerUp();
    }

    private static DimensionMark Dimension() =>
        new(Red, new Point(100, 200), new Point(300, 200), new Point(200, 150), DimensionKind.Horizontal, "50");

    [Fact]
    public void Clicking_a_mark_selects_it_and_clicking_nothing_lets_go()
    {
        _document.Add(new LineMark(Red, new Point(10, 10), new Point(100, 10)));

        _move.PointerDown(new Point(50, 12));
        _move.PointerUp();
        Assert.Equal(0, _move.Selected);
        Assert.Equal(new LineMark(Red, new Point(10, 10), new Point(100, 10)), _document.Marks[0]);

        _move.PointerDown(new Point(300, 300));
        Assert.Null(_move.Selected);
    }

    [Fact]
    public void Dragging_a_mark_moves_it_and_undo_puts_it_back_in_one_step()
    {
        var line = new LineMark(Red, new Point(10, 10), new Point(100, 10));
        _document.Add(line);

        Drag(new Point(50, 10), new Point(80, 50));

        Assert.Equal(new LineMark(Red, new Point(40, 50), new Point(130, 50)), _document.Marks[0]);
        _document.Undo();
        Assert.Equal(line, _document.Marks[0]);
        Assert.Equal(0, _move.Selected); // still selected
        _document.Undo();
        Assert.Empty(_document.Marks);
    }

    [Fact]
    public void A_click_without_moving_is_not_an_undo_step()
    {
        _document.Add(new LineMark(Red, new Point(10, 10), new Point(100, 10)));
        _document.MarkSaved();

        _move.PointerDown(new Point(50, 10));
        _move.PointerMove(new Point(50.5, 10.5), false); // a wobble
        _move.PointerUp();

        Assert.False(_document.HasUnsavedChanges);
    }

    [Fact]
    public void The_top_mark_is_the_one_that_moves()
    {
        _document.Add(new LineMark(Red, new Point(0, 50), new Point(100, 50)));
        _document.Add(new LineMark(Red, new Point(50, 0), new Point(50, 100)));

        _move.PointerDown(new Point(50, 50));

        Assert.Equal(1, _move.Selected);
    }

    [Fact]
    public void Dimension_text_moves_on_its_own_and_snaps_back_onto_the_line()
    {
        _document.Add(Dimension());

        Drag(new Point(200, 150), new Point(380, 100));
        var moved = (DimensionMark)_document.Marks[0];
        Assert.Equal(new Point(380, 100), moved.TextAt);
        Assert.Equal(new Point(100, 200), moved.A);
        Assert.False(moved.Layout.TextOnLine);

        Drag(new Point(380, 100), new Point(390, 147));
        Assert.Equal(new Point(390, 150), ((DimensionMark)_document.Marks[0]).TextAt); // within reach of the line
    }

    [Fact]
    public void Dragging_the_dimension_line_moves_it_in_or_out()
    {
        _document.Add(Dimension());

        Drag(new Point(140, 150), new Point(150, 120));

        var moved = (DimensionMark)_document.Marks[0];
        Assert.Equal(new Point(100, 200), moved.A);
        Assert.Equal(new Point(300, 200), moved.B);
        Assert.Equal(120, moved.Layout.P1.Y, 6);
    }

    [Fact]
    public void Dragging_an_extension_line_moves_the_whole_dimension()
    {
        _document.Add(Dimension());

        Drag(new Point(100, 180), new Point(110, 190));

        Assert.Equal(Dimension().MovedBy(new Vector(10, 10)), _document.Marks[0]);
    }

    [Fact]
    public void The_handle_turns_a_selected_dimensions_text()
    {
        _document.Add(Dimension());
        _move.PointerDown(new Point(200, 150));
        _move.PointerUp();
        var handle = Assert.NotNull(_move.RotateHandle);
        Assert.True(handle.Y < 150);

        // Pulled round to the right of the text: upright (90°), snapped from a little off.
        Drag(handle, new Point(260, 147));
        Assert.Equal(90, ((DimensionMark)_document.Marks[0]).TextAngle);

        // Shift turns it freely.
        var turned = Assert.NotNull(_move.RotateHandle);
        Drag(turned, new Point(250, 120), shift: true);
        var angle = ((DimensionMark)_document.Marks[0]).TextAngle;
        Assert.InRange(angle, 50, 60);
    }

    [Fact]
    public void Picking_a_colour_recolours_the_selection_and_undo_puts_it_back()
    {
        var line = new LineMark(Red, new Point(10, 10), new Point(100, 10));
        _document.Add(line);
        _document.MarkSaved();

        _move.Recolour(Palette.Colours[1]); // nothing selected yet
        Assert.Equal(line, _document.Marks[0]);

        _move.PointerDown(new Point(50, 10));
        _move.PointerUp();
        _move.Recolour(Red.Color); // already that colour: not an edit
        Assert.False(_document.HasUnsavedChanges);

        _move.Recolour(Palette.Colours[1]);
        Assert.Equal(line with { Style = Red with { Color = Palette.Colours[1] } }, _document.Marks[0]);
        Assert.Equal(_document.Marks[0], _move.SelectedMark);

        _document.Undo();
        Assert.Equal(line, _document.Marks[0]);
        Assert.Equal(0, _move.Selected);
    }

    [Fact]
    public void The_slider_changes_the_selections_thickness_and_a_drag_of_it_undoes_in_one_step()
    {
        var line = new LineMark(Red, new Point(10, 10), new Point(100, 10));
        _document.Add(line);
        _document.MarkSaved();

        _move.SetThickness(9); // nothing selected yet
        Assert.Equal(line, _document.Marks[0]);

        _move.PointerDown(new Point(50, 10));
        _move.PointerUp();
        _move.SetThickness(4); // already that thick: not an edit
        Assert.False(_document.HasUnsavedChanges);

        _move.SetThickness(6, finished: false);
        _move.SetThickness(8, finished: false);
        _move.SetThickness(9);
        Assert.Equal(line with { Style = Red with { Width = 9 } }, _document.Marks[0]);
        Assert.Equal(_document.Marks[0], _move.SelectedMark);

        _document.Undo(); // straight back, not through 8 and 6
        Assert.Equal(line, _document.Marks[0]);
    }

    [Fact]
    public void Double_clicking_finds_text_and_dimensions_but_not_other_marks()
    {
        var text = new TextMark(Red with { Width = 20 }, new Point(300, 300), "Note", Boxed: false);
        var dimension = Dimension();
        _document.Add(new LineMark(Red, new Point(10, 10), new Point(100, 10)));
        _document.Add(text);
        _document.Add(dimension);

        Assert.Null(_move.TextAt(new Point(50, 10)));
        Assert.Same(text, _move.TextAt(text.Bounds.TopLeft + new Vector(5, 5)));
        Assert.Same(dimension, _move.TextAt(dimension.Layout.TextCentre));
        Assert.Same(dimension, _move.TextAt(new Point(100, 175))); // its extension line
        Assert.Null(_move.TextAt(new Point(450, 50)));

        // A mark on top of the text is the one that's found.
        _document.Add(new RectangleMark(Red, new Rect(text.Bounds.TopLeft, new Size(10, 10))));
        Assert.Null(_move.TextAt(text.Bounds.TopLeft + new Vector(5, 5)));
    }

    [Fact]
    public void Nudging_and_deleting_the_selection_can_be_undone()
    {
        var line = new LineMark(Red, new Point(10, 10), new Point(100, 10));
        _document.Add(line);
        _move.PointerDown(new Point(50, 10));
        _move.PointerUp();

        _move.Nudge(new Vector(10, 0));
        Assert.Equal(line.MovedBy(new Vector(10, 0)), _document.Marks[0]);

        _move.DeleteSelected();
        Assert.Empty(_document.Marks);
        Assert.Null(_move.Selected);

        _document.Undo();
        _document.Undo();
        Assert.Equal(line, Assert.Single(_document.Marks));
    }

    private void Select(Point on)
    {
        _move.PointerDown(on);
        _move.PointerUp();
    }

    private void ShiftClick(Point on)
    {
        _move.PointerDown(on, add: true);
        _move.PointerUp();
    }

    [Fact]
    public void A_selected_line_shows_handles_at_its_ends_and_one_drags_just_that_end()
    {
        var arrow = new ArrowMark(Red, new Point(10, 10), new Point(100, 10));
        _document.Add(arrow);
        Assert.Empty(_move.Handles); // nothing selected yet

        Select(new Point(50, 10));
        Assert.Equal([(ShapeHandle.Start, new Point(10, 10)), (ShapeHandle.End, new Point(100, 10))], _move.Handles);
        Assert.Equal(MoveGrip.Handle, _move.GripAt(new Point(102, 12)));
        Assert.Equal(ShapeHandle.End, _move.HandleAt(new Point(102, 12)));

        Drag(new Point(102, 12), new Point(152, 62)); // grabbed a little off the handle: the end keeps that offset
        Assert.Equal(arrow with { End = new Point(150, 60) }, _document.Marks[0]);

        _document.Undo();
        Assert.Equal(arrow, _document.Marks[0]);
    }

    [Fact]
    public void Pressing_the_end_of_a_mark_that_isnt_selected_moves_all_of_it()
    {
        var line = new LineMark(Red, new Point(10, 10), new Point(100, 10));
        _document.Add(line);

        Drag(new Point(100, 10), new Point(120, 30));

        Assert.Equal(line.MovedBy(new Vector(20, 20)), _document.Marks[0]);
    }

    [Fact]
    public void Shift_snaps_a_dragged_end_to_straight_angles()
    {
        var line = new LineMark(Red, new Point(10, 10), new Point(100, 10));
        _document.Add(line);
        Select(new Point(50, 10));

        Drag(new Point(100, 10), new Point(200, 18), shift: true);

        Assert.Equal(line with { End = new Point(200, 10) }, _document.Marks[0]);
    }

    [Fact]
    public void A_boxs_corners_and_sides_resize_it()
    {
        var box = new RectangleMark(Red, new Rect(100, 100, 100, 50));
        _document.Add(box);
        Select(new Point(150, 100));
        Assert.Equal(8, _move.Handles.Count);

        Drag(new Point(200, 150), new Point(260, 170)); // bottom-right corner
        Assert.Equal(new Rect(100, 100, 160, 70), ((RectangleMark)_document.Marks[0]).Bounds);

        Drag(new Point(100, 135), new Point(80, 300)); // left side: only across
        Assert.Equal(new Rect(80, 100, 180, 70), ((RectangleMark)_document.Marks[0]).Bounds);

        Drag(new Point(80, 100), new Point(40, 90), shift: true); // top-left corner, square
        Assert.Equal(new Rect(40, -50, 220, 220), ((RectangleMark)_document.Marks[0]).Bounds);

        _document.Undo();
        _document.Undo();
        _document.Undo();
        Assert.Equal(box, _document.Marks[0]);
    }

    [Fact]
    public void A_circle_resizes_by_the_box_round_it()
    {
        _document.Add(new EllipseMark(Red, new Rect(100, 100, 100, 50)));
        Select(new Point(100, 125)); // its left edge

        Drag(new Point(150, 150), new Point(150, 200)); // bottom handle

        Assert.Equal(new Rect(100, 100, 100, 100), ((EllipseMark)_document.Marks[0]).Bounds);
    }

    [Fact]
    public void A_curves_middle_handle_bends_it()
    {
        var curve = new CurveMark(Red, new Point(10, 100), new Point(60, 100), new Point(110, 100), Arrowhead: false);
        _document.Add(curve);
        Select(new Point(30, 100));
        Assert.Equal(ShapeHandle.Through, _move.HandleAt(new Point(60, 100)));

        Drag(new Point(60, 100), new Point(60, 40));

        Assert.Equal(curve with { Through = new Point(60, 40) }, _document.Marks[0]);
    }

    [Fact]
    public void Text_and_freehand_have_no_handles()
    {
        var text = new TextMark(new StrokeStyle(Palette.Colours[0], 20), new Point(100, 100), "Label", Boxed: false);
        _document.Add(text);
        Select(text.Bounds.TopLeft + new Vector(5, 5));
        Assert.Empty(_move.Handles);

        _document.Add(new FreehandMark(Red, [new Point(300, 300), new Point(320, 310), new Point(340, 300)]));
        Select(new Point(320, 308));
        Assert.Equal(1, _move.Selected);
        Assert.Empty(_move.Handles);
    }

    [Fact]
    public void Moving_a_dimensions_point_measures_it_again_unless_its_text_was_typed()
    {
        var measured = new DimensionMark(Red, new Point(100, 200), new Point(300, 200), new Point(200, 150), DimensionKind.Horizontal, "200 px");
        _document.Add(measured);
        Select(new Point(200, 150)); // its dimension line
        Assert.Equal([(ShapeHandle.Start, new Point(100, 200)), (ShapeHandle.End, new Point(300, 200))], _move.Handles);

        Drag(new Point(300, 200), new Point(350, 230));
        var moved = Assert.IsType<DimensionMark>(_document.Marks[0]);
        Assert.Equal((new Point(350, 230), "250 px", new Point(200, 150)), (moved.B, moved.Text, moved.Through));

        _document.Undo();
        _document.Replace(_document.Marks[0], measured with { Text = "Gap" });
        Select(new Point(200, 150));
        Drag(new Point(300, 200), new Point(350, 230));
        Assert.Equal("Gap", Assert.IsType<DimensionMark>(_document.Marks[0]).Text);
    }

    [Fact]
    public void Double_clicking_a_dimensions_handle_doesnt_start_changing_its_words()
    {
        var dimension = Dimension();
        _document.Add(dimension);
        Select(new Point(200, 150));

        Assert.Null(_move.TextAt(new Point(300, 200))); // its point B
        Assert.Same(dimension, _move.TextAt(new Point(300, 175))); // its extension line, further along
    }

    [Fact]
    public void A_pasted_mark_lands_a_step_down_and_right_selected_and_undoes_in_one_step()
    {
        var line = new LineMark(Red, new Point(10, 10), new Point(100, 10));
        _document.Add(line);

        _move.Paste([new CopiedMark(line, Measures: false)], 1);

        Assert.Equal([line, line.MovedBy(new Vector(10, 10))], _document.Marks);
        Assert.Equal(1, _move.Selected);
        _document.Undo();
        Assert.Equal([line], _document.Marks);
    }

    [Fact]
    public void Each_paste_lands_a_step_further_on_and_a_step_is_the_same_on_screen_at_any_zoom()
    {
        var line = new LineMark(Red, new Point(10, 10), new Point(100, 10));
        _move.DipSize = 2; // zoomed out to 50%

        _move.Paste([new CopiedMark(line, Measures: false)], 1);
        _move.Paste([new CopiedMark(line, Measures: false)], 2);

        Assert.Equal([line.MovedBy(new Vector(20, 20)), line.MovedBy(new Vector(40, 40))], _document.Marks);
    }

    [Fact]
    public void A_pasted_mark_is_kept_inside_the_crop()
    {
        var crop = new Rect(50, 50, 200, 100);
        _document.CropTo(crop);

        // A step on would take it over the bottom-right edge; one copied from a bigger capture is nowhere near.
        foreach (var bounds in new[] { new Rect(200, 100, 40, 40), new Rect(1000, -500, 40, 40) })
        {
            _move.Paste([new CopiedMark(new RectangleMark(Red, bounds), Measures: false)], 1);
            Assert.True(crop.Contains(_document.Marks[^1].Extent), $"{_document.Marks[^1].Extent} is outside the crop");
        }

        // Too big to fit: its top-left corner goes in the crop's.
        _move.Paste([new CopiedMark(new RectangleMark(Red, new Rect(0, 0, 400, 300)), Measures: false)], 1);
        Assert.Equal(crop.TopLeft, _document.Marks[^1].Extent.TopLeft);
    }

    [Fact]
    public void A_pasted_dimension_measures_in_this_pictures_scale_without_setting_it()
    {
        // Here, the 200 px dimension was typed as 50 mm.
        _document.Add(Dimension() with { Text = "50 mm", SetsScale = true });
        var scale = _document.Scale;

        // From another capture, where its 100 px were typed as 10 in.
        var copied = new DimensionMark(Red, new Point(0, 300), new Point(100, 300), new Point(50, 280), DimensionKind.Horizontal, "10 in", SetsScale: true);
        _move.Paste([new CopiedMark(copied, Measures: true)], 1);

        var pasted = Assert.IsType<DimensionMark>(_document.Marks[1]);
        Assert.False(pasted.SetsScale);
        Assert.Equal("25 mm", pasted.Text);
        Assert.Equal(scale, _document.Scale);
        Assert.True(Assert.IsType<DimensionMark>(_document.Marks[0]).SetsScale);
    }

    [Fact]
    public void A_pasted_dimension_with_its_own_words_keeps_them()
    {
        _move.Paste([new CopiedMark(Dimension() with { Text = "Gap" }, Measures: false)], 1);

        Assert.Equal("Gap", Assert.IsType<DimensionMark>(_document.Marks[0]).Text);
    }

    [Fact]
    public void Shift_click_adds_a_mark_to_the_selection_and_takes_it_out_again()
    {
        _document.Add(new LineMark(Red, new Point(10, 10), new Point(100, 10)));
        _document.Add(new LineMark(Red, new Point(10, 50), new Point(100, 50)));
        Select(new Point(50, 10));

        ShiftClick(new Point(50, 50));
        Assert.Equal([0, 1], _move.Selection);
        Assert.Null(_move.Selected); // not just one
        Assert.Empty(_move.Handles);

        ShiftClick(new Point(50, 10));
        Assert.Equal([1], _move.Selection);
        Assert.Equal(1, _move.Selected);
        Assert.NotEmpty(_move.Handles);
    }

    [Fact]
    public void Dragging_one_of_several_selected_marks_moves_them_all_in_one_undo_step()
    {
        var top = new LineMark(Red, new Point(10, 10), new Point(100, 10));
        var box = new RectangleMark(Red, new Rect(200, 200, 50, 50));
        var other = new LineMark(Red, new Point(10, 300), new Point(100, 300));
        _document.Add(top);
        _document.Add(box);
        _document.Add(other);
        Select(new Point(50, 10));
        ShiftClick(new Point(200, 220));

        Drag(new Point(50, 10), new Point(70, 40));

        Assert.Equal([top.MovedBy(new Vector(20, 30)), box.MovedBy(new Vector(20, 30)), other], _document.Marks);
        Assert.Equal([0, 1], _move.Selection);
        _document.Undo();
        Assert.Equal([top, box, other], _document.Marks);
        Assert.Equal([0, 1], _move.Selection);
    }

    [Fact]
    public void Clicking_one_of_several_selected_marks_without_moving_selects_just_it()
    {
        _document.Add(new LineMark(Red, new Point(10, 10), new Point(100, 10)));
        _document.Add(new LineMark(Red, new Point(10, 50), new Point(100, 50)));
        _document.MarkSaved();
        _move.SelectAll();

        Select(new Point(50, 50));

        Assert.Equal([1], _move.Selection);
        Assert.False(_document.HasUnsavedChanges); // nothing moved
    }

    [Fact]
    public void A_box_from_an_empty_spot_selects_the_marks_wholly_inside_it()
    {
        _document.Add(new LineMark(Red, new Point(10, 10), new Point(100, 10)));
        _document.Add(new LineMark(Red, new Point(10, 50), new Point(100, 50)));
        _document.Add(new LineMark(Red, new Point(10, 90), new Point(300, 90))); // reaches out of the box
        _document.Add(new LineMark(Red, new Point(10, 200), new Point(100, 200)));
        _document.MarkSaved();

        _move.PointerDown(new Point(0, 0));
        _move.PointerMove(new Point(150, 120), false);
        Assert.Equal(new Rect(0, 0, 150, 120), _move.SelectionBox!.Value);
        Assert.Equal([0, 1], _move.Selection); // as it's dragged
        _move.PointerUp();
        Assert.Null(_move.SelectionBox);
        Assert.Equal([0, 1], _move.Selection);

        // Shift (or Ctrl) adds what's in the next box.
        _move.PointerDown(new Point(0, 180), add: true);
        _move.PointerMove(new Point(150, 220), false);
        _move.PointerUp();
        Assert.Equal([0, 1, 3], _move.Selection);

        // Otherwise, a box starts again; one round nothing selects nothing.
        _move.PointerDown(new Point(400, 300));
        _move.PointerMove(new Point(450, 350), false);
        _move.PointerUp();
        Assert.Empty(_move.Selection);
        Assert.False(_document.HasUnsavedChanges); // selecting changed no marks
    }

    [Fact]
    public void Several_selected_marks_nudge_recolour_thicken_and_delete_together_each_in_one_undo_step()
    {
        var line = new LineMark(Red, new Point(10, 10), new Point(100, 10));
        var box = new RectangleMark(Red, new Rect(200, 200, 50, 50));
        var pixelated = new RedactMark(Red, new Rect(300, 50, 40, 40), Pixelate: true);
        _document.Add(line);
        _document.Add(box);
        _document.Add(pixelated);
        _move.SelectAll();

        _move.Nudge(new Vector(1, 0));
        Assert.Equal([line.MovedBy(new Vector(1, 0)), box.MovedBy(new Vector(1, 0)), pixelated.MovedBy(new Vector(1, 0))], _document.Marks);
        _document.Undo();
        Assert.Equal([line, box, pixelated], _document.Marks);

        _move.Recolour(Palette.Colours[1]);
        Assert.Equal(Palette.Colours[1], _document.Marks[0].Style.Color);
        Assert.Equal(Palette.Colours[1], _document.Marks[1].Style.Color);
        Assert.Same(pixelated, _document.Marks[2]); // its colours come from the picture
        _document.Undo();
        Assert.Equal([line, box, pixelated], _document.Marks);

        _move.SetThickness(6, finished: false);
        _move.SetThickness(8);
        Assert.Equal(8, _document.Marks[0].Style.Width);
        Assert.Equal(8, _document.Marks[1].Style.Width);
        _document.Undo();
        Assert.Equal([line, box, pixelated], _document.Marks);

        _move.DeleteSelected();
        Assert.Empty(_document.Marks);
        Assert.Empty(_move.Selection);
        _document.Undo();
        Assert.Equal([line, box, pixelated], _document.Marks);
    }

    [Fact]
    public void Several_marks_pasted_together_keep_their_arrangement_inside_the_crop_and_are_selected()
    {
        var crop = new Rect(0, 0, 300, 200);
        _document.CropTo(crop);
        _document.Add(new LineMark(Red, new Point(10, 10), new Point(20, 10)));
        var left = new RectangleMark(Red, new Rect(200, 150, 20, 20));
        var right = new RectangleMark(Red, new Rect(275, 170, 20, 20)); // a step on takes it out
        var copied = new CopiedMark[] { new(left, Measures: false), new(right, Measures: false) };

        _move.Paste(copied, 1);

        var pasted = _document.Marks.Skip(1).ToList();
        Assert.Equal(right.Bounds.TopLeft - left.Bounds.TopLeft, ((RectangleMark)pasted[1]).Bounds.TopLeft - ((RectangleMark)pasted[0]).Bounds.TopLeft);
        Assert.All(pasted, mark => Assert.True(crop.Contains(mark.Extent), $"{mark.Extent} is outside the crop"));
        Assert.Equal([1, 2], _move.Selection);
        _document.Undo();
        Assert.Single(_document.Marks);
    }
}
