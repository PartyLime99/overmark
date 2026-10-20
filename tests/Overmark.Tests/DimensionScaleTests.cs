using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Overmark.Editor;

namespace Overmark.Tests;

public sealed class DimensionScaleTests : IDisposable
{
    private const double Tolerance = 8;

    private static readonly StrokeStyle Red = new(Palette.Colours[0], 4);

    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly EditorDocument _document = new(BlankImage());

    // Lengths are shown with the user's decimal point; these tests expect a full stop.
    public DimensionScaleTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    private static BitmapSource BlankImage()
    {
        var image = BitmapSource.Create(500, 400, 96, 96, PixelFormats.Bgr32, null, new byte[500 * 400 * 4], 500 * 4);
        image.Freeze();
        return image;
    }

    /// <summary>A horizontal dimension <paramref name="length"/> pixels long, at height <paramref name="y"/>, saying what it measures.</summary>
    private static DimensionMark Measuring(double length, double y) =>
        new(Red, new Point(10, y), new Point(10 + length, y), new Point(10 + length / 2, y - 20), DimensionKind.Horizontal, $"{length} px");

    private DimensionMark Mark(int index) => Assert.IsType<DimensionMark>(_document.Marks[index]);

    /// <summary>Types <paramref name="text"/> into the dimension at <paramref name="index"/>, as the move tool's double-click does.</summary>
    private void Type(int index, string text)
    {
        var tools = new DrawingTools();
        tools.Edited += _document.Replace;
        tools.EditText(_document.Marks[index]);
        tools.SetText(text);
        tools.Finish();
    }

    [Theory]
    [InlineData("50 mm", 0.25, "mm", 0, true)]
    [InlineData("  2.50in ", 0.0125, "in", 2, false)]
    [InlineData("1,5 CM", 0.0075, "cm", 1, true)]
    [InlineData("8\"", 0.04, "\"", 0, false)]
    [InlineData(".5 m", 0.0025, "m", 1, true)]
    [InlineData("200 µm", 1, "µm", 0, true)]
    public void A_length_with_a_unit_sets_a_scale(string text, double unitsPerPixel, string unit, int decimals, bool spaced)
    {
        var scale = Assert.IsType<DimensionScale>(DimensionScale.FromText(text, 200));

        Assert.Equal(unitsPerPixel, scale.UnitsPerPixel, 9);
        Assert.Equal((unit, decimals, spaced), (scale.Unit, scale.Decimals, scale.Spaced));
    }

    [Theory]
    [InlineData("50")]
    [InlineData("200 px")]
    [InlineData("Ø50 mm")]
    [InlineData("0 mm")]
    [InlineData("50 mm REF")]
    [InlineData("")]
    public void Anything_else_doesnt(string text) => Assert.Null(DimensionScale.FromText(text, 200));

    [Fact]
    public void Lengths_are_given_as_the_scale_was_typed()
    {
        var scale = DimensionScale.FromText("50.0 mm", 200)!;

        Assert.Equal("37.5 mm", scale.Format(150));
        Assert.Equal("1 mm = 4 px", scale.Describe());
        Assert.Equal("2.5in", DimensionScale.FromText("5.0in", 200)!.Format(100));
        Assert.Equal("150 px", DimensionScale.Measured(150, null));
        Assert.Equal("35 mm", DimensionScale.Measured(140, DimensionScale.FromText("50 mm", 200)));
    }

    [Fact]
    public void Typing_a_length_into_a_dimension_rescales_the_others_and_undoes_in_one_step()
    {
        _document.Add(Measuring(200, 100));
        _document.Add(Measuring(100, 200));
        _document.Add(Measuring(80, 300) with { Text = "Gap" });

        Type(0, "50.0 mm");

        Assert.Equal(DimensionScale.FromText("50.0 mm", 200), _document.Scale);
        Assert.True(Mark(0).SetsScale);
        Assert.Equal("25.0 mm", Mark(1).Text);
        Assert.Equal("Gap", Mark(2).Text); // typed over, so left alone

        _document.Undo();
        Assert.Null(_document.Scale);
        Assert.Equal(["200 px", "100 px", "Gap"], _document.Marks.Cast<DimensionMark>().Select(d => d.Text));

        _document.Redo();
        Assert.Equal("25.0 mm", Mark(1).Text);
    }

    [Fact]
    public void Leaving_a_dimensions_text_as_it_was_doesnt_set_the_scale()
    {
        _document.Add(Measuring(200, 100) with { Text = "50 mm" });

        Type(0, "50 mm");

        Assert.False(Mark(0).SetsScale);
        Assert.Null(_document.Scale);
    }

    [Fact]
    public void The_latest_length_typed_sets_the_scale_and_the_one_before_keeps_its_words()
    {
        _document.Add(Measuring(200, 100));
        _document.Add(Measuring(100, 200));
        _document.Add(Measuring(80, 300));
        Type(0, "50 mm");
        Assert.Equal("20 mm", Mark(2).Text);

        Type(1, "10 mm");

        Assert.Equal(DimensionScale.FromText("10 mm", 100), _document.Scale);
        Assert.Equal(("50 mm", false), (Mark(0).Text, Mark(0).SetsScale));
        Assert.True(Mark(1).SetsScale);
        Assert.Equal("8 mm", Mark(2).Text);
    }

    [Fact]
    public void Changing_the_scales_dimension_to_other_words_goes_back_to_pixels()
    {
        _document.Add(Measuring(200, 100));
        _document.Add(Measuring(100, 200));
        Type(0, "50 mm");

        Type(0, "Ø50");

        Assert.Null(_document.Scale);
        Assert.False(Mark(0).SetsScale);
        Assert.Equal("100 px", Mark(1).Text);
    }

    [Fact]
    public void Erasing_the_scales_dimension_goes_back_to_pixels()
    {
        _document.Add(Measuring(200, 100));
        _document.Add(Measuring(100, 200));
        Type(0, "50 mm");

        _document.EraseAt(new Point(110, 80), 2); // its dimension line
        _document.FinishErasing();

        Assert.Null(_document.Scale);
        Assert.Equal("100 px", Assert.IsType<DimensionMark>(Assert.Single(_document.Marks)).Text);

        _document.Undo();
        Assert.Equal("25 mm", Mark(1).Text);
    }

    [Fact]
    public void Moving_the_scales_dimension_point_rescales_the_others_once_its_let_go()
    {
        _document.Add(Measuring(200, 100));
        _document.Add(Measuring(100, 200));
        Type(0, "50 mm");
        var move = new MoveTool(_document);

        move.PointerDown(new Point(110, 80)); // select it by its line
        move.PointerUp();
        move.PointerDown(new Point(210, 100)); // its point B
        move.PointerMove(new Point(260, 100), false);
        move.PointerMove(new Point(360, 100), false);
        Assert.Equal("25 mm", Mark(1).Text); // not yet
        move.PointerUp();

        Assert.Equal("50 mm", Mark(0).Text); // as typed, now over 350 px
        Assert.Equal("14 mm", Mark(1).Text);

        _document.Undo();
        Assert.Equal(("25 mm", new Point(210, 100)), (Mark(1).Text, Mark(0).B));
    }

    [Fact]
    public void New_dimensions_start_in_the_scale()
    {
        var tools = new DrawingTools { Tool = ToolKind.Dimension, Scale = DimensionScale.FromText("50.0 mm", 200) };
        var committed = new List<Mark>();
        tools.Committed += committed.Add;

        foreach (var point in new[] { new Point(100, 100), new Point(300, 100), new Point(200, 60) })
        {
            tools.PointerDown(point, false, Tolerance);
            tools.PointerUp(point, false);
        }

        Assert.Equal("50.0 mm", tools.Typing?.Text);
        tools.Finish();
        var dimension = Assert.IsType<DimensionMark>(Assert.Single(committed));
        Assert.Equal(("50.0 mm", false), (dimension.Text, dimension.SetsScale)); // left as measured: doesn't set the scale
    }

    [Fact]
    public void A_new_dimension_with_a_length_typed_in_sets_the_scale()
    {
        _document.Add(Measuring(100, 200));
        var tools = new DrawingTools { Tool = ToolKind.Dimension };
        tools.Committed += _document.Add;
        foreach (var point in new[] { new Point(100, 100), new Point(300, 100), new Point(200, 60) })
        {
            tools.PointerDown(point, false, Tolerance);
            tools.PointerUp(point, false);
        }

        tools.SetText("2 in");
        tools.Finish();

        Assert.True(Mark(1).SetsScale);
        Assert.Equal("1 in", Mark(0).Text);
    }

    [Fact]
    public void Which_dimension_sets_the_scale_is_kept_with_the_marks()
    {
        var dimension = Measuring(200, 100) with { Text = "50 mm", SetsScale = true };

        Assert.Equal(dimension, MarkFormat.FromData(MarkFormat.ToData(dimension)));
    }
}
