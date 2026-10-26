using System.Windows;
using Overmark.Editor;

namespace Overmark.Tests;

/// <summary>Marks as they're put on the clipboard (the clipboard itself is left alone).</summary>
public class MarkClipboardTests
{
    private static readonly StrokeStyle Red = new(Palette.Colours[0], 4);

    [Fact]
    public void Marks_come_back_as_they_were_copied_in_order()
    {
        CopiedMark[] copied =
        [
            new(new ArrowMark(Red, new Point(10, 20), new Point(30, 40.5)), Measures: false),
            new(new DimensionMark(Red, new Point(1, 1), new Point(9, 1), new Point(5, -3), DimensionKind.Horizontal, "50 mm", SetsScale: true), Measures: true),
            new(new RectangleMark(Red, new Rect(1, 2, 3, 4)), Measures: false),
        ];

        Assert.Equal(copied, MarkClipboard.FromText(MarkClipboard.ToText(copied)));
    }

    [Fact]
    public void A_mark_of_a_kind_this_version_doesnt_know_is_left_out()
    {
        var text = """{"marks":[{"mark":{"type":"sticker","colour":"#FFFF0000","width":2}},{"mark":{"type":"line","colour":"#FFFF0000","width":2,"points":[1,2,3,4]}}]}""";

        var copied = Assert.Single(MarkClipboard.FromText(text)!);

        Assert.Equal(new LineMark(new StrokeStyle(System.Windows.Media.Color.FromRgb(0xFF, 0, 0), 2), new Point(1, 2), new Point(3, 4)), copied.Mark);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a mark")]
    [InlineData("{}")]
    [InlineData("""{"marks":[]}""")]
    [InlineData("""{"marks":[{"mark":{"type":"sticker","colour":"#FFFF0000","width":2}}]}""")]
    public void Anything_else_isnt_marks(string text) => Assert.Null(MarkClipboard.FromText(text));
}
