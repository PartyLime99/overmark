using Overmark.Recording;
using Windows.Graphics.Imaging;
using Windows.Media.Editing;
using Windows.Storage;

namespace Overmark.Tests;

public sealed class ScreenRecorderTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "OvermarkTests", Guid.NewGuid().ToString("N"));

    public ScreenRecorderTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task Records_an_mp4_the_right_way_up()
    {
        var path = Path.Combine(_folder, "test.mp4");

        var recorder = await ScreenRecorder.StartAsync(new RedOverBlue(320, 240), path, 10);
        await Task.Delay(1500);
        var length = await recorder.StopAsync();

        Assert.True(new FileInfo(path).Length > 1000);
        Assert.InRange(length.TotalSeconds, 1.3, 3);
        Assert.NotNull(recorder.FirstFrame);
        var preview = new byte[4];
        recorder.FirstFrame!.CopyPixels(new System.Windows.Int32Rect(160, 10, 1, 1), preview, 4, 0);
        Assert.True(preview[2] > 200 && preview[0] < 60, "the preview's top should be red");

        var (top, bottom) = await TopAndBottomColours(path);
        Assert.True(top.R > 200 && top.B < 60, $"top should be red, was {top}");
        Assert.True(bottom.B > 200 && bottom.R < 60, $"bottom should be blue, was {bottom}");
    }

    [Fact]
    public async Task Paused_time_is_left_out()
    {
        var path = Path.Combine(_folder, "paused.mp4");

        var recorder = await ScreenRecorder.StartAsync(new RedOverBlue(160, 120), path, 10);
        await Task.Delay(600);
        recorder.Pause();
        await Task.Delay(1000);
        recorder.Resume();
        await Task.Delay(600);
        var length = await recorder.StopAsync();

        Assert.InRange(length.TotalSeconds, 1.0, 1.8);
    }

    [Theory]
    [InlineData(1281, 721, 1280, 720)]
    [InlineData(5120, 1440, 3840, 1080)]
    [InlineData(1, 1, 2, 2)]
    public void Video_size_is_even_and_fits_the_encoder(int width, int height, int expectedWidth, int expectedHeight)
    {
        Assert.Equal((expectedWidth, expectedHeight), ScreenRecorder.OutputSize(width, height));
    }

    private static async Task<((byte R, byte G, byte B) Top, (byte R, byte G, byte B) Bottom)> TopAndBottomColours(string path)
    {
        var clip = await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(path));
        var composition = new MediaComposition();
        composition.Clips.Add(clip);
        using var thumbnail = await composition.GetThumbnailAsync(TimeSpan.FromMilliseconds(500), 320, 240, VideoFramePrecision.NearestFrame);
        var decoder = await BitmapDecoder.CreateAsync(thumbnail);
        var pixels = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
        var width = (int)decoder.PixelWidth;
        var height = (int)decoder.PixelHeight;

        (byte, byte, byte) At(int x, int y)
        {
            var i = (y * width + x) * 4;
            return (pixels[i + 2], pixels[i + 1], pixels[i]);
        }

        return (At(width / 2, height / 8), At(width / 2, height * 7 / 8));
    }

    /// <summary>
    /// Frames that are red on top and blue underneath, to check the video isn't upside down. Like real frames,
    /// they're stored bottom row first.
    /// </summary>
    private sealed class RedOverBlue(int width, int height) : IFrameSource
    {
        public int Width => width;

        public int Height => height;

        public void Capture(byte[] pixels)
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var i = (y * width + x) * 4;
                    var top = y >= height / 2; // row 0 is the bottom
                    pixels[i] = top ? (byte)0 : (byte)255;     // blue
                    pixels[i + 1] = 0;                        // green
                    pixels[i + 2] = top ? (byte)255 : (byte)0; // red
                    pixels[i + 3] = 255;
                }
            }
        }

        public void Dispose()
        {
        }
    }
}
