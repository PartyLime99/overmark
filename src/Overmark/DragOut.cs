using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Overmark.Capture;

namespace Overmark;

/// <summary>Dragging captures out of Overmark into other apps (Outlook, Teams, Word, a folder).</summary>
internal static class DragOut
{
    private static readonly string TemporaryFolder = Path.Combine(Path.GetTempPath(), "Overmark");

    /// <summary>Drags a file. Returns once it's been dropped or the drag is cancelled.</summary>
    public static void DragFile(DependencyObject source, string path) =>
        DragDrop.DoDragDrop(source, new DataObject(DataFormats.FileDrop, new[] { path }), DragDropEffects.Copy);

    /// <summary>Saves <paramref name="image"/> to a temporary PNG called <paramref name="fileName"/>, then drags that file.</summary>
    public static void DragImage(DependencyObject source, BitmapSource image, string fileName)
    {
        Directory.CreateDirectory(TemporaryFolder);
        var path = Path.Combine(TemporaryFolder, fileName);
        File.WriteAllBytes(path, CaptureOutput.EncodePng(image));
        DragFile(source, path);
    }

    /// <summary>Deletes temporary drag files more than a day old.</summary>
    public static void CleanUp()
    {
        try
        {
            if (!Directory.Exists(TemporaryFolder))
            {
                return;
            }

            foreach (var file in new DirectoryInfo(TemporaryFolder).GetFiles().Where(f => f.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-1)))
            {
                file.Delete();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Couldn't tidy temporary drag files: {ex.Message}");
        }
    }

    /// <summary>
    /// Makes <paramref name="element"/> draggable: moving the mouse far enough with the button down calls
    /// <paramref name="startDrag"/>; releasing without moving calls <paramref name="click"/>.
    /// </summary>
    public static void AttachGesture(UIElement element, Action startDrag, Action? click = null)
    {
        Point? pressedAt = null;
        element.PreviewMouseLeftButtonDown += (_, e) =>
        {
            pressedAt = e.GetPosition(element);
            element.CaptureMouse();
            e.Handled = true;
        };
        element.PreviewMouseMove += (_, e) =>
        {
            if (pressedAt is not { } origin || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var moved = e.GetPosition(element) - origin;
            if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            pressedAt = null;
            element.ReleaseMouseCapture();
            startDrag();
        };
        element.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (pressedAt is null)
            {
                return;
            }

            pressedAt = null;
            element.ReleaseMouseCapture();
            e.Handled = true;
            click?.Invoke();
        };
    }
}
