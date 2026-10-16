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
    /// Shows another capture in place of <paramref name="current"/>, as when it's picked from the list of today's
    /// captures: a new window opens exactly where the old one was, and the old one closes. Unsaved changes are
    /// asked about first; cancelling keeps the current capture. A capture that's already open elsewhere is just
    /// brought to the front.
    /// </summary>
    public void SwitchTo(EditorWindow current, string path)
    {
        if (TryActivate(path) || !current.ConfirmLeave() || Create(path) is not { } window)
        {
            return;
        }

        var maximised = current.WindowState == WindowState.Maximized;
        var bounds = maximised ? current.RestoreBounds : new Rect(current.Left, current.Top, current.Width, current.Height);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = bounds.Left;
        window.Top = bounds.Top;
        window.Width = bounds.Width;
        window.Height = bounds.Height;
        window.ShowRecent = current.ShowRecent;
        window.Show();
        if (maximised)
        {
            window.WindowState = WindowState.Maximized;
        }

        ForegroundWindow.Activate(window);
        current.CloseWithoutAsking();
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
        BitmapSource image;
        try
        {
            image = Load(path);
        }
        catch (Exception ex)
        {
            Log.Error($"Couldn't open {path} in the editor", ex);
            tray.Notify("Couldn't open the capture", ex.Message);
            return null;
        }

        EditorWindow? window = null;
        window = new EditorWindow(
            path, image, settings().Editor, rememberSettings, rendered => pins.Open(rendered, edit: () => Open(path)),
            history, other => SwitchTo(window!, other));
        _open[path] = window;
        window.Closed += (_, _) => _open.Remove(path);
        return window;
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
