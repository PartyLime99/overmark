using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Overmark.Editor;

/// <summary>A capture being edited: the original image, the marks on it, the crop, and undo/redo history.</summary>
internal sealed class EditorDocument
{
    private readonly List<Mark> _marks = [];
    private readonly Stack<Edit> _undo = new();
    private readonly Stack<Edit> _redo = new();

    // The edit on top of the undo stack when the document was last saved (null: the untouched original).
    private Edit? _savedAt;

    // Marks erased so far in the current eraser drag; they undo together.
    private EraseMarks? _erasing;

    // The mark being moved by the current drag, which undoes in one step.
    private ReplaceMark? _changing;

    public EditorDocument(BitmapSource image)
    {
        Image = image;
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

    public BitmapSource Image { get; }

    public IReadOnlyList<Mark> Marks => _marks;

    /// <summary>The part of the image that's kept, in image pixels. Marks outside it stay but aren't shown or saved.</summary>
    public Int32Rect Crop { get; private set; }

    public bool CanUndo => _undo.Count > 0 || _erasing is not null || _changing is not null;

    public bool CanRedo => _redo.Count > 0;

    public bool HasUnsavedChanges => (_undo.TryPeek(out var top) ? top : null) != _savedAt;

    public void Add(Mark mark) => Do(new AddMark(mark));

    /// <summary>Removes every mark touching a circle of <paramref name="radius"/> at <paramref name="point"/>.</summary>
    public void EraseAt(Point point, double radius)
    {
        var erased = false;
        for (var i = _marks.Count - 1; i >= 0; i--)
        {
            if (_marks[i].IsTouchedBy(point, radius))
            {
                (_erasing ??= new EraseMarks()).Removed.Add((i, _marks[i]));
                _marks.RemoveAt(i);
                erased = true;
            }
        }

        if (erased)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Ends an eraser drag, making everything it erased one undo step.</summary>
    public void FinishErasing()
    {
        if (_erasing is not { } edit)
        {
            return;
        }

        _erasing = null;
        _undo.Push(edit);
        _redo.Clear();
        Changed?.Invoke();
    }

    /// <summary>
    /// Swaps the mark at <paramref name="index"/> for a changed (moved) copy. Changes to the same mark until
    /// <see cref="FinishChanging"/> make one undo step, so a whole drag undoes at once.
    /// </summary>
    public void Change(int index, Mark changed)
    {
        FinishErasing();
        if (_changing?.Index != index)
        {
            FinishChanging();
            _changing = new ReplaceMark(index, _marks[index]);
        }

        _marks[index] = changed;
        _changing.After = changed;
        Changed?.Invoke();
    }

    /// <summary>Ends a drag, making the change one undo step (none if the mark ended up where it started).</summary>
    public void FinishChanging()
    {
        if (_changing is not { } edit)
        {
            return;
        }

        _changing = null;
        if (edit.After != edit.Before)
        {
            _undo.Push(edit);
            _redo.Clear();
        }

        Changed?.Invoke();
    }

    /// <summary>Removes one mark (the move tool's Delete key).</summary>
    public void Remove(int index)
    {
        var edit = new EraseMarks();
        edit.Removed.Add((index, _marks[index]));
        Do(edit);
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
                mark.Draw(dc);
            }

            dc.Pop();
        }

        var target = new RenderTargetBitmap(crop.Width, crop.Height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);

        // Screenshots have no transparency, so drop the alpha channel.
        var opaque = new FormatConvertedBitmap(target, PixelFormats.Bgr32, null, 0);
        opaque.Freeze();
        return opaque;
    }

    private void Do(Edit edit)
    {
        FinishErasing();
        FinishChanging();
        edit.Apply(this);
        _undo.Push(edit);
        _redo.Clear();
        Changed?.Invoke();
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

    private sealed class AddMark(Mark mark) : Edit
    {
        public override void Apply(EditorDocument document) => document._marks.Add(mark);

        // Undo is last-in first-out (and erasing puts marks back where they were), so this mark is the last one.
        public override void Revert(EditorDocument document) => document._marks.RemoveAt(document._marks.Count - 1);
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

    /// <summary>A mark swapped for a changed copy in the same place, e.g. when it's moved.</summary>
    private sealed class ReplaceMark(int index, Mark before) : Edit
    {
        public int Index { get; } = index;

        public Mark Before { get; } = before;

        public Mark After { get; set; } = before;

        public override void Apply(EditorDocument document) => document._marks[Index] = After;

        public override void Revert(EditorDocument document) => document._marks[Index] = Before;
    }

    private sealed class CropImage(Int32Rect before, Int32Rect after) : Edit
    {
        public override void Apply(EditorDocument document) => document.Crop = after;

        public override void Revert(EditorDocument document) => document.Crop = before;
    }
}
