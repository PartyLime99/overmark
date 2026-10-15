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

            var popup = new CapturePopup(capture, "Screenshot copied", "1280 × 720  ·  2026-10-15_10-30-00.png", @"C:\x.png", monitor, TimeSpan.FromSeconds(5), () => { }, () => { }, () => { });
            Save((FrameworkElement)popup.Content, new Size(400, 400), "popup.png");

            var videoPopup = new CapturePopup(capture, "Recording copied", "0:42  ·  1280 × 720  ·  2026-10-15_10-30-00.mp4", "x.mp4", monitor,
                TimeSpan.FromSeconds(5), () => { }, pin: null, delete: () => { }, hint: "Click to play  ·  drag into another app, or paste it");
            Save((FrameworkElement)videoPopup.Content, new Size(400, 400), "popup-video.png");

            var bar = new RecordingBar(new ScreenRect(100, 100, 1280, 720), monitor, "Ctrl+Shift+PrtScn");
            bar.Show(TimeSpan.FromSeconds(83), paused: false);
            Save((FrameworkElement)bar.Content, new Size(double.PositiveInfinity, double.PositiveInfinity), "recording-bar.png");
            bar.Show(TimeSpan.FromSeconds(83), paused: true);
            Save((FrameworkElement)bar.Content, new Size(double.PositiveInfinity, double.PositiveInfinity), "recording-bar-paused.png");

            var editor = new EditorWindow(@"C:\x.png", capture, new EditorSettings(), _ => { }, _ => { });
            Save((FrameworkElement)editor.Content, new Size(editor.Width, 220), "editor-top.png");
            editor.Close();

            SaveTyping();
            SaveTextMarks();

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
        editor.Close();
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
