using System.Windows;
using System.Windows.Media.Imaging;
using Overmark.Pinning;
using Overmark.Platform;
using Overmark.Settings;
using Overmark.Tray;

namespace Overmark.Editor;

/// <summary>Opens captures in editor windows, one window per file.</summary>
internal sealed class EditorLauncher(Func<AppSettings> settings, Action<EditorSettings> rememberSettings, PinBoard pins, TrayIcon tray)
{
    private readonly Dictionary<string, EditorWindow> _open = new(StringComparer.OrdinalIgnoreCase);

    public void Open(string path)
    {
        if (_open.TryGetValue(path, out var existing))
        {
            if (existing.WindowState == WindowState.Minimized)
            {
                existing.WindowState = WindowState.Normal;
            }

            ForegroundWindow.Activate(existing);
            return;
        }

        BitmapSource image;
        try
        {
            image = Load(path);
        }
        catch (Exception ex)
        {
            Log.Error($"Couldn't open {path} in the editor", ex);
            tray.Notify("Couldn't open the capture", ex.Message);
            return;
        }

        var window = new EditorWindow(path, image, settings().Editor, rememberSettings, rendered => pins.Open(rendered, edit: () => Open(path)));
        _open[path] = window;
        window.Closed += (_, _) => _open.Remove(path);
        window.Show();
        ForegroundWindow.Activate(window);
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
