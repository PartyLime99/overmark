using System.Windows;
using System.Windows.Media.Imaging;
using Overmark.Capture;
using Overmark.Pinning;
using Overmark.Platform;
using Overmark.Settings;
using Overmark.Tray;

namespace Overmark.Editor;

/// <summary>Opens captures in editor windows, one window per file.</summary>
internal sealed class EditorLauncher(Func<AppSettings> settings, Action<EditorSettings> rememberSettings, PinBoard pins, CaptureHistory history, TrayIcon tray)
{
    private readonly Dictionary<string, EditorWindow> _open = new(StringComparer.OrdinalIgnoreCase);
    private readonly EditStore _edits = CreateEditStore();

    public void Open(string path)
    {
        if (TryActivate(path) || Create(path) is not { } window)
        {
            return;
        }

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
    /// The x on a capture in the list: takes it off the list, asking first about unsaved changes, as closing an
    /// editor does. Closing the capture being edited moves on to the next one in the list (or closes the editor if
    /// there's none); one open in another window closes that window; one with a draft asks whether to save it.
    /// </summary>
    public void CloseCapture(EditorWindow current, string path)
    {
        if (string.Equals(current.CapturePath, path, StringComparison.OrdinalIgnoreCase))
        {
            // Picked before closing, while this one is still on the list to count from.
            var screenshots = history.TakenOn(DateTime.Today).Where(r => !r.IsVideo).Select(r => r.Path).ToList();
            var index = screenshots.FindIndex(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            var next = index + 1 < screenshots.Count ? screenshots[index + 1] : index > 0 ? screenshots[index - 1] : null;
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

    /// <summary>Shows <paramref name="path"/> in <paramref name="window"/>, in place of its capture. False if it couldn't be read.</summary>
    private bool ShowIn(EditorWindow window, string path)
    {
        if (Prepare(path) is not { } capture)
        {
            return false;
        }

        _open.Remove(window.CapturePath);
        window.ShowCapture(path, capture.Image, capture.Kept);
        _open[path] = window;
        return true;
    }

    /// <summary>Asks every open editor to close. Returns false if the user kept one open (to save it first).</summary>
    public bool CloseAll()
    {
        foreach (var window in _open.Values.ToList())
        {
            window.Close();
        }

        return _open.Count == 0;
    }

    private bool TryActivate(string path)
    {
        if (!_open.TryGetValue(path, out var existing))
        {
            return false;
        }

        if (existing.WindowState == WindowState.Minimized)
        {
            existing.WindowState = WindowState.Normal;
        }

        ForegroundWindow.Activate(existing);
        return true;
    }

    private EditorWindow? Create(string path)
    {
        if (Prepare(path) is not { } capture)
        {
            return null;
        }

        EditorWindow? window = null;
        window = new EditorWindow(
            path, capture.Image, settings().Editor, rememberSettings, Pin, history, other => SwitchTo(window!, other), _edits,
            capture.Kept, other => CloseCapture(window!, other));
        _open[path] = window;

        // The window may have moved on to other captures by the time it closes.
        window.Closed += (_, _) => _open.Remove(window.CapturePath);
        return window;

        // Editing the pin later reopens the capture that was pinned, wherever the window has got to since.
        void Pin(BitmapSource rendered)
        {
            var pinned = window!.CapturePath;
            pins.Open(rendered, edit: () => Open(pinned));
        }
    }

    /// <summary>
    /// The capture's picture, read for editing. If it was edited in the last day (and the file hasn't changed since),
    /// <c>Kept</c> brings back its marks, still editable, from the editable copy, rather than flattened into the
    /// picture. Null, after saying why, if it can't be read.
    /// </summary>
    private (BitmapSource Image, EditorDocument? Kept)? Prepare(string path)
    {
        var kept = _edits.Find(path);
        BitmapSource image;
        try
        {
            image = kept?.Original ?? Load(path);
        }
        catch (Exception ex)
        {
            Log.Error($"Couldn't open {path} in the editor", ex);
            tray.Notify("Couldn't open the capture", ex.Message);
            return null;
        }

        // Opening a capture that was closed off the list puts it back on.
        history.Reopen(path);
        return (image, kept is null ? null : new EditorDocument(kept.Original, kept.Crop, kept.Marks, unsaved: kept.Draft));
    }

    /// <summary>The store of editable copies, with anything over a day old cleared out.</summary>
    private static EditStore CreateEditStore()
    {
        var store = new EditStore(AppInfo.EditsFolder);
        store.CleanUp();
        return store;
    }

    private static BitmapSource Load(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();

        // Read the whole file now, so it isn't kept locked and can be saved over.
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        image.UriSource = new Uri(path);
        image.EndInit();
        image.Freeze();
        return image;
    }
}
