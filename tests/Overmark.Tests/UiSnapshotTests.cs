using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Overmark.Capture;
using Overmark.Editor;
using Overmark.Pinning;
using Overmark.Platform;
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

            var popup = new CapturePopup(capture, "Screenshot copied", "1280 × 720  ·  2026-10-15_10-30-00.png", @"C:\x.png", monitor, TimeSpan.FromSeconds(5), () => { }, () => { });
            Save((FrameworkElement)popup.Content, new Size(400, 400), "popup.png");

            var editor = new EditorWindow(@"C:\x.png", capture, new EditorSettings(), _ => { }, _ => { });
            Save((FrameworkElement)editor.Content, new Size(editor.Width, 220), "editor-top.png");
            editor.Close();

            var pin = new PinWindow(SampleCapture(420, 240), null, null);
            Save((FrameworkElement)pin.Content, new Size(double.PositiveInfinity, double.PositiveInfinity), "pin.png");

            var narrowPin = new PinWindow(SampleCapture(90, 90), null, null);
            Save((FrameworkElement)narrowPin.Content, new Size(double.PositiveInfinity, double.PositiveInfinity), "pin-small.png");

            var settings = new SettingsWindow(new AppSettings(), new NoRecorder(), _ => { }, () => { });
            Save((FrameworkElement)settings.Content, new Size(settings.Width, double.PositiveInfinity), "settings.png");
        });
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
