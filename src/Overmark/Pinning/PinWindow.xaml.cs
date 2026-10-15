using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Overmark.Capture;
using Overmark.Platform;
using static Overmark.Platform.NativeMethods;

namespace Overmark.Pinning;

/// <summary>
/// A capture floating on top of everything, e.g. to keep a reference visible while working. A title strip
/// along the top moves it (as does dragging the image), fades it, and closes it.
/// </summary>
/// <remarks>
/// It's moved by hand rather than with Windows' title-bar dragging: that dragging treats a double-click as
/// "maximise" and dragging to the top of the screen as "snap", which would blow the pin up to fill the screen.
/// </remarks>
internal partial class PinWindow : Window
{
    private const double MinZoom = 0.25;
    private const double MaxZoom = 4;

    private readonly BitmapSource _image;
    private readonly PixelPoint? _at;
    private double _zoom = 1;
    private PixelPoint? _dragStartPointer;
    private PixelPoint _dragStartWindow;

    /// <param name="at">Where to put the image's top-left pixel; null centres the pin on the screen under the mouse.</param>
    /// <param name="edit">Opens the capture in the editor, if it has a file.</param>
    public PinWindow(BitmapSource image, PixelPoint? at, Action? edit)
    {
        InitializeComponent();
        _image = image;
        _at = at;
        Picture.Source = image;
        ContextMenu = BuildMenu(edit);
        ShowAtZoom();

        CloseButton.Click += (_, _) => Close();
        SeeThrough.ValueChanged += (_, e) => Picture.Opacity = e.NewValue;

        // Buttons and the slider handle their own clicks; a press anywhere else starts moving the pin.
        MouseLeftButtonDown += (_, _) =>
        {
            GetWindowRect(Handle, out var rect);
            _dragStartWindow = new PixelPoint(rect.Left, rect.Top);
            _dragStartPointer = Pointer.Position;
            CaptureMouse();
        };
        MouseMove += (_, _) =>
        {
            if (_dragStartPointer is { } start && IsMouseCaptured)
            {
                var pointer = Pointer.Position;
                MoveTo(_dragStartWindow.X + pointer.X - start.X, _dragStartWindow.Y + pointer.Y - start.Y);
            }
        };
        MouseLeftButtonUp += (_, _) =>
        {
            _dragStartPointer = null;
            ReleaseMouseCapture();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
        MouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                SeeThrough.Value += e.Delta > 0 ? 0.1 : -0.1;
                e.Handled = true;
            }
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                // The window resizes to fit and keeps its top-left corner where it is.
                _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.25 : 0.8), MinZoom, MaxZoom);
                ShowAtZoom();
                e.Handled = true;
            }
        };

        // A pin is never maximised or minimised, whatever Windows tries.
        StateChanged += (_, _) =>
        {
            if (WindowState != WindowState.Normal)
            {
                WindowState = WindowState.Normal;
            }
        };

        SourceInitialized += (_, _) => SetWindowLong(Handle, GWL_EXSTYLE, GetWindowLong(Handle, GWL_EXSTYLE) | WS_EX_TOOLWINDOW);
        Loaded += (_, _) =>
        {
            ShowAtZoom();
            Place();
            Opacity = 1; // hidden until it's in the right place
        };
        DpiChanged += (_, _) => ShowAtZoom();
    }

    private IntPtr Handle => new WindowInteropHelper(this).Handle;

    private double Scale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    /// <summary>At 100% the image shows one image pixel per screen pixel, exactly as it was captured.</summary>
    private void ShowAtZoom()
    {
        Picture.Width = _image.PixelWidth * _zoom / Scale;
        Picture.Height = _image.PixelHeight * _zoom / Scale;
        RenderOptions.SetBitmapScalingMode(Picture, _zoom == 1 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
    }

    /// <summary>
    /// With the image exactly over the spot it was captured from (so it looks frozen in place), or centred on the
    /// screen under the mouse; either way kept on screen.
    /// </summary>
    private void Place()
    {
        UpdateLayout();
        GetWindowRect(Handle, out var rect);
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        var monitors = Displays.GetMonitors();

        int x, y;
        if (_at is { } at)
        {
            var offset = Picture.TranslatePoint(new Point(0, 0), this);
            (x, y) = (at.X - (int)Math.Round(offset.X * Scale), at.Y - (int)Math.Round(offset.Y * Scale));
        }
        else
        {
            var pointer = Pointer.Position;
            var screen = (monitors.FirstOrDefault(m => m.Bounds.Contains(pointer)) ?? monitors[0]).WorkArea;
            (x, y) = (screen.Left + (screen.Width - width) / 2, screen.Top + (screen.Height - height) / 2);
        }

        // Keep the title strip reachable, e.g. for a capture taken right at the top of the screen.
        var work = (monitors.FirstOrDefault(m => m.Bounds.Contains(new PixelPoint(x + width / 2, y + height / 2))) ?? monitors[0]).WorkArea;
        x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - width));
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - height));
        MoveTo(x, y);
    }

    private void MoveTo(int x, int y) => SetWindowPos(Handle, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);

    private ContextMenu BuildMenu(Action? edit)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("Copy", () => CaptureOutput.TryCopy(_image, out _)));
        menu.Items.Add(Item("Save as…", SaveAs));
        if (edit is not null)
        {
            menu.Items.Add(Item("Edit", edit));
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Close", Close));
        return menu;
    }

    private void SaveAs()
    {
        var dialog = new SaveFileDialog { Filter = "PNG image|*.png", FileName = "Overmark pin.png", AddExtension = true };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            File.WriteAllBytes(dialog.FileName, CaptureOutput.EncodePng(_image));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Couldn't save:\n\n{ex.Message}", "Overmark", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }
}
