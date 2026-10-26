using System.Windows;
using Overmark.Editor;

namespace Overmark.Tests;

/// <summary>Marks as they're put on the clipboard (the clipboard itself is left alone).</summary>
public class MarkClipboardTests
{
    private static readonly StrokeStyle Red = new(Palette.Colours[0], 4);

    [Fact]
    public void A_mark_comes_back_as_it_was_copied()
    {
        var arrow = new CopiedMark(new ArrowMark(Red, new Point(10, 20), new Point(30, 40.5)), Measures: false);
        var dimension = new CopiedMark(
            new DimensionMark(Red, new Point(1, 1), new Point(9, 1), new Point(5, -3), DimensionKind.Horizontal, "50 mm", SetsScale: true), Measures: true);

        Assert.Equal(arrow, MarkClipboard.FromText(MarkClipboard.ToText(arrow)));
        Assert.Equal(dimension, MarkClipboard.FromText(MarkClipboard.ToText(dimension)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a mark")]
    [InlineData("{}")]
    [InlineData("""{"mark":{"type":"sticker","colour":"#FFFF0000","width":2}}""")]
    public void Anything_else_isnt_a_mark(string text) => Assert.Null(MarkClipboard.FromText(text));
}
