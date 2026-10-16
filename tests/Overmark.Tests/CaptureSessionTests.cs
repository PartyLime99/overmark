using System.Windows.Input;
using Overmark.Capture;
using Overmark.Platform;

namespace Overmark.Tests;

public class CaptureSessionTests
{
    private static readonly MonitorInfo LeftMonitor = new("DISPLAY1", new ScreenRect(0, 0, 2560, 1440), new ScreenRect(0, 0, 2560, 1400), true, 120);
    private static readonly MonitorInfo RightMonitor = new("DISPLAY2", new ScreenRect(2560, 0, 2560, 1440), new ScreenRect(2560, 0, 2560, 1400), false, 120);
    private static readonly ScreenRect Desktop = new(0, 0, 5120, 1440);
    private static readonly WindowInfo Notepad = new(1, new ScreenRect(200, 200, 800, 600));

    private readonly List<PixelPoint> _cursorMoves = [];
    private CaptureOutcome? _outcome;
    private bool _finished;

    private CaptureSession Start(bool adjust = true, ScreenRect? lastRegion = null, PixelPoint? cursor = null, bool fromCentre = false)
    {
        var session = new CaptureSession(
            Desktop, [LeftMonitor, RightMonitor], [Notepad], lastRegion, adjust, cursor ?? new PixelPoint(50, 50), _cursorMoves.Add,
            fromCentre: fromCentre);
        session.Finished += outcome =>
        {
            _finished = true;
            _outcome = outcome;
        };
        return session;
    }

    private static void Drag(CaptureSession session, int x1, int y1, int x2, int y2)
    {
        session.PointerMoved(new PixelPoint(x1, y1));
        session.PointerPressed(new PixelPoint(x1, y1), 1, 6);
        session.PointerMoved(new PixelPoint((x1 + x2) / 2, (y1 + y2) / 2));
        session.PointerMoved(new PixelPoint(x2, y2));
        session.PointerReleased(new PixelPoint(x2, y2));
    }

    [Fact]
    public void Drag_then_enter_captures_the_dragged_region()
    {
        var session = Start();

        Drag(session, 10, 10, 409, 209);
        Assert.Equal(SessionMode.Adjusting, session.Mode);
        Assert.False(_finished);

        session.KeyPressed(Key.Enter, ModifierKeys.None);
        Assert.Equal(new CaptureOutcome(new ScreenRect(10, 10, 400, 200), CaptureKind.Region), _outcome);
    }

    [Fact]
    public void Without_adjusting_the_release_captures_straight_away()
    {
        var session = Start(adjust: false);

        Drag(session, 10, 10, 409, 209);

        Assert.Equal(new ScreenRect(10, 10, 400, 200), _outcome?.Region);
    }

    [Fact]
    public void Drag_can_cross_monitors()
    {
        var session = Start(adjust: false);

        Drag(session, 2500, 100, 2599, 199);

        Assert.Equal(new ScreenRect(2500, 100, 100, 100), _outcome?.Region);
    }

    [Fact]
    public void From_centre_the_press_is_the_middle_of_the_region()
    {
        var session = Start(adjust: false, fromCentre: true);

        Drag(session, 1000, 600, 1199, 699);

        Assert.Equal(new ScreenRect(801, 501, 399, 199), _outcome?.Region);
    }

    [Fact]
    public void The_centre_is_shown_only_while_dragging_from_it()
    {
        var corner = Start();
        corner.PointerPressed(new PixelPoint(1000, 600), 1, 6);
        corner.PointerMoved(new PixelPoint(1100, 650));
        Assert.Null(corner.DragCentre);

        var session = Start(fromCentre: true);
        session.PointerPressed(new PixelPoint(1000, 600), 1, 6);
        Assert.Null(session.DragCentre); // not a drag yet
        session.PointerMoved(new PixelPoint(1100, 650));
        Assert.Equal(new PixelPoint(1000, 600), session.DragCentre);
        Assert.Equal(new ScreenRect(900, 550, 201, 101), session.Selection);

        session.PointerReleased(new PixelPoint(1100, 650));
        Assert.Equal(SessionMode.Adjusting, session.Mode);
        Assert.Null(session.DragCentre);
    }

    [Fact]
    public void C_switches_to_centre_and_reshapes_a_drag_under_way()
    {
        var session = Start(adjust: false);
        session.PointerPressed(new PixelPoint(1000, 600), 1, 6);
        session.PointerMoved(new PixelPoint(1100, 650));
        Assert.Equal(new ScreenRect(1000, 600, 101, 51), session.Selection);

        Assert.True(session.KeyPressed(Key.C, ModifierKeys.None));
        Assert.True(session.FromCentre);
        Assert.Equal(new ScreenRect(900, 550, 201, 101), session.Selection);

        session.PointerReleased(new PixelPoint(1100, 650));
        Assert.Equal(new ScreenRect(900, 550, 201, 101), _outcome?.Region);
    }

    [Fact]
    public void Choosing_the_current_shape_again_changes_nothing()
    {
        var session = Start(fromCentre: true);
        var changes = 0;
        session.Changed += () => changes++;

        session.SetFromCentre(true);

        Assert.Equal(0, changes);
        Assert.True(session.FromCentre);
    }

    [Fact]
    public void Click_on_a_window_selects_it()
    {
        var session = Start();

        session.PointerMoved(new PixelPoint(500, 500));
        Assert.Equal(Notepad, session.HoveredWindow);
        session.PointerPressed(new PixelPoint(500, 500), 1, 6);
        session.PointerMoved(new PixelPoint(501, 501)); // a wobble, not a drag
        session.PointerReleased(new PixelPoint(501, 501));
        session.Confirm();

        Assert.Equal(new CaptureOutcome(Notepad.Bounds, CaptureKind.Window), _outcome);
    }

    [Fact]
    public void Click_on_empty_desktop_does_nothing()
    {
        var session = Start();

        session.PointerPressed(new PixelPoint(1500, 1300), 1, 6);
        session.PointerReleased(new PixelPoint(1500, 1300));

        Assert.Equal(SessionMode.Idle, session.Mode);
        Assert.False(_finished);
    }

    [Fact]
    public void Escape_cancels()
    {
        var session = Start();

        session.KeyPressed(Key.Escape, ModifierKeys.None);

        Assert.True(_finished);
        Assert.Null(_outcome);
    }

    [Fact]
    public void Right_click_drops_the_selection_then_cancels()
    {
        var session = Start();
        Drag(session, 10, 10, 409, 209);

        session.RightClicked();
        Assert.Equal(SessionMode.Idle, session.Mode);
        Assert.Null(session.Selection);
        Assert.False(_finished);

        session.RightClicked();
        Assert.True(_finished);
        Assert.Null(_outcome);
    }

    [Fact]
    public void Arrow_keys_move_the_pointer_before_a_selection()
    {
        var session = Start(cursor: new PixelPoint(100, 100));

        session.KeyPressed(Key.Right, ModifierKeys.None);
        session.KeyPressed(Key.Down, ModifierKeys.Shift);

        Assert.Equal([new PixelPoint(101, 100), new PixelPoint(101, 110)], _cursorMoves);
        Assert.Equal(new PixelPoint(101, 110), session.Cursor);
    }

    [Fact]
    public void Arrow_keys_move_and_resize_an_adjustable_selection()
    {
        var session = Start();
        Drag(session, 10, 10, 409, 209);

        session.KeyPressed(Key.Right, ModifierKeys.None);
        Assert.Equal(new ScreenRect(11, 10, 400, 200), session.Selection);

        session.KeyPressed(Key.Down, ModifierKeys.Control | ModifierKeys.Shift);
        Assert.Equal(new ScreenRect(11, 10, 400, 210), session.Selection);
    }

    [Fact]
    public void Dragging_a_handle_resizes_the_selection()
    {
        var session = Start();
        Drag(session, 10, 10, 409, 209); // columns 10..409

        session.PointerPressed(new PixelPoint(409, 100), 1, 6);
        Assert.Equal(SelectionHandle.Right, session.ActiveHandle);
        session.PointerMoved(new PixelPoint(459, 100));
        session.PointerReleased(new PixelPoint(459, 100));

        Assert.Equal(new ScreenRect(10, 10, 450, 200), session.Selection);
        Assert.Equal(SessionMode.Adjusting, session.Mode);
    }

    [Fact]
    public void Double_click_inside_the_selection_captures()
    {
        var session = Start();
        Drag(session, 10, 10, 409, 209);

        session.PointerPressed(new PixelPoint(200, 100), 2, 6);

        Assert.Equal(new ScreenRect(10, 10, 400, 200), _outcome?.Region);
    }

    [Fact]
    public void Last_region_is_clipped_to_the_desktop_and_captured_on_request()
    {
        var session = Start(lastRegion: new ScreenRect(5000, 1400, 500, 500));

        Assert.Equal(new ScreenRect(5000, 1400, 120, 40), session.LastRegion);
        session.KeyPressed(Key.L, ModifierKeys.None);

        Assert.Equal(new CaptureOutcome(new ScreenRect(5000, 1400, 120, 40), CaptureKind.LastRegion), _outcome);
    }

    [Fact]
    public void Last_region_does_nothing_when_there_is_none()
    {
        var session = Start();

        session.CaptureLastRegion();

        Assert.False(_finished);
    }

    [Fact]
    public void Full_screen_takes_the_monitor_under_the_pointer()
    {
        var session = Start(cursor: new PixelPoint(3000, 500));

        session.CaptureFullScreen();

        Assert.Equal(new CaptureOutcome(RightMonitor.Bounds, CaptureKind.FullScreen), _outcome);
    }

    [Fact]
    public void Hovering_the_last_region_button_previews_it()
    {
        var lastRegion = new ScreenRect(10, 10, 100, 100);
        var session = Start(lastRegion: lastRegion, cursor: new PixelPoint(500, 500));
        Assert.Equal(Notepad.Bounds, session.FocusRect);

        session.SetToolbarHovered(true);
        Assert.Null(session.FocusRect);
        Assert.False(session.ShowsCrosshair);

        session.SetLastRegionPreview(true);
        Assert.Equal(lastRegion, session.FocusRect);
    }

    [Fact]
    public void Crosshair_and_magnifier_can_be_turned_off_separately()
    {
        var session = new CaptureSession(
            Desktop, [LeftMonitor], [], null, false, new PixelPoint(10, 10), _ => { }, crosshair: false, magnifier: true);

        Assert.False(session.ShowsCrosshair);
        Assert.True(session.ShowsMagnifier);
    }

    [Fact]
    public void Record_mode_marks_the_outcome_as_a_recording()
    {
        var session = Start(adjust: false);

        session.KeyPressed(Key.R, ModifierKeys.None);
        Assert.True(session.RecordMode);
        Drag(session, 10, 10, 409, 209);

        Assert.Equal(new CaptureOutcome(new ScreenRect(10, 10, 400, 200), CaptureKind.Region, Record: true), _outcome);
    }

    [Fact]
    public void Screenshots_are_not_recordings()
    {
        var session = Start(adjust: false);

        Drag(session, 10, 10, 409, 209);

        Assert.False(_outcome!.Record);
    }

    [Fact]
    public void Nothing_happens_after_finishing()
    {
        var session = Start();
        session.Cancel();
        var changes = 0;
        session.Changed += () => changes++;

        session.PointerMoved(new PixelPoint(1, 1));
        session.KeyPressed(Key.Enter, ModifierKeys.None);

        Assert.Equal(0, changes);
    }
}
