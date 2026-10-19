using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Overmark.Editor;
using Overmark.Settings;

namespace Overmark.Tests;

/// <summary>Showing another capture in the same editor window (as switching from the list of today's captures does).</summary>
public class EditorSwitchingTests
{
    [Fact]
    public void Marks_go_on_the_capture_now_showing() => RunOnUiThread(() =>
    {
        var editor = new EditorWindow(@"C:\first.png", Image(200, 100), new EditorSettings { Tool = "Line" }, _ => { }, _ => { });
        Draw(editor, new Point(10, 10), new Point(50, 50));
        var first = Document(editor);
        Assert.Single(first.Marks);

        editor.ShowCapture(@"C:\second.png", Image(300, 150), null);
        var second = Document(editor);
        Draw(editor, new Point(20, 20), new Point(80, 30));

        Assert.NotSame(first, second);
        Assert.Single(first.Marks);
        Assert.Single(second.Marks);
        Assert.Equal(@"C:\second.png", editor.CapturePath);
        Assert.StartsWith("*second.png", editor.Title);
        Assert.Equal(300, ((FrameworkElement)editor.FindName("Picture")).Width);
        CloseQuietly(editor);
    });

    [Fact]
    public void A_capture_comes_back_with_its_kept_marks() => RunOnUiThread(() =>
    {
        var editor = new EditorWindow(@"C:\first.png", Image(200, 100), new EditorSettings(), _ => { }, _ => { });
        var image = Image(200, 100);
        var kept = new EditorDocument(image, new Int32Rect(0, 0, 200, 100), [new LineMark(new StrokeStyle(Colors.Red, 4), new Point(1, 1), new Point(9, 9))], unsaved: true);

        editor.ShowCapture(@"C:\kept.png", image, kept);

        Assert.Same(kept, Document(editor));
        Assert.True(kept.HasUnsavedChanges);
        Assert.Equal("Marks still editable", ((System.Windows.Controls.TextBlock)editor.FindName("StatusText")).Text);
        CloseQuietly(editor);
    });

    [Fact]
    public void The_x_on_a_capture_closes_it_without_opening_it() => RunOnUiThread(() =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "OvermarkTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var history = new Overmark.Capture.CaptureHistory(Path.Combine(folder, "history.json"));
            string Capture(string name, int minute)
            {
                var file = Path.Combine(folder, name);
                File.WriteAllText(file, "");
                history.Add(new Overmark.Capture.CaptureRecord(file, Overmark.Capture.CaptureKind.Region, DateTime.Today.AddMinutes(minute), 200, 100));
                return file;
            }

            var open = Capture("open.png", 1);
            var other = Capture("other.png", 2);
            var closed = new List<string>();
            var switched = new List<string>();
            var editor = new EditorWindow(open, Image(200, 100), new EditorSettings(), _ => { }, _ => { }, history, switched.Add, closeCapture: closed.Add);

            // The newest is listed first: the other capture, then the open one. Each tile has its x in the thumbnail's corner.
            var list = (System.Windows.Controls.Panel)editor.FindName("RecentList");
            var tile = (System.Windows.Controls.Button)list.Children[0];
            var thumbnail = (System.Windows.Controls.Grid)((System.Windows.Controls.StackPanel)tile.Content).Children[0];
            var x = thumbnail.Children.OfType<System.Windows.Controls.Button>().Single();
            x.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, x));

            Assert.Equal([other], closed);
            Assert.Empty(switched);
            CloseQuietly(editor);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    });

    [Fact]
    public void With_no_capture_a_note_takes_the_pictures_place() => RunOnUiThread(() =>
    {
        var editor = new EditorWindow(null, null, new EditorSettings(), _ => { }, _ => { }, deleteCapture: _ => { }, howToCapture: () => "Press it");
        var note = (FrameworkElement)editor.FindName("NoCaptureNote");
        Visibility Shown(string name) => ((FrameworkElement)editor.FindName(name)).Visibility;

        Assert.Null(editor.CapturePath);
        Assert.Equal("Overmark", editor.Title);
        Assert.Equal(Visibility.Visible, note.Visibility);
        Assert.Equal("Press it", ((System.Windows.Controls.TextBlock)editor.FindName("NoCaptureHint")).Text);
        Assert.All(["Scroller", "Dock", "SaveButton", "DeleteButton"], name => Assert.Equal(Visibility.Collapsed, Shown(name)));

        editor.ShowCapture(@"C:\first.png", Image(200, 100), null);
        Assert.Equal(Visibility.Collapsed, note.Visibility);
        Assert.All(["Scroller", "Dock", "SaveButton", "DeleteButton"], name => Assert.Equal(Visibility.Visible, Shown(name)));

        editor.ShowNoCapture();
        Assert.Null(editor.CapturePath);
        Assert.Equal(Visibility.Visible, note.Visibility);
        Assert.True(editor.ConfirmLeave()); // nothing to ask about
        CloseQuietly(editor);
    });

    [Fact]
    public void Texts_new_words_go_back_in_its_place_and_its_old_copy_is_hidden_meanwhile() => RunOnUiThread(() =>
    {
        var editor = new EditorWindow(@"C:\first.png", Image(200, 100), new EditorSettings(), _ => { }, _ => { });
        ((System.Windows.Controls.RadioButton)editor.FindName("MoveButton")).IsChecked = true;
        var text = new TextMark(new StrokeStyle(Colors.Red, 20), new Point(10, 10), "Helo", Boxed: false);
        var line = new LineMark(new StrokeStyle(Colors.Red, 4), new Point(1, 1), new Point(9, 9));
        Document(editor).Add(text);
        Document(editor).Add(line);
        var tools = Field<DrawingTools>(editor, "_tools");
        var committed = (CommittedMarksLayer)editor.FindName("CommittedLayer");

        tools.EditText(text);
        Assert.Same(text, committed.Hidden);
        Assert.Equal(Visibility.Visible, ((FrameworkElement)editor.FindName("TypingBox")).Visibility);
        tools.SetText("Hello");
        tools.Finish();

        Assert.Equal([text with { Text = "Hello" }, line], Document(editor).Marks);
        Assert.Null(committed.Hidden);
        CloseQuietly(editor);
    });

    [Fact]
    public void The_dots_by_the_slider_step_the_selected_marks_thickness_from_where_it_is() => RunOnUiThread(() =>
    {
        var editor = new EditorWindow(@"C:\first.png", Image(200, 100), new EditorSettings { StrokeWidth = 12 }, _ => { }, _ => { });
        ((System.Windows.Controls.RadioButton)editor.FindName("MoveButton")).IsChecked = true;
        var line = new LineMark(new StrokeStyle(Colors.Red, 4), new Point(10, 50), new Point(190, 50));
        Document(editor).Add(line);
        var move = Field<MoveTool>(editor, "_move");
        move.PointerDown(new Point(100, 50));
        move.PointerUp();
        void Click(string name)
        {
            var button = (System.Windows.Controls.Button)editor.FindName(name);
            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, button));
        }

        Click("ThickerButton");
        Assert.Equal(5, Document(editor).Marks[0].Style.Width); // from the line's 4, not the pen's 12
        Click("ThinnerButton");
        Click("ThinnerButton");
        Assert.Equal(3, Document(editor).Marks[0].Style.Width);
        Assert.Equal(3, ((System.Windows.Controls.Slider)editor.FindName("ThicknessSlider")).Value);

        Document(editor).Undo();
        Assert.Equal(4, Document(editor).Marks[0].Style.Width);
        CloseQuietly(editor);
    });

    private static EditorDocument Document(EditorWindow editor) => Field<EditorDocument>(editor, "_document");

    private static void Draw(EditorWindow editor, Point from, Point to)
    {
        var tools = Field<DrawingTools>(editor, "_tools");
        tools.PointerDown(from, false, 8);
        tools.PointerMove(to, false);
        tools.PointerUp(to, false);
    }

    /// <summary>Closes without the unsaved-changes question (which would be a real dialog).</summary>
    private static void CloseQuietly(EditorWindow editor) => editor.CloseWithoutAsking();

    private static T Field<T>(EditorWindow editor, string name) =>
        (T)typeof(EditorWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;

    private static BitmapSource Image(int width, int height)
    {
        var pixels = Enumerable.Repeat((byte)0xFF, width * height * 4).ToArray();
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4);
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
}
