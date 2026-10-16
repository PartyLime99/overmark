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
