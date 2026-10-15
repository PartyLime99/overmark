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
}
