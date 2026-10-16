using System.Collections.Specialized;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Overmark.Capture;
using Overmark.Hotkeys;
using Overmark.Platform;
using Overmark.Settings;
using Overmark.Tray;

namespace Overmark.Recording;

/// <summary>
/// Runs one screen recording at a time: the red frame and control bar while it records, then saving, copying the
/// file to the clipboard (so Ctrl+V drops it into a chat or email) and showing the preview.
/// </summary>
internal sealed class RecordingController(Func<AppSettings> settings, Func<Hotkey?> stopHotkey, CaptureHistory history, TrayIcon tray)
{
    private ScreenRecorder? _recorder;
    private RecordingFrame? _frame;
    private RecordingBar? _bar;
    private DispatcherTimer? _ticker;
    private CapturePopup? _popup;
    private string? _path;
    private ScreenRect _region;

    public bool IsRecording => _recorder is not null;

    public async void Start(ScreenRect region)
    {
        if (IsRecording)
        {
            return;
        }

        var current = settings();

        // H.264 needs even dimensions, so trim a pixel off an odd edge.
        _region = region with { Width = Math.Max(2, region.Width / 2 * 2), Height = Math.Max(2, region.Height / 2 * 2) };
        try
        {
            var folder = FileNamer.FolderFor(current);
            Directory.CreateDirectory(folder);
            _path = FileNamer.NextPath(folder, current.FileNamePattern, DateTime.Now, File.Exists, ".mp4");
            var frameRate = current.RecordFrameRate is 15 or 30 or 60 ? current.RecordFrameRate : 30;

            // Give the capture overlay a moment to leave the screen, so it isn't in the first frame.
            await Task.Delay(100);
            _recorder = await ScreenRecorder.StartAsync(new ScreenFrameSource(_region, current.RecordCursor), _path, frameRate);
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't start recording", ex);
            tray.Notify("Couldn't start recording", ex.Message);
            _recorder = null;
            return;
        }

        var monitors = Displays.GetMonitors();
        var monitor = monitors.FirstOrDefault(m => m.Bounds.Intersect(_region) is not null) ?? monitors[0];
        _frame = new RecordingFrame(_region);
        _frame.Show();
        _bar = new RecordingBar(_region, monitor, stopHotkey()?.DisplayText ?? "");
        _bar.PauseClicked += TogglePause;
        _bar.StopClicked += () => _ = StopAsync(keep: true);
        _bar.DiscardClicked += () => _ = StopAsync(keep: false);
        _bar.Show();

        _ticker = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _ticker.Tick += (_, _) => _bar?.Show(_recorder?.Elapsed ?? TimeSpan.Zero, _recorder?.IsPaused ?? false);
        _ticker.Start();
        tray.ShowRecording(true);
        Log.Info($"Recording {_region} to {_path}");
    }

    public void TogglePause()
    {
        if (_recorder is not { } recorder)
        {
            return;
        }

        if (recorder.IsPaused)
        {
            recorder.Resume();
        }
        else
        {
            recorder.Pause();
        }

        _bar?.Show(recorder.Elapsed, recorder.IsPaused);
    }

    /// <summary>Stops recording; keeps the video (copied and previewed) or throws it away.</summary>
    public async Task StopAsync(bool keep)
    {
        if (_recorder is not { } recorder || _path is not { } path)
        {
            return;
        }

        _recorder = null;
        tray.ShowRecording(false);
        _ticker?.Stop();
        _frame?.Close();
        _bar?.Close();
        _frame = null;
        _bar = null;

        TimeSpan length;
        try
        {
            length = await recorder.StopAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Recording failed", ex);
            tray.Notify("Recording failed", ex.Message);
            return;
        }

        if (!keep)
        {
            TryDelete(path);
            Log.Info("Recording discarded");
            return;
        }

        var duration = $"{(int)length.TotalMinutes}:{length.Seconds:00}";
        Log.Info($"Recorded {duration} to {path}");
        history.Add(new CaptureRecord(path, CaptureKind.Region, DateTime.Now, _region.Width, _region.Height, length.TotalSeconds));
        var copied = TryCopyFile(path);
        uint? clipboardVersion = copied ? NativeMethods.GetClipboardSequenceNumber() : null;
        var current = settings();
        var details = $"{duration}  ·  {_region.Width} × {_region.Height}  ·  {Path.GetFileName(path)}";
        if (current.PopupSeconds <= 0)
        {
            tray.Notify(copied ? "Recording copied" : "Recording saved", $"{details}. Click to play.", () => Play(path));
            return;
        }

        ShowPopup(recorder.FirstFrame ?? BlankFrame(), copied ? "Recording copied and saved" : "Recording saved", details, path, clipboardVersion,
            TimeSpan.FromSeconds(current.PopupSeconds));
    }

    private void ShowPopup(BitmapSource preview, string heading, string details, string path, uint? clipboardVersion, TimeSpan showFor)
    {
        _popup?.Close();
        var monitors = Displays.GetMonitors();
        var pointer = Pointer.Position;
        var monitor = monitors.FirstOrDefault(m => m.Bounds.Contains(pointer)) ?? monitors[0];
        var popup = new CapturePopup(preview, heading, details, path, monitor, showFor, () => Play(path), pin: null,
            delete: () => Delete(path, clipboardVersion), video: true);
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

    /// <summary>Puts the video file on the clipboard, so Ctrl+V pastes it into Teams, Outlook or a folder.</summary>
    private static bool TryCopyFile(string path)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                Clipboard.SetFileDropList(new StringCollection { path });
                return true;
            }
            catch (ExternalException) when (attempt < 5)
            {
                Thread.Sleep(50);
            }
            catch (ExternalException ex)
            {
                Log.Warn($"Couldn't copy the recording to the clipboard: {ex.Message}");
            }
        }

        return false;
    }

    /// <summary>Opens a recording in the default video player.</summary>
    public static void Play(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warn($"Couldn't open {path}: {ex.Message}");
        }
    }

    /// <summary>The preview's delete button: the video goes to the Recycle Bin and off the clipboard.</summary>
    private void Delete(string path, uint? clipboardVersion)
    {
        if (CaptureOutput.TryDiscard(path, clipboardVersion, out var problem))
        {
            Log.Info($"Deleted {path}");
            return;
        }

        Log.Warn($"Couldn't delete {path}: {problem}");
        tray.Notify("Couldn't delete the recording", $"{Path.GetFileName(path)}: {problem}");
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Couldn't delete discarded recording {path}: {ex.Message}");
        }
    }

    private static BitmapSource BlankFrame()
    {
        var blank = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgr32, null, new byte[16], 8);
        blank.Freeze();
        return blank;
    }
}
