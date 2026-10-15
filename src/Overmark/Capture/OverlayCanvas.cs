using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Overmark.Platform;

namespace Overmark.Capture;

/// <summary>
/// Draws one monitor's share of the capture overlay: dimming, the selection or highlighted window, the
/// full-screen crosshair, and the magnifier. Positions come from the session in physical desktop pixels.
/// </summary>
internal sealed class OverlayCanvas : FrameworkElement
{
    private const int MagnifierCells = 15; // odd, so there's a centre pixel
    private const double MagnifierSize = 120;
    private const double MagnifierOffset = 20;
    private const double HandleSize = 7;
    private const double LabelGap = 6;

    private static readonly Brush DimBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)));
    private static readonly Brush AccentBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)));
    private static readonly Brush LabelBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xE0, 0x20, 0x20, 0x20)));
    private static readonly Brush GridBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x50, 0, 0, 0)));
    private static readonly Typeface LabelFont = new("Segoe UI");
    private static readonly Typeface CoordinateFont = new("Consolas");

    private CaptureSession? _session;
    private MonitorInfo? _monitor;
    private ImageBrush? _magnifierBrush;

    /// <summary>Starts drawing <paramref name="session"/> over <paramref name="monitorImage"/>, this monitor's part of the screenshot.</summary>
    public void Attach(CaptureSession session, BitmapSource monitorImage, MonitorInfo monitor)
    {
        _session = session;
        _monitor = monitor;

        // One brush over this monitor's image, re-aimed for each frame, so the image is only uploaded once.
        _magnifierBrush = new ImageBrush(monitorImage) { ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(_magnifierBrush, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        InvalidateVisual();
    }

    public void Detach()
    {
        _session = null;
        _magnifierBrush = null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_session is null || _monitor is null)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var scale = dpi.DpiScaleX;
        var area = new Rect(0, 0, ActualWidth, ActualHeight);

        // Transparent fill so the whole window takes mouse input, including the undimmed hole.
        dc.DrawRectangle(Brushes.Transparent, null, area);

        var focus = _session.FocusRect;
        var dimming = new GeometryGroup { FillRule = FillRule.EvenOdd };
        dimming.Children.Add(new RectangleGeometry(area));
        if (focus is { } hole)
        {
            dimming.Children.Add(new RectangleGeometry(ToDips(hole, scale)));
        }

        dc.DrawGeometry(DimBrush, null, dimming);

        if (focus is { } rect)
        {
            DrawFocus(dc, rect, area, scale, dpi.PixelsPerDip);
        }

        if (_session.ShowsCrosshair)
        {
            DrawCrosshair(dc, area, scale);
        }

        if (_session.ShowsMagnifier && _monitor.Bounds.Contains(_session.Cursor))
        {
            DrawMagnifier(dc, area, scale, dpi.PixelsPerDip);
        }
    }

    private void DrawFocus(DrawingContext dc, ScreenRect focus, Rect area, double scale, double pixelsPerDip)
    {
        var px = 1 / scale;
        var rect = ToDips(focus, scale);

        // A one-pixel border just outside the area, so it never covers captured pixels.
        var dashed = _session!.PreviewingLastRegion && _session.Mode is SessionMode.Idle or SessionMode.Pressing;
        var pen = new Pen(AccentBrush, px);
        if (dashed)
        {
            pen.DashStyle = new DashStyle([4, 3], 0);
        }

        dc.DrawRectangle(null, pen, new Rect(rect.X - px / 2, rect.Y - px / 2, rect.Width + px, rect.Height + px));

        if (_session.Mode == SessionMode.Adjusting)
        {
            var handlePen = new Pen(AccentBrush, px);
            foreach (var centre in HandleCentres(rect))
            {
                dc.DrawRectangle(Brushes.White, handlePen, new Rect(centre.X - HandleSize / 2, centre.Y - HandleSize / 2, HandleSize, HandleSize));
            }
        }

        // Size above the top-left corner, or just inside it if there's no room.
        var size = Text($"{focus.Width} × {focus.Height}", LabelFont, 12, pixelsPerDip);
        var sizeBox = LabelBox(size);
        var above = rect.Y - sizeBox.Height - LabelGap;
        DrawLabel(dc, size, new Point(rect.X, above >= 0 ? above : rect.Y + LabelGap));

        if (_session.Mode == SessionMode.Adjusting)
        {
            var hint = Text("Enter or double-click to capture  ·  Esc to cancel  ·  Arrows move, Ctrl+arrows resize", LabelFont, 12, pixelsPerDip);
            var hintBox = LabelBox(hint);
            var below = rect.Bottom + LabelGap;
            DrawLabel(dc, hint, new Point(rect.X, below + hintBox.Height <= area.Height ? below : rect.Bottom - hintBox.Height - LabelGap));
        }
    }

    private void DrawCrosshair(DrawingContext dc, Rect area, double scale)
    {
        var px = 1 / scale;
        var under = new Pen(Brushes.Black, px);
        var over = new Pen(Brushes.White, px) { DashStyle = new DashStyle([4, 4], 0) };
        var cursor = _session!.Cursor;
        var bounds = _monitor!.Bounds;

        // Centred on the cursor's pixel row/column; each monitor draws the parts that cross it.
        if (cursor.X >= bounds.Left && cursor.X < bounds.Right)
        {
            var x = (cursor.X - bounds.Left + 0.5) / scale;
            dc.DrawLine(under, new Point(x, 0), new Point(x, area.Height));
            dc.DrawLine(over, new Point(x, 0), new Point(x, area.Height));
        }

        if (cursor.Y >= bounds.Top && cursor.Y < bounds.Bottom)
        {
            var y = (cursor.Y - bounds.Top + 0.5) / scale;
            dc.DrawLine(under, new Point(0, y), new Point(area.Width, y));
            dc.DrawLine(over, new Point(0, y), new Point(area.Width, y));
        }
    }

    private void DrawMagnifier(DrawingContext dc, Rect area, double scale, double pixelsPerDip)
    {
        var px = 1 / scale;
        var cursor = _session!.Cursor;
        var pointer = ToDips(cursor, scale);
        var coordinates = Text($"X: {cursor.X} Y: {cursor.Y}", CoordinateFont, 12, pixelsPerDip);
        var coordinatesBox = LabelBox(coordinates);
        var totalHeight = MagnifierSize + 4 + coordinatesBox.Height;

        // Below-right of the pointer, flipping to the other side near the monitor's edges.
        var x = pointer.X + MagnifierOffset;
        if (x + MagnifierSize > area.Width)
        {
            x = pointer.X - MagnifierOffset - MagnifierSize;
        }

        var y = pointer.Y + MagnifierOffset;
        if (y + totalHeight > area.Height)
        {
            y = pointer.Y - MagnifierOffset - totalHeight;
        }

        var lens = new Rect(x, y, MagnifierSize, MagnifierSize);
        var cell = MagnifierSize / MagnifierCells;
        var half = MagnifierCells / 2;

        dc.PushClip(new EllipseGeometry(lens));
        dc.DrawRectangle(Brushes.Black, null, lens);
        _magnifierBrush!.Viewbox = new Rect(
            cursor.X - _monitor!.Bounds.Left - half,
            cursor.Y - _monitor.Bounds.Top - half,
            MagnifierCells,
            MagnifierCells);
        dc.DrawRectangle(_magnifierBrush, null, lens);

        var gridPen = new Pen(GridBrush, px);
        for (var i = 1; i < MagnifierCells; i++)
        {
            dc.DrawLine(gridPen, new Point(x + i * cell, y), new Point(x + i * cell, y + MagnifierSize));
            dc.DrawLine(gridPen, new Point(x, y + i * cell), new Point(x + MagnifierSize, y + i * cell));
        }

        var centre = new Rect(x + half * cell, y + half * cell, cell, cell);
        dc.DrawRectangle(null, new Pen(Brushes.Black, 3 * px), centre);
        dc.DrawRectangle(null, new Pen(Brushes.White, px), centre);
        dc.Pop();

        var middle = new Point(x + MagnifierSize / 2, y + MagnifierSize / 2);
        dc.DrawEllipse(null, new Pen(Brushes.Black, 3 * px), middle, MagnifierSize / 2, MagnifierSize / 2);
        dc.DrawEllipse(null, new Pen(Brushes.White, px), middle, MagnifierSize / 2, MagnifierSize / 2);

        DrawLabel(dc, coordinates, new Point(x + (MagnifierSize - coordinatesBox.Width) / 2, y + MagnifierSize + 4));
    }

    private Rect ToDips(ScreenRect rect, double scale) => new(
        (rect.Left - _monitor!.Bounds.Left) / scale,
        (rect.Top - _monitor.Bounds.Top) / scale,
        rect.Width / scale,
        rect.Height / scale);

    private Point ToDips(PixelPoint point, double scale) => new(
        (point.X - _monitor!.Bounds.Left) / scale,
        (point.Y - _monitor.Bounds.Top) / scale);

    private static IEnumerable<Point> HandleCentres(Rect rect)
    {
        var midX = rect.X + rect.Width / 2;
        var midY = rect.Y + rect.Height / 2;
        yield return rect.TopLeft;
        yield return new Point(midX, rect.Top);
        yield return rect.TopRight;
        yield return new Point(rect.Right, midY);
        yield return rect.BottomRight;
        yield return new Point(midX, rect.Bottom);
        yield return rect.BottomLeft;
        yield return new Point(rect.Left, midY);
    }

    private static FormattedText Text(string text, Typeface font, double size, double pixelsPerDip) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, font, size, Brushes.White, pixelsPerDip);

    private static Size LabelBox(FormattedText text) => new(text.Width + 12, text.Height + 6);

    private static void DrawLabel(DrawingContext dc, FormattedText text, Point topLeft)
    {
        var box = LabelBox(text);
        dc.DrawRoundedRectangle(LabelBrush, null, new Rect(topLeft, box), 4, 4);
        dc.DrawText(text, new Point(topLeft.X + 6, topLeft.Y + 3));
    }

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
