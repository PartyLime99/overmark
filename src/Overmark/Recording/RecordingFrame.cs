using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Overmark.Platform;
using static Overmark.Platform.NativeMethods;

namespace Overmark.Recording;

/// <summary>
/// A red frame just outside the area being recorded. Clicks go straight through it, and it never appears in the
/// recording (or in any other capture).
/// </summary>
internal sealed class RecordingFrame : Window
{
    private const int Thickness = 2; // screen pixels

    private readonly ScreenRect _region;
    private readonly Border _border = new() { BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)) };

    public RecordingFrame(ScreenRect region)
    {
        _region = region;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Title = "Overmark recording frame";
        Content = _border;

        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLong(handle, GWL_EXSTYLE,
                GetWindowLong(handle, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            SetWindowDisplayAffinity(handle, WDA_EXCLUDEFROMCAPTURE);
            Place();
        };
        DpiChanged += (_, _) => Place();
    }

    private void Place()
    {
        var handle = new WindowInteropHelper(this).Handle;
        SetWindowPos(handle, HWND_TOPMOST, _region.Left - Thickness, _region.Top - Thickness,
            _region.Width + 2 * Thickness, _region.Height + 2 * Thickness, SWP_NOACTIVATE);
        _border.BorderThickness = new System.Windows.Thickness(Thickness / VisualTreeHelper.GetDpi(this).DpiScaleX);
    }
}
