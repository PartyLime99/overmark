using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Overmark.Capture;

namespace Overmark.Editor;

/// <summary>
/// Reading and writing the pictures the editor opens: captures, and images from elsewhere, picked with Open, dropped
/// on the editor or pasted into it.
/// </summary>
internal static class ImageFiles
{
    public const string DialogFilter = "Images|*.png;*.jpg;*.jpeg;*.jfif;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.heic;*.heif|All files|*.*";

    /// <summary>The kinds of image the editor opens. Windows needs an extra codec for some (HEIC); without it they say why they can't open.</summary>
    private static readonly string[] Readable = [".png", ".jpg", ".jpeg", ".jfif", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic", ".heif"];

    /// <summary>
    /// The kinds Save writes back in their own format. The rest are opened as a PNG copy, as saving over them would
    /// lose what they can hold and the editor can't (a GIF's animation, a TIFF's other pages), or there's no encoder.
    /// </summary>
    private static readonly string[] Writable = [".png", ".jpg", ".jpeg", ".jfif", ".bmp"];

    public static bool IsImage(string path) => Readable.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static bool CanSave(string path) => Writable.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reads a picture for editing. The whole file is read at once, so it isn't kept locked and can be saved over. A
    /// photo is turned the way its camera recorded it was held, and unusual pixel formats (CMYK, greys, 16 bits a
    /// channel, palettes) become ordinary 32-bit colour at 96 DPI.
    /// </summary>
    public static BitmapSource Load(string path)
    {
        using var stream = new MemoryStream(File.ReadAllBytes(path));
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        BitmapSource image = frame;
        if (Upright(frame) is { } turn)
        {
            image = new TransformedBitmap(image, turn);
        }

        return Ordinary(image);
    }

    /// <summary>
    /// How a photo needs turning to be the right way up, for a thumbnail (which ignores mirroring); none for anything
    /// that isn't a JPEG, or doesn't say. Reads only the file's metadata, and doesn't keep it open.
    /// </summary>
    public static Rotation ThumbnailRotation(string path)
    {
        if (Path.GetExtension(path).ToLowerInvariant() is not (".jpg" or ".jpeg" or ".jfif"))
        {
            return Rotation.Rotate0;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            return Upright(frame) switch
            {
                RotateTransform { Angle: 90 } => Rotation.Rotate90,
                RotateTransform { Angle: 180 } => Rotation.Rotate180,
                RotateTransform { Angle: 270 } => Rotation.Rotate270,
                _ => Rotation.Rotate0,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            return Rotation.Rotate0;
        }
    }

    /// <summary>
    /// <paramref name="image"/> encoded to save as <paramref name="path"/>: JPEG or BMP for those (on white, as they
    /// can't be see-through), otherwise PNG.
    /// </summary>
    public static byte[] Encode(BitmapSource image, string path)
    {
        BitmapEncoder encoder = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" or ".jfif" => new JpegBitmapEncoder { QualityLevel = 95 },
            ".bmp" => new BmpBitmapEncoder(),
            _ => new PngBitmapEncoder(),
        };

        var frame = encoder is PngBitmapEncoder ? image : new FormatConvertedBitmap(CaptureOutput.OnWhite(image), PixelFormats.Bgr24, null, 0);
        encoder.Frames.Add(BitmapFrame.Create(frame));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>True if <paramref name="data"/> (being dragged over the editor) holds something <see cref="Read"/> would open.</summary>
    public static bool HasImage(IDataObject data)
    {
        try
        {
            return data.GetDataPresent(DataFormats.FileDrop)
                ? data.GetData(DataFormats.FileDrop) is string[] files && files.Any(IsImage)
                : data.GetDataPresent("PNG") || data.GetDataPresent(DataFormats.Bitmap);
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// What's in <paramref name="data"/> (dropped on the editor, or on the clipboard): an image file to open (the first,
    /// if there are several), or a picture, such as one copied from a browser or another app, to save as a new capture.
    /// Both null if there's neither.
    /// </summary>
    public static (string? File, BitmapSource? Picture) Read(IDataObject data)
    {
        try
        {
            if (data.GetDataPresent(DataFormats.FileDrop))
            {
                return (data.GetData(DataFormats.FileDrop) is string[] files ? files.FirstOrDefault(IsImage) : null, null);
            }

            // PNG keeps any see-through parts (Office, browsers and Overmark itself put it on the clipboard).
            if (data.GetDataPresent("PNG") && data.GetData("PNG") is Stream png)
            {
                var frame = BitmapDecoder.Create(png, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                return (null, Ordinary(frame));
            }

            // A plain bitmap's fourth byte isn't reliably see-through-ness, so it's left opaque.
            if (data.GetDataPresent(DataFormats.Bitmap) && data.GetData(DataFormats.Bitmap) is BitmapSource bitmap)
            {
                return (null, Copy(new FormatConvertedBitmap(bitmap, PixelFormats.Bgr32, null, 0)));
            }
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException or NotSupportedException or FileFormatException or ArgumentException)
        {
            Log.Warn($"Couldn't read the picture: {ex.Message}");
        }

        return (null, null);
    }

    /// <summary>What's on the clipboard, as for <see cref="Read"/>.</summary>
    public static (string? File, BitmapSource? Picture) FromClipboard()
    {
        try
        {
            return Clipboard.GetDataObject() is { } data ? Read(data) : (null, null);
        }
        catch (ExternalException ex)
        {
            // Another app has the clipboard open.
            Log.Warn($"Couldn't read the clipboard: {ex.Message}");
            return (null, null);
        }
    }

    /// <summary>The turn that puts a photo the right way up, from its EXIF orientation, or null if it's already upright (or doesn't say).</summary>
    private static Transform? Upright(BitmapFrame frame)
    {
        ushort orientation;
        try
        {
            if (frame.Metadata is not BitmapMetadata metadata || metadata.GetQuery("System.Photo.Orientation") is not ushort value)
            {
                return null;
            }

            orientation = value;
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return null;
        }

        // 2, 4, 5 and 7 are mirrored as well as (or instead of) turned.
        var mirror = new ScaleTransform(-1, 1);
        return orientation switch
        {
            2 => mirror,
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            5 => new TransformGroup { Children = { mirror, new RotateTransform(270) } },
            6 => new RotateTransform(90),
            7 => new TransformGroup { Children = { mirror, new RotateTransform(90) } },
            8 => new RotateTransform(270),
            _ => null,
        };
    }

    /// <summary>
    /// <paramref name="image"/> as the editor expects a picture: 32-bit colour (see-through only if it can be) at 96
    /// DPI, frozen. Captures already are, and come back as they are.
    /// </summary>
    private static BitmapSource Ordinary(BitmapSource image)
    {
        var format = image.Format;
        var plain = format == PixelFormats.Bgr32 || format == PixelFormats.Bgra32 || format == PixelFormats.Pbgra32 || format == PixelFormats.Bgr24;
        if (plain && image.DpiX == 96 && image.DpiY == 96 && image is not TransformedBitmap)
        {
            image.Freeze();
            return image;
        }

        return Copy(plain ? image : new FormatConvertedBitmap(image, MayBeSeeThrough(image) ? PixelFormats.Bgra32 : PixelFormats.Bgr32, null, 0));
    }

    private static bool MayBeSeeThrough(BitmapSource image)
    {
        var format = image.Format;
        return format == PixelFormats.Bgra32 || format == PixelFormats.Pbgra32 || format == PixelFormats.Rgba64 || format == PixelFormats.Prgba64
            || format == PixelFormats.Rgba128Float || format == PixelFormats.Prgba128Float
            || image.Palette?.Colors.Any(colour => colour.A < 255) == true;
    }

    /// <summary>The pixels of <paramref name="image"/> in a picture of their own, at 96 DPI, so nothing still refers back to where they came from.</summary>
    private static BitmapSource Copy(BitmapSource image)
    {
        var stride = (image.PixelWidth * image.Format.BitsPerPixel + 7) / 8;
        var pixels = new byte[stride * image.PixelHeight];
        image.CopyPixels(pixels, stride, 0);
        var copy = BitmapSource.Create(image.PixelWidth, image.PixelHeight, 96, 96, image.Format, image.Palette, pixels, stride);
        copy.Freeze();
        return copy;
    }
}
