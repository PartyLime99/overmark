using System.Runtime.InteropServices;
using System.Text;
using Overmark.Platform;
using static Overmark.Platform.NativeMethods;

namespace Overmark.Capture;

internal sealed record WindowInfo(IntPtr Handle, ScreenRect Bounds);

/// <summary>Finds the top-level windows that a click in the overlay can snap to.</summary>
internal static class WindowFinder
{
    /// <summary>Visible windows, frontmost first, clipped to <paramref name="desktop"/>.</summary>
    public static IReadOnlyList<WindowInfo> FindWindows(ScreenRect desktop)
    {
        var ownProcess = (uint)Environment.ProcessId;
        var windows = new List<WindowInfo>();
        EnumWindowsProc callback = (handle, _) =>
        {
            if (Describe(handle, ownProcess, desktop) is { } window)
            {
                windows.Add(window);
            }

            return true;
        };
        EnumWindows(callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return windows;
    }

    private static WindowInfo? Describe(IntPtr handle, uint ownProcess, ScreenRect desktop)
    {
        if (!IsWindowVisible(handle) || IsIconic(handle))
        {
            return null;
        }

        GetWindowThreadProcessId(handle, out var process);
        if (process == ownProcess)
        {
            return null;
        }

        // Cloaked windows count as visible but aren't shown, e.g. apps on another virtual desktop.
        if (DwmGetWindowAttribute(handle, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
        {
            return null;
        }

        // Click-through windows are overlays, never what the user is pointing at.
        if ((GetWindowLong(handle, GWL_EXSTYLE) & WS_EX_TRANSPARENT) != 0)
        {
            return null;
        }

        // The desktop itself.
        if (ClassName(handle) is "Progman" or "WorkerW")
        {
            return null;
        }

        // The extended frame leaves out the invisible resize border and shadow that GetWindowRect includes.
        if (DwmGetWindowAttribute(handle, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT rect, Marshal.SizeOf<RECT>()) != 0
            && !GetWindowRect(handle, out rect))
        {
            return null;
        }

        return ScreenRect.FromRect(rect).Intersect(desktop) is { Width: >= 8, Height: >= 8 } visible
            ? new WindowInfo(handle, visible)
            : null;
    }

    private static string ClassName(IntPtr handle)
    {
        var name = new StringBuilder(256);
        return GetClassName(handle, name, name.Capacity) > 0 ? name.ToString() : "";
    }
}
