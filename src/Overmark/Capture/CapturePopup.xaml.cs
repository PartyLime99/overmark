using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Overmark.Platform;
using static Overmark.Platform.NativeMethods;

namespace Overmark.Capture;

/// <summary>
/// The preview that pops up in the corner after a capture. Click it to edit, drag it into another app, pin it, or
/// delete it if it was a mistake. It never takes focus away from what the user is doing, and stays up while the mouse
/// is over it.
/// </summary>
internal partial class CapturePopup : Window
{
    private readonly MonitorInfo _monitor;
    private readonly DispatcherTimer _closeTimer;
    private bool _closing;

    /// <param name="path">The saved file, or null if it couldn't be saved (then it can't be edited or dragged).</param>
    /// <param name="edit">What a click does: open the editor, or play a video.</param>
    /// <param name="pin">Pins the image; null hides the pin button (e.g. for a video).</param>
    /// <param name="delete">Throws the capture away; null hides the delete button (e.g. when nothing was saved).</param>
    /// <param name="video">True for a recording: the first action reads Play rather than Edit.</param>
    public CapturePopup(
        BitmapSource image, string heading, string details, string? path, MonitorInfo monitor, TimeSpan showFor,
        Action? edit, Action? pin, Action? delete, bool video = false)
    {
        InitializeComponent();
        if (video)
        {
            EditLabel.Text = "Play";
            EditIcon.Data = Geometry.Parse("M5,3 L15,9 L5,15 Z");
        }

        if (edit is null)
        {
            EditButton.Visibility = Visibility.Collapsed;
        }

        if (pin is null)
        {
            PinButton.Visibility = Visibility.Collapsed;
        }

        if (delete is null)
        {
            DeleteButton.Visibility = Visibility.Collapsed;
        }

        _monitor = monitor;
        Preview.Source = image;
        Heading.Text = heading;
        Details.Text = details;

        _closeTimer = new DispatcherTimer { Interval = showFor };
        _closeTimer.Tick += (_, _) => FadeOut();
        MouseEnter += (_, _) => _closeTimer.Stop();
        MouseLeave += (_, _) => _closeTimer.Start();
        PinButton.Click += (_, _) =>
        {
            pin?.Invoke();
            FadeOut();
        };
        DeleteButton.Click += (_, _) =>
        {
            FadeOut();
            delete?.Invoke();
        };
        CloseButton.Click += (_, _) => FadeOut();
        EditButton.Click += (_, _) =>
        {
            edit?.Invoke();
            FadeOut();
        };

        // Rounds the picture's corners to match its frame, whatever shape the capture is.
        Preview.SizeChanged += (_, _) => PreviewClip.Rect = new Rect(0, 0, Preview.ActualWidth, Preview.ActualHeight);

        if (path is null)
        {
            PreviewFrame.Cursor = null;
            PreviewFrame.ToolTip = null;
        }
        else
        {
            DragOut.AttachGesture(PreviewFrame, () => DragOut.DragFile(PreviewFrame, path), () =>
            {
                edit?.Invoke();
                FadeOut();
            });
        }

        SourceInitialized += (_, _) =>
        {
            // A tool window that never activates: clicking it doesn't take focus from the app the user is in.
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLong(handle, GWL_EXSTYLE, GetWindowLong(handle, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };
        Loaded += (_, _) =>
        {
            PlaceInCorner();
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
            _closeTimer.Start();
        };

        // Moving to a monitor with different scaling resizes the window, so line it up again.
        DpiChanged += (_, _) => Dispatcher.InvokeAsync(PlaceInCorner, DispatcherPriority.Loaded);
    }

    public void FadeOut()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        _closeTimer.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Bottom-right of the monitor's work area (above the taskbar), in physical pixels.</summary>
    private void PlaceInCorner()
    {
        var handle = new WindowInteropHelper(this).Handle;
        GetWindowRect(handle, out var rect);
        var work = _monitor.WorkArea;
        SetWindowPos(handle, HWND_TOPMOST, work.Right - (rect.Right - rect.Left), work.Bottom - (rect.Bottom - rect.Top), 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }
}
