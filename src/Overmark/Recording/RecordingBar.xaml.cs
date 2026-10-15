using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Overmark.Platform;
using static Overmark.Platform.NativeMethods;

namespace Overmark.Recording;

/// <summary>
/// The small bar shown during a recording: elapsed time, Pause/Resume, Stop and Discard. It never takes focus
/// and never appears in the recording (or in any other capture).
/// </summary>
internal partial class RecordingBar : Window
{
    private readonly ScreenRect _region;
    private readonly MonitorInfo _monitor;

    public RecordingBar(ScreenRect region, MonitorInfo monitor, string stopHotkey)
    {
        InitializeComponent();
        _region = region;
        _monitor = monitor;
        StopButton.ToolTip = string.IsNullOrEmpty(stopHotkey) ? "Stop and save" : $"Stop and save ({stopHotkey})";

        PauseButton.Click += (_, _) => PauseClicked?.Invoke();
        StopButton.Click += (_, _) => StopClicked?.Invoke();
        DiscardButton.Click += (_, _) => DiscardClicked?.Invoke();

        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLong(handle, GWL_EXSTYLE, GetWindowLong(handle, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            SetWindowDisplayAffinity(handle, WDA_EXCLUDEFROMCAPTURE);
        };
        Loaded += (_, _) =>
        {
            Place();
            Opacity = 1;
        };
    }

    public event Action? PauseClicked;

    public event Action? StopClicked;

    public event Action? DiscardClicked;

    public void Show(TimeSpan elapsed, bool paused)
    {
        TimeText.Text = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";

        // Segoe MDL2 Assets: Play when paused, Pause while recording.
        PauseGlyph.Text = ((char)(paused ? 0xE768 : 0xE769)).ToString();
        PauseButton.ToolTip = paused ? "Resume" : "Pause";

        // A solid dot while recording, hollow while paused.
        Dot.Fill = paused ? Brushes.Transparent : Dot.Stroke;
    }

    /// <summary>Centred just below the recorded area, or above it, or inside its bottom edge if there's no room outside.</summary>
    private void Place()
    {
        var handle = new WindowInteropHelper(this).Handle;
        GetWindowRect(handle, out var rect);
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        var work = _monitor.WorkArea;

        var x = Math.Clamp(_region.Left + (_region.Width - width) / 2, work.Left, Math.Max(work.Left, work.Right - width));
        int y;
        if (_region.Bottom + height <= work.Bottom)
        {
            y = _region.Bottom;
        }
        else if (_region.Top - height >= work.Top)
        {
            y = _region.Top - height;
        }
        else
        {
            y = Math.Min(_region.Bottom, work.Bottom) - height;
        }

        SetWindowPos(handle, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }
}
