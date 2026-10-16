using System.Windows.Input;
using Overmark.Platform;

namespace Overmark.Capture;

internal enum SessionMode
{
    /// <summary>Hovering: the window under the cursor is highlighted.</summary>
    Idle,

    /// <summary>Mouse button down, not moved far enough to be a drag yet. Releasing now snaps to the window.</summary>
    Pressing,

    /// <summary>Dragging out a new selection.</summary>
    Dragging,

    /// <summary>A selection is made and can be adjusted before Enter captures it.</summary>
    Adjusting,
}

internal enum CaptureKind
{
    Region,
    Window,
    LastRegion,
    FullScreen,
}

/// <summary>What to capture. <see cref="Record"/> means record a video of it rather than take a screenshot.</summary>
internal sealed record CaptureOutcome(ScreenRect Region, CaptureKind Kind, bool Record = false);

/// <summary>
/// The state of one capture overlay, shared by the overlay windows on every monitor. All positions are physical
/// desktop pixels. There's no UI in here, so it can be unit tested.
/// </summary>
internal sealed class CaptureSession
{
    // Movement (in pixels) that turns a press into a drag; anything less is a click.
    private const int DragThreshold = 3;

    private readonly IReadOnlyList<MonitorInfo> _monitors;
    private readonly IReadOnlyList<WindowInfo> _windows;
    private readonly bool _adjustBeforeCapture;
    private readonly bool _crosshair;
    private readonly bool _magnifier;
    private readonly bool _fromCentre;
    private readonly Action<PixelPoint> _moveCursor;
    private PixelPoint _pressPoint;
    private ScreenRect _dragAnchor;
    private CaptureKind _selectionKind;

    public CaptureSession(
        ScreenRect desktop,
        IReadOnlyList<MonitorInfo> monitors,
        IReadOnlyList<WindowInfo> windows,
        ScreenRect? lastRegion,
        bool adjustBeforeCapture,
        PixelPoint cursor,
        Action<PixelPoint> moveCursor,
        bool crosshair = true,
        bool magnifier = true,
        bool recordMode = false,
        bool fromCentre = false)
    {
        RecordMode = recordMode;
        Desktop = desktop;
        _monitors = monitors;
        _windows = windows;
        _adjustBeforeCapture = adjustBeforeCapture;
        _crosshair = crosshair;
        _magnifier = magnifier;
        _fromCentre = fromCentre;
        _moveCursor = moveCursor;
        LastRegion = lastRegion?.Intersect(desktop);
        Cursor = Clamp(cursor);
        UpdateHoveredWindow();
    }

    public event Action? Changed;

    /// <summary>Raised once when the session ends: with the region to capture, or null if cancelled.</summary>
    public event Action<CaptureOutcome?>? Finished;

    public ScreenRect Desktop { get; }

    /// <summary>The previous region capture, clipped to the current desktop; null if there's none.</summary>
    public ScreenRect? LastRegion { get; }

    public SessionMode Mode { get; private set; }

    public PixelPoint Cursor { get; private set; }

    public ScreenRect? Selection { get; private set; }

    public WindowInfo? HoveredWindow { get; private set; }

    /// <summary>The handle being dragged while adjusting, if any.</summary>
    public SelectionHandle ActiveHandle { get; private set; }

    public bool ToolbarHovered { get; private set; }

    public bool PreviewingLastRegion { get; private set; }

    public bool IsFinished { get; private set; }

    /// <summary>True when the area picked will be recorded as a video instead of captured as a screenshot.</summary>
    public bool RecordMode { get; private set; }

    /// <summary>The area left undimmed: the selection, the last-region preview, or the window under the cursor.</summary>
    public ScreenRect? FocusRect => Mode switch
    {
        SessionMode.Dragging or SessionMode.Adjusting => Selection,
        _ when PreviewingLastRegion => LastRegion,
        _ when ToolbarHovered => null,
        _ => HoveredWindow?.Bounds,
    };

    /// <summary>Where a centre-out drag started, while one is being dragged; null otherwise.</summary>
    public PixelPoint? DragCentre => _fromCentre && Mode == SessionMode.Dragging ? _pressPoint : null;

    public bool ShowsCrosshair => _crosshair && IsAiming;

    public bool ShowsMagnifier =>
        _magnifier && (IsAiming || (Mode == SessionMode.Adjusting && ActiveHandle is not (SelectionHandle.None or SelectionHandle.Move)));

    // Picking a point: before or during a drag, and not over the toolbar.
    private bool IsAiming => !ToolbarHovered && Mode is SessionMode.Idle or SessionMode.Pressing or SessionMode.Dragging;

    public SelectionHandle HandleAt(PixelPoint point, int tolerance) =>
        Mode == SessionMode.Adjusting && Selection is { } selection
            ? SelectionMath.HitTest(selection, point, tolerance)
            : SelectionHandle.None;

    public void PointerMoved(PixelPoint point)
    {
        if (IsFinished)
        {
            return;
        }

        Cursor = Clamp(point);
        switch (Mode)
        {
            case SessionMode.Idle:
                UpdateHoveredWindow();
                break;
            case SessionMode.Pressing when MovedPastThreshold():
                Mode = SessionMode.Dragging;
                Selection = DraggedRegion();
                break;
            case SessionMode.Dragging:
                Selection = DraggedRegion();
                break;
            case SessionMode.Adjusting when ActiveHandle != SelectionHandle.None:
                Selection = SelectionMath.Drag(_dragAnchor, ActiveHandle, Cursor.X - _pressPoint.X, Cursor.Y - _pressPoint.Y, Desktop);
                break;
        }

        Changed?.Invoke();
    }

    public void PointerPressed(PixelPoint point, int clickCount, int handleTolerance)
    {
        if (IsFinished)
        {
            return;
        }

        Cursor = Clamp(point);
        ToolbarHovered = false;
        PreviewingLastRegion = false;

        if (Mode == SessionMode.Adjusting && Selection is { } selection)
        {
            if (clickCount >= 2 && selection.Contains(Cursor))
            {
                Confirm();
                return;
            }

            var handle = SelectionMath.HitTest(selection, Cursor, handleTolerance);
            if (handle != SelectionHandle.None)
            {
                ActiveHandle = handle;
                _dragAnchor = selection;
                _pressPoint = Cursor;
                Changed?.Invoke();
                return;
            }

            // Pressing outside the selection starts again.
            Selection = null;
        }

        Mode = SessionMode.Pressing;
        _pressPoint = Cursor;
        UpdateHoveredWindow();
        Changed?.Invoke();
    }

    public void PointerReleased(PixelPoint point)
    {
        if (IsFinished)
        {
            return;
        }

        Cursor = Clamp(point);
        switch (Mode)
        {
            case SessionMode.Pressing when MovedPastThreshold():
            case SessionMode.Dragging:
                Select(DraggedRegion(), CaptureKind.Region);
                break;
            case SessionMode.Pressing when HoveredWindow is { } window:
                Select(window.Bounds, CaptureKind.Window);
                break;
            case SessionMode.Pressing:
                Mode = SessionMode.Idle;
                UpdateHoveredWindow();
                break;
            case SessionMode.Adjusting:
                ActiveHandle = SelectionHandle.None;
                break;
        }

        if (!IsFinished)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Right-click: drops the selection, or cancels if there isn't one.</summary>
    public void RightClicked()
    {
        if (IsFinished)
        {
            return;
        }

        if (Mode == SessionMode.Idle)
        {
            Cancel();
            return;
        }

        Mode = SessionMode.Idle;
        Selection = null;
        ActiveHandle = SelectionHandle.None;
        UpdateHoveredWindow();
        Changed?.Invoke();
    }

    /// <summary>Handles a key press. Returns false for keys the overlay doesn't use.</summary>
    public bool KeyPressed(Key key, ModifierKeys modifiers)
    {
        if (IsFinished)
        {
            return false;
        }

        var step = modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        switch (key)
        {
            case Key.Escape:
                Cancel();
                return true;
            case Key.Enter when Mode == SessionMode.Adjusting:
                Confirm();
                return true;
            case Key.L:
                CaptureLastRegion();
                return true;
            case Key.F:
                CaptureFullScreen();
                return true;
            case Key.R:
                SetRecordMode(!RecordMode);
                return true;
            case Key.Left:
                Nudge(-step, 0, modifiers);
                return true;
            case Key.Right:
                Nudge(step, 0, modifiers);
                return true;
            case Key.Up:
                Nudge(0, -step, modifiers);
                return true;
            case Key.Down:
                Nudge(0, step, modifiers);
                return true;
            default:
                return false;
        }
    }

    public void SetRecordMode(bool on)
    {
        if (IsFinished || RecordMode == on)
        {
            return;
        }

        RecordMode = on;
        Changed?.Invoke();
    }

    public void SetToolbarHovered(bool hovered)
    {
        if (IsFinished || ToolbarHovered == hovered)
        {
            return;
        }

        ToolbarHovered = hovered;
        Changed?.Invoke();
    }

    public void SetLastRegionPreview(bool previewing)
    {
        if (IsFinished || PreviewingLastRegion == previewing)
        {
            return;
        }

        PreviewingLastRegion = previewing;
        Changed?.Invoke();
    }

    public void Confirm()
    {
        if (Mode == SessionMode.Adjusting && Selection is { } selection)
        {
            Finish(new CaptureOutcome(selection, _selectionKind));
        }
    }

    public void CaptureLastRegion()
    {
        if (LastRegion is { } region)
        {
            Finish(new CaptureOutcome(region, CaptureKind.LastRegion));
        }
    }

    /// <summary>Captures the whole monitor the cursor is on.</summary>
    public void CaptureFullScreen()
    {
        var monitor = _monitors.FirstOrDefault(m => m.Bounds.Contains(Cursor)) ?? _monitors.FirstOrDefault();
        if (monitor?.Bounds.Intersect(Desktop) is { } bounds)
        {
            Finish(new CaptureOutcome(bounds, CaptureKind.FullScreen));
        }
    }

    public void Cancel() => Finish(null);

    private void Nudge(int dx, int dy, ModifierKeys modifiers)
    {
        if (Mode == SessionMode.Adjusting && Selection is { } selection)
        {
            if (ActiveHandle == SelectionHandle.None)
            {
                Selection = modifiers.HasFlag(ModifierKeys.Control)
                    ? SelectionMath.ResizeBy(selection, dx, dy, Desktop)
                    : SelectionMath.MoveBy(selection, dx, dy, Desktop);
                Changed?.Invoke();
            }

            return;
        }

        // Before and during a drag the arrow keys move the pointer itself, for pixel-exact corners.
        var target = Clamp(new PixelPoint(Cursor.X + dx, Cursor.Y + dy));
        _moveCursor(target);
        PointerMoved(target);
    }

    private void Select(ScreenRect region, CaptureKind kind)
    {
        _selectionKind = kind;
        if (!_adjustBeforeCapture)
        {
            Finish(new CaptureOutcome(region, kind));
            return;
        }

        Selection = region;
        Mode = SessionMode.Adjusting;
        ActiveHandle = SelectionHandle.None;
    }

    private void Finish(CaptureOutcome? outcome)
    {
        if (IsFinished)
        {
            return;
        }

        IsFinished = true;
        Finished?.Invoke(outcome is null ? null : outcome with { Record = RecordMode });
    }

    // From the press to the pointer: corner to corner, or out from the press as the centre.
    private ScreenRect DraggedRegion() =>
        _fromCentre ? SelectionMath.FromCentre(_pressPoint, Cursor, Desktop) : ScreenRect.FromCorners(_pressPoint, Cursor);

    private bool MovedPastThreshold() =>
        Math.Abs(Cursor.X - _pressPoint.X) >= DragThreshold || Math.Abs(Cursor.Y - _pressPoint.Y) >= DragThreshold;

    private void UpdateHoveredWindow() => HoveredWindow = _windows.FirstOrDefault(w => w.Bounds.Contains(Cursor));

    private PixelPoint Clamp(PixelPoint point) => new(
        Math.Clamp(point.X, Desktop.Left, Desktop.Right - 1),
        Math.Clamp(point.Y, Desktop.Top, Desktop.Bottom - 1));
}
