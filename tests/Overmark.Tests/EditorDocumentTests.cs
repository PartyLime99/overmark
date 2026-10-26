using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Overmark.Editor;

namespace Overmark.Tests;

public class EditorDocumentTests
{
    private static readonly StrokeStyle Red = new(Palette.Colours[0], 4);

    private static BitmapSource WhiteImage(int width, int height)
    {
        var pixels = Enumerable.Repeat((byte)0xFF, width * height * 4).ToArray();
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4);
        image.Freeze();
        return image;
    }

    private static LineMark Line(double y) => new(Red, new Point(0, y), new Point(40, y));

    [Fact]
    public void Undo_and_redo_step_through_marks()
    {
        var document = new EditorDocument(WhiteImage(4, 4));
        document.Add(Line(1));
        document.Add(Line(2));

        document.Undo();
        Assert.Equal([Line(1)], document.Marks);
        Assert.True(document.CanRedo);

        document.Redo();
        Assert.Equal([Line(1), Line(2)], document.Marks);
        Assert.False(document.CanRedo);
    }

    [Fact]
    public void A_new_mark_clears_redo()
    {
        var document = new EditorDocument(WhiteImage(4, 4));
        document.Add(Line(1));
        document.Undo();

        document.Add(Line(3));

        Assert.False(document.CanRedo);
        Assert.Equal([Line(3)], document.Marks);
    }

    [Fact]
    public void Unsaved_changes_are_tracked_through_undo_and_redo()
    {
        var document = new EditorDocument(WhiteImage(4, 4));
        Assert.False(document.HasUnsavedChanges);

        document.Add(Line(1));
        Assert.True(document.HasUnsavedChanges);

        document.MarkSaved();
        Assert.False(document.HasUnsavedChanges);

        document.Undo();
        Assert.True(document.HasUnsavedChanges);

        document.Redo();
        Assert.False(document.HasUnsavedChanges);
    }

    [Fact]
    public void The_eraser_removes_only_marks_it_touches()
    {
        var document = new EditorDocument(WhiteImage(40, 40));
        document.Add(Line(5));
        document.Add(Line(20));
        document.Add(Line(35));

        document.EraseAt(new Point(10, 21), radius: 2);
        document.FinishErasing();

        Assert.Equal([Line(5), Line(35)], document.Marks);
    }

    [Fact]
    public void One_eraser_drag_undoes_in_one_step_and_puts_marks_back_in_order()
    {
        var document = new EditorDocument(WhiteImage(40, 40));
        document.Add(Line(5));
        document.Add(Line(20));
        document.Add(Line(35));

        document.EraseAt(new Point(10, 5), radius: 2);
        document.EraseAt(new Point(10, 35), radius: 2);
        document.FinishErasing();
        Assert.Equal([Line(20)], document.Marks);

        document.Undo();
        Assert.Equal([Line(5), Line(20), Line(35)], document.Marks);

        document.Redo();
        Assert.Equal([Line(20)], document.Marks);

        document.Undo();
        document.Undo(); // then the third line drawn
        Assert.Equal([Line(5), Line(20)], document.Marks);
    }

    [Fact]
    public void Erasing_inside_an_empty_rectangle_leaves_it()
    {
        var rectangle = new RectangleMark(Red, new Rect(10, 10, 100, 100));

        Assert.False(rectangle.IsTouchedBy(new Point(60, 60), 5));
        Assert.True(rectangle.IsTouchedBy(new Point(60, 12), 5));
        Assert.True(new FreehandMark(Red, [new Point(50, 50)]).IsTouchedBy(new Point(54, 50), 3));

        var circle = new EllipseMark(Red, new Rect(0, 0, 100, 100));
        Assert.False(circle.IsTouchedBy(new Point(50, 50), 5));
        Assert.True(circle.IsTouchedBy(new Point(50, 2), 5));
    }

    [Fact]
    public void Changing_a_mark_repeatedly_is_one_undo_step()
    {
        var document = new EditorDocument(WhiteImage(4, 4));
        document.Add(Line(1));
        document.Add(Line(2));

        document.Change(0, Line(5));
        document.Change(0, Line(9));
        document.FinishChanging();
        Assert.Equal([Line(9), Line(2)], document.Marks);

        document.Undo();
        Assert.Equal([Line(1), Line(2)], document.Marks);
        document.Redo();
        Assert.Equal([Line(9), Line(2)], document.Marks);
    }

    [Fact]
    public void A_change_back_to_the_start_is_not_an_undo_step()
    {
        var document = new EditorDocument(WhiteImage(4, 4));
        document.Add(Line(1));
        document.MarkSaved();

        document.Change(0, Line(5));
        document.Change(0, Line(1));
        document.FinishChanging();

        Assert.False(document.HasUnsavedChanges);
    }

    [Fact]
    public void Removing_a_mark_can_be_undone_in_place()
    {
        var document = new EditorDocument(WhiteImage(4, 4));
        document.Add(Line(1));
        document.Add(Line(2));
        document.Add(Line(3));

        document.Remove(1);
        Assert.Equal([Line(1), Line(3)], document.Marks);

        document.Undo();
        Assert.Equal([Line(1), Line(2), Line(3)], document.Marks);
    }

    [Fact]
    public void Replacing_a_mark_keeps_its_place_and_undoes_in_one_step()
    {
        var document = new EditorDocument(WhiteImage(4, 4));
        var middle = new TextMark(Red, new Point(1, 1), "Old", Boxed: false);
        document.Add(Line(1));
        document.Add(middle);
        document.Add(Line(3));
        var changed = middle with { Text = "New" };

        document.Replace(middle, changed);
        Assert.Equal([Line(1), changed, Line(3)], document.Marks);
        document.Undo();
        Assert.Equal([Line(1), middle, Line(3)], document.Marks);

        // With nothing left to say, it goes; a mark that's gone already is left alone.
        document.Replace(middle, null);
        Assert.Equal([Line(1), Line(3)], document.Marks);
        document.Replace(middle, changed);
        Assert.Equal([Line(1), Line(3)], document.Marks);
        document.Undo();
        Assert.Equal([Line(1), middle, Line(3)], document.Marks);
    }

    [Fact]
    public void Touching_anywhere_on_text_erases_it()
    {
        var text = new TextMark(new StrokeStyle(Palette.Colours[0], 20), new Point(100, 100), "Label", Boxed: false);
        var area = text.TextArea;

        Assert.True(text.IsTouchedBy(new Point(area.Left + area.Width / 2, area.Top + area.Height / 2), 1));
        Assert.True(text.IsTouchedBy(new Point(area.Right + 4, area.Top), 5));
        Assert.False(text.IsTouchedBy(new Point(area.Right + 20, area.Top), 5));

        // The box around boxed text counts too.
        var boxed = text with { Boxed = true };
        Assert.True(boxed.Bounds.Contains(text.Bounds));
        Assert.True(boxed.IsTouchedBy(new Point(boxed.Bounds.Left + 1, boxed.Bounds.Top + 1), 0));
    }

    [Fact]
    public void Rendering_draws_text_in_its_colour()
    {
        var found = RunOnUiThread(() =>
        {
            var document = new EditorDocument(WhiteImage(120, 40));
            document.Add(new TextMark(new StrokeStyle(Palette.Colours[4], 30), new Point(5, 0), "HHH", Boxed: false));
            var rendered = document.Render();

            var pixels = new byte[120 * 40 * 4];
            rendered.CopyPixels(pixels, 120 * 4, 0);
            return Enumerable.Range(0, 120 * 40).Any(i =>
                Color.FromRgb(pixels[i * 4 + 2], pixels[i * 4 + 1], pixels[i * 4]) == Palette.Colours[4]);
        });

        Assert.True(found);
    }

    [Fact]
    public void Crop_is_whole_pixels_inside_the_image_and_undoable()
    {
        var document = new EditorDocument(WhiteImage(100, 80));

        document.CropTo(new Rect(10.4, -5, 50.2, 40));
        Assert.Equal(new Int32Rect(10, 0, 51, 35), document.Crop);
        Assert.True(document.HasUnsavedChanges);

        // A second crop can only shrink the first.
        document.CropTo(new Rect(0, 0, 30, 30));
        Assert.Equal(new Int32Rect(10, 0, 20, 30), document.Crop);

        document.Undo();
        document.Undo();
        Assert.Equal(new Int32Rect(0, 0, 100, 80), document.Crop);
    }

    [Fact]
    public void Rendering_a_crop_gives_just_that_area()
    {
        var (size, lineColour, background) = RunOnUiThread(() =>
        {
            var document = new EditorDocument(WhiteImage(40, 40));
            document.Add(Line(30));
            document.CropTo(new Rect(10, 20, 20, 15)); // the line at y = 30 is at y = 10 in the crop
            var rendered = document.Render();

            var pixels = new byte[rendered.PixelWidth * rendered.PixelHeight * 4];
            rendered.CopyPixels(pixels, rendered.PixelWidth * 4, 0);
            Color Pixel(int x, int y) => Color.FromRgb(
                pixels[(y * rendered.PixelWidth + x) * 4 + 2], pixels[(y * rendered.PixelWidth + x) * 4 + 1], pixels[(y * rendered.PixelWidth + x) * 4]);
            return ((rendered.PixelWidth, rendered.PixelHeight), Pixel(5, 10), Pixel(5, 2));
        });

        Assert.Equal((20, 15), size);
        Assert.Equal(Palette.Colours[0], lineColour);
        Assert.Equal(Colors.White, background);
    }

    [Fact]
    public void Rendering_draws_the_marks_onto_the_image()
    {
        var (size, lineColour, background) = RunOnUiThread(() =>
        {
            var document = new EditorDocument(WhiteImage(20, 20));
            document.Add(Line(10));
            var rendered = document.Render();

            var pixels = new byte[20 * 20 * 4];
            rendered.CopyPixels(pixels, 20 * 4, 0);
            Color Pixel(int x, int y) => Color.FromRgb(pixels[(y * 20 + x) * 4 + 2], pixels[(y * 20 + x) * 4 + 1], pixels[(y * 20 + x) * 4]);
            return ((rendered.PixelWidth, rendered.PixelHeight), Pixel(10, 10), Pixel(10, 2));
        });

        Assert.Equal((20, 20), size);
        Assert.Equal(Palette.Colours[0], lineColour);
        Assert.Equal(Colors.White, background);
    }

    [Fact]
    public void Removing_the_white_background_renders_see_through_and_undoes_in_one_step()
    {
        RunOnUiThread(() =>
        {
            // A black square on white. A capture's fourth byte is unused (here 0), which mustn't count as see-through.
            var pixels = new byte[10 * 10 * 4];
            for (var y = 0; y < 10; y++)
            {
                for (var x = 0; x < 10; x++)
                {
                    var p = (y * 10 + x) * 4;
                    var shade = x is >= 3 and < 7 && y is >= 3 and < 7 ? (byte)0 : (byte)255;
                    (pixels[p], pixels[p + 1], pixels[p + 2], pixels[p + 3]) = (shade, shade, shade, 0);
                }
            }

            var image = BitmapSource.Create(10, 10, 96, 96, PixelFormats.Bgr32, null, pixels, 40);
            image.Freeze();
            var document = new EditorDocument(image);
            Assert.False(document.IsSeeThrough);

            Assert.True(document.RemoveWhiteBackground());
            Assert.True(document.IsSeeThrough);
            Assert.True(document.HasUnsavedChanges);

            var rendered = document.Render();
            Assert.Equal(PixelFormats.Bgra32, rendered.Format);
            var bgra = new byte[10 * 10 * 4];
            rendered.CopyPixels(bgra, 40, 0);
            Assert.Equal(0, bgra[3]); // the corner
            Assert.Equal(new byte[] { 0, 0, 0, 255 }, bgra[220..224]); // (5, 5), inside the square

            document.Undo();
            Assert.Same(image, document.Image);
            Assert.False(document.IsSeeThrough);
            Assert.False(document.CanUndo);
            Assert.Equal(PixelFormats.Bgr32, document.Render().Format);

            document.Redo();
            Assert.True(document.IsSeeThrough);
            Assert.False(document.RemoveWhiteBackground()); // nothing more to remove, and no extra undo step
            return true;
        });
    }

    [Fact]
    public void Removing_all_white_takes_the_white_inside_too_as_its_own_undo_step()
    {
        RunOnUiThread(() =>
        {
            // A black ring on white, with white inside it.
            var pixels = new byte[10 * 10 * 4];
            for (var y = 0; y < 10; y++)
            {
                for (var x = 0; x < 10; x++)
                {
                    var p = (y * 10 + x) * 4;
                    var ring = x is >= 2 and <= 7 && y is >= 2 and <= 7 && (x is 2 or 7 || y is 2 or 7);
                    var shade = ring ? (byte)0 : (byte)255;
                    (pixels[p], pixels[p + 1], pixels[p + 2], pixels[p + 3]) = (shade, shade, shade, 255);
                }
            }

            var image = BitmapSource.Create(10, 10, 96, 96, PixelFormats.Bgra32, null, pixels, 40);
            image.Freeze();
            var document = new EditorDocument(image);
            byte Alpha(int x, int y)
            {
                var bgra = new byte[10 * 10 * 4];
                document.Image.CopyPixels(bgra, 40, 0);
                return bgra[(y * 10 + x) * 4 + 3];
            }

            Assert.True(document.RemoveWhiteBackground());
            Assert.Equal(0, Alpha(0, 0));
            Assert.Equal(255, Alpha(5, 5)); // inside the ring

            Assert.True(document.RemoveWhiteBackground(everywhere: true));
            Assert.Equal(0, Alpha(5, 5));
            Assert.Equal(255, Alpha(2, 2)); // the ring
            Assert.False(document.RemoveWhiteBackground(everywhere: true)); // none left

            document.Undo();
            Assert.Equal(255, Alpha(5, 5));
            Assert.Equal(0, Alpha(0, 0));
            return true;
        });
    }

    // WPF rendering needs a single-threaded-apartment thread; test runner threads aren't.
    private static T RunOnUiThread<T>(Func<T> work)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            throw error;
        }

        return result;
    }
}
