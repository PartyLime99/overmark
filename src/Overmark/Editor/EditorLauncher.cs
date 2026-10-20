using System.Windows;
using System.Windows.Media.Imaging;
using Overmark.Capture;
using Overmark.Hotkeys;
using Overmark.Pinning;
using Overmark.Platform;
using Overmark.Settings;
using Overmark.Tray;

namespace Overmark.Editor;

/// <summary>Opens captures in the editor, in the editor window that's already open if there is one.</summary>
internal sealed class EditorLauncher(
    Func<AppSettings> settings, Action<EditorSettings> rememberSettings, Action openSettings, PinBoard pins, CaptureHistory history, TrayIcon tray)
{
    private readonly Dictionary<string, EditorWindow> _open = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every editor window, including one with no capture open (which isn't in <see cref="_open"/>).</summary>
    private readonly List<EditorWindow> _windows = [];
    private readonly EditStore _edits = CreateEditStore();

    /// <summary>The editor window used last, which captures open in.</summary>
    private EditorWindow? _latest;

    /// <summary>
    /// Opens a capture (from the preview, a pin, the tray or the command line). An editor that's already open shows
    /// it in place of its capture and comes to the front, as when it's picked from the list of today's captures,
    /// rather than a second window opening. Only if that editor is waiting on a question (such as whether to delete
    /// its capture) does the capture get a window of its own, so the answer can't land on the wrong capture.
    /// </summary>
    public void Open(string path)
    {
        if (TryActivate(path))
        {
            return;
        }

        if (_latest is { IsShowingDialog: false } current)
        {
            current.KeepDraft();
            if (ShowIn(current, path))
            {
                ForegroundWindow.Activate(current);
            }

            return;
        }

        if (Create(path) is not { } window)
        {
            return;
        }

        window.Show();
        ForegroundWindow.Activate(window);
    }

    /// <summary>
    /// Brings up the editor (double-clicking the tray icon, or starting Overmark from its shortcut): the editor that's
    /// open comes to the front as it is; otherwise one opens on today's newest screenshot, or with no capture, where a
    /// note says how to take one.
    /// </summary>
    public void OpenEditor()
    {
        if (_latest is { } open)
        {
            Activate(open);
            return;
        }

        var newest = history.TakenOn(DateTime.Today).FirstOrDefault(r => !r.IsVideo);
        var window = (newest is null ? null : Create(newest.Path)) ?? Create(null, null, null);
        window.Show();
        ForegroundWindow.Activate(window);
    }

    /// <summary>
    /// Shows another capture in <paramref name="current"/>'s window, as when it's picked from the list of today's
    /// captures. Unsaved marks aren't asked about: they're kept as a draft, and come back (still unsaved) when that
    /// capture is opened again within a day. A capture that's already open elsewhere is just brought to the front.
    /// </summary>
    public void SwitchTo(EditorWindow current, string path)
    {
        if (TryActivate(path))
        {
            return;
        }

        current.KeepDraft();
        ShowIn(current, path);
    }

    /// <summary>
    /// Opens an image from elsewhere (picked with Open, or dropped on the editor or pasted as a file) in
    /// <paramref name="current"/>, as if it were picked from the list, and puts it on the list. Save writes back over
    /// it. One Save can't write back in its own format (a GIF or WebP, say) opens as a PNG copy in the screenshots
    /// folder instead, and the original is left alone.
    /// </summary>
    public void OpenFile(EditorWindow current, string path)
    {
        if (ImageFiles.CanSave(path))
        {
            if (!TryActivate(path))
            {
                current.KeepDraft();
                ShowIn(current, path, owner: current);
            }

            return;
        }

        BitmapSource picture;
        try
        {
            picture = ImageFiles.Load(path);
        }
        catch (Exception ex)
        {
            Log.Error($"Couldn't open {path} in the editor", ex);
            SayCouldntOpen(current, path, ex.Message);
            return;
        }

        OpenPicture(current, picture, CaptureKind.Opened);
    }

    /// <summary>
    /// A picture pasted into <paramref name="current"/>: saved as a new PNG in the screenshots folder, as a capture
    /// would be, put on the list, and opened.
    /// </summary>
    public void OpenPicture(EditorWindow current, BitmapSource picture) => OpenPicture(current, picture, CaptureKind.Pasted);

    private void OpenPicture(EditorWindow current, BitmapSource picture, CaptureKind kind)
    {
        string path;
        try
        {
            path = CaptureOutput.SaveNew(picture, FileNamer.FolderFor(settings()), settings().FileNamePattern);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Log.Error("Couldn't save a picture to edit", ex);
            MessageBox.Show(current, $"Couldn't save the picture to edit it:\n\n{ex.Message}", "Overmark", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Log.Info($"Saved a {kind.ToString().ToLowerInvariant()} picture as {path}");
        history.Add(new CaptureRecord(path, kind, DateTime.Now, picture.PixelWidth, picture.PixelHeight));
        current.KeepDraft();
        ShowIn(current, path, owner: current);
    }

    /// <summary>
    /// The x on a capture in the list: takes it off the list, asking first about unsaved changes, as closing an
    /// editor does. Closing the capture being edited moves on to the next one in the list (or closes the editor if
    /// there's none); one open in another window closes that window; one with a draft asks whether to save it.
    /// </summary>
    public void CloseCapture(EditorWindow current, string path)
    {
        if (string.Equals(current.CapturePath, path, StringComparison.OrdinalIgnoreCase))
        {
            // Picked before closing, while this one is still on the list to count from.
            var next = NextAfter(path);
            if (!current.ConfirmLeave())
            {
                return;
            }

            history.Close(path);
            if (next is null || TryActivate(next) || !ShowIn(current, next))
            {
                current.CloseWithoutAsking();
            }

            return;
        }

        if (_open.TryGetValue(path, out var other))
        {
            // That window asks about its own unsaved changes; cancelling keeps it open, and on the list.
            other.Close();
            if (_open.ContainsKey(path))
            {
                return;
            }
        }
        else if (_edits.HasDraft(path))
        {
            var answer = MessageBox.Show(
                current, $"Save your changes to {Path.GetFileName(path)}?", "Overmark", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel)
            {
                return;
            }

            if (answer == MessageBoxResult.No)
            {
                _edits.DiscardDraft(path);
            }
            else if (!_edits.SaveDraft(path, out var problem))
            {
                MessageBox.Show(current, $"Couldn't save {Path.GetFileName(path)}:\n\n{problem}", "Overmark", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        history.Close(path);
    }

    /// <summary>
    /// The bin button, once it's asked: sends the capture being edited to the Recycle Bin, unsaved changes and all
    /// (its editable copy goes too), and moves on to the next one in the list, as its x does. Rather than closing,
    /// the editor stays open with no capture if there's nothing left to show.
    /// </summary>
    public void DeleteCapture(EditorWindow current, string path)
    {
        // Picked before deleting, while it's still on the list to count from.
        var next = NextAfter(path);
        if (!RecycleBin.TrySend(path, out var problem))
        {
            Log.Warn($"Couldn't delete {path}: {problem}");
            MessageBox.Show(current, $"Couldn't delete {Path.GetFileName(path)}:\n\n{problem}", "Overmark", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Log.Info($"Deleted {path} from the editor");
        _edits.Forget(path);
        if (next is not null && TryActivate(next))
        {
            // The next one is open in another window, which has come to the front.
            current.CloseWithoutAsking();
        }
        else if (next is null || !ShowIn(current, next))
        {
            _open.Remove(path);
            current.ShowNoCapture();
        }
    }

    /// <summary>
    /// The screenshot to move on to when <paramref name="path"/> is closed or deleted: the next one down today's list
    /// (the one taken before it), or the one above if it's at the bottom. Null if it's the only one.
    /// </summary>
    private string? NextAfter(string path)
    {
        var screenshots = history.TakenOn(DateTime.Today).Where(r => !r.IsVideo).Select(r => r.Path).ToList();
        var index = screenshots.FindIndex(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        return index + 1 < screenshots.Count ? screenshots[index + 1] : index > 0 ? screenshots[index - 1] : null;
    }

    /// <summary>
    /// Shows <paramref name="path"/> in <paramref name="window"/>, in place of its capture. False if it couldn't be
    /// read, which <paramref name="owner"/> says (or else a notification does).
    /// </summary>
    private bool ShowIn(EditorWindow window, string path, Window? owner = null)
    {
        if (Prepare(path, owner) is not { } capture)
        {
            return false;
        }

        if (window.CapturePath is { } showing)
        {
            _open.Remove(showing);
        }

        window.ShowCapture(path, capture.Image, capture.Kept);
        _open[path] = window;
        return true;
    }

    /// <summary>Asks every open editor to close. Returns false if the user kept one open (to save it first).</summary>
    public bool CloseAll()
    {
        foreach (var window in _windows.ToList())
        {
            window.Close();
        }

        return _windows.Count == 0;
    }

    private bool TryActivate(string path)
    {
        if (!_open.TryGetValue(path, out var existing))
        {
            return false;
        }

        Activate(existing);
        return true;
    }

    private static void Activate(EditorWindow window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        ForegroundWindow.Activate(window);
    }

    /// <summary>An editor window for <paramref name="path"/>, or null (after saying why) if it can't be read.</summary>
    private EditorWindow? Create(string path) => Prepare(path) is { } capture ? Create(path, capture.Image, capture.Kept) : null;

    /// <summary>An editor window showing <paramref name="path"/>, or no capture if it's null.</summary>
    private EditorWindow Create(string? path, BitmapSource? image, EditorDocument? kept)
    {
        EditorWindow? window = null;
        window = new EditorWindow(
            path, image, settings().Editor, rememberSettings, Pin, history, other => SwitchTo(window!, other), _edits,
            kept, other => CloseCapture(window!, other), other => DeleteCapture(window!, other), HowToCapture,
            openSettings, file => OpenFile(window!, file), picture => OpenPicture(window!, picture));
        if (path is not null)
        {
            _open[path] = window;
        }

        _windows.Add(window);
        _latest = window;
        window.Activated += (_, _) => _latest = window;

        // The window may have moved on to other captures (or none) by the time it closes.
        window.Closed += (_, _) =>
        {
            if (window.CapturePath is { } showing)
            {
                _open.Remove(showing);
            }

            _windows.Remove(window);
            if (_latest == window)
            {
                _latest = _windows.LastOrDefault();
            }
        };
        return window;

        // Editing the pin later reopens the capture that was pinned, wherever the window has got to since.
        void Pin(BitmapSource rendered)
        {
            if (window!.CapturePath is { } pinned)
            {
                pins.Open(rendered, edit: () => Open(pinned));
            }
        }
    }

    /// <summary>What the editor says when no capture is open: how to take one, with the hotkey if there is one.</summary>
    private string HowToCapture() =>
        HotkeyBindings.FromSettings(settings().Hotkeys, out _).TryGetValue(HotkeyAction.RegionCapture, out var hotkeys)
            ? $"Take a screenshot to start editing: press {HotkeyBindings.Describe(hotkeys)}, and it opens here."
            : "Take a screenshot to start editing: right-click Overmark in the notification area and pick a capture, and it opens here.";

    /// <summary>
    /// The capture's picture, read for editing. If it was edited in the last day (and the file hasn't changed since),
    /// <c>Kept</c> brings back its marks, still editable, from the editable copy, rather than flattened into the
    /// picture. Null, after saying why, if it can't be read: in a message over <paramref name="owner"/> if there is one,
    /// otherwise in a notification.
    /// </summary>
    private (BitmapSource Image, EditorDocument? Kept)? Prepare(string path, Window? owner = null)
    {
        var kept = _edits.Find(path);
        BitmapSource image;
        try
        {
            image = kept?.Original ?? ImageFiles.Load(path);
        }
        catch (Exception ex)
        {
            Log.Error($"Couldn't open {path} in the editor", ex);
            if (owner is null)
            {
                tray.Notify("Couldn't open the capture", ex.Message);
            }
            else
            {
                SayCouldntOpen(owner, path, ex.Message);
            }

            return null;
        }

        // Opening a capture that was closed off the list puts it back on; an image from elsewhere goes on it.
        if (history.Knows(path))
        {
            history.Reopen(path);
        }
        else
        {
            history.Add(new CaptureRecord(path, CaptureKind.Opened, DateTime.Now, image.PixelWidth, image.PixelHeight));
        }

        return (image, kept is null ? null : new EditorDocument(kept.Original, kept.Crop, kept.Marks, unsaved: kept.Draft));
    }

    private static void SayCouldntOpen(Window owner, string path, string problem) =>
        MessageBox.Show(owner, $"Couldn't open {Path.GetFileName(path)}:\n\n{problem}", "Overmark", MessageBoxButton.OK, MessageBoxImage.Error);

    /// <summary>The store of editable copies, with anything over a day old cleared out.</summary>
    private static EditStore CreateEditStore()
    {
        var store = new EditStore(AppInfo.EditsFolder);
        store.CleanUp();
        return store;
    }

}
