using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Overmark.Editor;

/// <summary>Draws the document's finished marks. Only redrawn when the document changes.</summary>
internal sealed class CommittedMarksLayer : FrameworkElement
{
    private EditorDocument? _document;

    /// <summary>Draws <paramref name="document"/>'s marks, instead of any document it drew before.</summary>
    public void Attach(EditorDocument document)
    {
        if (_document is not null)
        {
            _document.Changed -= InvalidateVisual;
        }

        _document = document;
        document.Changed += InvalidateVisual;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_document is null)
        {
            return;
        }

        // Marks are clipped to the image here, just as they are when the image is saved.
        dc.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        foreach (var mark in _document.Marks)
        {
            mark.Draw(dc);
        }

        dc.Pop();
    }
}

/// <summary>
/// Draws what's in progress (the mark being drawn, a frame round text being typed, a curve's bend handle, a
/// dimension's picked points, the crop box, the eraser's circle, the move tool's selection) and takes the mouse
/// input for drawing. Coordinates are image pixels; <see cref="DipSize"/> keeps handles and text a constant size on
/// screen at any zoom.
/// </summary>
internal sealed class PreviewLayer : FrameworkElement
{
    private static readonly Brush AccentBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)));
    private static readonly Brush DimBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x88, 0, 0, 0)));
    private static readonly Brush LabelBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xE0, 0x20, 0x20, 0x20)));

    private DrawingTools? _tools;
    private MoveTool? _move;
    private Point? _eraserAt;

    /// <summary>How many image pixels one on-screen DIP covers at the current zoom.</summary>
    public double DipSize { get; set; } = 1;

    /// <summary>The bend handle's radius, in image pixels.</summary>
    public double HandleRadius => 6 * DipSize;

    /// <summary>The eraser's radius, in image pixels.</summary>
    public double EraserRadius => 10 * DipSize;

    /// <summary>Where to show the eraser's circle, or null to hide it.</summary>
    public Point? EraserAt
    {
        get => _eraserAt;
        set
        {
            _eraserAt = value;
            InvalidateVisual();
        }
    }

    /// <summary>Draws what <paramref name="tools"/> and <paramref name="move"/> show, instead of any it drew before.</summary>
    public void Attach(DrawingTools tools, MoveTool move)
    {
        if (_tools is not null)
        {
            _tools.PreviewChanged -= InvalidateVisual;
        }

        if (_move is not null)
        {
            _move.Changed -= InvalidateVisual;
        }

        _tools = tools;
        _move = move;
        tools.PreviewChanged += InvalidateVisual;
        move.Changed += InvalidateVisual;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var area = new Rect(RenderSize);

        // Transparent fill so the whole image takes mouse input.
        dc.DrawRectangle(Brushes.Transparent, null, area);
        if (_tools is null || _move is null)
        {
            return;
        }

        if (_move.SelectedMark is { } selected)
        {
            DrawSelection(dc, selected, _move.RotateHandle);
        }

        if (_tools.Preview is { } preview)
        {
            dc.PushClip(new RectangleGeometry(area));
            preview.Draw(dc);
            dc.Pop();
        }

        if (_tools.Typing is { } typing)
        {
            // The text is drawn by the preview, exactly as it will be kept; the editor's text box on top only adds the caret.
            var frame = typing.Frame;
            frame.Inflate(4 * DipSize, 3 * DipSize);
            DrawDashed(dc, new RectangleGeometry(frame));
        }

        DrawDimensionPicking(dc, area);

        if (_tools.IsBending)
        {
            DrawHandle(dc, _tools.BendHandle);
        }

        if (_tools.CropBox is { } box)
        {
            DrawCropBox(dc, area, box);
        }

        if (EraserAt is { } eraser)
        {
            dc.DrawEllipse(null, new Pen(Brushes.Black, 3 * DipSize), eraser, EraserRadius, EraserRadius);
            dc.DrawEllipse(null, new Pen(Brushes.White, DipSize), eraser, EraserRadius, EraserRadius);
        }
    }

    /// <summary>A dashed outline round the selected mark; for a dimension, round its text too, with the handle that turns it.</summary>
    private void DrawSelection(DrawingContext dc, Mark selected, Point? handle)
    {
        var outline = selected.Extent;
        outline.Inflate(4 * DipSize, 4 * DipSize);
        DrawDashed(dc, new RectangleGeometry(outline));

        if (selected is not DimensionMark dimension || handle is not { } at)
        {
            return;
        }

        var layout = dimension.Layout;
        var corners = layout.TextCorners(3 * DipSize);
        var text = new StreamGeometry();
        using (var context = text.Open())
        {
            context.BeginFigure(corners[0], isFilled: false, isClosed: true);
            context.PolyLineTo(corners[1..], isStroked: true, isSmoothJoin: false);
        }

        DrawDashed(dc, text);
        var top = layout.TextCentre - layout.TextY * (layout.TextSize.Height / 2 + 3 * DipSize);
        DrawDashed(dc, new LineGeometry(top, at));
        DrawHandle(dc, at);
    }

    /// <summary>The points picked for a dimension so far, a guide line to the pointer, and what it will measure.</summary>
    private void DrawDimensionPicking(DrawingContext dc, Rect area)
    {
        var points = _tools!.DimensionPoints;
        if (_tools.DimensionGuide is { } guide && points.Count == 1)
        {
            DrawDashed(dc, new LineGeometry(points[0], guide));
        }

        foreach (var point in points)
        {
            dc.DrawEllipse(Brushes.White, new Pen(AccentBrush, 1.5 * DipSize), point, 3.5 * DipSize, 3.5 * DipSize);
        }

        if (_tools.DimensionHint is { } hint)
        {
            // Beside the pointer, or above it near the bottom of the image.
            var text = LabelText(hint.Text);
            var label = LabelAround(text, hint.At + new Vector(16 * DipSize, 16 * DipSize));
            if (label.Bottom > area.Bottom)
            {
                label.Y = hint.At.Y - label.Height - 16 * DipSize;
            }

            label.X = Math.Max(area.Left, Math.Min(label.X, area.Right - label.Width));
            DrawLabel(dc, text, label);
        }
    }

    private void DrawHandle(DrawingContext dc, Point at) =>
        dc.DrawEllipse(Brushes.White, new Pen(AccentBrush, 2 * DipSize), at, HandleRadius, HandleRadius);

    /// <summary>Black and white dashes, which show up on any background.</summary>
    private void DrawDashed(DrawingContext dc, Geometry shape)
    {
        dc.DrawGeometry(null, new Pen(Brushes.Black, DipSize), shape);
        dc.DrawGeometry(null, new Pen(Brushes.White, DipSize) { DashStyle = new DashStyle([3, 3], 0) }, shape);
    }

    private void DrawCropBox(DrawingContext dc, Rect area, Rect box)
    {
        var outside = new GeometryGroup { FillRule = FillRule.EvenOdd };
        outside.Children.Add(new RectangleGeometry(area));
        outside.Children.Add(new RectangleGeometry(box));
        dc.DrawGeometry(DimBrush, null, outside);
        dc.DrawRectangle(null, new Pen(Brushes.Black, 3 * DipSize), box);
        dc.DrawRectangle(null, new Pen(Brushes.White, DipSize) { DashStyle = new DashStyle([4, 4], 0) }, box);

        var hint = LabelText("Enter to crop  ·  Esc to cancel");
        var padding = 6 * DipSize;
        var label = LabelAround(hint, new Point(box.Left, box.Bottom + padding));
        if (label.Bottom > area.Bottom)
        {
            label.Y = box.Bottom - label.Height - padding;
        }

        DrawLabel(dc, hint, label);
    }

    private FormattedText LabelText(string text) => new(
        text,
        CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight,
        new Typeface("Segoe UI"),
        12 * DipSize,
        Brushes.White,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);

    /// <summary>The box for a label with its top-left at <paramref name="at"/>.</summary>
    private Rect LabelAround(FormattedText text, Point at)
    {
        var padding = 6 * DipSize;
        return new Rect(at.X, at.Y, text.Width + 2 * padding, text.Height + padding);
    }

    private void DrawLabel(DrawingContext dc, FormattedText text, Rect label)
    {
        var padding = 6 * DipSize;
        dc.DrawRoundedRectangle(LabelBrush, null, label, 4 * DipSize, 4 * DipSize);
        dc.DrawText(text, new Point(label.X + padding, label.Y + padding / 2));
    }

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
