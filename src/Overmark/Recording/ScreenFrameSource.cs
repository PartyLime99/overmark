using System.ComponentModel;
using System.Runtime.InteropServices;
using Overmark.Platform;
using static Overmark.Platform.NativeMethods;

namespace Overmark.Recording;

/// <summary>
/// Produces video frames: 32-bit BGRA pixels, <see cref="Width"/> × 4 bytes per row, with the <b>bottom</b> row
/// first, which is the order Windows' video encoder expects.
/// </summary>
internal interface IFrameSource : IDisposable
{
    int Width { get; }

    int Height { get; }

    /// <summary>Fills <paramref name="pixels"/> (Width × Height × 4 bytes) with the current frame.</summary>
    void Capture(byte[] pixels);
}

/// <summary>
/// Frames copied from a region of the desktop, the same way screenshots are, with the mouse pointer drawn in.
/// The bitmap is kept between frames so each one is just a copy.
/// </summary>
internal sealed class ScreenFrameSource : IFrameSource
{
    private readonly ScreenRect _region;
    private readonly bool _includeCursor;
    private readonly IntPtr _screenDc;
    private readonly IntPtr _memoryDc;
    private readonly IntPtr _bitmap;
    private readonly IntPtr _previous;
    private readonly IntPtr _bits;

    public ScreenFrameSource(ScreenRect region, bool includeCursor)
    {
        _region = region;
        _includeCursor = includeCursor;
        _screenDc = GetDC(IntPtr.Zero);
        _memoryDc = CreateCompatibleDC(_screenDc);
        var header = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = region.Width,
            biHeight = region.Height, // positive: bottom row first, as the video encoder wants
            biPlanes = 1,
            biBitCount = 32,
            biCompression = BI_RGB,
        };
        _bitmap = CreateDIBSection(_screenDc, ref header, DIB_RGB_COLORS, out _bits, IntPtr.Zero, 0);
        if (_bitmap == IntPtr.Zero)
        {
            var error = Marshal.GetLastPInvokeError();
            DeleteDC(_memoryDc);
            ReleaseDC(IntPtr.Zero, _screenDc);
            throw new Win32Exception(error, "Couldn't allocate memory for recording.");
        }

        _previous = SelectObject(_memoryDc, _bitmap);
    }

    public int Width => _region.Width;

    public int Height => _region.Height;

    public void Capture(byte[] pixels)
    {
        BitBlt(_memoryDc, 0, 0, _region.Width, _region.Height, _screenDc, _region.Left, _region.Top, SRCCOPY | CAPTUREBLT);
        if (_includeCursor)
        {
            DrawCursor();
        }

        GdiFlush();
        Marshal.Copy(_bits, pixels, 0, _region.Width * _region.Height * 4);
    }

    public void Dispose()
    {
        SelectObject(_memoryDc, _previous);
        DeleteObject(_bitmap);
        DeleteDC(_memoryDc);
        ReleaseDC(IntPtr.Zero, _screenDc);
    }

    private void DrawCursor()
    {
        var cursor = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref cursor) || (cursor.flags & CURSOR_SHOWING) == 0 || !GetIconInfo(cursor.hCursor, out var icon))
        {
            return;
        }

        DrawIconEx(
            _memoryDc,
            cursor.ptScreenPos.X - icon.xHotspot - _region.Left,
            cursor.ptScreenPos.Y - icon.yHotspot - _region.Top,
            cursor.hCursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL);

        // GetIconInfo hands over copies of the cursor's bitmaps.
        if (icon.hbmMask != IntPtr.Zero)
        {
            DeleteObject(icon.hbmMask);
        }

        if (icon.hbmColor != IntPtr.Zero)
        {
            DeleteObject(icon.hbmColor);
        }
    }
}
