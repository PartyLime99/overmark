using System.Windows;
using System.Windows.Media;

namespace Overmark.Editor;

/// <summary>
/// Text being typed, for the editor's text box: what's there so far, where its first letter goes, its size and
/// colour, and the frame shown round it. All in image pixels. <see cref="Angle"/> turns the text and its frame
/// (clockwise, in degrees) about the frame's centre, for a dimension's text that's been turned.
/// </summary>
internal sealed record TypingState(string Text, Point Origin, double FontSize, Color Colour, Rect Frame, double Angle = 0);

/// <summary>
/// Turns pointer input into marks for the current tool. Colour and thickness are taken when a mark starts, so
/// changing them only affects the next mark. All points are image pixels. No UI, so it can be unit tested.
/// </summary>
/// <remarks>
/// Curves take two steps: drag out a straight line, then drag the handle in its middle to bend it. Enter,
/// clicking elsewhere or switching tools finishes the curve; Esc throws it away.
/// The crop tool works the same way: drag a box, then Enter applies it (<see cref="CropApplied"/>).
/// The text tool starts typing where it's clicked (<see cref="Typing"/>); the editor's text box feeds in what's typed
/// (<see cref="SetText"/>), and Enter or clicking elsewhere finishes it.
/// The dimension tool takes three clicks: the two points to measure between, then where the dimension line goes
/// (which also picks horizontal, vertical or aligned). Shift snaps the second point to 0°, 45° or 90° from the first,
/// like a line. Then its text is typed like the text tool's, starting with the measured length in pixels.
/// Text or a dimension already drawn can have its words changed (<see cref="EditText"/>, from double-clicking it with
/// the move tool); it's typed in the same way, and finishing it raises <see cref="Edited"/>.
/// The eraser and move tool make no marks; the editor handles them.
/// </remarks>
internal sealed class DrawingTools
{
    // Lines, arrows, curves and rectangles shorter than this aren't kept (a stray click).
    private const double MinimumSize = 1;

    // Crop boxes smaller than this are treated as a stray click.
    private const double MinimumCrop = 4;

    private readonly List<Point> _points = [];
    private readonly List<Point> _dimensionPoints = [];
    private ToolKind _tool = ToolKind.Arrow;
    private Color _colour = Palette.Colours[0];
    private double _width = Palette.DefaultThickness;
    private bool _curveArrowhead;
    private bool _textBoxed;
    private Point _textAnchor;
    private TextMark? _text;
    private DimensionMark? _dimension;
    private Point _pointer;
    private bool _shift;
    private bool _pressingDimension;
    private double _dragThreshold;
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

    /// <summary>
    /// Raised when text (or a dimension) already in the document has had its words changed: the mark as it was, and
    /// as it is now, or null if all its text was deleted (a dimension is kept without text, as when it's drawn).
    /// </summary>
    public event Action<Mark, Mark?>? Edited;

    /// <summary>Raised when the user confirms a crop box.</summary>
    public event Action<Rect>? CropApplied;

    /// <summary>Raised whenever <see cref="Preview"/>, the bend handle, the crop box, typing or a dimension's points change.</summary>
    public event Action? PreviewChanged;

    /// <summary>
    /// Switching tools finishes a curve that's being bent or text being typed, and drops a crop box that wasn't
    /// applied or a dimension that wasn't placed.
    /// </summary>
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
            FinishText();
            Cancel();
            _tool = value;
        }
    }

    /// <summary>The colour for the next mark. Text being typed changes colour straight away, as it isn't drawn yet.</summary>
    public Color Colour
    {
        get => _colour;
        set
        {
            _colour = value;
            Restyle(colour: value);
        }
    }

    /// <summary>The thickness slider's value, in image pixels. Like the colour, it applies to text being typed too.</summary>
    public double Width
    {
        get => _width;
        set
        {
            _width = value;
            Restyle(width: value);
        }
    }

    /// <summary>Whether text goes on a box of a contrasting colour.</summary>
    public bool TextBoxed
    {
        get => _textBoxed;
        set
        {
            _textBoxed = value;
            Restyle(boxed: value);
        }
    }

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

    /// <summary>The text being typed (possibly nothing yet), or null when nothing is being typed.</summary>
    public TypingState? Typing { get; private set; }

    /// <summary>
    /// The mark in the document whose words are being changed, as it was. The preview shows it as it's being typed,
    /// so the document's copy shouldn't be drawn meanwhile. Null when nothing is being changed.
    /// </summary>
    public Mark? Editing { get; private set; }

    /// <summary>The points picked so far for a dimension that hasn't been placed yet (none, one or two).</summary>
    public IReadOnlyList<Point> DimensionPoints => _dimensionPoints;

    /// <summary>
    /// After a dimension's first point, where the second would go: the pointer, or with Shift, snapped to 0°, 45° or
    /// 90°. A guide line runs to it from the first point.
    /// </summary>
    public Point? DimensionGuide => _dimensionPoints.Count == 1 ? SecondDimensionPoint(_pointer, _shift) : null;

    /// <summary>While a dimension is being placed: what it will measure, shown beside the pointer.</summary>
    public (string Text, Point At)? DimensionHint =>
        _dimensionPoints.Count == 2 && Preview is DimensionMark dimension
            ? ($"{dimension.Kind}  ·  {Math.Round(dimension.Layout.Measured)} px  ·  click to place", _pointer)
            : null;

    /// <summary>True if something is part-done: a mark being dragged, a curve waiting to be bent, a crop box, text being typed, or a dimension.</summary>
    public bool HasPending => _drawing || _bending || CropBox is not null || Typing is not null || _dimensionPoints.Count > 0;

    /// <param name="handleTolerance">
    /// How close (in image pixels) a press must be to grab a curve's bend handle; also how far a press has to be
    /// dragged to count as a drag rather than a click.
    /// </param>
    public void PointerDown(Point point, bool shift, double handleTolerance)
    {
        _dragThreshold = handleTolerance;
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

        // Likewise for text: clicking away from it finishes it, and with the text tool starts the next text there.
        // A click that finishes a dimension's text does only that.
        var wasDimension = _dimension is not null;
        FinishText();
        switch (_tool)
        {
            case ToolKind.Text:
                _textAnchor = point;
                ShowTyping(TextAtAnchor(new StrokeStyle(_colour, Palette.StrokeWidth(ToolKind.Text, _width)), "", _textBoxed));
                return;
            case ToolKind.Dimension when !wasDimension:
                PickDimensionPoint(point, shift);
                return;
            case ToolKind.Dimension or ToolKind.Eraser or ToolKind.Move:
                return;
        }

        _drawing = true;
        _style = new StrokeStyle(_colour, Palette.StrokeWidth(_tool, _width));
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

        if (_dimensionPoints.Count > 0)
        {
            _pointer = point;
            _shift = shift;
            ShowPlacingDimension();
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

        // Dragging from the first point to the second picks both in one go.
        if (_pressingDimension)
        {
            _pressingDimension = false;
            if (_dimensionPoints.Count == 1 && (point - _dimensionPoints[0]).Length > _dragThreshold)
            {
                _pointer = point;
                _dimensionPoints.Add(SecondDimensionPoint(point, shift));
                ShowPlacingDimension();
            }

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

    /// <summary>Redraws the mark in progress, or a dimension's guide line, when Shift is pressed or released without the mouse moving.</summary>
    public void ShiftChanged(bool shift)
    {
        _shift = shift;
        if (_drawing)
        {
            ShowDrawing(shift);
        }
        else if (_dimensionPoints.Count == 1)
        {
            PreviewChanged?.Invoke();
        }
    }

    /// <summary>The editor's text box changed: shows the new text in place.</summary>
    public void SetText(string text)
    {
        if (_dimension is { } dimension)
        {
            ShowDimensionTyping(dimension with { Text = text });
        }
        else if (_text is { } typed)
        {
            ShowTyping(typed with { Text = text });
        }
    }

    /// <summary>
    /// Starts changing the words of <paramref name="mark"/>, text or a dimension already in the document (it's been
    /// double-clicked with the move tool). It's typed like new text, starting with what it says now, and keeps its own
    /// colour and size unless they're changed while it's typed. Enter or clicking elsewhere raises <see cref="Edited"/>;
    /// Esc leaves it as it was. Anything part-done is finished first, as when switching tools.
    /// </summary>
    public void EditText(Mark mark)
    {
        FinishCurve();
        FinishText();
        Cancel();
        switch (mark)
        {
            case TextMark text:
                Editing = mark;
                _textAnchor = new Point(text.Position.X, text.Position.Y + TextMark.LineHeight(text.FontSize) / 2);
                ShowTyping(text);
                break;
            case DimensionMark dimension:
                Editing = mark;
                ShowDimensionTyping(dimension);
                break;
        }
    }

    /// <summary>Enter: finishes a curve waiting to be bent or text being typed, or applies a crop box.</summary>
    public void Finish()
    {
        FinishCurve();
        FinishText();
        if (!_drawing && CropBox is { } box)
        {
            CropBox = null;
            PreviewChanged?.Invoke();
            CropApplied?.Invoke(box);
        }
    }

    /// <summary>Throws away the mark, crop box, text or dimension in progress (Esc).</summary>
    public void Cancel()
    {
        if (!HasPending && Preview is null)
        {
            return;
        }

        _drawing = _bending = _draggingHandle = _pressingDimension = false;
        Preview = null;
        CropBox = null;
        Typing = null;
        Editing = null;
        _text = null;
        _dimension = null;
        _dimensionPoints.Clear();
        PreviewChanged?.Invoke();
    }

    private void FinishCurve()
    {
        if (_bending && Preview is { } curve)
        {
            Commit(curve);
        }
    }

    /// <summary>
    /// Keeps the text being typed, unless it's blank. Trailing spaces and empty lines are dropped. A dimension is
    /// kept even without text. Text that was already in the document goes back in its place (<see cref="Edited"/>).
    /// </summary>
    private void FinishText()
    {
        if (Typing is null)
        {
            return;
        }

        Mark? finished = (_text, _dimension) switch
        {
            (_, { } dimension) => dimension with { Text = dimension.Text.TrimEnd() },
            ({ } text, _) when text.Text.TrimEnd() is { Length: > 0 } trimmed => text with { Text = trimmed },
            _ => null,
        };
        var edited = Editing;
        Typing = null;
        Editing = null;
        Preview = null;
        _text = null;
        _dimension = null;
        if (edited is not null)
        {
            Edited?.Invoke(edited, finished);
        }
        else if (finished is not null)
        {
            Committed?.Invoke(finished);
        }

        PreviewChanged?.Invoke();
    }

    /// <summary>Text with its first line centred on the point that was clicked, whatever the font size.</summary>
    private TextMark TextAtAnchor(StrokeStyle style, string text, bool boxed) =>
        new(style, new Point(_textAnchor.X, _textAnchor.Y - TextMark.LineHeight(style.Width) / 2), text, boxed);

    private void ShowTyping(TextMark text)
    {
        _text = text;
        Preview = text;
        Typing = new TypingState(text.Text, text.Position, text.FontSize, text.Style.Color, text.Bounds);
        PreviewChanged?.Invoke();
    }

    /// <summary>Picks a dimension's first point, or its second, which must be somewhere else.</summary>
    private void PickDimensionPoint(Point point, bool shift)
    {
        _pointer = point;
        switch (_dimensionPoints.Count)
        {
            case 0:
                _dimensionPoints.Add(point);
                _pressingDimension = true;
                break;
            case 1:
                var second = SecondDimensionPoint(point, shift);
                if ((second - _dimensionPoints[0]).Length >= MinimumSize)
                {
                    _dimensionPoints.Add(second);
                }

                break;
            case 2:
                // The third click places the dimension line; then its text is typed, starting with the measured length.
                var placed = PlacingDimension(point);
                _dimensionPoints.Clear();
                ShowDimensionTyping(placed with { Text = $"{Math.Round(placed.Layout.Measured)} px" });
                return;
        }

        ShowPlacingDimension();
    }

    /// <summary>
    /// Where a dimension's second point goes for the pointer at <paramref name="point"/>: right there, or with Shift,
    /// snapped to 0°, 45° or 90° from the first point. Shift doesn't affect the third click, as the dimension line's
    /// direction is already set by what it measures.
    /// </summary>
    private Point SecondDimensionPoint(Point point, bool shift) =>
        shift ? MarkGeometry.SnapAngle(_dimensionPoints[0], point) : point;

    private DimensionMark PlacingDimension(Point through)
    {
        var (a, b) = (_dimensionPoints[0], _dimensionPoints[1]);
        return new DimensionMark(new StrokeStyle(_colour, _width), a, b, through, DimensionMark.KindFor(a, b, through), "");
    }

    private void ShowPlacingDimension()
    {
        Preview = _dimensionPoints.Count == 2 ? PlacingDimension(_pointer) : null;
        PreviewChanged?.Invoke();
    }

    /// <summary>The text box goes exactly over where the dimension draws its text, turned with it if it's been turned.</summary>
    private void ShowDimensionTyping(DimensionMark dimension)
    {
        _dimension = dimension;
        Preview = dimension;
        var layout = dimension.Layout;
        var formatted = TextMark.Format(dimension.Text, dimension.FontSize, Brushes.Black);
        var height = Math.Max(formatted.Height, TextMark.LineHeight(dimension.FontSize));
        var origin = layout.TextCentre - new Vector(formatted.WidthIncludingTrailingWhitespace / 2, height / 2);
        var size = layout.TextSize;
        var frame = new Rect(layout.TextCentre - new Vector(size.Width / 2, size.Height / 2), size);
        Typing = new TypingState(dimension.Text, origin, dimension.FontSize, dimension.Style.Color, frame, layout.TextAngle);
        PreviewChanged?.Invoke();
    }

    /// <summary>
    /// A change of colour, thickness or box applies to text and dimensions that aren't finished yet. Only what changed
    /// is applied, so text that was already drawn keeps the rest of its own style while its words are changed.
    /// </summary>
    private void Restyle(Color? colour = null, double? width = null, bool? boxed = null)
    {
        if (_text is { } text)
        {
            var size = width is { } thickness ? Palette.StrokeWidth(ToolKind.Text, thickness) : text.FontSize;
            ShowTyping(TextAtAnchor(new StrokeStyle(colour ?? text.Style.Color, size), text.Text, boxed ?? text.Boxed));
        }
        else if (_dimension is { } dimension)
        {
            ShowDimensionTyping(dimension with { Style = new StrokeStyle(colour ?? dimension.Style.Color, width ?? dimension.Style.Width) });
        }
        else if (_dimensionPoints.Count > 0)
        {
            ShowPlacingDimension();
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
            ToolKind.Ellipse when rectangle.Width >= MinimumSize || rectangle.Height >= MinimumSize => new EllipseMark(_style, rectangle),
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
