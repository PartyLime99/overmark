using System.Windows.Media.Imaging;
using Overmark.Platform;

namespace Overmark.Pinning;

/// <summary>Keeps one pin on screen at a time: pinning something new replaces the old pin.</summary>
internal sealed class PinBoard
{
    private PinWindow? _current;

    /// <param name="at">Where to put the image's top-left pixel; null centres it on the screen under the mouse.</param>
    /// <param name="edit">Opens the capture in the editor, if it has a file.</param>
    public void Open(BitmapSource image, PixelPoint? at = null, Action? edit = null)
    {
        Close();
        var pin = new PinWindow(image, at, edit);
        pin.Closed += (_, _) =>
        {
            if (_current == pin)
            {
                _current = null;
            }
        };
        _current = pin;
        pin.Show();
    }

    public void Close() => _current?.Close();
}
