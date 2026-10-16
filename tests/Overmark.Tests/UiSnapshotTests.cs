using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Overmark.Capture;
using Overmark.Editor;
using Overmark.Pinning;
using Overmark.Platform;
using Overmark.Recording;
using Overmark.Settings;

namespace Overmark.Tests;

/// <summary>
/// Renders Overmark's windows to PNG files without ever showing them on screen, for checking layout by eye.
/// Only runs when OVERMARK_SNAPSHOTS names a folder to write to.
/// </summary>
public class UiSnapshotTests
{
    private static readonly string? Folder = Environment.GetEnvironmentVariable("OVERMARK_SNAPSHOTS");

    [Fact]
    public void Render_windows_off_screen()
    {
        if (string.IsNullOrEmpty(Folder))
        {
            return;
        }

        RunOnUiThread(() =>
        {
            var capture = SampleCapture(1280, 720);
            var monitor = new MonitorInfo("DISPLAY1", new ScreenRect(0, 0, 2560, 1440), new ScreenRect(0, 0, 2560, 1380), true, 120);

            var popup = new CapturePopup(capture, "Copied and saved", "1280 × 720  ·  2026-10-15_10-30-00.png", @"C:\x.png", monitor, TimeSpan.FromSeconds(5), () => { }, () => { }, () => { });
            Save((FrameworkElement)popup.Content, new Size(400, 400), "popup.png");

            var videoPopup = new CapturePopup(capture, "Recording copied and saved", "0:42  ·  1280 × 720  ·  2026-10-15_10-30-00.mp4", "x.mp4", monitor,
                TimeSpan.FromSeconds(5), () => { }, pin: null, delete: () => { }, video: true);
            Save((FrameworkElement)videoPopup.Content, new Size(400, 400), "popup-video.png");

            var bar = new RecordingBar(new ScreenRect(100, 100, 1280, 720), monitor, "Ctrl+Shift+PrtScn");
            bar.Show(TimeSpan.FromSeconds(83), paused: false);
            Save((FrameworkElement)bar.Content, new Size(double.PositiveInfinity, double.PositiveInfinity), "recording-bar.png");
            bar.Show(TimeSpan.FromSeconds(83), paused: true);
            Save((FrameworkElement)bar.Content, new Size(double.PositiveInfinity, double.PositiveInfinity), "recording-bar-paused.png");

            var editor = new EditorWindow(@"C:\x.png", capture, new EditorSettings(), _ => { }, _ => { });
            Save((FrameworkElement)editor.Content, new Size(1180, 760), "editor.png");
            editor.Close();

            SaveTyping();
            SaveTextMarks();
            SaveDimensions();
            SaveOverlay();
            SaveTodayPane();

            var pin = new PinWindow(SampleCapture(420, 240), null, null);
            Save((FrameworkElement)pin.Content, new Size(double.PositiveInfinity, double.PositiveInfinity), "pin.png");

            var narrowPin = new PinWindow(SampleCapture(90, 90), null, null);
            Save((FrameworkElement)narrowPin.Content, new Size(double.PositiveInfinity, double.PositiveInfinity), "pin-small.png");

            var settings = new SettingsWindow(new AppSettings(), new NoRecorder(), _ => { }, () => { });
            Save((FrameworkElement)settings.Content, new Size(settings.Width, double.PositiveInfinity), "settings.png");
        });
    }

    /// <summary>
    /// The editor with one finished text and one being typed. The text box's own letters are made faintly green, so
    /// the snapshot shows whether they sit exactly on the drawn text (they're invisible in the app).
    /// </summary>
    private static void SaveTyping()
    {
        var editor = new EditorWindow(@"C:\x.png", SampleCapture(640, 300), new EditorSettings { Tool = "Text", TextBoxed = true }, _ => { }, _ => { });
        var content = (FrameworkElement)editor.Content;
        var size = new Size(900, 480);
        content.Measure(size);
        content.Arrange(new Rect(size));

        var tools = (DrawingTools)typeof(EditorWindow).GetField("_tools", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        var box = (TextBox)editor.FindName("TypingBox");
        tools.PointerDown(new Point(60, 120), false, 8);
        box.Text = "Boxed note";
        tools.Finish();

        tools.TextBoxed = false;
        tools.PointerDown(new Point(60, 200), false, 8);
        box.Text = "Typing gjy";
        box.Foreground = new SolidColorBrush(Color.FromArgb(0x90, 0x00, 0xFF, 0x00));
        tools.Width = tools.Width; // places the box again now that layout has run
        Save(content, size, "editor-typing.png");

        // Not closed: with marks on it, closing would ask (in a real dialog, on screen) whether to save them.
    }

    private static void SaveTextMarks()
    {
        var document = new EditorDocument(SampleCapture(520, 260));
        StrokeStyle Style(int colour, double size) => new(Palette.Colours[colour], size);
        document.Add(new TextMark(Style(0, 20), new Point(20, 80), "Red, plain", false));
        document.Add(new TextMark(Style(0, 20), new Point(200, 80), "Red, boxed", true));
        document.Add(new TextMark(Style(2, 20), new Point(20, 130), "Yellow, boxed", true));
        document.Add(new TextMark(Style(7, 20), new Point(200, 130), "White, boxed", true));
        document.Add(new TextMark(Style(6, 12), new Point(370, 85), "Black 12 px", false));
        document.Add(new TextMark(Style(4, 28), new Point(20, 180), "Two lines\r\nof blue", true));
        var rendered = document.Render();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rendered));
        using var file = File.Create(Path.Combine(Folder!, "text-marks.png"));
        encoder.Save(file);
    }

    /// <summary>
    /// Dimensions in their different arrangements, then the editor with one selected by the move tool (showing the
    /// handle that turns its text) and another being placed (showing the hint beside the pointer).
    /// </summary>
    private static void SaveDimensions()
    {
        StrokeStyle style = new(Palette.Colours[0], 4);
        Mark[] marks =
        [
            new DimensionMark(style, new Point(60, 140), new Point(260, 140), new Point(160, 110), DimensionKind.Horizontal, "200"),
            new DimensionMark(style, new Point(60, 220), new Point(150, 220), new Point(100, 250), DimensionKind.Horizontal, "90 mm (3.54 in) REF"),
            new DimensionMark(style, new Point(330, 120), new Point(330, 300), new Point(300, 200), DimensionKind.Vertical, "180"),
            new DimensionMark(style, new Point(420, 300), new Point(560, 180), new Point(560, 300), DimensionKind.Aligned, "184", TextAngle: -40.6),
            new DimensionMark(style, new Point(420, 100), new Point(520, 100), new Point(470, 70), DimensionKind.Horizontal, "Ø36 thru", new Point(640, 40)),
            new DimensionMark(style, new Point(600, 260), new Point(640, 260), new Point(620, 330), DimensionKind.Horizontal, "40", new Point(690, 330)),
            new DimensionMark(new StrokeStyle(Palette.Colours[4], 2), new Point(60, 320), new Point(200, 380), new Point(110, 400), DimensionKind.Aligned, "152"),
        ];

        var document = new EditorDocument(SampleCapture(760, 420));
        foreach (var mark in marks)
        {
            document.Add(mark);
        }

        var rendered = document.Render();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rendered));
        using (var file = File.Create(Path.Combine(Folder!, "dimensions.png")))
        {
            encoder.Save(file);
        }

        var editor = new EditorWindow(@"C:\x.png", SampleCapture(760, 420), new EditorSettings { StrokeWidth = 4 }, _ => { }, _ => { });
        var content = (FrameworkElement)editor.Content;
        var size = new Size(1000, 560);
        content.Measure(size);
        content.Arrange(new Rect(size));
        T Field<T>(string name) => (T)typeof(EditorWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        var editorDocument = Field<EditorDocument>("_document");
        editorDocument.Add(marks[3]);
        editorDocument.Add(marks[0]);

        ((System.Windows.Controls.Primitives.ToggleButton)editor.FindName("MoveButton")).IsChecked = true;
        var move = Field<MoveTool>("_move");
        move.PointerDown(((DimensionMark)marks[3]).Layout.TextCentre); // the aligned dimension's text
        move.PointerUp();

        var tools = Field<DrawingTools>("_tools");
        tools.Tool = ToolKind.Dimension;
        tools.PointerDown(new Point(450, 330), false, 8);
        tools.PointerUp(new Point(450, 330), false);
        tools.PointerDown(new Point(650, 330), false, 8);
        tools.PointerUp(new Point(650, 330), false);
        tools.PointerMove(new Point(560, 380), false);
        Save(content, size, "editor-dimensions.png");

        // Not closed, for the same reason as in SaveTyping.
    }

    /// <summary>
    /// The capture overlay part-way through a drag, with the magnifier, and with the toolbar shown too (in use it
    /// hides once a drag starts): a screenshot dragged from a corner, then a video dragged out from the centre (the same area).
    /// </summary>
    private static void SaveOverlay()
    {
        var bounds = new ScreenRect(0, 0, 2000, 1125);
        var monitor = new MonitorInfo("DISPLAY1", bounds, new ScreenRect(0, 0, 2000, 1065), true, 120);
        var image = SampleCapture(2000, 1125);
        foreach (var record in new[] { false, true })
        {
            var press = record ? new PixelPoint(750, 500) : new PixelPoint(400, 300);
            var session = new CaptureSession(bounds, [monitor], [], null, false, press, _ => { }, recordMode: record, fromCentre: record);
            session.PointerPressed(press, 1, 6);
            session.PointerMoved(new PixelPoint(1100, 700));

            var window = new OverlayWindow(monitor);
            var frozen = (Image)window.FindName("FrozenImage");
            frozen.Source = image;
            frozen.Width = 1600;
            frozen.Height = 900;
            ((FrameworkElement)window.FindName("Toolbar")).Visibility = Visibility.Visible;
            ((System.Windows.Controls.Primitives.ToggleButton)window.FindName("ScreenshotButton")).IsChecked = !record;
            ((System.Windows.Controls.Primitives.ToggleButton)window.FindName("RecordButton")).IsChecked = record;
            ((System.Windows.Controls.Primitives.ToggleButton)window.FindName("CornerButton")).IsChecked = !record;
            ((System.Windows.Controls.Primitives.ToggleButton)window.FindName("CentreButton")).IsChecked = record;
            ((OverlayCanvas)window.FindName("Overlay")).Attach(session, image, monitor);
            Save((FrameworkElement)window.Content, new Size(1600, 900), record ? "overlay-video.png" : "overlay.png");
        }
    }

    /// <summary>
    /// The editor with the list of today's captures: three screenshots (the second one open) and a recording.
    /// Thumbnails load in the background, so the dispatcher is run for a moment before the snapshot.
    /// </summary>
    private static void SaveTodayPane()
    {
        var folder = Path.Combine(Path.GetTempPath(), "OvermarkSnapshots", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var history = new CaptureHistory(Path.Combine(folder, "history.json"));
            var at = DateTime.Today.AddHours(14).AddMinutes(32);
            string Shot(string name, int width, int height)
            {
                var file = Path.Combine(folder, name);
                File.WriteAllBytes(file, CaptureOutput.EncodePng(SampleCapture(width, height)));
                return file;
            }

            history.Add(new CaptureRecord(Shot("a.png", 1920, 1080), CaptureKind.FullScreen, at.AddMinutes(-40), 1920, 1080));
            var clip = Path.Combine(folder, "clip.mp4");
            File.WriteAllText(clip, "");
            history.Add(new CaptureRecord(clip, CaptureKind.Region, at.AddMinutes(-11), 1280, 720, 42));
            var open = Shot("b.png", 900, 500);
            history.Add(new CaptureRecord(open, CaptureKind.Window, at.AddMinutes(-3), 900, 500));
            history.Add(new CaptureRecord(Shot("c.png", 640, 360), CaptureKind.Region, at, 640, 360));

            var editor = new EditorWindow(open, SampleCapture(900, 500), new EditorSettings(), _ => { }, _ => { }, history, _ => { });
            var content = (FrameworkElement)editor.Content;
            var size = new Size(1300, 760);
            content.Measure(size);
            content.Arrange(new Rect(size));
            RunDispatcherFor(TimeSpan.FromSeconds(1.5));
            Save(content, size, "editor-today.png");
            editor.Close();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static void RunDispatcherFor(TimeSpan time)
    {
        var until = DateTime.UtcNow + time;
        while (DateTime.UtcNow < until)
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background, () => frame.Continue = false);
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            Thread.Sleep(20);
        }
    }

    private sealed class NoRecorder : Overmark.Hotkeys.IHotkeyRecorder
    {
        public void StartRecording(Action<Overmark.Hotkeys.Hotkey> recorder)
        {
        }

        public void StopRecording()
        {
        }
    }

    private static BitmapSource SampleCapture(int width, int height)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x2B, 0x57, 0x9A)), null, new Rect(0, 0, width, 60));
            for (var y = 100; y < height; y += 40)
            {
                dc.DrawRectangle(Brushes.LightGray, null, new Rect(40, y, width * 0.6, 14));
            }
        }

        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        image.Freeze();
        return image;
    }

    private static void Save(FrameworkElement element, Size available, string name)
    {
        // Laid out at 125% scaling, like a typical laptop or 1440p monitor, so layout rounding to whole screen pixels
        // shows up as it would on screen.
        if (VisualTreeHelper.GetParent(element) is null)
        {
            VisualTreeHelper.SetRootDpi(element, new DpiScale(1.25, 1.25));
        }

        element.Measure(available);
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();
        var size = element.DesiredSize;
        var target = new RenderTargetBitmap((int)Math.Ceiling(size.Width * 1.25), (int)Math.Ceiling(size.Height * 1.25), 120, 120, PixelFormats.Pbgra32);
        var backed = new DrawingVisual();
        using (var dc = backed.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)), null, new Rect(size));
            dc.DrawRectangle(new VisualBrush(element), null, new Rect(size));
        }

        target.Render(backed);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        Directory.CreateDirectory(Folder!);
        using var file = File.Create(Path.Combine(Folder!, name));
        encoder.Save(file);
    }

    private static void RunOnUiThread(Action work)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            throw error;
        }
    }
}
