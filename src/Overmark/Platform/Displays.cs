using System.Runtime.InteropServices;
using static Overmark.Platform.NativeMethods;

namespace Overmark.Platform;

internal sealed record MonitorInfo(string DeviceName, ScreenRect Bounds, ScreenRect WorkArea, bool IsPrimary, uint Dpi)
{
    public int ScalePercent => (int)Math.Round(Dpi * 100 / 96.0);
}

internal static class Displays
{
    /// <summary>All monitors: the main one first, then left to right.</summary>
    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();
        MonitorEnumProc callback = (IntPtr handle, IntPtr hdc, ref RECT rect, IntPtr data) =>
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(handle, ref info))
            {
                var dpi = GetDpiForMonitor(handle, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 ? dpiX : 96u;
                monitors.Add(new MonitorInfo(
                    info.szDevice,
                    ScreenRect.FromRect(info.rcMonitor),
                    ScreenRect.FromRect(info.rcWork),
                    (info.dwFlags & MONITORINFOF_PRIMARY) != 0,
                    dpi));
            }

            return true;
        };
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        GC.KeepAlive(callback);

        return [.. monitors.OrderByDescending(m => m.IsPrimary).ThenBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top)];
    }

    /// <summary>The smallest rectangle covering every monitor (it can start at negative coordinates).</summary>
    public static ScreenRect VirtualScreen(IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0)
        {
            return default;
        }

        var left = monitors.Min(m => m.Bounds.Left);
        var top = monitors.Min(m => m.Bounds.Top);
        var right = monitors.Max(m => m.Bounds.Right);
        var bottom = monitors.Max(m => m.Bounds.Bottom);
        return new ScreenRect(left, top, right - left, bottom - top);
    }

    public static string DescribeDpiAwareness()
    {
        var context = GetThreadDpiAwarenessContext();
        if (AreDpiAwarenessContextsEqual(context, DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
        {
            return "per-monitor v2";
        }

        return GetAwarenessFromDpiAwarenessContext(context) switch
        {
            0 => "unaware (Windows is scaling Overmark, so captures would be blurry)",
            1 => "system only (captures on scaled monitors may be offset)",
            2 => "per-monitor v1",
            _ => "unknown",
        };
    }
}
