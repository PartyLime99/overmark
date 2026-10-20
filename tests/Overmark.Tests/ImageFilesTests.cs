using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Overmark.Capture;
using Overmark.Editor;

namespace Overmark.Tests;

public sealed class ImageFilesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "OvermarkTests", Guid.NewGuid().ToString("N"));

    public ImageFilesTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    /// <summary>A picture <paramref name="width"/> × <paramref name="height"/>, red, with a see-through left half if asked.</summary>
    private static BitmapSource Picture(int width, int height, bool seeThrough = false)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var x = i / 4 % width;
            (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (0, 0, 255, seeThrough && x < width / 2 ? (byte)0 : (byte)255);
        }

        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        image.Freeze();
        return image;
    }

    private string Write(string name, BitmapEncoder encoder, BitmapSource image, BitmapMetadata? metadata = null)
    {
        encoder.Frames.Add(BitmapFrame.Create(image, null, metadata, null));
        var path = Path.Combine(_folder, name);
        using var file = File.Create(path);
        encoder.Save(file);
        return path;
    }

    [Theory]
    [InlineData("shot.png", true, true)]
    [InlineData("Photo.JPG", true, true)]
    [InlineData("scan.jpeg", true, true)]
    [InlineData("old.bmp", true, true)]
    [InlineData("anim.gif", true, false)]
    [InlineData("page.tiff", true, false)]
    [InlineData("phone.webp", true, false)]
    [InlineData("notes.txt", false, false)]
    public void Knows_which_images_it_opens_and_which_it_can_save_back(string name, bool opens, bool saves)
    {
        Assert.Equal(opens, ImageFiles.IsImage(name));
        Assert.Equal(saves, ImageFiles.CanSave(name));
    }

    [Fact]
    public void Saves_in_the_files_own_format()
    {
        var image = Picture(8, 8);

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, ImageFiles.Encode(image, @"C:\x.png")[..4]);
        Assert.Equal(new byte[] { 0xFF, 0xD8 }, ImageFiles.Encode(image, @"C:\x.jpeg")[..2]);
        Assert.Equal("BM"u8.ToArray(), ImageFiles.Encode(image, @"C:\x.bmp")[..2]);
    }

    [Fact]
    public void A_see_through_picture_saved_as_a_JPEG_goes_on_white()
    {
        var path = Path.Combine(_folder, "x.jpg");
        File.WriteAllBytes(path, ImageFiles.Encode(Picture(16, 16, seeThrough: true), path));

        var loaded = ImageFiles.Load(path);
        var pixels = new byte[16 * 16 * 4];
        new FormatConvertedBitmap(loaded, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, 16 * 4, 0);

        Assert.True(pixels[(8 * 16 + 1) * 4 + 1] > 240); // left half: white (green is high)
        Assert.True(pixels[(8 * 16 + 14) * 4 + 1] < 30); // right half: still red
    }

    [Fact]
    public void A_photo_is_turned_the_way_the_camera_was_held()
    {
        foreach (var (orientation, size) in new[] { ((ushort)1, (8, 4)), ((ushort)6, (4, 8)), ((ushort)8, (4, 8)), ((ushort)3, (8, 4)), ((ushort)2, (8, 4)), ((ushort)5, (4, 8)) })
        {
            var metadata = new BitmapMetadata("jpg");
            metadata.SetQuery("/app1/ifd/{ushort=274}", orientation);
            var path = Write($"photo{orientation}.jpg", new JpegBitmapEncoder(), Picture(8, 4), metadata);

            var loaded = ImageFiles.Load(path);

            Assert.Equal(size, (loaded.PixelWidth, loaded.PixelHeight));
            Assert.True(loaded.IsFrozen);
        }

        Assert.Equal(Rotation.Rotate90, ImageFiles.ThumbnailRotation(Path.Combine(_folder, "photo6.jpg")));
        Assert.Equal(Rotation.Rotate270, ImageFiles.ThumbnailRotation(Path.Combine(_folder, "photo8.jpg")));
        Assert.Equal(Rotation.Rotate0, ImageFiles.ThumbnailRotation(Path.Combine(_folder, "photo1.jpg")));
        File.Delete(Path.Combine(_folder, "photo6.jpg")); // not left open
    }

    [Fact]
    public void Unusual_formats_become_ordinary_colour_at_96_DPI()
    {
        var grey = BitmapSource.Create(4, 4, 300, 300, PixelFormats.Gray8, null, new byte[16], 4);
        var path = Write("grey.png", new PngBitmapEncoder(), grey);

        var loaded = ImageFiles.Load(path);

        Assert.Equal(PixelFormats.Bgr32, loaded.Format);
        Assert.Equal((96.0, 96.0), (loaded.DpiX, loaded.DpiY));
    }

    [Fact]
    public void Reading_a_file_doesnt_keep_it_locked()
    {
        var path = Write("shot.png", new PngBitmapEncoder(), Picture(4, 4));

        ImageFiles.Load(path);

        File.WriteAllBytes(path, [1, 2, 3]); // would throw if it were still open
    }

    [Fact]
    public void Dropped_or_copied_files_open_the_first_image()
    {
        var data = new DataObject(DataFormats.FileDrop, new[] { @"C:\notes.txt", @"C:\b.png", @"C:\c.jpg" });

        Assert.True(ImageFiles.HasImage(data));
        Assert.Equal<(string?, BitmapSource?)>((@"C:\b.png", null), ImageFiles.Read(data));

        var none = new DataObject(DataFormats.FileDrop, new[] { @"C:\notes.txt" });
        Assert.False(ImageFiles.HasImage(none));
        Assert.Equal<(string?, BitmapSource?)>((null, null), ImageFiles.Read(none));
    }

    [Fact]
    public void A_copied_picture_is_read_from_its_PNG_keeping_see_through_parts()
    {
        var data = new DataObject();
        data.SetData("PNG", new MemoryStream(CaptureOutput.EncodePng(Picture(6, 3, seeThrough: true))), autoConvert: false);

        Assert.True(ImageFiles.HasImage(data));
        var (file, picture) = ImageFiles.Read(data);

        Assert.Null(file);
        Assert.Equal((6, 3), (picture!.PixelWidth, picture.PixelHeight));
        Assert.True(Transparency.IsSeeThrough(picture));
    }

    [Fact]
    public void A_copied_plain_bitmap_is_read_as_opaque()
    {
        var data = new DataObject();
        data.SetImage(Picture(6, 3, seeThrough: true));

        var (_, picture) = ImageFiles.Read(data);

        Assert.Equal(PixelFormats.Bgr32, picture!.Format);
        Assert.False(Transparency.IsSeeThrough(picture));
    }

    [Fact]
    public void Text_isnt_a_picture()
    {
        var data = new DataObject(DataFormats.UnicodeText, "hello");

        Assert.False(ImageFiles.HasImage(data));
        Assert.Equal<(string?, BitmapSource?)>((null, null), ImageFiles.Read(data));
    }

    [Fact]
    public void A_pasted_picture_is_saved_as_a_new_png()
    {
        var path = CaptureOutput.SaveNew(Picture(5, 5), _folder, "'pasted'");
        var second = CaptureOutput.SaveNew(Picture(5, 5), _folder, "'pasted'");

        Assert.Equal(("pasted.png", "pasted_2.png"), (Path.GetFileName(path), Path.GetFileName(second)));
        Assert.Equal(5, ImageFiles.Load(path).PixelWidth);
    }
}
