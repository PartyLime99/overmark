using System.Windows;
using System.Windows.Interop;
using static Overmark.Platform.NativeMethods;

namespace Overmark.Platform;

internal static class ForegroundWindow
{
    /// <summary>
    /// Brings <paramref name="window"/> to the front with keyboard focus, and returns whether it got focus.
    /// Windows normally stops a background app taking focus; briefly joining the foreground app's input queue is
    /// the standard way round that when responding to the user (a hotkey, or a click on the preview popup).
    /// </summary>
    public static bool Activate(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        var handle = new WindowInteropHelper(window).Handle;

        // Even when Windows withholds focus, raising the window to the very top and straight back puts it in
        // front of everything without leaving it always-on-top. (Skipped for windows meant to stay on top.)
        if (!window.Topmost)
        {
            SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
            SetWindowPos(handle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
        }

        var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var ownThread = GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != ownThread
            && AttachThreadInput(ownThread, foregroundThread, true);
        try
        {
            BringWindowToTop(handle);
            SetForegroundWindow(handle);
            window.Activate();
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(ownThread, foregroundThread, false);
            }
        }

        return GetForegroundWindow() == handle;
    }
}
