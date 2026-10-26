using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Overmark.Editor;

/// <summary>
/// A capture being edited: the original image, the marks on it, the crop, and undo/redo history.
/// Dimensions are kept in step with the picture's <see cref="Scale"/>: when it changes, those that say what they
/// measured say it in the new scale, as part of the same undo step as the change that caused it.
/// </summary>
internal sealed class EditorDocument
{
    private readonly List<Mark> _marks = [];
    private readonly Stack<Edit> _undo = new();
    private readonly Stack<Edit> _redo = new();

    // The edit on top of the undo stack when the document was last saved (null: the untouched original).
    private Edit? _savedAt;

    // Marks erased so far in the current eraser drag; they undo together. The scale as it was before the drag.
    private EraseMarks? _erasing;
    private DimensionScale? _erasingScale;

    // The marks being moved by the current drag, which undo in one step. The scale as it was before the drag.
    private ChangeMarks? _changing;
    private DimensionScale? _changingScale;

    public EditorDocument(BitmapSource image)
    {
        Image = image;
        IsSeeThrough = Transparency.IsSeeThrough(image);
        Crop = new Int32Rect(0, 0, image.PixelWidth, image.PixelHeight);
    }

    /// <summary>
    /// A capture brought back from an editable copy, with the marks and crop it had (but no undo history).
    /// <paramref name="unsaved"/> starts it with unsaved changes, for marks that were never saved to the file.
    /// </summary>
    public EditorDocument(BitmapSource image, Int32Rect crop, IEnumerable<Mark> marks, bool unsaved)
        : this(image)
    {
        Crop = crop;
        _marks.AddRange(marks);
        if (unsaved)
        {
            _savedAt = NeverSaved.Instance;
        }
    }

    public event Action? Changed;

    public BitmapSource Image { get; private set; }

    /// <summary>Whether some of <see cref="Image"/> is see-through, e.g. after <see cref="RemoveWhiteBackground"/>.</summary>
    public bool IsSeeThrough { get; private set; }

    public IReadOnlyList<Mark> Marks => _marks;

    /// <summary>The part of the image that's kept, in image pixels. Marks outside it stay but aren't shown or saved.</summary>
    public Int32Rect Crop { get; private set; }

    public bool CanUndo => _undo.Count > 0 || _erasing is not null || _changing is not null;

    public bool CanRedo => _redo.Count > 0;

    public bool HasUnsavedChanges => (_undo.TryPeek(out var top) ? top : null) != _savedAt;

    /// <summary>
    /// What the picture's pixels measure in real units, from the dimension that had a length typed into it last
    /// (<see cref="DimensionMark.SetsScale"/>); null while dimensions measure in pixels.
    /// </summary>
    public DimensionScale? Scale =>
        _marks.OfType<DimensionMark>().Where(d => d.SetsScale).Select(d => DimensionScale.FromText(d.Text, d.Layout.Measured)).LastOrDefault(s => s is not null);

    /// <summary>
    /// True if <paramref name="dimension"/> says what it measures, in the current scale: its text hasn't been typed
    /// over, so it follows when the scale changes or its points move.
    /// </summary>
    public bool ShowsMeasurement(DimensionMark dimension) =>
        !dimension.SetsScale && dimension.Text == DimensionScale.Measured(dimension.Layout.Measured, Scale);

    /// <summary>Adds a mark. A dimension that had a length typed into it sets the scale (see <see cref="DimensionMark.SetsScale"/>).</summary>
    public void Add(Mark mark) => Do(new AddMarks([mark]), mark);

    /// <summary>Adds several marks on top, in order (pasted together), as one undo step.</summary>
    public void Add(IReadOnlyList<Mark> marks)
    {
        if (marks.Count > 0)
        {
            Do(new AddMarks(marks), marks.LastOrDefault(mark => mark is DimensionMark { SetsScale: true }));
        }
    }

    /// <summary>Removes every mark touching a circle of <paramref name="radius"/> at <paramref name="point"/>.</summary>
    public void EraseAt(Point point, double radius)
    {
        var erased = false;
        for (var i = _marks.Count - 1; i >= 0; i--)
        {
            if (_marks[i].IsTouchedBy(point, radius))
            {
                if (_erasing is null)
                {
                    _erasingScale = Scale;
                    _erasing = new EraseMarks();
                }

                _erasing.Removed.Add((i, _marks[i]));
                _marks.RemoveAt(i);
                erased = true;
            }
        }

        if (erased)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Ends an eraser drag, making everything it erased one undo step (erasing the dimension that set the scale takes the scale away).</summary>
    public void FinishErasing()
    {
        if (_erasing is not { } edit)
        {
            return;
        }

        _erasing = null;
        _undo.Push(WithScaleFollowed(edit, _erasingScale, null));
        _redo.Clear();
        Changed?.Invoke();
    }

    /// <summary>
    /// Swaps the mark at <paramref name="index"/> for a changed (moved) copy. Changes to the same mark until
    /// <see cref="FinishChanging"/> make one undo step, so a whole drag undoes at once.
    /// </summary>
    public void Change(int index, Mark changed) => Change([(index, changed)]);

    /// <summary>
    /// Swaps marks for changed (moved) copies, by their indexes. Changes to the same marks until
    /// <see cref="FinishChanging"/> make one undo step, so a drag undoes at once however many marks it moves.
    /// </summary>
    public void Change(IReadOnlyList<(int Index, Mark Changed)> changes)
    {
        FinishErasing();
        if (_changing is null || !_changing.IsOf(changes.Select(change => change.Index)))
        {
            FinishChanging();
            _changingScale = Scale;
            _changing = new ChangeMarks([.. changes.Select(change => (change.Index, _marks[change.Index]))]);
        }

        foreach (var (index, changed) in changes)
        {
            _marks[index] = changed;
            _changing.Set(index, changed);
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Ends a drag, making the change one undo step (none if the marks ended up where they started). Moving the points
    /// of the dimension that sets the scale changes the scale, and the other dimensions follow now.
    /// </summary>
    public void FinishChanging()
    {
        if (_changing is not { } edit)
        {
            return;
        }

        _changing = null;
        if (edit.HasChanged)
        {
            _undo.Push(WithScaleFollowed(edit, _changingScale, null));
            _redo.Clear();
        }

        Changed?.Invoke();
    }

    /// <summary>Removes one mark.</summary>
    public void Remove(int index) => Remove([index]);

    /// <summary>Removes marks by their indexes (the move tool's Delete key), as one undo step.</summary>
    public void Remove(IEnumerable<int> indexes)
    {
        // Taken out from the top down, as the eraser does, so the indexes below stay right.
        var edit = new EraseMarks();
        foreach (var index in indexes.Distinct().OrderDescending())
        {
            edit.Removed.Add((index, _marks[index]));
        }

        if (edit.Removed.Count > 0)
        {
            Do(edit);
        }
    }

    /// <summary>
    /// Swaps <paramref name="mark"/> for <paramref name="changed"/> in the same place (its words changed), as one undo
    /// step, or removes it if <paramref name="changed"/> is null. Does nothing if the mark is no longer there, or
    /// hasn't changed. A dimension that's had a length typed into it sets the scale.
    /// </summary>
    public void Replace(Mark mark, Mark? changed)
    {
        var index = _marks.FindIndex(m => ReferenceEquals(m, mark));
        if (index < 0)
        {
            return;
        }

        if (changed is null)
        {
            Remove(index);
            return;
        }

        if (changed != mark)
        {
            Do(new ReplaceMark(index, mark) { After = changed }, changed);
        }
    }

    /// <summary>Crops to <paramref name="area"/> (image pixels), widened to whole pixels and kept inside the current crop.</summary>
    public void CropTo(Rect area)
    {
        var left = Math.Max(Crop.X, (int)Math.Floor(area.Left));
        var top = Math.Max(Crop.Y, (int)Math.Floor(area.Top));
        var right = Math.Min(Crop.X + Crop.Width, (int)Math.Ceiling(area.Right));
        var bottom = Math.Min(Crop.Y + Crop.Height, (int)Math.Ceiling(area.Bottom));
        if (right - left < 1 || bottom - top < 1)
        {
            return;
        }

        var crop = new Int32Rect(left, top, right - left, bottom - top);
        if (crop != Crop)
        {
            Do(new CropImage(Crop, crop));
        }
    }

    /// <summary>
    /// Makes the white background of the kept part see-through (see <see cref="Transparency"/>), as one undo step, or
    /// with <paramref name="everywhere"/> all its pure white. Marks stay as they are. Returns false if there was no
    /// white to remove.
    /// </summary>
    public bool RemoveWhiteBackground(bool everywhere = false)
    {
        if (Transparency.RemoveWhite(Image, Crop, everywhere) is not { } cleared)
        {
            return false;
        }

        Do(new ReplaceImage((Image, IsSeeThrough), (cleared, true)));
        return true;
    }

    public void Undo()
    {
        FinishErasing();
        FinishChanging();
        if (_undo.TryPop(out var edit))
        {
            edit.Revert(this);
            _redo.Push(edit);
            Changed?.Invoke();
        }
    }

    public void Redo()
    {
        if (_redo.TryPop(out var edit))
        {
            edit.Apply(this);
            _undo.Push(edit);
            Changed?.Invoke();
        }
    }

    public void MarkSaved()
    {
        _savedAt = _undo.TryPeek(out var top) ? top : null;
        Changed?.Invoke();
    }

    /// <summary>The cropped capture with every mark drawn on it: what gets saved, copied, dragged and pinned.</summary>
    public BitmapSource Render()
    {
        var crop = Crop;
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new TranslateTransform(-crop.X, -crop.Y));
            dc.DrawImage(Image, new Rect(0, 0, Image.PixelWidth, Image.PixelHeight));
            foreach (var mark in _marks)
            {
                mark.Draw(dc, Image);
            }

            dc.Pop();
        }

        var target = new RenderTargetBitmap(crop.Width, crop.Height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);

        // Screenshots have no transparency, so drop the alpha channel, unless the background has been made see-through.
        var result = new FormatConvertedBitmap(target, IsSeeThrough ? PixelFormats.Bgra32 : PixelFormats.Bgr32, null, 0);
        result.Freeze();
        return result;
    }

    /// <summary>Makes an edit, as one undo step. <paramref name="mark"/> is the mark it adds or changes, if any.</summary>
    private void Do(Edit edit, Mark? mark = null)
    {
        FinishErasing();
        FinishChanging();
        var scale = Scale;
        edit.Apply(this);
        _undo.Push(WithScaleFollowed(edit, scale, mark));
        _redo.Clear();
        Changed?.Invoke();
    }

    /// <summary>
    /// After <paramref name="edit"/> has been made, keeps the dimensions in step with the scale, returning the edit
    /// together with what that changed, to undo as one. If <paramref name="mark"/> is a dimension that's just had a
    /// length typed into it, it becomes the only one setting the scale: any other keeps what it says, but no longer
    /// sets it. Then, if the scale isn't what it was (<paramref name="before"/>), the dimensions that said what they
    /// measured in it say what they measure in the new one.
    /// </summary>
    private Edit WithScaleFollowed(Edit edit, DimensionScale? before, Mark? mark)
    {
        var follow = new ReplaceMarks();
        if (mark is DimensionMark { SetsScale: true })
        {
            for (var i = 0; i < _marks.Count; i++)
            {
                if (_marks[i] is DimensionMark { SetsScale: true } other && !ReferenceEquals(other, mark))
                {
                    follow.Replace(this, i, other with { SetsScale = false });
                }
            }
        }

        var after = Scale;
        if (after != before)
        {
            for (var i = 0; i < _marks.Count; i++)
            {
                if (_marks[i] is DimensionMark { SetsScale: false } dimension && !follow.Changes(i)
                    && dimension.Text == DimensionScale.Measured(dimension.Layout.Measured, before))
                {
                    follow.Replace(this, i, dimension with { Text = DimensionScale.Measured(dimension.Layout.Measured, after) });
                }
            }
        }

        return follow.IsEmpty ? edit : new Together(edit, follow);
    }

    /// <summary>Stands for a save that never happened, so a document brought back with unsaved marks reads as unsaved.</summary>
    private sealed class NeverSaved : Edit
    {
        public static readonly NeverSaved Instance = new();

        public override void Apply(EditorDocument document)
        {
        }

        public override void Revert(EditorDocument document)
        {
        }
    }

    private abstract class Edit
    {
        public abstract void Apply(EditorDocument document);

        public abstract void Revert(EditorDocument document);
    }

    private sealed class AddMarks(IReadOnlyList<Mark> marks) : Edit
    {
        public override void Apply(EditorDocument document) => document._marks.AddRange(marks);

        // Undo is last-in first-out (and erasing puts marks back where they were), so these marks are the last ones.
        public override void Revert(EditorDocument document) => document._marks.RemoveRange(document._marks.Count - marks.Count, marks.Count);
    }

    /// <summary>Marks removed by one eraser drag, with the position each had when it was removed.</summary>
    private sealed class EraseMarks : Edit
    {
        public List<(int Index, Mark Mark)> Removed { get; } = [];

        public override void Apply(EditorDocument document)
        {
            foreach (var (index, _) in Removed)
            {
                document._marks.RemoveAt(index);
            }
        }

        public override void Revert(EditorDocument document)
        {
            for (var i = Removed.Count - 1; i >= 0; i--)
            {
                document._marks.Insert(Removed[i].Index, Removed[i].Mark);
            }
        }
    }

    /// <summary>Marks swapped for changed copies in their places, as they're dragged.</summary>
    private sealed class ChangeMarks(IEnumerable<(int Index, Mark Before)> marks) : Edit
    {
        private readonly List<(int Index, Mark Before, Mark After)> _changes = [.. marks.Select(mark => (mark.Index, mark.Before, mark.Before))];

        public bool HasChanged => _changes.Any(change => change.After != change.Before);

        /// <summary>True if this changes just the marks at <paramref name="indexes"/>.</summary>
        public bool IsOf(IEnumerable<int> indexes) => indexes.Order().SequenceEqual(_changes.Select(change => change.Index).Order());

        public void Set(int index, Mark after)
        {
            var at = _changes.FindIndex(change => change.Index == index);
            _changes[at] = _changes[at] with { After = after };
        }

        public override void Apply(EditorDocument document)
        {
            foreach (var (index, _, after) in _changes)
            {
                document._marks[index] = after;
            }
        }

        public override void Revert(EditorDocument document)
        {
            foreach (var (index, before, _) in _changes)
            {
                document._marks[index] = before;
            }
        }
    }

    /// <summary>A mark swapped for a changed copy in the same place, e.g. when its words change.</summary>
    private sealed class ReplaceMark(int index, Mark before) : Edit
    {
        public int Index { get; } = index;

        public Mark Before { get; } = before;

        public Mark After { get; set; } = before;

        public override void Apply(EditorDocument document) => document._marks[Index] = After;

        public override void Revert(EditorDocument document) => document._marks[Index] = Before;
    }

    /// <summary>Several marks swapped for changed copies in their places, e.g. dimensions following a new scale.</summary>
    private sealed class ReplaceMarks : Edit
    {
        private readonly List<(int Index, Mark Before, Mark After)> _changes = [];

        public bool IsEmpty => _changes.Count == 0;

        public bool Changes(int index) => _changes.Any(change => change.Index == index);

        /// <summary>Makes a change now, and keeps it to undo and redo.</summary>
        public void Replace(EditorDocument document, int index, Mark after)
        {
            _changes.Add((index, document._marks[index], after));
            document._marks[index] = after;
        }

        public override void Apply(EditorDocument document)
        {
            foreach (var (index, _, after) in _changes)
            {
                document._marks[index] = after;
            }
        }

        public override void Revert(EditorDocument document)
        {
            for (var i = _changes.Count - 1; i >= 0; i--)
            {
                document._marks[_changes[i].Index] = _changes[i].Before;
            }
        }
    }

    /// <summary>Edits made one after another and undone as one, the last first.</summary>
    private sealed class Together(params Edit[] edits) : Edit
    {
        public override void Apply(EditorDocument document)
        {
            foreach (var edit in edits)
            {
                edit.Apply(document);
            }
        }

        public override void Revert(EditorDocument document)
        {
            for (var i = edits.Length - 1; i >= 0; i--)
            {
                edits[i].Revert(document);
            }
        }
    }

    /// <summary>The picture swapped for a changed copy, e.g. with its background made see-through.</summary>
    private sealed class ReplaceImage((BitmapSource Image, bool SeeThrough) before, (BitmapSource Image, bool SeeThrough) after) : Edit
    {
        public override void Apply(EditorDocument document) => (document.Image, document.IsSeeThrough) = after;

        public override void Revert(EditorDocument document) => (document.Image, document.IsSeeThrough) = before;
    }

    private sealed class CropImage(Int32Rect before, Int32Rect after) : Edit
    {
        public override void Apply(EditorDocument document) => document.Crop = after;

        public override void Revert(EditorDocument document) => document.Crop = before;
    }
}
