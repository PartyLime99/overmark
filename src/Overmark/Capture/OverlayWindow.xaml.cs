using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Overmark.Platform;
using static Overmark.Platform.NativeMethods;

namespace Overmark.Capture;

/// <summary>
/// Covers one monitor during a capture. There's one per monitor (rather than one spanning them all) so each
/// gets its own monitor's scaling and the frozen image stays pixel-exact on mixed-scaling setups.
/// Windows are kept and reused between captures, because creating them is most of the time it takes the
/// overlay to appear.
/// </summary>
internal partial class OverlayWindow : Window
{
    private const double HandleToleranceDips = 6;
    private const double ToolbarGapDips = 22;

    private static readonly Brush RecordDotBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)));

    private CaptureSession? _session;
    private bool _hasToolbar;
    private int _framesUntilReveal;

    public OverlayWindow(MonitorInfo monitor)
    {
        InitializeComponent();
        Monitor = monitor;

        SourceInitialized += (_, _) => OnSourceInitialized();
        DpiChanged += (_, _) => PlaceOnMonitor();

        MouseMove += (_, _) => _session?.PointerMoved(Pointer.Position);
        Overlay.MouseLeftButtonDown += OnLeftButtonDown;
        Overlay.MouseLeftButtonUp += OnLeftButtonUp;
        Overlay.MouseRightButtonUp += (_, e) =>
        {
            _session?.RightClicked();
            e.Handled = true;
        };
        PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            e.Handled = _session?.KeyPressed(key, Keyboard.Modifiers) ?? false;
        };

        Toolbar.MouseEnter += (_, _) => _session?.SetToolbarHovered(true);
        Toolbar.MouseLeave += (_, _) => _session?.SetToolbarHovered(false);
        LastRegionButton.MouseEnter += (_, _) => _session?.SetLastRegionPreview(true);
        LastRegionButton.MouseLeave += (_, _) => _session?.SetLastRegionPreview(false);
        LastRegionButton.Click += (_, _) => _session?.CaptureLastRegion();
        FullScreenButton.Click += (_, _) => _session?.CaptureFullScreen();
        CancelButton.Click += (_, _) => _session?.Cancel();
        SettingsButton.Click += (_, _) => SettingsRequested?.Invoke();
        ScreenshotButton.Click += (_, _) => Choose(session => session.SetRecordMode(false));
        RecordButton.Click += (_, _) => Choose(session => session.SetRecordMode(true));
        CornerButton.Click += (_, _) => Choose(session => session.SetFromCentre(false));
        CentreButton.Click += (_, _) => Choose(session => session.SetFromCentre(true));
    }

    /// <summary>Raised once the first frame of a capture is on screen.</summary>
    public event Action? Revealed;

    /// <summary>Raised when the toolbar's Settings button is clicked.</summary>
    public event Action? SettingsRequested;

    public MonitorInfo Monitor { get; }

    private int HandleTolerance => (int)Math.Round(HandleToleranceDips * VisualTreeHelper.GetDpi(this).DpiScaleX);

    private IntPtr Handle => new WindowInteropHelper(this).Handle;

    /// <summary>Creates the native window ahead of time, so the first capture doesn't wait for it.</summary>
    public void Prepare() => new WindowInteropHelper(this).EnsureHandle();

    /// <summary>Shows <paramref name="monitorImage"/> (this monitor's part of the screenshot) with the session's overlay on top.</summary>
    public void Begin(CaptureSession session, BitmapSource monitorImage, bool hasToolbar)
    {
        _session = session;
        _hasToolbar = hasToolbar;
        FrozenImage.Source = monitorImage;
        Overlay.Attach(session, monitorImage, Monitor);
        LastRegionButton.IsEnabled = session.LastRegion is not null;
        LastRegionButton.ToolTip = LastRegionButton.IsEnabled ? "Capture the last region again (L)" : "No previous region yet";
        session.Changed += OnSessionChanged;
        OnSessionChanged();

        // Stay cloaked until the new frame has been drawn, so the previous capture never flashes up.
        Prepare();
        SetCloaked(true);
        Show();
        PlaceOnMonitor();
        _framesUntilReveal = 2;
        CompositionTarget.Rendering += OnRendering;
    }

    public void End()
    {
        CompositionTarget.Rendering -= OnRendering;
        if (_session is not null)
        {
            _session.Changed -= OnSessionChanged;
            _session = null;
        }

        Overlay.ReleaseMouseCapture();
        SetCloaked(true);
        Hide();
        FrozenImage.Source = null;
        Overlay.Detach();
    }

    // Rendering fires just before each frame, so by the second call the first frame has been drawn.
    private void OnRendering(object? sender, EventArgs e)
    {
        if (--_framesUntilReveal > 0)
        {
            return;
        }

        CompositionTarget.Rendering -= OnRendering;
        SetCloaked(false);
        Revealed?.Invoke();
    }

    private void OnSourceInitialized()
    {
        // A tool window stays out of Alt+Tab.
        SetWindowLong(Handle, GWL_EXSTYLE, GetWindowLong(Handle, GWL_EXSTYLE) | WS_EX_TOOLWINDOW);
        SetCloaked(true);
        PlaceOnMonitor();
    }

    /// <summary>Covers the monitor exactly, in physical pixels, above other always-on-top windows.</summary>
    private void PlaceOnMonitor()
    {
        var bounds = Monitor.Bounds;
        SetWindowPos(Handle, HWND_TOPMOST, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SWP_NOACTIVATE);
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        FrozenImage.Width = bounds.Width / scale;
        FrozenImage.Height = bounds.Height / scale;

        // The toolbar sits just above the taskbar (or the screen's bottom edge, if the taskbar is elsewhere).
        var taskbar = Math.Max(0, bounds.Bottom - Monitor.WorkArea.Bottom);
        Toolbar.Margin = new Thickness(0, 0, 0, taskbar / scale + ToolbarGapDips);
    }

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private void SetCloaked(bool cloaked)
    {
        var value = cloaked ? 1 : 0;
        DwmSetWindowAttribute(Handle, DWMWA_CLOAK, ref value, sizeof(int));
    }

    private void OnLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_session is null)
        {
            return;
        }

        // Capturing the mouse keeps a drag going when it crosses onto another monitor's window.
        Overlay.CaptureMouse();
        _session.PointerPressed(Pointer.Position, e.ClickCount, HandleTolerance);
        e.Handled = true;
    }

    private void OnLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _session?.PointerReleased(Pointer.Position);
        Overlay.ReleaseMouseCapture();
        e.Handled = true;
    }

    // A click toggles the button itself, so the buttons are set from the session again afterwards: clicking the
    // chosen one leaves it chosen.
    private void Choose(Action<CaptureSession> choice)
    {
        if (_session is { } session)
        {
            choice(session);
            OnSessionChanged();
        }
    }

    private void OnSessionChanged()
    {
        if (_session is not { IsFinished: false } session)
        {
            return;
        }

        Overlay.InvalidateVisual();
        Toolbar.Visibility = _hasToolbar && session.Mode == SessionMode.Idle ? Visibility.Visible : Visibility.Collapsed;
        ScreenshotButton.IsChecked = !session.RecordMode;
        RecordButton.IsChecked = session.RecordMode;
        CornerButton.IsChecked = !session.FromCentre;
        CentreButton.IsChecked = session.FromCentre;
        RecordDot.Fill = session.RecordMode ? Brushes.White : RecordDotBrush; // white on the red button once chosen

        var handle = session.ActiveHandle != SelectionHandle.None
            ? session.ActiveHandle
            : session.HandleAt(session.Cursor, HandleTolerance);
        Cursor = handle switch
        {
            SelectionHandle.Move => Cursors.SizeAll,
            SelectionHandle.Left or SelectionHandle.Right => Cursors.SizeWE,
            SelectionHandle.Top or SelectionHandle.Bottom => Cursors.SizeNS,
            SelectionHandle.TopLeft or SelectionHandle.BottomRight => Cursors.SizeNWSE,
            SelectionHandle.TopRight or SelectionHandle.BottomLeft => Cursors.SizeNESW,
            _ => Cursors.Cross,
        };
    }
}
