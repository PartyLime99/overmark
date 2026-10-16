using Overmark.Capture;

namespace Overmark.Tests;

public sealed class CaptureHistoryTests : IDisposable
{
    private static readonly DateTime Today = DateTime.Today.AddHours(10);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "OvermarkTests", Guid.NewGuid().ToString("N"));

    public CaptureHistoryTests() => Directory.CreateDirectory(_folder);

    private string HistoryPath => Path.Combine(_folder, "history.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Lists_the_days_captures_newest_first()
    {
        var history = new CaptureHistory(HistoryPath);
        history.Add(Record("a.png", Today));
        history.Add(Record("b.png", Today.AddMinutes(5)));
        history.Add(Record("old.png", Today.AddDays(-1)));

        var today = history.TakenOn(Today);

        Assert.Equal(["b.png", "a.png"], today.Select(r => Path.GetFileName(r.Path)));
    }

    [Fact]
    public void Leaves_out_captures_whose_files_are_gone()
    {
        var history = new CaptureHistory(HistoryPath);
        history.Add(Record("kept.png", Today));
        history.Add(Record("deleted.png", Today.AddMinutes(1)));
        File.Delete(Path.Combine(_folder, "deleted.png"));

        Assert.Equal(["kept.png"], history.TakenOn(Today).Select(r => Path.GetFileName(r.Path)));
    }

    [Fact]
    public void Survives_a_restart()
    {
        new CaptureHistory(HistoryPath).Add(Record("a.png", Today, seconds: 42));

        var record = Assert.Single(new CaptureHistory(HistoryPath).TakenOn(Today));

        Assert.Equal(CaptureKind.FullScreen, record.Kind);
        Assert.True(record.IsVideo);
        Assert.Equal(42, record.Seconds);
    }

    [Fact]
    public void The_same_file_is_listed_once()
    {
        var history = new CaptureHistory(HistoryPath);
        history.Add(Record("a.png", Today));
        history.Add(Record("a.png", Today.AddMinutes(1)));

        Assert.Single(history.TakenOn(Today));
    }

    [Fact]
    public void Old_captures_are_dropped()
    {
        var history = new CaptureHistory(HistoryPath);
        history.Add(Record("ancient.png", Today.AddDays(-30)));
        history.Add(Record("new.png", Today));

        Assert.DoesNotContain("ancient.png", File.ReadAllText(HistoryPath));
    }

    [Fact]
    public void The_latest_screenshot_skips_recordings()
    {
        var history = new CaptureHistory(HistoryPath);
        history.Add(Record("shot.png", Today));
        history.Add(Record("clip.mp4", Today.AddMinutes(1), seconds: 5));

        Assert.Equal("shot.png", Path.GetFileName(history.LatestScreenshot()?.Path));
    }

    [Fact]
    public void An_unreadable_file_starts_a_new_history()
    {
        File.WriteAllText(HistoryPath, "{ not json");
        var history = new CaptureHistory(HistoryPath);

        Assert.Empty(history.TakenOn(Today));
        history.Add(Record("a.png", Today));
        Assert.Single(history.TakenOn(Today));
    }

    [Fact]
    public void Adding_raises_changed()
    {
        var history = new CaptureHistory(HistoryPath);
        var raised = 0;
        history.Changed += () => raised++;

        history.Add(Record("a.png", Today));

        Assert.Equal(1, raised);
    }

    /// <summary>A capture whose file exists, so it's listed.</summary>
    private CaptureRecord Record(string name, DateTime takenAt, double? seconds = null)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, "");
        return new CaptureRecord(path, CaptureKind.FullScreen, takenAt, 1280, 720, seconds);
    }
}
