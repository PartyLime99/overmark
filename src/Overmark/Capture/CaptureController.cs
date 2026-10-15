using System.Diagnostics;
using System.Windows.Media.Imaging;
using Overmark.Hotkeys;
using Overmark.Pinning;
using Overmark.Platform;
using Overmark.Settings;
using Overmark.Tray;

namespace Overmark.Capture;

/// <summary>Turns hotkeys and toolbar buttons into captures, and hands each finished capture to the clipboard and disk.</summary>
internal sealed class CaptureController(
    Func<AppSettings> settings,
    Action<ScreenRect> rememberRegion,
    Action<string> openEditor,
    Action openSettings,
    PinBoard pins,
    TrayIcon tray) : IDisposable
{
    // One overlay window per monitor, kept between captures (creating them is the slow part of opening the overlay).
    private readonly List<OverlayWindow> _pool = [];
    private readonly List<OverlayWindow> _active = [];
    private CaptureSession? _session;
    private CapturePopup? _popup;

    /// <summary>The file the most recent capture was saved to, if any.</summary>
    public string? LastSavedPath { get; private set; }

    public void Run(HotkeyAction action)
    {
        if (_session is { } session)
        {
            // Hotkeys pressed while the overlay is open act on it.
            switch (action)
            {
                case HotkeyAction.LastRegion:
                    session.CaptureLastRegion();
                    break;
                case HotkeyAction.FullScreen:
                    session.CaptureFullScreen();
                    break;
            }

            return;
        }

        try
        {
            switch (action)
            {
                case HotkeyAction.RegionCapture:
                    OpenOverlay();
                    break;
                case HotkeyAction.LastRegion:
                    CaptureLastRegionNow();
                    break;
                case HotkeyAction.FullScreen:
                    CaptureFullScreenNow();
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"{action.DisplayName()} failed", ex);
            CloseOverlay();
            tray.Notify("Capture failed", ex.Message);
        }
    }

    /// <summary>Creates the overlay windows ahead of the first capture.</summary>
    public void Prepare()
    {
        foreach (var window in WindowsFor(Displays.GetMonitors()))
        {
            window.Prepare();
        }
    }

    public void Dispose()
    {
        foreach (var window in _pool)
        {
            window.Close();
        }

        _pool.Clear();
    }

    private void OpenOverlay()
    {
        var timer = Stopwatch.StartNew();
        var monitors = Displays.GetMonitors();
        var desktop = Displays.VirtualScreen(monitors);
        var screenshot = ScreenCapture.Capture(desktop);
        var copyMs = timer.ElapsedMilliseconds;
        var windows = WindowFinder.FindWindows(desktop);
        var cursor = Pointer.Position;
        var current = settings();

        var session = new CaptureSession(
            desktop, monitors, windows, current.LastRegion?.ToScreenRect(), current.AdjustBeforeCapture, cursor, Pointer.MoveTo);
        session.Finished += outcome => OnSessionFinished(screenshot, outcome);
        _session = session;

        // The toolbar goes on the monitor the pointer is on.
        var toolbarMonitor = monitors.FirstOrDefault(m => m.Bounds.Contains(cursor)) ?? monitors[0];
        OverlayWindow? toolbarWindow = null;
        foreach (var window in WindowsFor(monitors))
        {
            var hasToolbar = window.Monitor == toolbarMonitor;
            window.Begin(session, screenshot.Crop(window.Monitor.Bounds), hasToolbar);
            _active.Add(window);
            if (hasToolbar)
            {
                toolbarWindow = window;
            }
        }

        var shownMs = timer.ElapsedMilliseconds;
        var focused = ForegroundWindow.Activate(toolbarWindow!);
        toolbarWindow!.Focus();

        void LogTiming()
        {
            toolbarWindow.Revealed -= LogTiming;
            Log.Info($"Overlay visible after {timer.ElapsedMilliseconds} ms (screen copy {copyMs} ms, windows shown at {shownMs} ms; " +
                     $"{windows.Count} windows to snap to; keyboard focus {(focused ? "taken" : "NOT taken")})");
        }

        toolbarWindow.Revealed += LogTiming;
    }

    /// <summary>The pooled windows for <paramref name="monitors"/>, creating or dropping windows when the monitor layout has changed.</summary>
    private List<OverlayWindow> WindowsFor(IReadOnlyList<MonitorInfo> monitors)
    {
        foreach (var stale in _pool.Where(w => !monitors.Contains(w.Monitor)).ToList())
        {
            stale.Close();
            _pool.Remove(stale);
        }

        foreach (var monitor in monitors.Where(m => _pool.All(w => w.Monitor != m)))
        {
            var window = new OverlayWindow(monitor);
            window.SettingsRequested += () =>
            {
                _session?.Cancel();
                openSettings();
            };
            _pool.Add(window);
        }

        return [.. monitors.Select(m => _pool.First(w => w.Monitor == m))];
    }

    private void OnSessionFinished(Screenshot screenshot, CaptureOutcome? outcome)
    {
        CloseOverlay();
        if (outcome is null)
        {
            Log.Info("Capture cancelled");
            return;
        }

        if (outcome.Kind is CaptureKind.Region or CaptureKind.Window)
        {
            rememberRegion(outcome.Region);
        }

        _ = DeliverAsync(screenshot.Crop(outcome.Region), outcome);
    }

    private void CloseOverlay()
    {
        _session = null;
        foreach (var window in _active)
        {
            window.End();
        }

        _active.Clear();
    }

    private void CaptureLastRegionNow()
    {
        var desktop = Displays.VirtualScreen(Displays.GetMonitors());
        if (settings().LastRegion?.ToScreenRect().Intersect(desktop) is not { } region)
        {
            // Nothing to repeat yet, so let the user pick a region.
            OpenOverlay();
            return;
        }

        _ = DeliverAsync(ScreenCapture.Capture(region).Image, new CaptureOutcome(region, CaptureKind.LastRegion));
    }

    private void CaptureFullScreenNow()
    {
        var monitors = Displays.GetMonitors();
        var cursor = Pointer.Position;
        var monitor = monitors.FirstOrDefault(m => m.Bounds.Contains(cursor)) ?? monitors[0];
        _ = DeliverAsync(ScreenCapture.Capture(monitor.Bounds).Image, new CaptureOutcome(monitor.Bounds, CaptureKind.FullScreen));
    }

    private async Task DeliverAsync(BitmapSource image, CaptureOutcome outcome)
    {
        try
        {
            var current = settings();
            var folder = string.IsNullOrWhiteSpace(current.SaveFolder)
                ? FileNamer.DefaultFolder
                : Environment.ExpandEnvironmentVariables(current.SaveFolder);
            var result = await CaptureOutput.DeliverAsync(image, folder, current.FileNamePattern);

            var size = $"{outcome.Region.Width} × {outcome.Region.Height}";
            Log.Info($"Captured {outcome.Kind} {outcome.Region}: copied {result.Copied}, saved to {result.SavedPath ?? "(not saved)"}" +
                     (result.Problem is null ? "" : $"; {result.Problem}"));

            if (result.SavedPath is { } saved)
            {
                LastSavedPath = saved;
            }

            if (current.PopupSeconds > 0 && (result.Copied || result.SavedPath is not null))
            {
                ShowPopup(image, outcome, result, TimeSpan.FromSeconds(current.PopupSeconds));
                return;
            }

            switch (result)
            {
                case { Copied: true, SavedPath: { } path }:
                    tray.Notify("Screenshot copied", $"{size}, saved as {Path.GetFileName(path)}. Click to edit.", () => openEditor(path));
                    break;
                case { SavedPath: { } path }:
                    tray.Notify("Screenshot saved", $"{size}, saved as {Path.GetFileName(path)}. {result.Problem} Click to edit.", () => openEditor(path));
                    break;
                case { Copied: true }:
                    tray.Notify("Screenshot copied, not saved", $"{size}. Couldn't save to {folder}: {result.Problem}");
                    break;
                default:
                    tray.Notify("Capture failed", result.Problem ?? "Unknown problem");
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't deliver capture", ex);
            tray.Notify("Capture failed", ex.Message);
        }
    }

    /// <summary>Shows the capture in the corner of the screen the user is on, replacing any earlier preview.</summary>
    private void ShowPopup(BitmapSource image, CaptureOutcome outcome, DeliveryResult result, TimeSpan showFor)
    {
        _popup?.Close();

        var heading = result switch
        {
            { Copied: true, SavedPath: not null } => "Screenshot copied",
            { SavedPath: not null } => "Screenshot saved",
            _ => "Screenshot copied, not saved",
        };
        var path = result.SavedPath;
        var details = $"{outcome.Region.Width} × {outcome.Region.Height}" + (path is null ? "" : $"  ·  {Path.GetFileName(path)}");
        if (result.Problem is { } problem)
        {
            details += Environment.NewLine + problem;
        }

        Action? edit = path is null ? null : () => openEditor(path);
        var monitors = Displays.GetMonitors();
        var pointer = Pointer.Position;
        var monitor = monitors.FirstOrDefault(m => m.Bounds.Contains(pointer)) ?? monitors[0];
        var pinAt = new PixelPoint(outcome.Region.Left, outcome.Region.Top);

        var popup = new CapturePopup(image, heading, details, path, monitor, showFor, edit, () => pins.Open(image, pinAt, edit));
        popup.Closed += (_, _) =>
        {
            if (_popup == popup)
            {
                _popup = null;
            }
        };
        _popup = popup;
        popup.Show();
    }
}
