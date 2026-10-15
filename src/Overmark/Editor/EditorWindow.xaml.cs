using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Overmark.Capture;
using Overmark.Platform;
using Overmark.Settings;

namespace Overmark.Editor;

/// <summary>
/// Draws on a capture with the colour and thickness chosen beforehand, then copies, saves, drags or pins it.
/// Coordinates on the drawing surface are image pixels; zoom is a scale on top of that.
/// </summary>
internal partial class EditorWindow : Window
{
    private const double MinZoom = 0.1;
    private const double MaxZoom = 8;
    private const double SurfacePadding = 24;

    private readonly string _path;
    private readonly EditorDocument _document;
    private readonly DrawingTools _tools = new();
    private readonly Action<EditorSettings> _rememberSettings;
    private readonly Action<BitmapSource> _pin;
    private readonly Dictionary<ToolKind, RadioButton> _toolButtons;
    private readonly List<RadioButton> _swatches = [];
    private readonly RadioButton _customSwatch;
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };
    private ToolKind _lastDrawingTool = ToolKind.Arrow;
    private Color? _customColour;
    private double _zoom = 1;
    private bool _fitToWindow = true;
    private bool _applyingSettings;
    private Point? _erasingFrom;

    public EditorWindow(string path, BitmapSource image, EditorSettings settings, Action<EditorSettings> rememberSettings, Action<BitmapSource> pin)
    {
        InitializeComponent();
        _path = path;
        _rememberSettings = rememberSettings;
        _pin = pin;
        _document = new EditorDocument(image);

        Picture.Source = image;
        foreach (var layer in new FrameworkElement[] { Layers, Picture, CommittedLayer, DrawingLayer })
        {
            layer.Width = image.PixelWidth;
            layer.Height = image.PixelHeight;
        }

        CommittedLayer.Attach(_document);
        DrawingLayer.Attach(_tools);
        ShowCrop();

        _toolButtons = new()
        {
            [ToolKind.Freehand] = FreehandTool,
            [ToolKind.Line] = LineTool,
            [ToolKind.Arrow] = ArrowTool,
            [ToolKind.Curve] = CurveTool,
            [ToolKind.Rectangle] = RectangleTool,
            [ToolKind.Ellipse] = EllipseTool,
            [ToolKind.Highlighter] = HighlighterTool,
            [ToolKind.Eraser] = EraserTool,
            [ToolKind.Crop] = CropTool,
        };
        foreach (var (tool, button) in _toolButtons)
        {
            button.Checked += (_, _) => SelectTool(tool);
        }

        _customSwatch = BuildSwatches();
        ThicknessSlider.ValueChanged += (_, e) => SetThickness(e.NewValue);
        ArrowheadToggle.Checked += (_, _) => SetCurveArrowhead(true);
        ArrowheadToggle.Unchecked += (_, _) => SetCurveArrowhead(false);
        CustomColourButton.Click += (_, _) => PickCustomColour();
        UndoButton.Click += (_, _) => Undo();
        RedoButton.Click += (_, _) => _document.Redo();
        FitButton.Click += (_, _) => FitToWindow();
        ActualSizeButton.Click += (_, _) => SetZoom(1, null);
        CopyButton.Click += (_, _) => Copy();
        SaveButton.Click += (_, _) => Save();
        PinButton.Click += (_, _) => Pin();

        DrawingLayer.MouseLeftButtonDown += OnPointerDown;
        DrawingLayer.MouseMove += OnPointerMove;
        DrawingLayer.MouseLeftButtonUp += OnPointerUp;
        DrawingLayer.MouseLeave += (_, _) => DrawingLayer.EraserAt = null;
        Scroller.PreviewMouseWheel += OnMouseWheel;
        PreviewKeyDown += OnKeyDown;
        PreviewKeyUp += (_, e) =>
        {
            if (e.Key is Key.LeftShift or Key.RightShift)
            {
                _tools.ShiftChanged(false);
            }
        };

        _tools.Committed += _document.Add;
        _tools.CropApplied += _document.CropTo;
        _document.Changed += UpdateCommands;
        _document.Changed += ShowCrop;
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            StatusText.Text = "";
        };

        Loaded += (_, _) => FitToWindow();
        SizeChanged += (_, _) =>
        {
            if (_fitToWindow)
            {
                FitToWindow();
            }
        };
        DpiChanged += (_, _) =>
        {
            if (_fitToWindow)
            {
                FitToWindow();
            }
            else
            {
                ApplyZoom();
            }
        };
        Closing += OnClosing;

        ApplySettings(settings);
        UpdateCommands();

        // Last, so the toolbar is complete (swatches included) when it's measured.
        SizeToImage(image);
    }

    private static bool IsShiftDown => Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

    private double DeviceScale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    /// <summary>
    /// Opens big enough to show the capture at actual size when it fits, up to most of the screen, and at least
    /// wide enough for the toolbar to sit on one row.
    /// </summary>
    private void SizeToImage(BitmapSource image)
    {
        var monitor = Displays.GetMonitors().FirstOrDefault(m => m.Bounds.Contains(Pointer.Position));
        var scale = (monitor?.Dpi ?? 96) / 96.0;
        var workArea = SystemParameters.WorkArea;

        // Measured with unlimited width, the toolbar's wrap panel reports its one-row width.
        Toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var toolbarWidth = Toolbar.DesiredSize.Width + 16; // plus the window frame

        var width = Math.Max(image.PixelWidth / scale + 2 * SurfacePadding + 40, toolbarWidth);
        Width = Math.Max(MinWidth, Math.Min(width, workArea.Width * 0.9));
        Height = Math.Max(MinHeight, Math.Min(image.PixelHeight / scale + 2 * SurfacePadding + 120, workArea.Height * 0.85));
    }

    private RadioButton BuildSwatches()
    {
        var template = (ControlTemplate)FindResource("SwatchTemplate");
        for (var i = 0; i < Palette.Colours.Count; i++)
        {
            var swatch = MakeSwatch(template, Palette.Colours[i], $"{Palette.ColourNames[i]} ({i + 1})");
            _swatches.Add(swatch);
            SwatchPanel.Children.Add(swatch);
        }

        var custom = MakeSwatch(template, Colors.Transparent, "Custom colour (9)");
        custom.Visibility = Visibility.Collapsed;
        SwatchPanel.Children.Add(custom);
        return custom;
    }

    private RadioButton MakeSwatch(ControlTemplate template, Color colour, string toolTip)
    {
        var swatch = new RadioButton
        {
            Template = template,
            GroupName = "Colours",
            Focusable = false,
            Margin = new Thickness(1, 0, 1, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(colour),
            ToolTip = toolTip,
        };
        swatch.Checked += (_, _) => SelectColour(((SolidColorBrush)swatch.Background).Color);
        return swatch;
    }

    private void SelectTool(ToolKind tool)
    {
        _tools.Tool = tool;
        ArrowheadToggle.Visibility = tool == ToolKind.Curve ? Visibility.Visible : Visibility.Collapsed;

        // The eraser shows its own circle instead of the crosshair.
        DrawingLayer.Cursor = tool == ToolKind.Eraser ? Cursors.None : Cursors.Cross;
        if (tool != ToolKind.Eraser)
        {
            DrawingLayer.EraserAt = null;
        }

        // The eraser and crop are one-off jobs, so the next capture opens with the last drawing tool.
        if (tool is not (ToolKind.Eraser or ToolKind.Crop))
        {
            _lastDrawingTool = tool;
            RememberChoices();
        }
    }

    private void SelectColour(Color colour)
    {
        _tools.Colour = colour;
        ThicknessPreview.Fill = new SolidColorBrush(colour);
        RememberChoices();
    }

    private void SetThickness(double thickness)
    {
        _tools.Width = thickness;
        ThicknessText.Text = $"{thickness:0} px";
        ThicknessPreview.Width = ThicknessPreview.Height = Math.Clamp(thickness, 3, 30);
        RememberChoices();
    }

    private void SetCurveArrowhead(bool on)
    {
        _tools.CurveArrowhead = on;
        RememberChoices();
    }

    private void PickCustomColour()
    {
        using var dialog = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
            Color = System.Drawing.Color.FromArgb(_tools.Colour.R, _tools.Colour.G, _tools.Colour.B),
        };
        if (dialog.ShowDialog(new Win32Owner(this)) != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }

        SetCustomColour(Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B));
        _customSwatch.IsChecked = true;
        SelectColour(_customColour!.Value); // in case the custom swatch was already selected
    }

    private void SetCustomColour(Color colour)
    {
        _customColour = colour;
        _customSwatch.Background = new SolidColorBrush(colour);
        _customSwatch.Visibility = Visibility.Visible;
    }

    private void ApplySettings(EditorSettings settings)
    {
        _applyingSettings = true;
        try
        {
            if (ParseColour(settings.CustomColour) is { } custom)
            {
                SetCustomColour(custom);
            }

            var colour = ParseColour(settings.Colour) ?? Palette.Colours[0];
            var index = Palette.Colours.ToList().IndexOf(colour);
            if (index >= 0)
            {
                _swatches[index].IsChecked = true;
            }
            else
            {
                SetCustomColour(colour);
                _customSwatch.IsChecked = true;
            }

            var thickness = double.IsFinite(settings.StrokeWidth) && settings.StrokeWidth > 0 ? settings.StrokeWidth : Palette.DefaultThickness;
            ThicknessSlider.Value = Math.Clamp(thickness, Palette.MinimumThickness, Palette.MaximumThickness);
            SetThickness(ThicknessSlider.Value);
            ArrowheadToggle.IsChecked = settings.CurveArrowhead;
            _tools.CurveArrowhead = settings.CurveArrowhead;

            var tool = Enum.TryParse<ToolKind>(settings.Tool, true, out var saved) && saved is not (ToolKind.Eraser or ToolKind.Crop)
                ? saved
                : ToolKind.Arrow;
            _toolButtons[tool].IsChecked = true;
        }
        finally
        {
            _applyingSettings = false;
        }
    }

    private void RememberChoices()
    {
        if (_applyingSettings)
        {
            return;
        }

        _rememberSettings(new EditorSettings
        {
            Tool = _lastDrawingTool.ToString(),
            Colour = Hex(_tools.Colour),
            StrokeWidth = _tools.Width,
            CurveArrowhead = _tools.CurveArrowhead,
            CustomColour = _customColour is { } custom ? Hex(custom) : null,
        });
    }

    private void OnPointerDown(object sender, MouseButtonEventArgs e)
    {
        DrawingLayer.CaptureMouse();
        var point = e.GetPosition(DrawingLayer);
        if (_tools.Tool == ToolKind.Eraser)
        {
            _erasingFrom = point;
            _document.EraseAt(point, DrawingLayer.EraserRadius);
        }
        else
        {
            _tools.PointerDown(point, IsShiftDown, DrawingLayer.HandleRadius * 1.5);
        }

        e.Handled = true;
    }

    private void OnPointerMove(object sender, MouseEventArgs e)
    {
        var point = e.GetPosition(DrawingLayer);
        if (_tools.Tool != ToolKind.Eraser)
        {
            _tools.PointerMove(point, IsShiftDown);
            return;
        }

        DrawingLayer.EraserAt = point;
        if (_erasingFrom is { } from)
        {
            // Erase along the whole path, so a fast drag doesn't skip over thin marks.
            var radius = DrawingLayer.EraserRadius;
            var steps = Math.Max(1, (int)Math.Ceiling((point - from).Length / (radius / 2)));
            for (var i = 1; i <= steps; i++)
            {
                _document.EraseAt(from + (point - from) * i / steps, radius);
            }

            _erasingFrom = point;
        }
    }

    private void OnPointerUp(object sender, MouseButtonEventArgs e)
    {
        if (_erasingFrom is not null)
        {
            _erasingFrom = null;
            _document.FinishErasing();
        }
        else
        {
            _tools.PointerUp(e.GetPosition(DrawingLayer), IsShiftDown);
        }

        DrawingLayer.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var step = IsShiftDown ? 5 : 1;
        e.Handled = true;
        switch (key)
        {
            case Key.LeftShift or Key.RightShift:
                _tools.ShiftChanged(true);
                e.Handled = false;
                break;
            case Key.Z when ctrl && IsShiftDown:
            case Key.Y when ctrl:
                _document.Redo();
                break;
            case Key.Z when ctrl:
                Undo();
                break;
            case Key.C when ctrl:
                Copy();
                break;
            case Key.S when ctrl:
                Save();
                break;
            case Key.D0 or Key.NumPad0 when ctrl:
                FitToWindow();
                break;
            case Key.D1 or Key.NumPad1 when ctrl:
                SetZoom(1, null);
                break;
            case Key.OemPlus or Key.Add when ctrl:
                SetZoom(_zoom * 1.25, null);
                break;
            case Key.OemMinus or Key.Subtract when ctrl:
                SetZoom(_zoom / 1.25, null);
                break;
            case Key.Enter:
                _tools.Finish();
                break;
            case Key.Escape:
                _tools.Cancel();
                break;
            case Key.OemOpenBrackets:
                ThicknessSlider.Value -= step;
                break;
            case Key.OemCloseBrackets:
                ThicknessSlider.Value += step;
                break;
            case Key.P when !ctrl:
                FreehandTool.IsChecked = true;
                break;
            case Key.L when !ctrl:
                LineTool.IsChecked = true;
                break;
            case Key.A when !ctrl:
                ArrowTool.IsChecked = true;
                break;
            case Key.C when !ctrl:
                CurveTool.IsChecked = true;
                break;
            case Key.R when !ctrl:
                RectangleTool.IsChecked = true;
                break;
            case Key.O when !ctrl:
                EllipseTool.IsChecked = true;
                break;
            case Key.H when !ctrl:
                HighlighterTool.IsChecked = true;
                break;
            case Key.E when !ctrl:
                EraserTool.IsChecked = true;
                break;
            case Key.X when !ctrl:
                CropTool.IsChecked = true;
                break;
            case >= Key.D1 and <= Key.D8 when !ctrl:
                _swatches[key - Key.D1].IsChecked = true;
                break;
            case >= Key.NumPad1 and <= Key.NumPad8 when !ctrl:
                _swatches[key - Key.NumPad1].IsChecked = true;
                break;
            case Key.D9 or Key.NumPad9 when !ctrl && _customColour is not null:
                _customSwatch.IsChecked = true;
                break;
            default:
                e.Handled = false;
                break;
        }
    }

    /// <summary>Undo drops a part-drawn mark or crop box first, then works back through finished edits.</summary>
    private void Undo()
    {
        if (_tools.HasPending)
        {
            _tools.Cancel();
        }
        else
        {
            _document.Undo();
        }
    }

    private void UpdateCommands()
    {
        UndoButton.IsEnabled = _document.CanUndo;
        RedoButton.IsEnabled = _document.CanRedo;
        Title = $"{(_document.HasUnsavedChanges ? "*" : "")}{Path.GetFileName(_path)} - Overmark";
    }

    /// <summary>Shows only the cropped part: the surface is the crop's size, with the full image shifted inside it.</summary>
    private void ShowCrop()
    {
        var crop = _document.Crop;
        if (Surface.Width == crop.Width && Surface.Height == crop.Height && Layers.Margin.Left == -crop.X && Layers.Margin.Top == -crop.Y)
        {
            return;
        }

        Surface.Width = crop.Width;
        Surface.Height = crop.Height;
        Layers.Margin = new Thickness(-crop.X, -crop.Y, 0, 0);
        if (_fitToWindow && IsLoaded)
        {
            FitToWindow();
        }
    }

    private void Copy()
    {
        _tools.Finish();
        ShowStatus(CaptureOutput.TryCopy(_document.Render(), out var problem) ? "Copied to the clipboard" : problem!);
    }

    /// <summary>Pins the image and gets the editor out of the way; edits stay in the minimised editor.</summary>
    private void Pin()
    {
        _tools.Finish();
        _pin(_document.Render());
        WindowState = WindowState.Minimized;
    }

    /// <summary>Saves over the capture file. Returns false if that failed.</summary>
    private bool Save()
    {
        _tools.Finish();
        try
        {
            // Write next to the file and swap it in, so a failed save never leaves a half-written capture.
            var temporary = _path + ".saving";
            File.WriteAllBytes(temporary, CaptureOutput.EncodePng(_document.Render()));
            File.Move(temporary, _path, overwrite: true);
            _document.MarkSaved();
            ShowStatus("Saved");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Couldn't save {_path}", ex);
            MessageBox.Show(this, $"Couldn't save {Path.GetFileName(_path)}:\n\n{ex.Message}", "Overmark", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _tools.Finish();
        if (!_document.HasUnsavedChanges)
        {
            return;
        }

        var answer = MessageBox.Show(
            this, $"Save your changes to {Path.GetFileName(_path)}?", "Overmark", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel || (answer == MessageBoxResult.Yes && !Save()))
        {
            e.Cancel = true;
        }
    }

    private void ShowStatus(string text)
    {
        StatusText.Text = text;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private void FitToWindow()
    {
        var width = Scroller.ViewportWidth > 0 ? Scroller.ViewportWidth : Scroller.ActualWidth;
        var height = Scroller.ViewportHeight > 0 ? Scroller.ViewportHeight : Scroller.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var fit = Math.Min(
            (width - 2 * SurfacePadding) * DeviceScale / _document.Crop.Width,
            (height - 2 * SurfacePadding) * DeviceScale / _document.Crop.Height);

        // Small captures are shown at actual size rather than blown up.
        _zoom = Math.Clamp(Math.Min(1, fit), MinZoom, MaxZoom);
        _fitToWindow = true;
        ApplyZoom();
    }

    /// <summary>Zooms so the image point under <paramref name="anchor"/> (a point in the scroll area) stays put.</summary>
    private void SetZoom(double zoom, Point? anchor)
    {
        var previous = _zoom;
        _zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        _fitToWindow = false;

        var at = anchor ?? new Point(Scroller.ViewportWidth / 2, Scroller.ViewportHeight / 2);
        var contentX = Scroller.HorizontalOffset + at.X - SurfacePadding;
        var contentY = Scroller.VerticalOffset + at.Y - SurfacePadding;
        ApplyZoom();
        Scroller.UpdateLayout();

        var ratio = _zoom / previous;
        Scroller.ScrollToHorizontalOffset(contentX * ratio + SurfacePadding - at.X);
        Scroller.ScrollToVerticalOffset(contentY * ratio + SurfacePadding - at.Y);
    }

    /// <summary>Zoom is in screen pixels per image pixel, so 100% shows the capture exactly as it was on screen.</summary>
    private void ApplyZoom()
    {
        var scale = _zoom / DeviceScale;
        SurfaceScale.ScaleX = scale;
        SurfaceScale.ScaleY = scale;
        RenderOptions.SetBitmapScalingMode(Picture, _zoom >= 1 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        DrawingLayer.DipSize = 1 / scale;
        DrawingLayer.InvalidateVisual();
        ZoomText.Text = $"{Math.Round(_zoom * 100)}%";
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return;
        }

        SetZoom(_zoom * (e.Delta > 0 ? 1.25 : 0.8), e.GetPosition(Scroller));
        e.Handled = true;
    }

    private static Color? ParseColour(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return ColorConverter.ConvertFromString(text) is Color colour ? Color.FromRgb(colour.R, colour.G, colour.B) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string Hex(Color colour) => $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}";

    /// <summary>Lets the Windows Forms colour dialog sit on top of this window.</summary>
    private sealed class Win32Owner(Window window) : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle { get; } = new WindowInteropHelper(window).Handle;
    }
}
