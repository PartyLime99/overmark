using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Overmark.Editor;

/// <summary>Draws the document's finished marks. Only redrawn when the document changes.</summary>
internal sealed class CommittedMarksLayer : FrameworkElement
{
    private EditorDocument? _document;

    public void Attach(EditorDocument document)
    {
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
/// Draws what's in progress (the mark being drawn, a curve's bend handle, the crop box, the eraser's circle) and
/// takes the mouse input for drawing. Coordinates are image pixels; <see cref="DipSize"/> keeps handles and text a
/// constant size on screen at any zoom.
/// </summary>
internal sealed class PreviewLayer : FrameworkElement
{
    private static readonly Brush AccentBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)));
    private static readonly Brush DimBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x88, 0, 0, 0)));
    private static readonly Brush LabelBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xE0, 0x20, 0x20, 0x20)));

    private DrawingTools? _tools;
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

    public void Attach(DrawingTools tools)
    {
        _tools = tools;
        tools.PreviewChanged += InvalidateVisual;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var area = new Rect(RenderSize);

        // Transparent fill so the whole image takes mouse input.
        dc.DrawRectangle(Brushes.Transparent, null, area);
        if (_tools is null)
        {
            return;
        }

        if (_tools.Preview is { } preview)
        {
            dc.PushClip(new RectangleGeometry(area));
            preview.Draw(dc);
            dc.Pop();
        }

        if (_tools.IsBending)
        {
            dc.DrawEllipse(Brushes.White, new Pen(AccentBrush, 2 * DipSize), _tools.BendHandle, HandleRadius, HandleRadius);
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

    private void DrawCropBox(DrawingContext dc, Rect area, Rect box)
    {
        var outside = new GeometryGroup { FillRule = FillRule.EvenOdd };
        outside.Children.Add(new RectangleGeometry(area));
        outside.Children.Add(new RectangleGeometry(box));
        dc.DrawGeometry(DimBrush, null, outside);
        dc.DrawRectangle(null, new Pen(Brushes.Black, 3 * DipSize), box);
        dc.DrawRectangle(null, new Pen(Brushes.White, DipSize) { DashStyle = new DashStyle([4, 4], 0) }, box);

        var hint = new FormattedText(
            "Enter to crop  ·  Esc to cancel",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            12 * DipSize,
            Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var padding = 6 * DipSize;
        var label = new Rect(box.Left, box.Bottom + padding, hint.Width + 2 * padding, hint.Height + padding);
        if (label.Bottom > area.Bottom)
        {
            label.Y = box.Bottom - label.Height - padding;
        }

        dc.DrawRoundedRectangle(LabelBrush, null, label, 4 * DipSize, 4 * DipSize);
        dc.DrawText(hint, new Point(label.X + padding, label.Y + padding / 2));
    }

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
