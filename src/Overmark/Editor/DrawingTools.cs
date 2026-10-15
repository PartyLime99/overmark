using System.Windows;
using System.Windows.Media;

namespace Overmark.Editor;

/// <summary>
/// Turns pointer input into marks for the current tool. Colour and thickness are taken when a mark starts, so
/// changing them only affects the next mark. All points are image pixels. No UI, so it can be unit tested.
/// </summary>
/// <remarks>
/// Curves take two steps: drag out a straight line, then drag the handle in its middle to bend it. Enter,
/// clicking elsewhere or switching tools finishes the curve; Esc throws it away.
/// The crop tool works the same way: drag a box, then Enter applies it (<see cref="CropApplied"/>).
/// The eraser makes no marks; the editor handles it.
/// </remarks>
internal sealed class DrawingTools
{
    // Lines, arrows, curves and rectangles shorter than this aren't kept (a stray click).
    private const double MinimumSize = 1;

    // Crop boxes smaller than this are treated as a stray click.
    private const double MinimumCrop = 4;

    private readonly List<Point> _points = [];
    private ToolKind _tool = ToolKind.Arrow;
    private bool _curveArrowhead;
    private StrokeStyle _style;
    private Point _start;
    private Point _end;
    private Point _snappedEnd;
    private Point _through;
    private bool _drawing;
    private bool _bending;
    private bool _draggingHandle;

    /// <summary>Raised when a mark is finished and belongs in the document.</summary>
    public event Action<Mark>? Committed;

    /// <summary>Raised when the user confirms a crop box.</summary>
    public event Action<Rect>? CropApplied;

    /// <summary>Raised whenever <see cref="Preview"/>, the bend handle or the crop box changes.</summary>
    public event Action? PreviewChanged;

    /// <summary>Switching tools finishes a curve that's being bent, and drops a crop box that wasn't applied.</summary>
    public ToolKind Tool
    {
        get => _tool;
        set
        {
            if (_tool == value)
            {
                return;
            }

            FinishCurve();
            Cancel();
            _tool = value;
        }
    }

    public Color Colour { get; set; } = Palette.Colours[0];

    /// <summary>The thickness slider's value, in image pixels.</summary>
    public double Width { get; set; } = Palette.DefaultThickness;

    public bool CurveArrowhead
    {
        get => _curveArrowhead;
        set
        {
            _curveArrowhead = value;
            if (_bending)
            {
                ShowBendingCurve();
            }
        }
    }

    /// <summary>The mark being drawn, shown on screen but not yet in the document.</summary>
    public Mark? Preview { get; private set; }

    /// <summary>True while a curve is waiting to be bent; <see cref="BendHandle"/> is where its handle is.</summary>
    public bool IsBending => _bending;

    public Point BendHandle => _through;

    /// <summary>The crop box being dragged or waiting for Enter, in image pixels.</summary>
    public Rect? CropBox { get; private set; }

    /// <summary>True if something is part-done: a mark being dragged, a curve waiting to be bent, or a crop box.</summary>
    public bool HasPending => _drawing || _bending || CropBox is not null;

    /// <param name="handleTolerance">How close (in image pixels) a press must be to grab a curve's bend handle.</param>
    public void PointerDown(Point point, bool shift, double handleTolerance)
    {
        if (_bending)
        {
            if ((point - _through).Length <= handleTolerance)
            {
                _draggingHandle = true;
                return;
            }

            // Clicking elsewhere finishes the curve and starts the next mark.
            FinishCurve();
        }

        if (_tool == ToolKind.Eraser)
        {
            return;
        }

        _drawing = true;
        _style = new StrokeStyle(Colour, Palette.StrokeWidth(_tool, Width));
        _start = _end = point;
        _points.Clear();
        _points.Add(point);
        ShowDrawing(shift);
    }

    public void PointerMove(Point point, bool shift)
    {
        if (_draggingHandle)
        {
            _through = point;
            ShowBendingCurve();
            return;
        }

        if (!_drawing)
        {
            return;
        }

        _end = point;
        if (_tool is ToolKind.Freehand or ToolKind.Highlighter && (point - _points[^1]).Length >= 1)
        {
            _points.Add(point);
        }

        ShowDrawing(shift);
    }

    public void PointerUp(Point point, bool shift)
    {
        if (_draggingHandle)
        {
            _draggingHandle = false;
            return;
        }

        if (!_drawing)
        {
            return;
        }

        PointerMove(point, shift);
        _drawing = false;
        if (_tool == ToolKind.Crop)
        {
            if (CropBox is { } box && (box.Width < MinimumCrop || box.Height < MinimumCrop))
            {
                CropBox = null;
                PreviewChanged?.Invoke();
            }

            return;
        }

        if (Preview is not { } mark)
        {
            return;
        }

        if (_tool == ToolKind.Curve)
        {
            _bending = true;
            _through = MarkGeometry.Midpoint(_start, _snappedEnd);
            ShowBendingCurve();
            return;
        }

        Commit(mark);
    }

    /// <summary>Redraws the mark in progress when Shift is pressed or released without the mouse moving.</summary>
    public void ShiftChanged(bool shift)
    {
        if (_drawing)
        {
            ShowDrawing(shift);
        }
    }

    /// <summary>Enter: finishes a curve waiting to be bent, or applies a crop box.</summary>
    public void Finish()
    {
        FinishCurve();
        if (!_drawing && CropBox is { } box)
        {
            CropBox = null;
            PreviewChanged?.Invoke();
            CropApplied?.Invoke(box);
        }
    }

    /// <summary>Throws away the mark or crop box in progress (Esc).</summary>
    public void Cancel()
    {
        if (!HasPending && Preview is null)
        {
            return;
        }

        _drawing = _bending = _draggingHandle = false;
        Preview = null;
        CropBox = null;
        PreviewChanged?.Invoke();
    }

    private void FinishCurve()
    {
        if (_bending && Preview is { } curve)
        {
            Commit(curve);
        }
    }

    private void ShowDrawing(bool shift)
    {
        if (_tool == ToolKind.Crop)
        {
            CropBox = new Rect(_start, _end);
            PreviewChanged?.Invoke();
            return;
        }

        _snappedEnd = shift && _tool is ToolKind.Line or ToolKind.Arrow or ToolKind.Curve ? MarkGeometry.SnapAngle(_start, _end) : _end;
        var longEnough = (_snappedEnd - _start).Length >= MinimumSize;
        var rectangle = MarkGeometry.RectangleFrom(_start, _end, square: shift);

        Preview = _tool switch
        {
            ToolKind.Freehand => new FreehandMark(_style, [.. _points]),
            ToolKind.Highlighter when shift => new HighlighterMark(_style, [_start, MarkGeometry.SnapAngle(_start, _end)]),
            ToolKind.Highlighter => new HighlighterMark(_style, [.. _points]),
            ToolKind.Line when longEnough => new LineMark(_style, _start, _snappedEnd),
            ToolKind.Arrow when longEnough => new ArrowMark(_style, _start, _snappedEnd),
            ToolKind.Curve when longEnough => new CurveMark(_style, _start, MarkGeometry.Midpoint(_start, _snappedEnd), _snappedEnd, _curveArrowhead),
            ToolKind.Rectangle when rectangle.Width >= MinimumSize || rectangle.Height >= MinimumSize => new RectangleMark(_style, rectangle),
            _ => null,
        };
        PreviewChanged?.Invoke();
    }

    private void ShowBendingCurve()
    {
        Preview = new CurveMark(_style, _start, _through, _snappedEnd, _curveArrowhead);
        PreviewChanged?.Invoke();
    }

    private void Commit(Mark mark)
    {
        _drawing = _bending = _draggingHandle = false;
        Preview = null;
        Committed?.Invoke(mark);
        PreviewChanged?.Invoke();
    }
}
