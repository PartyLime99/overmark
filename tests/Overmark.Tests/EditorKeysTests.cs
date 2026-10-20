using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Overmark.Editor;
using Overmark.Settings;

namespace Overmark.Tests;

/// <summary>Keys pressed in an editor window that's never shown.</summary>
public class EditorKeysTests
{
    private static BitmapSource Image(int width, int height)
    {
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, new byte[width * height * 4], width * 4);
        image.Freeze();
        return image;
    }

    private static T Field<T>(EditorWindow editor, string name) =>
        (T)typeof(EditorWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;

    private static void Press(EditorWindow editor, Key key) =>
        editor.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new OffScreen(), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });

    [Fact]
    public void Esc_drops_a_mark_being_drawn_and_goes_back_to_the_move_tool() => RunOnUiThread(() =>
    {
        var editor = new EditorWindow(@"C:\x.png", Image(400, 300), new EditorSettings { Tool = "Pixelate" }, _ => { }, _ => { });
        var tools = Field<DrawingTools>(editor, "_tools");
        var document = Field<EditorDocument>(editor, "_document");
        Assert.Equal(ToolKind.Pixelate, tools.Tool);
        tools.PointerDown(new Point(10, 10), false, 8);
        tools.PointerMove(new Point(100, 60), false);
        Assert.NotNull(tools.Preview);

        Press(editor, Key.Escape);

        Assert.Equal(ToolKind.Move, tools.Tool);
        Assert.Null(tools.Preview);
        Assert.Empty(document.Marks);
        Assert.True(((RadioButton)editor.FindName("MoveButton")).IsChecked);
        editor.CloseWithoutAsking();
    });

    [Fact]
    public void Esc_drops_text_being_typed_rather_than_keeping_it() => RunOnUiThread(() =>
    {
        var editor = new EditorWindow(@"C:\x.png", Image(400, 300), new EditorSettings { Tool = "Text" }, _ => { }, _ => { });
        var tools = Field<DrawingTools>(editor, "_tools");
        tools.PointerDown(new Point(50, 50), false, 8);
        tools.SetText("Half typed");

        Press(editor, Key.Escape);

        Assert.Equal(ToolKind.Move, tools.Tool);
        Assert.Empty(Field<EditorDocument>(editor, "_document").Marks);
        editor.CloseWithoutAsking();
    });

    [Fact]
    public void With_nothing_part_done_Esc_still_picks_the_move_tool() => RunOnUiThread(() =>
    {
        var editor = new EditorWindow(@"C:\x.png", Image(400, 300), new EditorSettings { Tool = "Arrow" }, _ => { }, _ => { });

        Press(editor, Key.Escape);

        Assert.Equal(ToolKind.Move, Field<DrawingTools>(editor, "_tools").Tool);
        editor.CloseWithoutAsking();
    });

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
