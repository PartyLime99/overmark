using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

    private static readonly ToolKind[] LineTools = [ToolKind.Line, ToolKind.Arrow, ToolKind.Curve];
    private static readonly ToolKind[] ShapeTools = [ToolKind.Rectangle, ToolKind.Ellipse];

    /// <summary>The tool's name and a one-line reminder of how it works, for the pill above the picture.</summary>
    private static readonly Dictionary<ToolKind, (string Name, string Hint)> ToolHints = new()
    {
        [ToolKind.Move] = ("Move", "drag a mark to move it · arrow keys nudge · Delete removes"),
        [ToolKind.Freehand] = ("Freehand", "drag to draw · a click makes a dot"),
        [ToolKind.Line] = ("Line", "drag from start to end · Shift snaps to 0°, 45° or 90°"),
        [ToolKind.Arrow] = ("Arrow", "drag from tail to tip · Shift snaps to 0°, 45° or 90°"),
        [ToolKind.Curve] = ("Curve", "drag a line, then bend it from the handle in the middle · Enter finishes"),
        [ToolKind.Rectangle] = ("Rectangle", "drag a box · Shift makes a square"),
        [ToolKind.Ellipse] = ("Circle", "drag · Shift makes a perfect circle"),
        [ToolKind.Highlighter] = ("Highlighter", "four times as wide, see-through · Shift keeps it straight"),
        [ToolKind.Text] = ("Text", "click and type · Shift+Enter for a new line · size is 4 × thickness"),
        [ToolKind.Dimension] = ("Dimension", "click two points (Shift snaps 0°, 45°, 90°), move to pick H, V or aligned, click to place"),
        [ToolKind.Eraser] = ("Eraser", "click or drag across marks to remove them"),
        [ToolKind.Crop] = ("Crop", "drag a box, then press Enter"),
    };

    private readonly string _path;
    private readonly EditorDocument _document;
    private readonly DrawingTools _tools = new();
    private readonly MoveTool _move;
    private readonly Action<EditorSettings> _rememberSettings;
    private readonly Action<BitmapSource> _pin;
    private readonly Dictionary<ToolKind, RadioButton> _toolButtons;
    private readonly List<RadioButton> _swatches = [];
    private readonly RadioButton _customSwatch;
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };
    private readonly Dictionary<Popup, DateTime> _flyoutClosedAt = [];
    private ToolKind _lastDrawingTool = ToolKind.Arrow;
    private ToolKind _lastLineTool = ToolKind.Arrow;
    private ToolKind _lastShapeTool = ToolKind.Rectangle;
    private bool _syncingTools;
    private Color? _customColour;
    private double _zoom = 1;
    private bool _fitToWindow = true;
    private bool _applyingSettings;
    private bool _deleted;
    private Point? _erasingFrom;

    public EditorWindow(string path, BitmapSource image, EditorSettings settings, Action<EditorSettings> rememberSettings, Action<BitmapSource> pin)
    {
        InitializeComponent();
        _path = path;
        _rememberSettings = rememberSettings;
        _pin = pin;
        _document = new EditorDocument(image);
        _move = new MoveTool(_document);

        Picture.Source = image;
        foreach (var layer in new FrameworkElement[] { Layers, Picture, CommittedLayer, DrawingLayer })
        {
            layer.Width = image.PixelWidth;
            layer.Height = image.PixelHeight;
        }

        CommittedLayer.Attach(_document);
        DrawingLayer.Attach(_tools, _move);
        ShowCrop();

        _toolButtons = new()
        {
            [ToolKind.Move] = MoveButton,
            [ToolKind.Freehand] = FreehandTool,
            [ToolKind.Line] = LineTool,
            [ToolKind.Arrow] = ArrowTool,
            [ToolKind.Curve] = CurveTool,
            [ToolKind.Rectangle] = RectangleTool,
            [ToolKind.Ellipse] = EllipseTool,
            [ToolKind.Highlighter] = HighlighterTool,
            [ToolKind.Text] = TextTool,
            [ToolKind.Dimension] = DimensionTool,
            [ToolKind.Eraser] = EraserTool,
            [ToolKind.Crop] = CropTool,
        };
        foreach (var (tool, button) in _toolButtons)
        {
            button.Checked += (_, _) =>
            {
                if (!_syncingTools)
                {
                    SelectTool(tool);
                }
            };
        }

        SetUpFlyout(LinesGroup, LinesFlyout, LineTools, () => _lastLineTool);
        SetUpFlyout(ShapesGroup, ShapesFlyout, ShapeTools, () => _lastShapeTool);

        _customSwatch = BuildSwatches();
        ThicknessSlider.ValueChanged += (_, e) => SetThickness(e.NewValue);
        ArrowheadToggle.Checked += (_, _) => SetCurveArrowhead(true);
        ArrowheadToggle.Unchecked += (_, _) => SetCurveArrowhead(false);
        TextBoxToggle.Checked += (_, _) => SetTextBoxed(true);
        TextBoxToggle.Unchecked += (_, _) => SetTextBoxed(false);
        TypingBox.TextChanged += (_, _) => _tools.SetText(TypingBox.Text);
        CustomColourButton.Click += (_, _) => PickCustomColour();
        UndoButton.Click += (_, _) => Undo();
        RedoButton.Click += (_, _) => _document.Redo();
        FitButton.Click += (_, _) => FitToWindow();
        ActualSizeButton.Click += (_, _) => SetZoom(1, null);
        CopyButton.Click += (_, _) => Copy();
        SaveButton.Click += (_, _) => Save();
        PinButton.Click += (_, _) => Pin();
        DeleteButton.Click += (_, _) => DeleteCapture();
        MinimiseButton.Click += (_, _) => SystemCommands.MinimizeWindow(this);
        MaximiseButton.Click += (_, _) =>
        {
            if (WindowState == WindowState.Maximized)
            {
                SystemCommands.RestoreWindow(this);
            }
            else
            {
                SystemCommands.MaximizeWindow(this);
            }
        };
        CloseButton.Click += (_, _) => Close();
        SourceInitialized += (_, _) => RoundCorners();
        StateChanged += (_, _) => FitMaximisedWindow();

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
        _tools.PreviewChanged += ShowTypingBox;
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
    /// wide enough for the dock to sit on one row.
    /// </summary>
    private void SizeToImage(BitmapSource image)
    {
        var monitor = Displays.GetMonitors().FirstOrDefault(m => m.Bounds.Contains(Pointer.Position));
        var scale = (monitor?.Dpi ?? 96) / 96.0;
        var workArea = SystemParameters.WorkArea;

        // Measured with unlimited width, the dock's wrap panel reports its one-row size (margins included).
        Dock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var padding = SurfaceFrame.Padding;
        var chrome = TitleBar.Height + Dock.DesiredSize.Height + padding.Top + padding.Bottom + 24; // plus the scroll area's slack

        var width = Math.Max(image.PixelWidth / scale + padding.Left + padding.Right + 24, Dock.DesiredSize.Width + 8);
        Width = Math.Max(MinWidth, Math.Min(width, workArea.Width * 0.9));
        Height = Math.Max(MinHeight, Math.Min(image.PixelHeight / scale + chrome, workArea.Height * 0.85));
    }

    /// <summary>
    /// A dock button standing for several tools. Clicking it picks the one last used from the group; clicking it
    /// again, while one of them is picked, opens a flyout with all of them.
    /// </summary>
    private void SetUpFlyout(ToggleButton group, Popup flyout, ToolKind[] members, Func<ToolKind> last)
    {
        flyout.CustomPopupPlacementCallback = (popup, target, _) =>
            [new CustomPopupPlacement(new Point((target.Width - popup.Width) / 2, -popup.Height + 4), PopupPrimaryAxis.Horizontal)];
        flyout.Closed += (_, _) => _flyoutClosedAt[flyout] = DateTime.UtcNow;

        group.Click += (_, _) =>
        {
            if (members.Contains(_tools.Tool))
            {
                // The click that closed the flyout (by landing outside it) shouldn't open it again straight away.
                var justClosed = _flyoutClosedAt.TryGetValue(flyout, out var closedAt) && DateTime.UtcNow - closedAt < TimeSpan.FromMilliseconds(250);
                flyout.IsOpen = !justClosed;
            }
            else
            {
                _toolButtons[last()].IsChecked = true;
            }

            ShowToolButtons(); // the click toggled the button; put it back to match the tool
        };

        foreach (var tool in members)
        {
            _toolButtons[tool].Click += (_, _) => flyout.IsOpen = false;
        }
    }

    /// <summary>Marks the selected tool in the dock and flyouts, and shows each group's current (or last) tool.</summary>
    private void ShowToolButtons()
    {
        var tool = _tools.Tool;
        if (LineTools.Contains(tool))
        {
            _lastLineTool = tool;
        }
        else if (ShapeTools.Contains(tool))
        {
            _lastShapeTool = tool;
        }

        _syncingTools = true;
        try
        {
            // The flyouts' buttons aren't in the dock's group, so the others are unchecked here rather than by WPF.
            foreach (var (kind, button) in _toolButtons)
            {
                button.IsChecked = kind == tool;
            }

            LinesGroup.IsChecked = LineTools.Contains(tool);
            ShapesGroup.IsChecked = ShapeTools.Contains(tool);
        }
        finally
        {
            _syncingTools = false;
        }

        LinesIcon.Data = ToolIcon(_lastLineTool);
        ShapesIcon.Data = ToolIcon(_lastShapeTool);
    }

    /// <summary>The icon of the tool's button, borrowed for its group's button in the dock.</summary>
    private Geometry ToolIcon(ToolKind tool) =>
        ((DockPanel)_toolButtons[tool].Content).Children.OfType<System.Windows.Shapes.Path>().First().Data;

    private void CloseFlyouts()
    {
        LinesFlyout.IsOpen = false;
        ShapesFlyout.IsOpen = false;
    }

    /// <summary>Windows 11 rounds the corners of normal windows; a window with its own title bar has to ask.</summary>
    private void RoundCorners()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var round = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
    }

    /// <summary>
    /// A maximised window reaches past the screen's edges by the width of its (invisible) frame, so the content is
    /// pulled in by that much to keep the title bar and dock on screen.
    /// </summary>
    private void FitMaximisedWindow()
    {
        // Segoe Fluent Icons: Restore when maximised, Maximize otherwise.
        MaximiseGlyph.Text = ((char)(WindowState == WindowState.Maximized ? 0xE923 : 0xE922)).ToString();
        MaximiseButton.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximise";
        if (WindowState != WindowState.Maximized)
        {
            Root.Margin = new Thickness(0);
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (!NativeMethods.GetWindowRect(handle, out var rect))
        {
            return;
        }

        var centre = new PixelPoint((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2);
        if (Displays.GetMonitors().FirstOrDefault(m => m.Bounds.Contains(centre)) is not { } monitor)
        {
            return;
        }

        var work = monitor.WorkArea;
        var scale = DeviceScale;
        Root.Margin = new Thickness(
            Math.Max(0, work.Left - rect.Left) / scale,
            Math.Max(0, work.Top - rect.Top) / scale,
            Math.Max(0, rect.Right - work.Right) / scale,
            Math.Max(0, rect.Bottom - work.Bottom) / scale);
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
        swatch.Checked += (_, _) => SelectColour(SwatchColour(swatch));

        // Checked doesn't fire for the pen's own colour, but clicking it should still recolour a selected mark.
        swatch.Click += (_, _) => _move.Recolour(SwatchColour(swatch));
        return swatch;
    }

    private static Color SwatchColour(RadioButton swatch) => ((SolidColorBrush)swatch.Background).Color;

    /// <summary>A swatch picked by its number key, which (like a click) recolours a selected mark.</summary>
    private void PickSwatch(RadioButton swatch)
    {
        swatch.IsChecked = true;
        _move.Recolour(SwatchColour(swatch));
    }

    private void SelectTool(ToolKind tool)
    {
        _tools.Tool = tool;
        ArrowheadToggle.Visibility = tool == ToolKind.Curve ? Visibility.Visible : Visibility.Collapsed;
        TextBoxToggle.Visibility = tool == ToolKind.Text ? Visibility.Visible : Visibility.Collapsed;
        ToolNameText.Text = ToolHints[tool].Name;
        HintText.Text = ToolHints[tool].Hint;
        ShowToolButtons();
        ShowPen();

        // The eraser shows its own circle instead of the crosshair; the move tool's pointer shows what it would grab.
        DrawingLayer.Cursor = tool switch
        {
            ToolKind.Eraser => Cursors.None,
            ToolKind.Text => Cursors.IBeam,
            ToolKind.Move => Cursors.Arrow,
            _ => Cursors.Cross,
        };
        if (tool != ToolKind.Eraser)
        {
            DrawingLayer.EraserAt = null;
        }

        if (tool != ToolKind.Move)
        {
            _move.ClearSelection();
        }

        // Moving, erasing and cropping are one-off jobs, so the next capture opens with the last drawing tool.
        if (tool is not (ToolKind.Move or ToolKind.Eraser or ToolKind.Crop))
        {
            _lastDrawingTool = tool;
            RememberChoices();
        }
    }

    /// <summary>Loads the pen with <paramref name="colour"/>, and gives it to the move tool's selected mark, if any.</summary>
    private void SelectColour(Color colour)
    {
        _tools.Colour = colour;
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        Resources["PenBrush"] = brush;
        ShowPen();
        RememberChoices();
        _move.Recolour(colour);
    }

    private void SetThickness(double thickness)
    {
        _tools.Width = thickness;
        ShowPen();
        RememberChoices();
    }

    /// <summary>
    /// The loaded pen in the dock: a sample in the pen's colour and thickness (or "Aa" at the text size for the text
    /// tool), the colour's name, and the thickness, or for the text tool the text size it gives.
    /// </summary>
    private void ShowPen()
    {
        var colour = _tools.Colour;
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        var tool = _tools.Tool;
        var text = tool == ToolKind.Text;

        PenSample.Visibility = text ? Visibility.Collapsed : Visibility.Visible;
        PenSampleText.Visibility = text ? Visibility.Visible : Visibility.Collapsed;
        PenSample.Stroke = brush;
        PenSampleText.Foreground = brush;
        PenSampleText.FontSize = Math.Min(Palette.StrokeWidth(ToolKind.Text, _tools.Width), 22);

        // Capped so the sample stays inside its tile; the number beside it is exact.
        var highlighter = tool == ToolKind.Highlighter;
        PenSample.StrokeThickness = highlighter ? Math.Min(Palette.StrokeWidth(tool, _tools.Width), 22) : Math.Min(_tools.Width, 14);
        PenSample.Opacity = highlighter ? Palette.HighlighterAlpha / 255.0 : 1;
        PenSample.StrokeStartLineCap = PenSample.StrokeEndLineCap = highlighter ? PenLineCap.Flat : PenLineCap.Round;

        var index = Palette.Colours.ToList().IndexOf(colour);
        PenColourText.Text = index >= 0 ? Palette.ColourNames[index] : "Custom";
        ThicknessText.Text = text ? $"{Palette.StrokeWidth(ToolKind.Text, _tools.Width):0} px text" : $"{_tools.Width:0} px";
    }

    private void SetCurveArrowhead(bool on)
    {
        _tools.CurveArrowhead = on;
        RememberChoices();
    }

    private void SetTextBoxed(bool on)
    {
        _tools.TextBoxed = on;
        RememberChoices();
    }

    /// <summary>
    /// Puts the text box over text being typed, so it takes the keyboard and shows the caret, lined up so its
    /// (invisible) letters sit exactly on the drawn ones. Hides it again when the text is finished or dropped.
    /// </summary>
    private void ShowTypingBox()
    {
        if (_tools.Typing is not { } typing)
        {
            if (TypingBox.Visibility == Visibility.Visible)
            {
                TypingBox.Visibility = Visibility.Collapsed;
                TypingBox.Clear();
                Focus();
            }

            return;
        }

        TypingBox.FontSize = typing.FontSize;
        TypingBox.CaretBrush = new SolidColorBrush(typing.Colour);
        if (TypingBox.Visibility != Visibility.Visible)
        {
            // A dimension starts with its measured length, selected so typing replaces it.
            TypingBox.Visibility = Visibility.Visible;
            TypingBox.Text = typing.Text;
            TypingBox.SelectAll();
            TypingBox.Focus();
        }

        TypingBox.UpdateLayout();

        // Where the box draws its first letter, which isn't quite its top-left corner.
        var first = TypingBox.GetRectFromCharacterIndex(0);
        var inset = first.IsEmpty ? new Vector() : (Vector)first.TopLeft;
        Canvas.SetLeft(TypingBox, typing.Origin.X - inset.X);
        Canvas.SetTop(TypingBox, typing.Origin.Y - inset.Y);
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
            TextBoxToggle.IsChecked = settings.TextBoxed;
            _tools.TextBoxed = settings.TextBoxed;

            var tool = Enum.TryParse<ToolKind>(settings.Tool, true, out var saved) && saved is not (ToolKind.Move or ToolKind.Eraser or ToolKind.Crop)
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
            TextBoxed = _tools.TextBoxed,
            CustomColour = _customColour is { } custom ? Hex(custom) : null,
        });
    }

    private void OnPointerDown(object sender, MouseButtonEventArgs e)
    {
        DrawingLayer.CaptureMouse();
        var point = e.GetPosition(DrawingLayer);
        switch (_tools.Tool)
        {
            case ToolKind.Eraser:
                _erasingFrom = point;
                _document.EraseAt(point, DrawingLayer.EraserRadius);
                break;
            case ToolKind.Move:
                _move.PointerDown(point);
                break;
            default:
                _tools.PointerDown(point, IsShiftDown, DrawingLayer.HandleRadius * 1.5);
                break;
        }

        e.Handled = true;
    }

    private void OnPointerMove(object sender, MouseEventArgs e)
    {
        var point = e.GetPosition(DrawingLayer);
        if (_tools.Tool == ToolKind.Move)
        {
            if (DrawingLayer.IsMouseCaptured)
            {
                _move.PointerMove(point, IsShiftDown);
            }
            else
            {
                DrawingLayer.Cursor = _move.GripAt(point) switch
                {
                    MoveGrip.TextAngle => Cursors.Hand,
                    null => Cursors.Arrow,
                    _ => Cursors.SizeAll,
                };
            }

            return;
        }

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
        else if (_tools.Tool == ToolKind.Move)
        {
            _move.PointerUp();
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
        if (_tools.Typing is not null)
        {
            // While typing, keys go to the text box (Ctrl+C copies text, Ctrl+Z undoes typing, and so on), apart from these.
            switch (key)
            {
                case Key.Enter when !IsShiftDown:
                    _tools.Finish();
                    break;
                case Key.Escape:
                    _tools.Cancel();
                    break;
                case Key.Tab:
                    // Would move the keyboard away from the text box, with nowhere useful to go.
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
                default:
                    e.Handled = false;
                    break;
            }

            return;
        }

        if (_move.Selected is not null && !ctrl)
        {
            // The move tool's selection: arrow keys nudge it, Delete removes it, Esc lets go of it.
            var nudge = IsShiftDown ? 10 : 1;
            Vector? offset = key switch
            {
                Key.Left => new Vector(-nudge, 0),
                Key.Right => new Vector(nudge, 0),
                Key.Up => new Vector(0, -nudge),
                Key.Down => new Vector(0, nudge),
                _ => null,
            };
            if (offset is { } by)
            {
                _move.Nudge(by);
                return;
            }

            if (key is Key.Delete or Key.Back)
            {
                _move.DeleteSelected();
                return;
            }

            if (key == Key.Escape)
            {
                _move.ClearSelection();
                return;
            }
        }

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
            case Key.Escape when LinesFlyout.IsOpen || ShapesFlyout.IsOpen:
                CloseFlyouts();
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
            case Key.T when !ctrl:
                TextTool.IsChecked = true;
                break;
            case Key.D when !ctrl:
                DimensionTool.IsChecked = true;
                break;
            case Key.V when !ctrl:
                MoveButton.IsChecked = true;
                break;
            case Key.E when !ctrl:
                EraserTool.IsChecked = true;
                break;
            case Key.X when !ctrl:
                CropTool.IsChecked = true;
                break;
            case >= Key.D1 and <= Key.D8 when !ctrl:
                PickSwatch(_swatches[key - Key.D1]);
                break;
            case >= Key.NumPad1 and <= Key.NumPad8 when !ctrl:
                PickSwatch(_swatches[key - Key.NumPad1]);
                break;
            case Key.D9 or Key.NumPad9 when !ctrl && _customColour is not null:
                PickSwatch(_customSwatch);
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
        FileNameText.Text = Path.GetFileName(_path);
        UnsavedPill.Visibility = _document.HasUnsavedChanges ? Visibility.Visible : Visibility.Collapsed;
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

    /// <summary>
    /// Throws the capture away, after asking: the file goes to the Recycle Bin (so it can be put back) and the editor
    /// closes, without asking about unsaved changes.
    /// </summary>
    private void DeleteCapture()
    {
        var name = Path.GetFileName(_path);
        var answer = MessageBox.Show(
            this, $"Delete {name}?\n\nIt goes to the Recycle Bin, so you can put it back from there.", "Overmark",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        if (!RecycleBin.TrySend(_path, out var problem))
        {
            Log.Warn($"Couldn't delete {_path}: {problem}");
            MessageBox.Show(this, $"Couldn't delete {name}:\n\n{problem}", "Overmark", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Log.Info($"Deleted {_path} from the editor");
        _deleted = true;
        Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_deleted)
        {
            return;
        }

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

        var padding = SurfaceFrame.Padding;
        var fit = Math.Min(
            (width - padding.Left - padding.Right) * DeviceScale / _document.Crop.Width,
            (height - padding.Top - padding.Bottom) * DeviceScale / _document.Crop.Height);

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
        var padding = SurfaceFrame.Padding;
        var contentX = Scroller.HorizontalOffset + at.X - padding.Left;
        var contentY = Scroller.VerticalOffset + at.Y - padding.Top;
        ApplyZoom();
        Scroller.UpdateLayout();

        var ratio = _zoom / previous;
        Scroller.ScrollToHorizontalOffset(contentX * ratio + padding.Left - at.X);
        Scroller.ScrollToVerticalOffset(contentY * ratio + padding.Top - at.Y);
    }

    /// <summary>Zoom is in screen pixels per image pixel, so 100% shows the capture exactly as it was on screen.</summary>
    private void ApplyZoom()
    {
        var scale = _zoom / DeviceScale;
        SurfaceScale.ScaleX = scale;
        SurfaceScale.ScaleY = scale;
        RenderOptions.SetBitmapScalingMode(Picture, _zoom >= 1 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        DrawingLayer.DipSize = 1 / scale;
        _move.DipSize = 1 / scale;
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
