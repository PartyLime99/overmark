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
    private const double LensSize = 128;
    private const double CardPadding = 6;
    private const double CardWidth = LensSize + 2 * CardPadding;
    private const double CardRadius = 18;
    private const double InfoRowHeight = 17;
    private const double MagnifierOffset = 22;
    private const double HandleSize = 9;
    private const double BracketLength = 18;
    private const double BracketThickness = 4;
    private const double CentreMarkSize = 7;
    private const double LabelGap = 10;
    private const double PillPaddingX = 10;
    private const double PillPaddingY = 5;
    private const double PillGap = 8;

    private static readonly Brush DimBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x7A, 0x0C, 0x10, 0x16)));
    private static readonly Brush InkBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x16, 0x19, 0x1D)));
    private static readonly Brush MutedOnDarkBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xB7, 0xBD, 0xC6)));
    private static readonly Brush MutedBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x5A, 0x62, 0x6D)));
    private static readonly Brush EdgeBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x59, 0, 0, 0)));
    private static readonly Brush GridBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x1A, 0x16, 0x19, 0x1D)));
    private static readonly Brush LensRimBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xE3, 0xE7, 0xEC)));
    private static readonly Brush[] ShadowBrushes =
    [
        Frozen(new SolidColorBrush(Color.FromArgb(0x10, 0, 0, 0))),
        Frozen(new SolidColorBrush(Color.FromArgb(0x16, 0, 0, 0))),
        Frozen(new SolidColorBrush(Color.FromArgb(0x22, 0, 0, 0))),
    ];

    private static readonly Typeface LabelFont = new("Segoe UI");
    private static readonly Typeface CoordinateFont = new(new FontFamily("Cascadia Mono, Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private CaptureSession? _session;
    private MonitorInfo? _monitor;
    private BitmapSource? _monitorImage;
    private ImageBrush? _magnifierBrush;

    /// <summary>Starts drawing <paramref name="session"/> over <paramref name="monitorImage"/>, this monitor's part of the screenshot.</summary>
    public void Attach(CaptureSession session, BitmapSource monitorImage, MonitorInfo monitor)
    {
        _session = session;
        _monitor = monitor;
        _monitorImage = monitorImage;

        // One brush over this monitor's image, re-aimed for each frame, so the image is only uploaded once.
        _magnifierBrush = new ImageBrush(monitorImage) { ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(_magnifierBrush, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        InvalidateVisual();
    }

    public void Detach()
    {
        _session = null;
        _monitorImage = null;
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

        if (_session.DragCentre is { } centre)
        {
            DrawCentreMark(dc, centre, scale);
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
        var selecting = _session!.Mode is SessionMode.Dragging or SessionMode.Adjusting;
        var previewing = _session.PreviewingLastRegion && _session.Mode is SessionMode.Idle or SessionMode.Pressing;

        // A white outline just outside the area, so it never covers captured pixels, with a faint dark edge
        // beyond it so it still shows against white.
        var width = (selecting ? 2 : 3) * px;
        dc.DrawRectangle(null, new Pen(EdgeBrush, px), Grow(rect, width + px / 2));
        var outline = new Pen(Brushes.White, width);
        if (previewing)
        {
            outline.DashStyle = new DashStyle([3, 2], 0);
        }

        dc.DrawRectangle(null, outline, Grow(rect, width / 2));

        if (selecting)
        {
            DrawBrackets(dc, Grow(rect, width + BracketThickness / 2));
        }

        if (_session.Mode == SessionMode.Adjusting)
        {
            var handlePen = new Pen(InkBrush, 1.5 * px);
            foreach (var centre in HandleCentres(rect))
            {
                dc.DrawRectangle(Brushes.White, handlePen, new Rect(centre.X - HandleSize / 2, centre.Y - HandleSize / 2, HandleSize, HandleSize));
            }
        }

        // The size in a dark pill above the top-left corner, or just inside it if there's no room.
        var note = selecting ? $"at {focus.Left}, {focus.Top}" : previewing ? "last region" : "click to capture this window";
        var size = Text($"{focus.Width} × {focus.Height}", CoordinateFont, 12.5, pixelsPerDip, Brushes.White);
        var detail = Text(note, LabelFont, 11.5, pixelsPerDip, MutedOnDarkBrush);
        var pill = PillSize(size, detail);
        var above = rect.Y - pill.Height - LabelGap;
        DrawPill(dc, size, detail, new Point(rect.X, above >= 0 ? above : rect.Y + LabelGap));

        if (_session.Mode == SessionMode.Adjusting)
        {
            var hint = Text("Enter or double-click to capture  ·  Esc to cancel  ·  Arrows move, Ctrl+arrows resize", LabelFont, 12, pixelsPerDip, Brushes.White);
            var hintSize = PillSize(hint, null);
            var below = rect.Bottom + LabelGap;
            DrawPill(dc, hint, null, new Point(rect.X, below + hintSize.Height <= area.Height ? below : rect.Bottom - hintSize.Height - LabelGap));
        }
    }

    /// <summary>Thick white corner marks just outside a selection.</summary>
    private static void DrawBrackets(DrawingContext dc, Rect outside)
    {
        var length = Math.Min(BracketLength, Math.Max(6, Math.Min(outside.Width, outside.Height) / 3));
        var pen = new Pen(Brushes.White, BracketThickness) { StartLineCap = PenLineCap.Square, EndLineCap = PenLineCap.Square, LineJoin = PenLineJoin.Miter };
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            void Corner(Point corner, double dx, double dy)
            {
                context.BeginFigure(new Point(corner.X, corner.Y + dy * length), false, false);
                context.LineTo(corner, true, true);
                context.LineTo(new Point(corner.X + dx * length, corner.Y), true, true);
            }

            Corner(outside.TopLeft, 1, 1);
            Corner(outside.TopRight, -1, 1);
            Corner(outside.BottomLeft, 1, -1);
            Corner(outside.BottomRight, -1, -1);
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    /// <summary>A small white cross on the pixel a centre-out drag started from.</summary>
    private void DrawCentreMark(DrawingContext dc, PixelPoint centre, double scale)
    {
        var px = 1 / scale;
        var middle = new Point((centre.X - _monitor!.Bounds.Left + 0.5) / scale, (centre.Y - _monitor.Bounds.Top + 0.5) / scale);
        foreach (var pen in new[] { new Pen(EdgeBrush, 4 * px), new Pen(Brushes.White, 2 * px) })
        {
            dc.DrawLine(pen, new Point(middle.X - CentreMarkSize, middle.Y), new Point(middle.X + CentreMarkSize, middle.Y));
            dc.DrawLine(pen, new Point(middle.X, middle.Y - CentreMarkSize), new Point(middle.X, middle.Y + CentreMarkSize));
        }
    }

    private void DrawCrosshair(DrawingContext dc, Rect area, double scale)
    {
        var px = 1 / scale;
        var under = new Pen(EdgeBrush, 3 * px);
        var over = new Pen(Brushes.White, px);
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

    /// <summary>
    /// A white card beside the pointer: a round lens of zoomed pixels with the centre pixel outlined, and under it
    /// the pointer's position and the colour of the pixel under it.
    /// </summary>
    private void DrawMagnifier(DrawingContext dc, Rect area, double scale, double pixelsPerDip)
    {
        var px = 1 / scale;
        var cursor = _session!.Cursor;
        var pointer = ToDips(cursor, scale);
        var colour = PixelAt(cursor);

        var pointerLabel = Text("Pointer", LabelFont, 11, pixelsPerDip, MutedBrush);
        var pointerValue = Text($"{cursor.X}, {cursor.Y}", CoordinateFont, 11, pixelsPerDip, InkBrush);
        var colourLabel = Text("Colour", LabelFont, 11, pixelsPerDip, MutedBrush);
        var colourValue = Text(colour is { } c ? $"{c.R:X2}{c.G:X2}{c.B:X2}" : "–", CoordinateFont, 11, pixelsPerDip, InkBrush);
        var cardHeight = CardPadding + LensSize + 8 + 2 * InfoRowHeight + 8;

        // Below-right of the pointer, flipping to the other side near the monitor's edges.
        var x = pointer.X + MagnifierOffset;
        if (x + CardWidth > area.Width)
        {
            x = pointer.X - MagnifierOffset - CardWidth;
        }

        var y = pointer.Y + MagnifierOffset;
        if (y + cardHeight > area.Height)
        {
            y = pointer.Y - MagnifierOffset - cardHeight;
        }

        var card = new Rect(x, y, CardWidth, cardHeight);

        // A soft shadow from a few faint layers, cheap enough to redraw on every pointer move.
        for (var i = 0; i < ShadowBrushes.Length; i++)
        {
            var spread = (ShadowBrushes.Length - i) * 3;
            var shadow = new Rect(card.X - spread, card.Y - spread + 6, card.Width + 2 * spread, card.Height + 2 * spread);
            dc.DrawRoundedRectangle(ShadowBrushes[i], null, shadow, CardRadius + spread, CardRadius + spread);
        }

        dc.DrawRoundedRectangle(Brushes.White, null, card, CardRadius, CardRadius);

        var lens = new Rect(x + CardPadding, y + CardPadding, LensSize, LensSize);
        var cell = LensSize / MagnifierCells;
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
            dc.DrawLine(gridPen, new Point(lens.X + i * cell, lens.Y), new Point(lens.X + i * cell, lens.Bottom));
            dc.DrawLine(gridPen, new Point(lens.X, lens.Y + i * cell), new Point(lens.Right, lens.Y + i * cell));
        }

        var centre = new Rect(lens.X + half * cell, lens.Y + half * cell, cell, cell);
        dc.DrawRectangle(null, new Pen(Brushes.White, 4 * px), centre);
        dc.DrawRectangle(null, new Pen(InkBrush, 2 * px), centre);
        dc.Pop();

        var middle = new Point(lens.X + LensSize / 2, lens.Y + LensSize / 2);
        dc.DrawEllipse(null, new Pen(LensRimBrush, px), middle, LensSize / 2, LensSize / 2);

        // The two rows under the lens: label on the left, value on the right.
        var left = x + CardPadding + 6;
        var right = card.Right - CardPadding - 6;
        var row = lens.Bottom + 8;
        dc.DrawText(pointerLabel, new Point(left, row));
        dc.DrawText(pointerValue, new Point(right - pointerValue.Width, row + (pointerLabel.Height - pointerValue.Height) / 2));

        row += InfoRowHeight;
        dc.DrawText(colourLabel, new Point(left, row));
        var valueX = right - colourValue.Width;
        dc.DrawText(colourValue, new Point(valueX, row + (colourLabel.Height - colourValue.Height) / 2));
        if (colour is { } swatch)
        {
            var dot = new Point(valueX - 9, row + colourLabel.Height / 2);
            dc.DrawEllipse(new SolidColorBrush(swatch), new Pen(EdgeBrush, px), dot, 4.5, 4.5);
        }
    }

    /// <summary>The colour of one pixel of the frozen screen, or null if it can't be read.</summary>
    private Color? PixelAt(PixelPoint point)
    {
        if (_monitorImage is not { } image || image.Format.BitsPerPixel != 32)
        {
            return null;
        }

        var x = point.X - _monitor!.Bounds.Left;
        var y = point.Y - _monitor.Bounds.Top;
        if (x < 0 || y < 0 || x >= image.PixelWidth || y >= image.PixelHeight)
        {
            return null;
        }

        // 32-bit screen formats are stored blue, green, red, then alpha or padding.
        var pixel = new byte[4];
        image.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromRgb(pixel[2], pixel[1], pixel[0]);
    }

    private Rect ToDips(ScreenRect rect, double scale) => new(
        (rect.Left - _monitor!.Bounds.Left) / scale,
        (rect.Top - _monitor.Bounds.Top) / scale,
        rect.Width / scale,
        rect.Height / scale);

    private Point ToDips(PixelPoint point, double scale) => new(
        (point.X - _monitor!.Bounds.Left) / scale,
        (point.Y - _monitor.Bounds.Top) / scale);

    private static Rect Grow(Rect rect, double by) => new(rect.X - by, rect.Y - by, rect.Width + 2 * by, rect.Height + 2 * by);

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

    private static FormattedText Text(string text, Typeface font, double size, double pixelsPerDip, Brush brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, font, size, brush, pixelsPerDip);

    private static Size PillSize(FormattedText main, FormattedText? detail) => new(
        main.Width + (detail is null ? 0 : PillGap + detail.Width) + 2 * PillPaddingX,
        Math.Max(main.Height, detail?.Height ?? 0) + 2 * PillPaddingY);

    /// <summary>A dark rounded label: the main text, then optional quieter text beside it.</summary>
    private static void DrawPill(DrawingContext dc, FormattedText main, FormattedText? detail, Point topLeft)
    {
        var size = PillSize(main, detail);
        dc.DrawRoundedRectangle(InkBrush, null, new Rect(topLeft, size), 9, 9);
        var height = size.Height - 2 * PillPaddingY;
        dc.DrawText(main, new Point(topLeft.X + PillPaddingX, topLeft.Y + PillPaddingY + (height - main.Height) / 2));
        if (detail is not null)
        {
            dc.DrawText(detail, new Point(topLeft.X + PillPaddingX + main.Width + PillGap, topLeft.Y + PillPaddingY + (height - detail.Height) / 2));
        }
    }

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
