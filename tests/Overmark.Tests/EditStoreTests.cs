using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Overmark.Editor;

namespace Overmark.Tests;

public sealed class EditStoreTests : IDisposable
{
    private static readonly StrokeStyle Red = new(Palette.Colours[0], 4);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "OvermarkTests", Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc);

    public EditStoreTests() => Directory.CreateDirectory(_folder);

    private string Capture => Path.Combine(_folder, "capture.png");

    private string StoreFolder => Path.Combine(_folder, "edits");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void A_saved_capture_opens_with_its_marks()
    {
        var store = Store();
        var document = Document(new LineMark(Red, new Point(1, 1), new Point(10, 10)));
        SaveFile(document);
        store.KeepSaved(Capture, document);

        var kept = Assert.IsType<KeptEdit>(store.Find(Capture));

        Assert.False(kept.Draft);
        Assert.Equal(new LineMark(Red, new Point(1, 1), new Point(10, 10)), Assert.Single(kept.Marks));
        Assert.Equal(20, kept.Original.PixelWidth);
    }

    [Fact]
    public void A_draft_wins_until_it_is_discarded()
    {
        var store = Store();
        var document = Document(new LineMark(Red, new Point(1, 1), new Point(10, 10)));
        SaveFile(document);
        store.KeepSaved(Capture, document);

        document.Add(new LineMark(Red, new Point(2, 2), new Point(5, 5)));
        store.KeepDraft(Capture, document);
        Assert.True(store.HasDraft(Capture));
        var draft = Assert.IsType<KeptEdit>(store.Find(Capture));
        Assert.True(draft.Draft);
        Assert.Equal(2, draft.Marks.Count);

        store.DiscardDraft(Capture);
        Assert.False(store.HasDraft(Capture));
        Assert.Single(Assert.IsType<KeptEdit>(store.Find(Capture)).Marks);
    }

    [Fact]
    public void Saving_replaces_the_draft()
    {
        var store = Store();
        var document = Document(new LineMark(Red, new Point(1, 1), new Point(10, 10)));
        SaveFile(document);
        store.KeepDraft(Capture, document);

        SaveFile(document);
        store.KeepSaved(Capture, document);

        Assert.False(store.HasDraft(Capture));
        Assert.False(Assert.IsType<KeptEdit>(store.Find(Capture)).Draft);
    }

    [Fact]
    public void A_file_changed_by_something_else_opens_as_it_is()
    {
        var store = Store();
        var document = Document(new LineMark(Red, new Point(1, 1), new Point(10, 10)));
        SaveFile(document);
        store.KeepSaved(Capture, document);

        File.WriteAllBytes(Capture, [1, 2, 3]);

        Assert.Null(store.Find(Capture));
    }

    [Fact]
    public void Copies_last_a_day()
    {
        var store = Store();
        var document = Document(new LineMark(Red, new Point(1, 1), new Point(10, 10)));
        SaveFile(document);
        store.KeepSaved(Capture, document);

        _now += TimeSpan.FromHours(23);
        Assert.NotNull(store.Find(Capture));

        _now += TimeSpan.FromHours(2);
        Assert.Null(store.Find(Capture));
        store.CleanUp();
        Assert.Empty(Directory.EnumerateFiles(StoreFolder));
    }

    [Fact]
    public void Forgetting_removes_everything()
    {
        var store = Store();
        var document = Document(new LineMark(Red, new Point(1, 1), new Point(10, 10)));
        SaveFile(document);
        store.KeepSaved(Capture, document);
        store.KeepDraft(Capture, document);

        store.Forget(Capture);

        Assert.Null(store.Find(Capture));
        Assert.Empty(Directory.EnumerateFiles(StoreFolder));
    }

    [Fact]
    public void The_crop_comes_back()
    {
        var store = Store();
        var document = Document();
        document.CropTo(new Rect(2, 1, 10, 8));
        SaveFile(document);
        store.KeepSaved(Capture, document);

        Assert.Equal(new Int32Rect(2, 1, 10, 8), Assert.IsType<KeptEdit>(store.Find(Capture)).Crop);
    }

    [Fact]
    public void A_restored_draft_reads_as_unsaved_until_saved()
    {
        var restored = new EditorDocument(WhiteImage(), new Int32Rect(0, 0, 20, 10), [], unsaved: true);
        Assert.True(restored.HasUnsavedChanges);

        restored.MarkSaved();
        Assert.False(restored.HasUnsavedChanges);

        var saved = new EditorDocument(WhiteImage(), new Int32Rect(0, 0, 20, 10), [], unsaved: false);
        Assert.False(saved.HasUnsavedChanges);
    }

    [Fact]
    public void Every_kind_of_mark_survives_the_round_trip()
    {
        Mark[] marks =
        [
            new FreehandMark(Red, [new Point(1, 2), new Point(3, 4.5), new Point(6, 7)]),
            new HighlighterMark(new StrokeStyle(Palette.Colours[2], 5), [new Point(0, 0), new Point(9, 0)]),
            new LineMark(Red, new Point(1, 1), new Point(8, 2)),
            new ArrowMark(new StrokeStyle(Color.FromRgb(0x12, 0x34, 0x56), 7.5), new Point(0, 9), new Point(9, 0)),
            new CurveMark(Red, new Point(0, 0), new Point(4, 9), new Point(9, 0), Arrowhead: true),
            new RectangleMark(Red, new Rect(1, 2, 3, 4)),
            new EllipseMark(Red, new Rect(5, 6, 7, 8)),
            new TextMark(Red, new Point(3, 3), "Two lines\r\nof text", Boxed: true),
            new DimensionMark(Red, new Point(1, 1), new Point(9, 1), new Point(5, -3), DimensionKind.Horizontal, "8 px", new Point(12, -6), -30),
            new DimensionMark(Red, new Point(1, 1), new Point(1, 9), new Point(-3, 5), DimensionKind.Vertical, ""),
        ];

        foreach (var mark in marks)
        {
            var json = JsonSerializer.Serialize(MarkFormat.ToData(mark));
            var back = MarkFormat.FromData(JsonSerializer.Deserialize<MarkData>(json)!);

            Assert.NotNull(back);
            Assert.Equal(mark.GetType(), back.GetType());
            Assert.Equal(json, JsonSerializer.Serialize(MarkFormat.ToData(back)));
        }
    }

    [Fact]
    public void Unknown_or_broken_marks_are_skipped()
    {
        Assert.Null(MarkFormat.FromData(new MarkData { Type = "sticker", Colour = "#FFFF0000", Width = 2 }));
        Assert.Null(MarkFormat.FromData(new MarkData { Type = "line", Colour = "#FFFF0000", Width = 2, Points = [1, 2, 3] }));
        Assert.Null(MarkFormat.FromData(new MarkData { Type = "line", Colour = "red", Width = 2, Points = [1, 2, 3, 4] }));
    }

    private EditStore Store() => new(StoreFolder, () => _now);

    private static BitmapSource WhiteImage()
    {
        var pixels = Enumerable.Repeat((byte)0xFF, 20 * 10 * 4).ToArray();
        var image = BitmapSource.Create(20, 10, 96, 96, PixelFormats.Bgr32, null, pixels, 20 * 4);
        image.Freeze();
        return image;
    }

    private static EditorDocument Document(params Mark[] marks)
    {
        var document = new EditorDocument(WhiteImage());
        foreach (var mark in marks)
        {
            document.Add(mark);
        }

        return document;
    }

    /// <summary>Writes the capture file, as saving it in the editor would (each write is a new version of the file).</summary>
    private void SaveFile(EditorDocument document)
    {
        File.WriteAllBytes(Capture, [.. Enumerable.Repeat((byte)document.Marks.Count, 16 + document.Marks.Count)]);
        File.SetLastWriteTimeUtc(Capture, _now.AddSeconds(document.Marks.Count + Directory.EnumerateFiles(_folder).Count()));
    }
}
