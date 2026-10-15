using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Overmark.Capture;

/// <summary>What happened to a capture. <see cref="Problem"/> describes whatever didn't work.</summary>
internal sealed record DeliveryResult(bool Copied, string? SavedPath, string? Problem);

/// <summary>Puts a finished capture on the clipboard and saves it as a PNG.</summary>
internal static class CaptureOutput
{
    /// <summary>Must be called on the UI thread (the clipboard needs it); encoding and saving happen in the background.</summary>
    public static async Task<DeliveryResult> DeliverAsync(BitmapSource image, string folder, string? fileNamePattern)
    {
        var png = await Task.Run(() => EncodePng(image));
        var copied = TryCopyToClipboard(image, png, out var problem);
        try
        {
            var path = await Task.Run(() => Save(png, folder, fileNamePattern));
            return new DeliveryResult(copied, path, problem);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new DeliveryResult(copied, null, ex.Message);
        }
    }

    /// <summary>Copies <paramref name="image"/> to the clipboard. UI thread only.</summary>
    public static bool TryCopy(BitmapSource image, out string? problem) => TryCopyToClipboard(image, EncodePng(image), out problem);

    public static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static bool TryCopyToClipboard(BitmapSource image, byte[] png, out string? problem)
    {
        // The plain bitmap works everywhere; apps that understand PNG (Office, Teams, browsers) prefer it.
        var data = new DataObject();
        data.SetImage(image);
        data.SetData("PNG", new MemoryStream(png), autoConvert: false);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, copy: true);
                problem = null;
                return true;
            }
            catch (ExternalException) when (attempt < 5)
            {
                // Another app has the clipboard open; it's usually free again within moments.
                Thread.Sleep(50);
            }
            catch (ExternalException ex)
            {
                problem = $"Couldn't copy to the clipboard: {ex.Message}";
                return false;
            }
        }
    }

    private static string Save(byte[] png, string folder, string? fileNamePattern)
    {
        Directory.CreateDirectory(folder);
        for (var attempt = 1; ; attempt++)
        {
            var path = FileNamer.NextPath(folder, fileNamePattern, DateTime.Now, File.Exists);
            try
            {
                // CreateNew never overwrites, even if another capture claims the same name at the same moment.
                using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                file.Write(png);
                return path;
            }
            catch (IOException) when (attempt < 5 && File.Exists(path))
            {
            }
        }
    }
}
