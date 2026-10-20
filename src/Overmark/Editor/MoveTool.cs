using System.Windows;
using System.Windows.Media;

namespace Overmark.Editor;

/// <summary>What the move tool has hold of.</summary>
internal enum MoveGrip
{
    /// <summary>The whole mark (for a dimension, by an extension line).</summary>
    Whole,

    /// <summary>A dimension's text, which moves on its own.</summary>
    DimensionText,

    /// <summary>A dimension's line, which moves in or out while the measured points stay put.</summary>
    DimensionLine,

    /// <summary>The round handle that turns a dimension's text.</summary>
    TextAngle,

    /// <summary>One of the selected mark's handles (see <see cref="Mark.Handles"/>), which reshapes it.</summary>
    Handle,
}

/// <summary>
/// The move tool: click a mark to select it and drag to move it; the arrow keys nudge it and Delete removes it.
/// A selected mark shows handles where it can be reshaped: dragging the end of a line, arrow or curve moves that
/// end (and a curve's middle bends it), a box's corners and sides resize it, and a dimension's measured points move.
/// For a dimension, dragging its text moves just the text (snapping onto the dimension line when it's close), the
/// round handle above the text turns it, dragging the dimension line moves it in or out, and dragging an extension
/// line moves the whole dimension. Picking a colour or thickness gives it to the selection. Double-clicking text (or a
/// dimension) is for changing its words, which the editor does with <see cref="DrawingTools.EditText"/>. Each drag,
/// nudge, recolour or change of thickness is one undo step. No UI, so it can be unit tested.
/// </summary>
internal sealed class MoveTool
{
    private readonly EditorDocument _document;
    private (MoveGrip Grip, Point From, Mark Original, (ShapeHandle Handle, Point At)? Handle)? _drag;
    private bool _moving;
    private int _markCount;

    public MoveTool(EditorDocument document)
    {
        _document = document;
        _markCount = document.Marks.Count;
        document.Changed += KeepSelection;
    }

    /// <summary>Raised when the selection changes, or the selected mark moves.</summary>
    public event Action? Changed;

    /// <summary>How many image pixels one on-screen DIP covers, so the reach of a click and the handle's place stay the same on screen.</summary>
    public double DipSize { get; set; } = 1;

    /// <summary>The index of the selected mark in the document, if any.</summary>
    public int? Selected { get; private set; }

    public Mark? SelectedMark { get; private set; }

    /// <summary>Where the round handle for turning a selected dimension's text is, or null.</summary>
    public Point? RotateHandle =>
        SelectedMark is DimensionMark { Text.Length: > 0 } dimension ? HandleFor(dimension.Layout) : null;

    /// <summary>The handle's radius, in image pixels.</summary>
    public double HandleRadius => 6 * DipSize;

    /// <summary>The handles for reshaping the selected mark, if it has any.</summary>
    public IReadOnlyList<(ShapeHandle Handle, Point At)> Handles => SelectedMark?.Handles ?? [];

    private double Reach => 5 * DipSize;

    /// <summary>What a press at <paramref name="point"/> would take hold of, for the pointer's shape; null if nothing.</summary>
    public MoveGrip? GripAt(Point point) => Find(point)?.Grip;

    /// <summary>The selected mark's handle a press at <paramref name="point"/> would take hold of, if any.</summary>
    public ShapeHandle? HandleAt(Point point) => Find(point)?.Handle?.Handle;

    /// <summary>
    /// The text, or dimension, whose words a double-click at <paramref name="point"/> would change: the top-most mark
    /// there, if it's one of those. (Not the handles that turn a dimension's text or move its points, which a
    /// double-click just takes hold of.)
    /// </summary>
    public Mark? TextAt(Point point) =>
        Find(point) is { Grip: not (MoveGrip.TextAngle or MoveGrip.Handle) } hit && _document.Marks[hit.Index] is (TextMark or DimensionMark) and var mark
            ? mark
            : null;

    /// <summary>Selects whatever is under the pointer (or nothing) and gets ready to drag it.</summary>
    public void PointerDown(Point point)
    {
        var found = Find(point);
        Select(found?.Index);
        _drag = found is { } hit ? (hit.Grip, point, _document.Marks[hit.Index], hit.Handle) : null;
        _moving = false;
    }

    public void PointerMove(Point point, bool shift)
    {
        if (_drag is not { } drag || Selected is not { } index)
        {
            return;
        }

        // A click that wobbles a little doesn't move anything.
        var offset = point - drag.From;
        if (!_moving && offset.Length < 2 * DipSize && drag.Grip != MoveGrip.TextAngle)
        {
            return;
        }

        _moving = true;
        var moved = (drag.Grip, drag.Original) switch
        {
            (MoveGrip.DimensionText, DimensionMark dimension) => MoveText(dimension, offset),
            (MoveGrip.DimensionLine, DimensionMark dimension) => MoveLine(dimension, offset),
            (MoveGrip.TextAngle, DimensionMark dimension) => Turn(dimension, point, shift),
            (MoveGrip.Handle, var mark) when drag.Handle is { } handle => Reshape(mark, handle.Handle, handle.At + offset, shift),
            _ => drag.Original.MovedBy(offset),
        };
        Change(index, moved);
    }

    public void PointerUp()
    {
        if (_drag is null)
        {
            return;
        }

        _drag = null;
        _document.FinishChanging();
    }

    /// <summary>Moves the selected mark a little (the arrow keys).</summary>
    public void Nudge(Vector offset)
    {
        if (Selected is { } index && SelectedMark is { } mark)
        {
            Change(index, mark.MovedBy(offset));
            _document.FinishChanging();
        }
    }

    /// <summary>
    /// Gives the selected mark a new colour (picking a colour while a mark is selected), as one undo step. A
    /// pixelated redact box takes its colours from the picture, so it's left alone.
    /// </summary>
    public void Recolour(Color colour)
    {
        if (Selected is { } index && SelectedMark is { } mark and not RedactMark { Pixelate: true } && mark.Style.Color != colour)
        {
            Change(index, mark with { Style = mark.Style with { Color = colour } });
            _document.FinishChanging();
        }
    }

    /// <summary>
    /// Gives the selected mark the thickness slider's <paramref name="thickness"/>. Changes make one undo step once
    /// <paramref name="finished"/>, so dragging the slider undoes in one go.
    /// </summary>
    public void SetThickness(double thickness, bool finished = true)
    {
        if (Selected is not { } index || SelectedMark is not { } mark)
        {
            return;
        }

        if (mark.WithThickness(thickness) is var changed && changed != mark)
        {
            Change(index, changed);
        }

        if (finished)
        {
            _document.FinishChanging();
        }
    }

    public void DeleteSelected()
    {
        if (Selected is { } index)
        {
            Select(null);
            _document.Remove(index);
        }
    }

    public void ClearSelection()
    {
        if (Selected is not null)
        {
            Select(null);
        }
    }

    /// <summary>
    /// The mark with a handle dragged to <paramref name="to"/>. A dimension whose text says what it measured (it
    /// hasn't been typed over) is measured again, so its text keeps up.
    /// </summary>
    private Mark Reshape(Mark mark, ShapeHandle handle, Point to, bool shift)
    {
        var reshaped = mark.Reshaped(handle, to, shift);
        if (mark is DimensionMark dimension && reshaped is DimensionMark changed && _document.ShowsMeasurement(dimension))
        {
            return changed with { Text = DimensionScale.Measured(changed.Layout.Measured, _document.Scale) };
        }

        return reshaped;
    }

    /// <summary>Text dragged near the dimension line snaps onto it; anywhere else it stays where it's put.</summary>
    private DimensionMark MoveText(DimensionMark dimension, Vector offset)
    {
        var layout = dimension.Layout;
        return dimension with { TextAt = layout.SnapText(layout.TextCentre + offset, 8 * DipSize) };
    }

    /// <summary>The dimension line moves in or out; text that's been placed goes with it.</summary>
    private static DimensionMark MoveLine(DimensionMark dimension, Vector offset)
    {
        var moved = dimension with { Through = dimension.Through + offset };
        return moved.TextAt is { } at ? moved with { TextAt = at + (moved.Layout.Middle - dimension.Layout.Middle) } : moved;
    }

    /// <summary>The text turns to face the pointer, snapping to level, upright and the line's angle unless Shift is held.</summary>
    private static DimensionMark Turn(DimensionMark dimension, Point pointer, bool shift)
    {
        var layout = dimension.Layout;
        var towards = pointer - layout.TextCentre;
        var angle = DimensionMark.Upright(Math.Atan2(towards.Y, towards.X) * 180 / Math.PI + 90);
        if (!shift)
        {
            angle = DimensionMark.SnapAngle(angle, layout.LineAngle);
        }

        return dimension with { TextAngle = Math.Round(angle, 1) };
    }

    private Point HandleFor(DimensionLayout layout) => layout.TextCentre - layout.TextY * (layout.TextSize.Height / 2 + 18 * DipSize);

    /// <summary>
    /// The top-most mark under <paramref name="point"/>, and which part of it. The selected mark's handles come first
    /// (the one nearest the pointer, if they're close together), then the handle that turns a dimension's text.
    /// </summary>
    private (int Index, MoveGrip Grip, (ShapeHandle Handle, Point At)? Handle)? Find(Point point)
    {
        if (Selected is { } selected)
        {
            var reach = HandleRadius * 1.5;
            var nearest = Handles.Where(h => (point - h.At).Length <= reach).OrderBy(h => (point - h.At).Length).ToList();
            if (nearest.Count > 0)
            {
                return (selected, MoveGrip.Handle, nearest[0]);
            }

            if (RotateHandle is { } handle && (point - handle).Length <= reach)
            {
                return (selected, MoveGrip.TextAngle, null);
            }
        }

        var marks = _document.Marks;
        for (var i = marks.Count - 1; i >= 0; i--)
        {
            if (marks[i] is DimensionMark dimension)
            {
                MoveGrip? grip = dimension.Layout.PartAt(point, Reach) switch
                {
                    DimensionPart.Text => MoveGrip.DimensionText,
                    DimensionPart.Line => MoveGrip.DimensionLine,
                    DimensionPart.Extension => MoveGrip.Whole,
                    _ => null,
                };
                if (grip is { } found)
                {
                    return (i, found, null);
                }
            }
            else if (marks[i].IsTouchedBy(point, Reach))
            {
                return (i, MoveGrip.Whole, null);
            }
        }

        return null;
    }

    private void Change(int index, Mark moved)
    {
        SelectedMark = moved;
        _document.Change(index, moved);
    }

    private void Select(int? index)
    {
        Selected = index;
        SelectedMark = index is { } i ? _document.Marks[i] : null;
        Changed?.Invoke();
    }

    /// <summary>
    /// Undo, redo, the eraser or a new mark can change the marks under the selection. If the count is the same, the
    /// mark in the same place is still selected (e.g. after undoing a move); otherwise the selection follows the mark
    /// if it's still there.
    /// </summary>
    private void KeepSelection()
    {
        var marks = _document.Marks;
        var sameCount = marks.Count == _markCount;
        _markCount = marks.Count;
        if (Selected is { } index && !(index < marks.Count && ReferenceEquals(marks[index], SelectedMark)))
        {
            if (sameCount)
            {
                SelectedMark = marks[index];
            }
            else
            {
                var found = marks.ToList().FindIndex(mark => ReferenceEquals(mark, SelectedMark));
                Selected = found >= 0 ? found : null;
                SelectedMark = found >= 0 ? marks[found] : null;
            }
        }

        Changed?.Invoke();
    }
}
