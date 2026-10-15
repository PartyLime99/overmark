using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Overmark.Platform;
using static Overmark.Platform.NativeMethods;

namespace Overmark.Capture;

/// <summary>A frozen copy of part of the desktop. <see cref="Bounds"/> is in physical desktop pixels.</summary>
internal sealed record Screenshot(BitmapSource Image, ScreenRect Bounds)
{
    /// <summary>A copy of the part covering <paramref name="region"/>, which must lie inside <see cref="Bounds"/>.</summary>
    /// <remarks>
    /// Copies the pixels once. A CroppedBitmap would be lazy instead, and is much slower to display.
    /// </remarks>
    public BitmapSource Crop(ScreenRect region)
    {
        if (region == Bounds)
        {
            return Image;
        }

        var stride = region.Width * 4;
        var pixels = new byte[stride * region.Height];
        Image.CopyPixels(new Int32Rect(region.Left - Bounds.Left, region.Top - Bounds.Top, region.Width, region.Height), pixels, stride, 0);
        var cropped = BitmapSource.Create(region.Width, region.Height, 96, 96, Image.Format, null, pixels, stride);
        cropped.Freeze();
        return cropped;
    }
}

internal static class ScreenCapture
{
    /// <summary>Copies <paramref name="area"/> of the desktop (any monitors it covers) at full resolution.</summary>
    /// <remarks>HDR monitors aren't tone-mapped yet, so captures from them can look washed out.</remarks>
    public static Screenshot Capture(ScreenRect area)
    {
        var screenDc = GetDC(IntPtr.Zero);
        var memoryDc = CreateCompatibleDC(screenDc);
        var header = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = area.Width,
            biHeight = -area.Height, // negative: rows run top to bottom, as WPF expects
            biPlanes = 1,
            biBitCount = 32,
            biCompression = BI_RGB,
        };
        var bitmap = CreateDIBSection(screenDc, ref header, DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);
        try
        {
            if (bitmap == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Couldn't allocate memory for the screenshot.");
            }

            var previous = SelectObject(memoryDc, bitmap);
            var copied = BitBlt(memoryDc, 0, 0, area.Width, area.Height, screenDc, area.Left, area.Top, SRCCOPY | CAPTUREBLT);
            var error = Marshal.GetLastPInvokeError();
            SelectObject(memoryDc, previous);
            if (!copied)
            {
                throw new Win32Exception(error, "Couldn't copy the screen.");
            }

            GdiFlush();
            var stride = area.Width * 4;
            var image = BitmapSource.Create(area.Width, area.Height, 96, 96, PixelFormats.Bgr32, null, bits, stride * area.Height, stride);
            image.Freeze();
            return new Screenshot(image, area);
        }
        finally
        {
            if (bitmap != IntPtr.Zero)
            {
                DeleteObject(bitmap);
            }

            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}
