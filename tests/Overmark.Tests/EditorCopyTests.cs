using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Overmark.Editor;
using Overmark.Settings;

namespace Overmark.Tests;

/// <summary>
/// What Ctrl+C copies in an editor window that's never shown: the mark picked or drawn last, or the picture. And
/// pasting marks. The clipboard itself is left alone.
/// </summary>
[Collection(WindowTests.Name)]
public class EditorCopyTests
{
    [Fact]
    public void The_mark_drawn_last_is_copied() => RunOnUiThread(() =>
    {
        var editor = Editor("Line");
        Draw(editor, new Point(10, 10), new Point(50, 50));
        Draw(editor, new Point(60, 10), new Point(90, 50));

        Assert.Same(Document(editor).Marks[1], MarkToCopy(editor));
        editor.CloseWithoutAsking();
    });

    [Fact]
    public void A_selected_mark_is_copied_as_it_is_now_and_letting_go_copies_the_picture() => RunOnUiThread(() =>
    {
        var editor = Editor("Line");
        Draw(editor, new Point(10, 10), new Point(50, 10));
        Draw(editor, new Point(10, 60), new Point(50, 60));
        Press(editor, Key.Escape); // to the move tool
        var move = Field<MoveTool>(editor, "_move");
        move.PointerDown(new Point(30, 10));
        move.PointerUp();
        Press(editor, Key.Right);

        var copied = Assert.IsType<LineMark>(MarkToCopy(editor));
        Assert.Same(Document(editor).Marks[0], copied);
        Assert.Equal(new Point(11, 10), copied.Start); // nudged

        Press(editor, Key.Escape);
        Assert.Null(MarkToCopy(editor));
        editor.CloseWithoutAsking();
    });

    [Fact]
    public void Undoing_the_mark_drawn_last_copies_the_picture() => RunOnUiThread(() =>
    {
        var editor = Editor("Line");
        Draw(editor, new Point(10, 10), new Point(50, 50));

        Document(editor).Undo();

        Assert.Null(MarkToCopy(editor));
        editor.CloseWithoutAsking();
    });

    [Fact]
    public void Coming_back_to_a_capture_copies_its_picture() => RunOnUiThread(() =>
    {
        var editor = Editor("Line");
        Draw(editor, new Point(10, 10), new Point(50, 50));
        var first = Document(editor);

        editor.ShowCapture(@"C:\second.png", Image(300, 200), null);
        editor.ShowCapture(@"C:\x.png", first.Image, first);

        Assert.Null(MarkToCopy(editor));
        editor.CloseWithoutAsking();
    });

    [Fact]
    public void Clicking_the_open_captures_tile_copies_its_picture() => RunOnUiThread(() =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "OvermarkTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var history = new Overmark.Capture.CaptureHistory(Path.Combine(folder, "history.json"));
            var open = Path.Combine(folder, "open.png");
            File.WriteAllText(open, "");
            history.Add(new Overmark.Capture.CaptureRecord(open, Overmark.Capture.CaptureKind.Region, DateTime.Today, 400, 300));
            var editor = new EditorWindow(open, Image(400, 300), new EditorSettings { Tool = "Line" }, _ => { }, _ => { }, history, _ => { });
            Draw(editor, new Point(10, 10), new Point(50, 50));
            Assert.NotNull(MarkToCopy(editor));

            var tile = (Button)((Panel)editor.FindName("RecentList")).Children[0];
            tile.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, tile));

            Assert.Null(MarkToCopy(editor));
            editor.CloseWithoutAsking();
        }
        finally
        {
            // The list reads thumbnails in the background, which can still have a file open for a moment.
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    Directory.Delete(folder, recursive: true);
                    break;
                }
                catch (IOException) when (attempt < 40)
                {
                    Thread.Sleep(50);
                }
            }
        }
    });

    [Fact]
    public void A_pasted_mark_is_picked_with_the_move_tool_and_pasting_it_again_steps_further_on() => RunOnUiThread(() =>
    {
        var editor = Editor("Arrow");
        var move = Field<MoveTool>(editor, "_move");
        var step = Math.Round(10 * move.DipSize);
        var line = new LineMark(new StrokeStyle(Colors.Red, 4), new Point(10, 10), new Point(50, 10));

        PasteMark(editor, new CopiedMark(line, Measures: false));
        PasteMark(editor, new CopiedMark(line, Measures: false));

        Assert.Equal([line.MovedBy(new Vector(step, step)), line.MovedBy(new Vector(2 * step, 2 * step))], Document(editor).Marks);
        Assert.Equal(ToolKind.Move, Field<DrawingTools>(editor, "_tools").Tool);
        Assert.Equal(1, move.Selected);
        Assert.Same(Document(editor).Marks[1], MarkToCopy(editor));

        // Something else copied starts again from one step on.
        var other = line with { End = new Point(90, 10) };
        PasteMark(editor, new CopiedMark(other, Measures: false));
        Assert.Equal(other.MovedBy(new Vector(step, step)), Document(editor).Marks[2]);
        editor.CloseWithoutAsking();
    });

    private static EditorWindow Editor(string tool) =>
        new(@"C:\x.png", Image(400, 300), new EditorSettings { Tool = tool }, _ => { }, _ => { });

    private static EditorDocument Document(EditorWindow editor) => Field<EditorDocument>(editor, "_document");

    private static Mark? MarkToCopy(EditorWindow editor) =>
        (Mark?)typeof(EditorWindow).GetProperty("MarkToCopy", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor);

    /// <summary>Pastes <paramref name="copied"/> as Ctrl+V does when it's on the clipboard.</summary>
    private static void PasteMark(EditorWindow editor, CopiedMark copied) =>
        typeof(EditorWindow).GetMethod("PasteMark", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, [MarkClipboard.ToText(copied), copied]);

    private static void Draw(EditorWindow editor, Point from, Point to)
    {
        var tools = Field<DrawingTools>(editor, "_tools");
        tools.PointerDown(from, false, 8);
        tools.PointerMove(to, false);
        tools.PointerUp(to, false);
    }

    private static T Field<T>(EditorWindow editor, string name) =>
        (T)typeof(EditorWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;

    private static void Press(EditorWindow editor, Key key) =>
        editor.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new OffScreen(), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });

    private static BitmapSource Image(int width, int height)
    {
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, new byte[width * height * 4], width * 4);
        image.Freeze();
        return image;
    }

    private static void RunOnUiThread(Action work)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                work();
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
    }

    /// <summary>Where the key presses come from: nowhere on screen.</summary>
    private sealed class OffScreen : PresentationSource
    {
        public override Visual? RootVisual { get; set; }

        public override bool IsDisposed => false;

        protected override CompositionTarget? GetCompositionTargetCore() => null;
    }
}
