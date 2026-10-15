using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;

namespace Overmark.Recording;

/// <summary>
/// Records frames from an <see cref="IFrameSource"/> to an MP4 (H.264, no audio) with Windows' own encoder, which
/// uses the graphics card when it can. Frames are taken at a steady rate on their own thread; if the encoder falls
/// behind, frames are skipped rather than delaying the recording.
/// </summary>
internal sealed class ScreenRecorder
{
    // H.264 encoders commonly top out around 4K; bigger areas are scaled down to fit.
    private const int MaxWidth = 3840;
    private const int MaxHeight = 2160;

    private readonly IFrameSource _source;
    private readonly int _framesPerSecond;
    private readonly BlockingCollection<(byte[] Pixels, TimeSpan Time)> _frames = new(boundedCapacity: 30);
    private readonly Stopwatch _clock = new();
    private readonly object _pauseLock = new();
    private TimeSpan _pausedTotal;
    private TimeSpan? _pausedAt;
    private Thread? _captureThread;
    private Task? _encoding;
    private FileStream? _file;
    private volatile bool _stopping;

    private ScreenRecorder(IFrameSource source, int framesPerSecond)
    {
        _source = source;
        _framesPerSecond = framesPerSecond;
    }

    /// <summary>The first frame, for the preview shown when recording ends.</summary>
    public BitmapSource? FirstFrame { get; private set; }

    public bool IsPaused => _pausedAt is not null;

    /// <summary>How long has been recorded, not counting pauses.</summary>
    public TimeSpan Elapsed
    {
        get
        {
            lock (_pauseLock)
            {
                return _clock.Elapsed - _pausedTotal - (_pausedAt is { } at ? _clock.Elapsed - at : TimeSpan.Zero);
            }
        }
    }

    /// <summary>Starts recording to <paramref name="path"/> (which mustn't exist yet). Takes ownership of <paramref name="source"/>.</summary>
    public static async Task<ScreenRecorder> StartAsync(IFrameSource source, string path, int framesPerSecond)
    {
        var recorder = new ScreenRecorder(source, framesPerSecond);
        try
        {
            await recorder.BeginAsync(path);
            return recorder;
        }
        catch
        {
            source.Dispose();
            recorder._file?.Dispose();
            throw;
        }
    }

    public void Pause()
    {
        lock (_pauseLock)
        {
            _pausedAt ??= _clock.Elapsed;
        }
    }

    public void Resume()
    {
        lock (_pauseLock)
        {
            if (_pausedAt is { } at)
            {
                _pausedTotal += _clock.Elapsed - at;
                _pausedAt = null;
            }
        }
    }

    /// <summary>Stops recording and waits for the video to be finished. Returns its length.</summary>
    public async Task<TimeSpan> StopAsync()
    {
        Resume();
        var length = Elapsed;
        _stopping = true;
        await Task.Run(() => _captureThread?.Join());
        try
        {
            await _encoding!;
        }
        finally
        {
            _file?.Dispose();
            _source.Dispose();
        }

        return length;
    }

    /// <summary>The video's size: the frame size, scaled down to fit the encoder, in even numbers as H.264 needs.</summary>
    internal static (int Width, int Height) OutputSize(int width, int height)
    {
        var scale = Math.Min(1.0, Math.Min((double)MaxWidth / width, (double)MaxHeight / height));
        return (Even(width * scale), Even(height * scale));

        static int Even(double value) => Math.Max(2, (int)value / 2 * 2);
    }

    private async Task BeginAsync(string path)
    {
        var input = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, (uint)_source.Width, (uint)_source.Height);
        var stream = new MediaStreamSource(new VideoStreamDescriptor(input)) { BufferTime = TimeSpan.Zero };
        stream.Starting += (_, e) => e.Request.SetActualStartPosition(TimeSpan.Zero);
        stream.SampleRequested += OnSampleRequested;

        var (width, height) = OutputSize(_source.Width, _source.Height);
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto);
        profile.Audio = null;
        profile.Video.Width = (uint)width;
        profile.Video.Height = (uint)height;
        profile.Video.FrameRate.Numerator = (uint)_framesPerSecond;
        profile.Video.FrameRate.Denominator = 1;
        profile.Video.PixelAspectRatio.Numerator = 1;
        profile.Video.PixelAspectRatio.Denominator = 1;

        // Plenty for sharp screen text without huge files: about 6 Mbit/s for 1080p at 30 frames a second.
        profile.Video.Bitrate = (uint)Math.Clamp(width * (double)height * _framesPerSecond * 0.1, 1_000_000, 40_000_000);

        _file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite);
        var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
        var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(stream, _file.AsRandomAccessStream(), profile);
        if (!prepared.CanTranscode)
        {
            throw new InvalidOperationException(
                $"Windows can't make this video ({prepared.FailureReason}). On Windows N editions, install the Media Feature Pack.");
        }

        _clock.Start();
        _captureThread = new Thread(CaptureFrames) { IsBackground = true, Name = "Overmark recording" };
        _captureThread.Start();
        _encoding = prepared.TranscodeAsync().AsTask();
    }

    private void CaptureFrames()
    {
        var interval = TimeSpan.FromSeconds(1.0 / _framesPerSecond);
        var next = TimeSpan.Zero;
        try
        {
            while (!_stopping)
            {
                if (IsPaused)
                {
                    Thread.Sleep(20);
                    continue;
                }

                var now = Elapsed;
                if (now < next)
                {
                    Thread.Sleep(Math.Max(1, (int)(next - now).TotalMilliseconds));
                    continue;
                }

                var pixels = new byte[_source.Width * _source.Height * 4];
                _source.Capture(pixels);
                FirstFrame ??= ToBitmap(pixels);

                // If the encoder is behind, skip this frame rather than hold up the recording.
                _frames.TryAdd((pixels, now));

                // After a hiccup, carry on from now instead of rushing to catch up.
                next = now - next > interval ? now + interval : next + interval;
            }
        }
        finally
        {
            _frames.CompleteAdding();
        }
    }

    private void OnSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
    {
        var request = args.Request;
        var deferral = request.GetDeferral();
        Task.Run(() =>
        {
            try
            {
                // No frame and no more coming means the recording has ended.
                if (_frames.TryTake(out var frame, Timeout.Infinite))
                {
                    var sample = MediaStreamSample.CreateFromBuffer(frame.Pixels.AsBuffer(), frame.Time);
                    sample.Duration = TimeSpan.FromSeconds(1.0 / _framesPerSecond);
                    request.Sample = sample;
                }
                else
                {
                    request.Sample = null;
                }
            }
            finally
            {
                deferral.Complete();
            }
        });
    }

    /// <summary>A picture of one frame, turned the right way up (frames are stored bottom row first).</summary>
    private BitmapSource ToBitmap(byte[] pixels)
    {
        var stride = _source.Width * 4;
        var upright = new byte[pixels.Length];
        for (var row = 0; row < _source.Height; row++)
        {
            Buffer.BlockCopy(pixels, row * stride, upright, (_source.Height - 1 - row) * stride, stride);
        }

        var bitmap = BitmapSource.Create(_source.Width, _source.Height, 96, 96, PixelFormats.Bgr32, null, upright, stride);
        bitmap.Freeze();
        return bitmap;
    }
}
