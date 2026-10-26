using System.Windows;
using System.Windows.Media;

namespace Overmark.Editor;

/// <summary>What the move tool has hold of.</summary>
internal enum MoveGrip
{
    /// <summary>The whole mark (for a dimension, by an extension line), or every selected mark.</summary>
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
/// Shift+click (or Ctrl+click) adds a mark to the selection (or takes it out), dragging a box from an empty spot
/// selects the marks wholly inside it (Shift or Ctrl adds them), and <see cref="SelectAll"/> selects every mark; dragging any of several
/// selected marks moves them all, and the arrow keys, Delete, colours and thickness change them all.
/// A mark selected on its own shows handles where it can be reshaped: dragging the end of a line, arrow or curve moves
/// that end (and a curve's middle bends it), a box's corners and sides resize it, and a dimension's measured points
/// move. For a dimension, dragging its text moves just the text (snapping onto the dimension line when it's close),
/// the round handle above the text turns it, dragging the dimension line moves it in or out, and dragging an extension
/// line moves the whole dimension. Double-clicking text (or a dimension) is for changing its words, which the editor
/// does with <see cref="DrawingTools.EditText"/>. Marks pasted from the clipboard land selected. Each drag, nudge,
/// recolour, change of thickness, delete or paste is one undo step, however many marks it changes. No UI, so it can
/// be unit tested.
/// </summary>
internal sealed class MoveTool
{
    private readonly EditorDocument _document;

    // The selected marks' indexes in the document, lowest first, and the marks themselves (so the selection can follow
    // them when the marks under it change).
    private readonly List<int> _selection = [];
    private readonly List<Mark> _selected = [];

    private Drag? _drag;
    private bool _moving;
    private int _markCount;

    // A box being dragged out from an empty spot, and what was selected before it (kept with Shift or Ctrl, otherwise nothing).
    private (Point From, Point To, IReadOnlyList<int> Before)? _box;

    public MoveTool(EditorDocument document)
    {
        _document = document;
        _markCount = document.Marks.Count;
        document.Changed += KeepSelection;
    }

    /// <summary>Raised when the selection changes, or the selected marks move, or the selection box does.</summary>
    public event Action? Changed;

    /// <summary>How many image pixels one on-screen DIP covers, so the reach of a click and the handle's place stay the same on screen.</summary>
    public double DipSize { get; set; } = 1;

    /// <summary>The selected marks' indexes in the document, lowest (bottom-most) first.</summary>
    public IReadOnlyList<int> Selection => _selection;

    /// <summary>The selected marks, in the order they're drawn.</summary>
    public IReadOnlyList<Mark> SelectedMarks => _selected;

    /// <summary>The index of the selected mark in the document, while just one is selected.</summary>
    public int? Selected => _selection.Count == 1 ? _selection[0] : null;

    /// <summary>The selected mark, while just one is selected: only then does it have handles.</summary>
    public Mark? SelectedMark => _selected.Count == 1 ? _selected[0] : null;

    /// <summary>The box being dragged out to select the marks inside it, or null.</summary>
    public Rect? SelectionBox => _box is { } box ? new Rect(box.From, box.To) : null;

    /// <summary>Where the round handle for turning a selected dimension's text is, or null.</summary>
    public Point? RotateHandle =>
        SelectedMark is DimensionMark { Text.Length: > 0 } dimension ? HandleFor(dimension.Layout) : null;

    /// <summary>The handle's radius, in image pixels.</summary>
    public double HandleRadius => 6 * DipSize;

    /// <summary>The handles for reshaping the selected mark, if just one is selected and it has any.</summary>
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

    /// <summary>
    /// Takes hold of whatever is under the pointer, ready to drag it, or starts a selection box from an empty spot.
    /// Pressing a mark selects just it, unless it's one of several selected, which then move together. With
    /// <paramref name="add"/> (Shift or Ctrl held), pressing a mark adds it to the selection (or takes it out), and a
    /// box adds what's in it.
    /// </summary>
    public void PointerDown(Point point, bool add = false)
    {
        _drag = null;
        _box = null;
        _moving = false;
        if (Find(point) is not { } hit)
        {
            if (!add)
            {
                Select([]);
            }

            _box = (point, point, [.. _selection]);
            return;
        }

        // The handles are only there while a mark is selected on its own, and reshape just it.
        if (hit.Grip is MoveGrip.Handle or MoveGrip.TextAngle)
        {
            _drag = new Drag(hit.Grip, point, [(hit.Index, _document.Marks[hit.Index])], hit.Handle);
            return;
        }

        if (add && _selection.Contains(hit.Index))
        {
            Select([.. _selection.Where(index => index != hit.Index)]);
        }
        else if (add)
        {
            Select([.. _selection, hit.Index]);
            _drag = WholeSelection(point);
        }
        else if (_selection.Count > 1 && _selection.Contains(hit.Index))
        {
            // Letting go without moving selects just the one pressed, so its handles can be reached.
            _drag = WholeSelection(point) with { Only = hit.Index };
        }
        else
        {
            Select([hit.Index]);
            _drag = new Drag(hit.Grip, point, [(hit.Index, _document.Marks[hit.Index])], null);
        }
    }

    public void PointerMove(Point point, bool shift)
    {
        if (_box is { } box)
        {
            // What's wholly inside the box is selected as it's dragged out.
            _box = box with { To = point };
            var area = new Rect(box.From, point);
            var marks = _document.Marks;
            Select([.. box.Before, .. Enumerable.Range(0, marks.Count).Where(index => area.Contains(marks[index].Extent))]);
            return;
        }

        if (_drag is not { } drag)
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
        if (drag.Grip == MoveGrip.Whole)
        {
            Change([.. drag.Marks.Select(mark => (mark.Index, mark.Original.MovedBy(offset)))]);
            return;
        }

        var (index, original) = drag.Marks[0];
        var moved = (drag.Grip, original) switch
        {
            (MoveGrip.DimensionText, DimensionMark dimension) => MoveText(dimension, offset),
            (MoveGrip.DimensionLine, DimensionMark dimension) => MoveLine(dimension, offset),
            (MoveGrip.TextAngle, DimensionMark dimension) => Turn(dimension, point, shift),
            (MoveGrip.Handle, var mark) when drag.Handle is { } handle => Reshape(mark, handle.Handle, handle.At + offset, shift),
            _ => original.MovedBy(offset),
        };
        Change([(index, moved)]);
    }

    public void PointerUp()
    {
        if (_box is not null)
        {
            _box = null;
            Changed?.Invoke();
            return;
        }

        if (_drag is not { } drag)
        {
            return;
        }

        _drag = null;
        if (!_moving && drag.Only is { } only)
        {
            Select([only]);
        }

        _document.FinishChanging();
    }

    /// <summary>Selects every mark (Ctrl+A).</summary>
    public void SelectAll() => Select(Enumerable.Range(0, _document.Marks.Count));

    /// <summary>Moves the selected marks a little (the arrow keys).</summary>
    public void Nudge(Vector offset) => ChangeSelected(mark => mark.MovedBy(offset));

    /// <summary>
    /// Gives the selected marks a new colour (picking a colour while marks are selected), as one undo step. A
    /// pixelated redact box takes its colours from the picture, so it's left alone.
    /// </summary>
    public void Recolour(Color colour) =>
        ChangeSelected(mark => mark is RedactMark { Pixelate: true } ? mark : mark with { Style = mark.Style with { Color = colour } });

    /// <summary>
    /// Gives the selected marks the thickness slider's <paramref name="thickness"/>. Changes make one undo step once
    /// <paramref name="finished"/>, so dragging the slider undoes in one go.
    /// </summary>
    public void SetThickness(double thickness, bool finished = true) => ChangeSelected(mark => mark.WithThickness(thickness), finished);

    public void DeleteSelected()
    {
        if (_selection.Count > 0)
        {
            var deleting = _selection.ToList();
            Select([]);
            _document.Remove(deleting);
        }
    }

    public void ClearSelection()
    {
        if (_selection.Count > 0)
        {
            Select([]);
        }
    }

    /// <summary>
    /// Adds marks copied to the clipboard on top, as one undo step, and selects them. They land <paramref name="steps"/>
    /// steps (10 on-screen pixels each) down and to the right of where they were copied from, as they were arranged,
    /// moved back inside the crop together if that would take any of them out. A dimension comes as an ordinary one,
    /// which doesn't set the scale; if it said what it measured, it says what it measures here.
    /// </summary>
    public void Paste(IReadOnlyList<CopiedMark> copied, int steps)
    {
        if (copied.Count == 0)
        {
            return;
        }

        var step = Math.Max(1, Math.Round(10 * DipSize)) * steps;
        var marks = copied.Select(mark => Pasted(mark).MovedBy(new Vector(step, step))).ToList();
        var into = IntoCrop(marks.Select(mark => mark.Extent).Aggregate(Rect.Union));
        _document.Add([.. marks.Select(mark => mark.MovedBy(into))]);
        Select(Enumerable.Range(_document.Marks.Count - marks.Count, marks.Count));
    }

    /// <summary>A copied mark as it's pasted: a dimension doesn't set the scale, and measures again if it said what it measured.</summary>
    private Mark Pasted(CopiedMark copied)
    {
        if (copied.Mark is not DimensionMark dimension)
        {
            return copied.Mark;
        }

        dimension = dimension with { SetsScale = false };
        return copied.Measures ? dimension with { Text = DimensionScale.Measured(dimension.Layout.Measured, _document.Scale) } : dimension;
    }

    private Drag WholeSelection(Point from) => new(MoveGrip.Whole, from, [.. _selection.Select(index => (index, _document.Marks[index]))], null);

    /// <summary>
    /// Changes every selected mark with <paramref name="change"/>, as one undo step once <paramref name="finished"/>.
    /// Marks it leaves the same stay as they are.
    /// </summary>
    private void ChangeSelected(Func<Mark, Mark> change, bool finished = true)
    {
        var changes = _selection.Select((index, i) => (index, change(_selected[i]) is var changed && changed != _selected[i] ? changed : _selected[i])).ToList();
        if (changes.Where((change, i) => !ReferenceEquals(change.Item2, _selected[i])).Any())
        {
            Change(changes);
        }

        if (finished)
        {
            _document.FinishChanging();
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

    /// <summary>How far <paramref name="area"/> has to move to be inside the crop (to its top or left edge, where it's too big to fit).</summary>
    private Vector IntoCrop(Rect area)
    {
        var crop = _document.Crop;
        return new Vector(Into(area.Left, area.Width, crop.X, crop.Width), Into(area.Top, area.Height, crop.Y, crop.Height));

        static double Into(double start, double length, double from, double size) =>
            start + length > from + size ? Math.Max(from, from + size - length) - start
            : start < from ? from - start
            : 0;
    }

    /// <summary>
    /// The top-most mark under <paramref name="point"/>, and which part of it. The handles of a mark selected on its
    /// own come first (the one nearest the pointer, if they're close together), then the handle that turns a
    /// dimension's text.
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

    private void Change(IReadOnlyList<(int Index, Mark Changed)> changes)
    {
        foreach (var (index, changed) in changes)
        {
            if (_selection.IndexOf(index) is var at and >= 0)
            {
                _selected[at] = changed;
            }
        }

        _document.Change(changes);
    }

    private void Select(IEnumerable<int> indexes)
    {
        var marks = _document.Marks;
        var selecting = indexes.Distinct().Order().ToList(); // before clearing, in case it's read from the selection
        _selection.Clear();
        _selection.AddRange(selecting);
        _selected.Clear();
        _selected.AddRange(_selection.Select(index => marks[index]));
        Changed?.Invoke();
    }

    /// <summary>
    /// Undo, redo, the eraser or a new mark can change the marks under the selection. If the count is the same, the
    /// marks in the same places are still selected (e.g. after undoing a move); otherwise the selection follows each
    /// mark if it's still there.
    /// </summary>
    private void KeepSelection()
    {
        var marks = _document.Marks;
        var sameCount = marks.Count == _markCount;
        _markCount = marks.Count;
        var kept = new List<(int Index, Mark Mark)>();
        for (var i = 0; i < _selection.Count; i++)
        {
            var (index, mark) = (_selection[i], _selected[i]);
            if (index < marks.Count && (sameCount || ReferenceEquals(marks[index], mark)))
            {
                kept.Add((index, marks[index]));
            }
            else if (IndexOf(marks, mark) is var found and >= 0)
            {
                kept.Add((found, mark));
            }
        }

        _selection.Clear();
        _selected.Clear();
        foreach (var (index, mark) in kept.DistinctBy(k => k.Index).OrderBy(k => k.Index))
        {
            _selection.Add(index);
            _selected.Add(mark);
        }

        Changed?.Invoke();
    }

    private static int IndexOf(IReadOnlyList<Mark> marks, Mark mark)
    {
        for (var i = 0; i < marks.Count; i++)
        {
            if (ReferenceEquals(marks[i], mark))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// A drag under way: where it started, the marks it moves as they were then (one, unless it moves the whole
    /// selection), and which part it has hold of. <see cref="Only"/> is the mark selected on its own if the press
    /// lets go without moving, having pressed one of several selected marks.
    /// </summary>
    private sealed record Drag(MoveGrip Grip, Point From, IReadOnlyList<(int Index, Mark Original)> Marks, (ShapeHandle Handle, Point At)? Handle, int? Only = null);
}
