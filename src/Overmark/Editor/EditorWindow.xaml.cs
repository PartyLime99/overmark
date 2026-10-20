using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
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
using Overmark.Recording;
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
        [ToolKind.Move] = ("Move", "drag a mark to move it, or its handles to reshape it · double-click text to change it · arrow keys nudge · Delete removes"),
        [ToolKind.Freehand] = ("Freehand", "drag to draw · a click makes a dot"),
        [ToolKind.Line] = ("Line", "drag from start to end · Shift snaps to 0°, 45° or 90°"),
        [ToolKind.Arrow] = ("Arrow", "drag from tail to tip · Shift snaps to 0°, 45° or 90°"),
        [ToolKind.Curve] = ("Curve", "drag a line, then bend it from the handle in the middle · Enter finishes"),
        [ToolKind.Rectangle] = ("Rectangle", "drag a box · Shift makes a square"),
        [ToolKind.Ellipse] = ("Circle", "drag · Shift makes a perfect circle"),
        [ToolKind.Highlighter] = ("Highlighter", "four times as wide, see-through · Shift keeps it straight"),
        [ToolKind.Redact] = ("Redact", "drag a box over what to hide · a solid box in the pen's colour, or pixelated · Shift makes a square"),
        [ToolKind.Text] = ("Text", "click and type · Shift+Enter for a new line · size is 4 × thickness"),
        [ToolKind.Dimension] = ("Dimension", "click two points, then move to pick H, V or aligned and click · type a length (50 mm) to set the scale"),
        [ToolKind.Eraser] = ("Eraser", "click or drag across marks to remove them"),
        [ToolKind.Crop] = ("Crop", "drag a box, then press Enter"),
    };

    /// <summary>Stands in for the picture while no capture is open; it isn't shown.</summary>
    private static readonly BitmapSource NoPicture = MakeNoPicture();

    // The capture being edited, or none (null) with a note in its place. All three change when another capture is
    // shown in this window.
    private string? _path;
    private EditorDocument _document;
    private readonly DrawingTools _tools = new();
    private MoveTool _move;
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
    private bool _leaving;
    private Point? _erasingFrom;

    // Today's captures down the left side, with thumbnails kept between refreshes of the list.
    private readonly CaptureHistory? _history;
    private readonly Action<string>? _switchTo;
    private readonly Action<string>? _closeCapture;
    private readonly Action<string>? _deleteCapture;
    private readonly Func<string>? _howToCapture;

    // Opening images from elsewhere: a file (picked with Open, dropped, or pasted), or a picture pasted or dropped.
    private readonly Action<string>? _openFile;
    private readonly Action<BitmapSource>? _openPicture;

    // Keeps the marks editable for a day after the editor closes; null leaves them flattened, as before.
    private readonly EditStore? _edits;
    private readonly Dictionary<string, (DateTime Written, BitmapSource Image)> _thumbnails = new(StringComparer.OrdinalIgnoreCase);
    private int _recentVersion;

    /// <param name="history">The captures to list down the left side; null leaves the list out.</param>
    /// <param name="switchTo">Opens another capture in place of this one, when it's picked from the list.</param>
    /// <param name="edits">Where marks are kept editable between editors; null flattens them for good on save.</param>
    /// <param name="document">The capture with its marks still editable (from <paramref name="edits"/>); null starts from <paramref name="image"/>.</param>
    /// <param name="path">The capture to edit, or null to open with none (a note says how to take one).</param>
    /// <param name="closeCapture">Takes a capture off the list, when its x is clicked (asking about unsaved marks first).</param>
    /// <param name="deleteCapture">Deletes the capture being edited, once the bin button has asked; null leaves the button out.</param>
    /// <param name="howToCapture">What the note says when no capture is open.</param>
    /// <param name="openFile">Opens an image file from elsewhere in place of this capture; null leaves out opening, pasting and dropping images.</param>
    /// <param name="openPicture">Saves a pasted (or dropped) picture as a capture and opens it in place of this one.</param>
    public EditorWindow(
        string? path, BitmapSource? image, EditorSettings settings, Action<EditorSettings> rememberSettings, Action<BitmapSource> pin,
        CaptureHistory? history = null, Action<string>? switchTo = null, EditStore? edits = null, EditorDocument? document = null,
        Action<string>? closeCapture = null, Action<string>? deleteCapture = null, Func<string>? howToCapture = null,
        Action? openSettings = null, Action<string>? openFile = null, Action<BitmapSource>? openPicture = null)
    {
        InitializeComponent();
        _path = path;
        _rememberSettings = rememberSettings;
        _pin = pin;
        _history = history;
        _switchTo = switchTo;
        _closeCapture = closeCapture;
        _deleteCapture = deleteCapture;
        _howToCapture = howToCapture;
        _openFile = openFile;
        _openPicture = openPicture;
        _edits = edits;
        _document = document ?? new EditorDocument(image ?? NoPicture);
        _move = new MoveTool(_document);
        AttachDocument();

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
            [ToolKind.Redact] = RedactTool,
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
        ThicknessSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => _document.FinishChanging()), handledEventsToo: true);
        ThinnerButton.Click += (_, _) => StepThickness(-1);
        ThickerButton.Click += (_, _) => StepThickness(1);
        ArrowheadToggle.Checked += (_, _) => SetCurveArrowhead(true);
        ArrowheadToggle.Unchecked += (_, _) => SetCurveArrowhead(false);
        TextBoxToggle.Checked += (_, _) => SetTextBoxed(true);
        TextBoxToggle.Unchecked += (_, _) => SetTextBoxed(false);
        PixelateToggle.Checked += (_, _) => SetRedactPixelate(true);
        PixelateToggle.Unchecked += (_, _) => SetRedactPixelate(false);
        TypingBox.TextChanged += (_, _) => _tools.SetText(TypingBox.Text);
        CustomColourButton.Click += (_, _) => PickCustomColour();
        UndoButton.Click += (_, _) => Undo();
        RedoButton.Click += (_, _) => _document.Redo();
        RemoveWhiteButton.Click += (_, _) => RemoveWhiteBackground();
        FitButton.Click += (_, _) => FitToWindow();
        ActualSizeButton.Click += (_, _) => SetZoom(1, null);
        CopyButton.Click += (_, _) => Copy();
        SaveButton.Click += (_, _) => Save();
        PinButton.Click += (_, _) => Pin();
        DeleteButton.Click += (_, _) => DeleteCapture();
        SettingsButton.Click += (_, _) => openSettings?.Invoke();
        SettingsButton.Visibility = openSettings is null ? Visibility.Collapsed : Visibility.Visible;
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

        // Lambdas rather than method groups, so they reach whichever capture is showing now.
        _tools.Committed += mark => SayIfScaleChanges(() => _document.Add(mark));
        _tools.CropApplied += area => _document.CropTo(area);
        _tools.Edited += (mark, changed) => SayIfScaleChanges(() => _document.Replace(mark, changed));
        _tools.PreviewChanged += ShowTypingBox;
        _tools.PreviewChanged += () => CommittedLayer.Hidden = _tools.Editing;
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

        RecentToggle.Checked += (_, _) => ShowRecentPane(true);
        RecentToggle.Unchecked += (_, _) => ShowRecentPane(false);
        OpenFolderButton.Click += (_, _) => ShowInExplorer();
        OpenImageButton.Click += (_, _) => OpenImage();
        NoCaptureOpenButton.Click += (_, _) => OpenImage();
        if (openFile is null || openPicture is null)
        {
            OpenImageButton.Visibility = Visibility.Collapsed;
            NoCaptureOpenPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            AllowDrop = true;
            PreviewDragEnter += OnDragOver;
            PreviewDragOver += OnDragOver;
            PreviewDrop += OnDrop;
        }
        if (history is null)
        {
            RecentToggle.Visibility = Visibility.Collapsed;
            RecentPane.Visibility = Visibility.Collapsed;
        }
        else
        {
            history.Changed += ListRecentCaptures;
            Closed += (_, _) => history.Changed -= ListRecentCaptures;
            ListRecentCaptures();
        }

        ApplySettings(settings);
        ShowWhetherCaptureOpen();
        UpdateCommands();
        if (document is not null)
        {
            ShowStatus("Marks still editable");
        }

        // Last, so the toolbar is complete (swatches included) when it's measured.
        SizeToImage(image);
    }

    private static bool IsShiftDown => Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

    private double DeviceScale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    /// <summary>
    /// Opens big enough to show the capture at actual size when it fits, up to most of the screen, and at least
    /// wide enough for the dock to sit on one row. With no capture, it opens at its usual size.
    /// </summary>
    private void SizeToImage(BitmapSource? image)
    {
        var monitor = Displays.GetMonitors().FirstOrDefault(m => m.Bounds.Contains(Pointer.Position));
        var scale = (monitor?.Dpi ?? 96) / 96.0;
        var workArea = SystemParameters.WorkArea;
        if (image is null)
        {
            Width = Math.Max(MinWidth, Math.Min(Width, workArea.Width * 0.9));
            Height = Math.Max(MinHeight, Math.Min(Height, workArea.Height * 0.85));
            return;
        }

        // Measured with unlimited width, the dock's wrap panel reports its one-row size (margins included).
        Dock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var padding = SurfaceFrame.Padding;
        var chrome = TitleBar.Height + Dock.DesiredSize.Height + padding.Top + padding.Bottom + 24; // plus the scroll area's slack

        var pane = RecentPane.Visibility == Visibility.Visible ? RecentPane.Width : 0;
        var width = Math.Max(image.PixelWidth / scale + padding.Left + padding.Right + 24, Dock.DesiredSize.Width + 8) + pane;
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
        PixelateToggle.Visibility = tool == ToolKind.Redact ? Visibility.Visible : Visibility.Collapsed;
        ShowScale();
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

    /// <summary>The slider's thickness goes to the pen, and to the move tool's selected mark, if any.</summary>
    private void SetThickness(double thickness)
    {
        _tools.Width = thickness;
        ShowPen();
        RememberChoices();

        // Dragging the slider's thumb changes the selected mark as it goes, as one undo step when it's let go.
        _move.SetThickness(thickness, finished: !ThicknessSlider.IsMouseCaptureWithin);
    }

    /// <summary>
    /// The dots either side of the slider, and [ and ]: thinner or thicker by <paramref name="by"/>, stepping from the
    /// thickness of what's being changed (text being typed, or the selected mark) if there is one, else the pen's.
    /// </summary>
    private void StepThickness(double by)
    {
        var changing = _tools.Typing is not null ? _tools.Preview : _move.SelectedMark;
        var from = changing is null ? ThicknessSlider.Value : Math.Round(changing.Thickness);
        var thickness = Math.Clamp(from + by, ThicknessSlider.Minimum, ThicknessSlider.Maximum);
        if (ThicknessSlider.Value == thickness)
        {
            // The pen is already there, but what's being changed isn't.
            SetThickness(thickness);
        }
        else
        {
            ThicknessSlider.Value = thickness;
        }
    }

    /// <summary>
    /// The loaded pen in the dock: a sample in the pen's colour and thickness (or "Aa" at the text size for the text
    /// tool, or a box for the redact tool), the colour's name, and the thickness, or for the text tool the text size it
    /// gives, and for pixelating the block size.
    /// </summary>
    private void ShowPen()
    {
        var colour = _tools.Colour;
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        var tool = _tools.Tool;
        var text = tool == ToolKind.Text;
        var redact = tool == ToolKind.Redact;
        var pixelate = redact && _tools.RedactPixelate;

        PenSample.Visibility = text || redact ? Visibility.Collapsed : Visibility.Visible;
        PenSampleText.Visibility = text ? Visibility.Visible : Visibility.Collapsed;
        PenSampleBox.Visibility = redact ? Visibility.Visible : Visibility.Collapsed;
        PenSampleBox.Background = pixelate ? (Brush)FindResource("MosaicBrush") : brush;
        PenSample.Stroke = brush;
        PenSampleText.Foreground = brush;
        PenSampleText.FontSize = Math.Min(Palette.StrokeWidth(ToolKind.Text, _tools.Width), 22);

        // Capped so the sample stays inside its tile; the number beside it is exact.
        var highlighter = tool == ToolKind.Highlighter;
        PenSample.StrokeThickness = highlighter ? Math.Min(Palette.StrokeWidth(tool, _tools.Width), 22) : Math.Min(_tools.Width, 14);
        PenSample.Opacity = highlighter ? Palette.HighlighterAlpha / 255.0 : 1;
        PenSample.StrokeStartLineCap = PenSample.StrokeEndLineCap = highlighter ? PenLineCap.Flat : PenLineCap.Round;

        var index = Palette.Colours.ToList().IndexOf(colour);
        PenColourText.Text = pixelate ? "Pixelate" : index >= 0 ? Palette.ColourNames[index] : "Custom";
        ThicknessText.Text = text ? $"{Palette.StrokeWidth(ToolKind.Text, _tools.Width):0} px text"
            : pixelate ? $"{Palette.BlockSize(_tools.Width)} px blocks"
            : redact ? "solid box"
            : $"{_tools.Width:0} px";
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

    private void SetRedactPixelate(bool on)
    {
        _tools.RedactPixelate = on;
        ShowPen();
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
        var left = typing.Origin.X - inset.X;
        var top = typing.Origin.Y - inset.Y;
        Canvas.SetLeft(TypingBox, left);
        Canvas.SetTop(TypingBox, top);

        // A dimension's text may have been turned: the box turns with it, about its centre, so the caret stays on the letters.
        var frame = typing.Frame;
        TypingBox.RenderTransform = typing.Angle == 0
            ? Transform.Identity
            : new RotateTransform(typing.Angle, frame.X + frame.Width / 2 - left, frame.Y + frame.Height / 2 - top);
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
            PixelateToggle.IsChecked = settings.RedactPixelate;
            _tools.RedactPixelate = settings.RedactPixelate;
            ShowRecent = settings.ShowRecentCaptures;

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
            RedactPixelate = _tools.RedactPixelate,
            CustomColour = _customColour is { } custom ? Hex(custom) : null,
            ShowRecentCaptures = RecentToggle.IsChecked == true,
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
                // A click away from text whose words are being changed finishes it; double-clicking text starts changing them.
                _tools.Finish();
                if (e.ClickCount == 2 && _move.TextAt(point) is { } text)
                {
                    _move.ClearSelection();
                    _tools.EditText(text);
                }
                else
                {
                    _move.PointerDown(point);
                }

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
                    MoveGrip.Handle => _move.HandleAt(point) switch
                    {
                        ShapeHandle.TopLeft or ShapeHandle.BottomRight => Cursors.SizeNWSE,
                        ShapeHandle.TopRight or ShapeHandle.BottomLeft => Cursors.SizeNESW,
                        ShapeHandle.Top or ShapeHandle.Bottom => Cursors.SizeNS,
                        ShapeHandle.Left or ShapeHandle.Right => Cursors.SizeWE,
                        _ => Cursors.Cross,
                    },
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

        // Opening or pasting an image works with or without a capture open (while typing, Ctrl+V pastes text).
        if (ctrl && key == Key.O && _openFile is not null)
        {
            OpenImage();
            return;
        }

        if (ctrl && key == Key.V && _tools.Typing is null && _openFile is not null)
        {
            Paste();
            return;
        }

        if (_path is null)
        {
            // No capture open: nothing to draw on, copy or save.
            e.Handled = false;
            return;
        }

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
                StepThickness(-step);
                break;
            case Key.OemCloseBrackets:
                StepThickness(step);
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
            case Key.M when !ctrl:
                RedactTool.IsChecked = true;
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
            case Key.B when !ctrl:
                RemoveWhiteBackground();
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

    /// <summary>
    /// Makes a change to the marks, and if it set the scale dimensions measure in (a length was typed into one) or took
    /// it away, says so.
    /// </summary>
    private void SayIfScaleChanges(Action change)
    {
        var before = _document.Scale;
        change();
        var after = _document.Scale;
        if (after is not null && after != before)
        {
            ShowStatus($"Scale set: {after.Describe()}");
        }
        else if (after is null && before is not null)
        {
            ShowStatus("Dimensions measure in pixels again");
        }
    }

    /// <summary>The scale dimensions measure in, for the dimension tool and in its pill; nothing while it's pixels.</summary>
    private void ShowScale()
    {
        var scale = _path is null ? null : _document.Scale;
        _tools.Scale = scale;
        ScaleText.Text = scale?.Describe() ?? "";
        ScaleChip.Visibility = scale is not null && _tools.Tool == ToolKind.Dimension ? Visibility.Visible : Visibility.Collapsed;
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
        var name = _path is null ? null : Path.GetFileName(_path);
        Title = name is null ? "Overmark" : $"{(_document.HasUnsavedChanges ? "*" : "")}{name} - Overmark";
        FileNameText.Text = name ?? "Overmark";
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

    /// <summary>Shows the picture as it is now, over a checkerboard where it's see-through.</summary>
    private void ShowPicture()
    {
        Picture.Source = _document.Image;
        DrawingLayer.Picture = _document.Image;
        Checkerboard.Visibility = _document.IsSeeThrough ? Visibility.Visible : Visibility.Collapsed;
        Surface.Background = _document.IsSeeThrough ? Brushes.Transparent : Brushes.White;
    }

    private void RemoveWhiteBackground()
    {
        _tools.Finish();
        ShowStatus(_document.RemoveWhiteBackground() ? "White background removed (Ctrl+Z brings it back)" : "No white background touching the edges");
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
        if (_path is not { } path)
        {
            return false;
        }

        try
        {
            // Write next to the file and swap it in, so a failed save never leaves a half-written capture. An image
            // opened from elsewhere keeps its format (a JPEG stays a JPEG).
            var temporary = path + ".saving";
            File.WriteAllBytes(temporary, ImageFiles.Encode(_document.Render(), path));
            File.Move(temporary, path, overwrite: true);
            _document.MarkSaved();
            ShowStatus("Saved");
            KeepEditable(path);

            // Its thumbnail in the list now shows the marks.
            _thumbnails.Remove(path);
            ListRecentCaptures();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Couldn't save {path}", ex);
            MessageBox.Show(this, $"Couldn't save {Path.GetFileName(path)}:\n\n{ex.Message}", "Overmark", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>Ctrl+O, or the open buttons: picks an image file to mark up, which opens in place of this capture.</summary>
    private void OpenImage()
    {
        if (_openFile is null)
        {
            return;
        }

        _tools.Finish();
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Open an image to mark up", Filter = ImageFiles.DialogFilter };
        if (dialog.ShowDialog(this) == true)
        {
            _openFile(dialog.FileName);
        }
    }

    /// <summary>
    /// Ctrl+V: a picture on the clipboard is saved as a new capture and opens in place of this one; an image file
    /// copied in File Explorer opens as it is.
    /// </summary>
    private void Paste()
    {
        var (file, picture) = ImageFiles.FromClipboard();
        if (file is not null)
        {
            _openFile?.Invoke(file);
        }
        else if (picture is not null)
        {
            _openPicture?.Invoke(picture);
        }
        else
        {
            ShowStatus("Nothing to paste: copy a picture, or an image file, first");
        }
    }

    /// <summary>An image file, or a picture, dragged over the editor can be dropped on it to open.</summary>
    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (ImageFiles.HasImage(e.Data))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            // Files, but not images: not here (text dragged into text being typed is left to the text box).
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!ImageFiles.HasImage(e.Data))
        {
            return;
        }

        e.Handled = true;
        var (file, picture) = ImageFiles.Read(e.Data);

        // Opening it may ask questions, which shouldn't hold up the app the image was dragged from.
        Dispatcher.InvokeAsync(() =>
        {
            Activate();
            if (file is not null)
            {
                _openFile?.Invoke(file);
            }
            else if (picture is not null)
            {
                _openPicture?.Invoke(picture);
            }
        });
    }

    /// <summary>
    /// Throws the capture away, after asking: the file goes to the Recycle Bin (so it can be put back), unsaved changes
    /// and all, and the editor moves on to the next capture (see <see cref="EditorLauncher.DeleteCapture"/>).
    /// </summary>
    private void DeleteCapture()
    {
        if (_path is not { } path || _deleteCapture is not { } delete)
        {
            return;
        }

        var answer = MessageBox.Show(
            this, $"Delete {Path.GetFileName(path)}?\n\nIt goes to the Recycle Bin, so you can put it back from there.", "Overmark",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes)
        {
            delete(path);
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_leaving && !ConfirmLeave())
        {
            e.Cancel = true;
        }
    }

    /// <summary>
    /// Before closing or switching to another capture: asks whether to save unsaved changes (saving them if so).
    /// Returns false if the user cancelled, or the save failed, so the capture should stay open.
    /// </summary>
    public bool ConfirmLeave()
    {
        _tools.Finish();
        if (_path is not { } path || !_document.HasUnsavedChanges)
        {
            return true;
        }

        var answer = MessageBox.Show(
            this, $"Save your changes to {Path.GetFileName(path)}?", "Overmark", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.No)
        {
            // Not saving means the unsaved marks go, including any kept from switching away earlier.
            _edits?.DiscardDraft(path);
            return true;
        }

        return answer == MessageBoxResult.Yes && Save();
    }

    /// <summary>
    /// Before switching to another capture: unsaved marks are kept as a draft for a day, rather than asking about
    /// saving, so they're still there (and still unsaved) when the capture is opened again.
    /// </summary>
    public void KeepDraft()
    {
        _tools.Finish();
        if (_path is not null && _document.HasUnsavedChanges)
        {
            _edits?.KeepDraft(_path, _document);
        }
    }

    /// <summary>Closes without asking about unsaved changes (they've been asked about, or kept as a draft).</summary>
    public void CloseWithoutAsking()
    {
        _leaving = true;
        Close();
    }

    /// <summary>
    /// After a save, which flattened the marks into the file: keeps them editable for a day, or forgets the editable
    /// copy if there's nothing on the capture left to edit.
    /// </summary>
    private void KeepEditable(string path)
    {
        if (_edits is null)
        {
            return;
        }

        var crop = _document.Crop;
        var image = _document.Image;
        if (_document.Marks.Count == 0 && crop.X == 0 && crop.Y == 0 && crop.Width == image.PixelWidth && crop.Height == image.PixelHeight)
        {
            _edits.Forget(path);
        }
        else
        {
            _edits.KeepSaved(path, _document);
        }
    }

    /// <summary>Whether the list of today's captures is showing (it never shows without a history to list).</summary>
    public bool ShowRecent
    {
        get => RecentToggle.IsChecked == true;
        set => RecentToggle.IsChecked = value && _history is not null;
    }

    private void ShowRecentPane(bool show)
    {
        RecentPane.Visibility = show && _history is not null ? Visibility.Visible : Visibility.Collapsed;
        RememberChoices();

        // The picture's area changed size, so fit it again once the layout has caught up.
        if (_fitToWindow && IsLoaded)
        {
            Dispatcher.InvokeAsync(FitToWindow, DispatcherPriority.Loaded);
        }
    }

    /// <summary>
    /// Fills the list with today's captures, newest first, the open one marked. Thumbnails are read in the
    /// background (small, so it's quick) and kept, so refreshing the list after a new capture only reads the new one.
    /// </summary>
    private void ListRecentCaptures()
    {
        if (_history is null)
        {
            return;
        }

        var records = _history.TakenOn(DateTime.Today);
        RecentCount.Text = records.Count == 1 ? "1 capture" : $"{records.Count} captures";
        RecentList.Children.Clear();
        if (records.Count == 0)
        {
            RecentList.Children.Add(new TextBlock
            {
                Text = "Captures you take today show up here.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x62, 0x6D)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(6, 4, 6, 0),
            });
            return;
        }

        var toLoad = new List<(string Path, Image Target)>();
        foreach (var record in records)
        {
            var (item, thumbnail) = MakeRecentItem(record);
            RecentList.Children.Add(item);
            if (thumbnail is null)
            {
                continue;
            }

            var written = File.GetLastWriteTimeUtc(record.Path);
            if (_thumbnails.TryGetValue(record.Path, out var known) && known.Written == written)
            {
                thumbnail.Source = known.Image;
            }
            else
            {
                toLoad.Add((record.Path, thumbnail));
            }
        }

        if (toLoad.Count > 0)
        {
            var version = ++_recentVersion;
            _ = Task.Run(() =>
            {
                foreach (var (file, target) in toLoad)
                {
                    var written = File.GetLastWriteTimeUtc(file);
                    if (LoadThumbnail(file) is not { } loaded)
                    {
                        continue;
                    }

                    Dispatcher.InvokeAsync(() =>
                    {
                        _thumbnails[file] = (written, loaded);
                        if (version == _recentVersion)
                        {
                            target.Source = loaded;
                        }
                    });
                }
            });
        }
    }

    /// <summary>One capture in the list: its thumbnail (or a play tile for a video), what kind it is and when.</summary>
    private (Button Item, Image? Thumbnail) MakeRecentItem(CaptureRecord record)
    {
        const double tileWidth = 144;
        const double tileHeight = 81;
        var tile = new Grid
        {
            Width = tileWidth,
            Height = tileHeight,
            Clip = new RectangleGeometry(new Rect(0, 0, tileWidth, tileHeight), 6, 6),
            Background = new SolidColorBrush(record.IsVideo ? Color.FromRgb(0x2B, 0x30, 0x38) : Color.FromRgb(0xEE, 0xF0, 0xF3)),
        };

        Image? thumbnail = null;
        if (record.IsVideo)
        {
            tile.Children.Add(new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M0,0 L12,7 L0,14 Z"),
                Fill = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var length = TimeSpan.FromSeconds(record.Seconds ?? 0);
            tile.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xC0, 0x16, 0x19, 0x1D)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(0, 0, 5, 5),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Child = new TextBlock
                {
                    Text = $"{(int)length.TotalMinutes}:{length.Seconds:00}",
                    FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                    FontSize = 10,
                    Foreground = Brushes.White,
                },
            });
        }
        else
        {
            thumbnail = new Image { Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Top };
            RenderOptions.SetBitmapScalingMode(thumbnail, BitmapScalingMode.HighQuality);
            tile.Children.Add(thumbnail);

            // Unsaved marks left behind by switching away (the thumbnail is the saved file, without them).
            if (!string.Equals(record.Path, _path, StringComparison.OrdinalIgnoreCase) && _edits?.HasDraft(record.Path) == true)
            {
                tile.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF1, 0xCC)),
                    CornerRadius = new CornerRadius(7),
                    Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(5, 5, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    ToolTip = "Has unsaved marks, kept from when you switched away",
                    Child = new TextBlock
                    {
                        Text = "Unsaved",
                        FontSize = 10,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x4E, 0x00)),
                    },
                });
            }
        }

        var caption = new DockPanel { Margin = new Thickness(2, 5, 2, 1) };
        var time = new TextBlock
        {
            Text = record.TakenAt.ToString("t", CultureInfo.CurrentCulture),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x62, 0x6D)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(time, System.Windows.Controls.Dock.Right); // "Dock" alone is the dock along the bottom
        caption.Children.Add(time);
        caption.Children.Add(new TextBlock { Text = KindName(record), FontSize = 12, FontWeight = FontWeights.SemiBold });

        var content = new StackPanel();
        content.Children.Add(tile);
        content.Children.Add(caption);

        var open = string.Equals(record.Path, _path, StringComparison.OrdinalIgnoreCase);
        var item = new Button
        {
            Style = (Style)FindResource("RecentItem"),
            Content = content,
            Tag = open ? "open" : null,
            ToolTip = $"{Path.GetFileName(record.Path)}  ·  {record.Width} × {record.Height}" + (record.IsVideo ? "  ·  click to play" : open ? "  ·  open now" : ""),
        };
        item.Click += (_, _) => OpenRecent(record);

        // An x in the corner takes it off the list: always there on the open capture, on the others while hovered.
        if (_closeCapture is { } closeCapture)
        {
            var close = new Button
            {
                Style = (Style)FindResource("TileClose"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 5, 5, 0),
                Visibility = open ? Visibility.Visible : Visibility.Hidden,
                ToolTip = open ? "Close this capture (asks first if it has unsaved changes)" : "Take it off the list (asks first if it has unsaved marks)",
                Content = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M1,1 L9,9 M9,1 L1,9"),
                    Stroke = Brushes.White,
                    StrokeThickness = 1.6,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                },
            };
            close.Click += (_, e) =>
            {
                // Stop the click here: it would carry on to the tile it sits on, which opens the capture again.
                e.Handled = true;
                closeCapture(record.Path);
            };
            tile.Children.Add(close);
            if (!open)
            {
                item.MouseEnter += (_, _) => close.Visibility = Visibility.Visible;
                item.MouseLeave += (_, _) => close.Visibility = Visibility.Hidden;
            }
        }

        return (item, thumbnail);
    }

    /// <summary>The capture this window is editing, or null if none is open.</summary>
    public string? CapturePath => _path;

    /// <summary>
    /// Whether a dialog is open over this window (a question such as whether to save or delete, or the colour
    /// picker), which Windows shows by disabling the window until it's answered.
    /// </summary>
    public bool IsShowingDialog
    {
        get
        {
            var handle = new WindowInteropHelper(this).Handle;
            return handle != IntPtr.Zero && !NativeMethods.IsWindowEnabled(handle);
        }
    }

    /// <summary>
    /// Shows another capture in this window, in place of the current one, so switching doesn't flash a new window
    /// open and the old one closed. The current one's unsaved changes must already be dealt with (kept as a draft,
    /// saved or thrown away). <paramref name="document"/> brings back a capture whose marks are still editable.
    /// </summary>
    public void ShowCapture(string path, BitmapSource image, EditorDocument? document) =>
        Show(path, document ?? new EditorDocument(image), kept: document is not null);

    /// <summary>Shows no capture, with a note in the picture's place saying how to take one (as when the last one is deleted).</summary>
    public void ShowNoCapture() => Show(null, new EditorDocument(NoPicture), kept: false);

    private void Show(string? path, EditorDocument document, bool kept)
    {
        _tools.Cancel();
        _document.Changed -= OnDocumentChanged;
        _path = path;
        _document = document;
        _move = new MoveTool(_document);
        _leaving = false;
        _erasingFrom = null;
        DrawingLayer.EraserAt = null;
        AttachDocument();

        ShowWhetherCaptureOpen();
        UpdateCommands();
        ListRecentCaptures();
        _fitToWindow = true;
        if (IsLoaded)
        {
            FitToWindow();
        }
        else
        {
            ApplyZoom();
        }

        _statusTimer.Stop();
        StatusText.Text = "";
        if (kept)
        {
            ShowStatus("Marks still editable");
        }
    }

    /// <summary>
    /// With a capture open, the picture and everything for drawing on it; without one, a note in the picture's place
    /// saying how to take one, and nothing to copy, save or delete.
    /// </summary>
    private void ShowWhetherCaptureOpen()
    {
        var open = _path is not null;
        var appearing = open && Scroller.Visibility != Visibility.Visible;
        foreach (var element in new UIElement[] { Scroller, ToolPill, ZoomPill, Dock, PinButton, CopyButton, SaveButton, OpenFolderButton })
        {
            element.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        }

        DeleteButton.Visibility = open && _deleteCapture is not null ? Visibility.Visible : Visibility.Collapsed;
        NoCaptureNote.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        NoCaptureHint.Text = _howToCapture?.Invoke() ?? "Take a screenshot to start editing, and it opens here.";

        // The picture's area has only just appeared, so fit the picture to it once the layout has caught up.
        if (appearing && IsLoaded)
        {
            Dispatcher.InvokeAsync(FitToWindow, DispatcherPriority.Loaded);
        }
    }

    /// <summary>Shows the current capture: the layers sized to its picture, its marks drawn, and its changes followed.</summary>
    private void AttachDocument()
    {
        var image = _document.Image;
        foreach (var layer in new FrameworkElement[] { Layers, Picture, CommittedLayer, DrawingLayer })
        {
            layer.Width = image.PixelWidth;
            layer.Height = image.PixelHeight;
        }

        CommittedLayer.Attach(_document);
        DrawingLayer.Attach(_tools, _move);
        _document.Changed += OnDocumentChanged;
        ShowCrop();
        ShowPicture();
        ShowScale();
    }

    private void OnDocumentChanged()
    {
        UpdateCommands();
        ShowCrop();
        ShowPicture();
        ShowScale();
    }

    private static string KindName(CaptureRecord record) => record.IsVideo
        ? "Video"
        : record.Kind switch
        {
            CaptureKind.Window => "Window",
            CaptureKind.LastRegion => "Last region",
            CaptureKind.FullScreen => "Full screen",
            CaptureKind.Pasted => "Pasted",
            CaptureKind.Opened => "Opened",
            _ => "Region",
        };

    /// <summary>A video plays in the default player; a screenshot opens in place of this one.</summary>
    private void OpenRecent(CaptureRecord record)
    {
        if (record.IsVideo)
        {
            RecordingController.Play(record.Path);
        }
        else if (!string.Equals(record.Path, _path, StringComparison.OrdinalIgnoreCase))
        {
            _switchTo?.Invoke(record.Path);
        }
    }

    /// <summary>A small copy of a capture for the list, or null if it can't be read (e.g. it's being written).</summary>
    private static BitmapSource? LoadThumbnail(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.DecodePixelWidth = 288; // twice the tile's width, so it's sharp on high-DPI screens
            image.Rotation = ImageFiles.ThumbnailRotation(path); // a photo opened from elsewhere, the right way up
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            Log.Warn($"Couldn't read a thumbnail of {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Opens the capture's folder in File Explorer, with the capture selected.</summary>
    private void ShowInExplorer()
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            Process.Start("explorer.exe", $"/select,\"{_path}\"");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Log.Warn($"Couldn't show {_path} in File Explorer: {ex.Message}");
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

    private static BitmapSource MakeNoPicture()
    {
        var picture = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgr32, null, new byte[4], 4);
        picture.Freeze();
        return picture;
    }

    /// <summary>Lets the Windows Forms colour dialog sit on top of this window.</summary>
    private sealed class Win32Owner(Window window) : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle { get; } = new WindowInteropHelper(window).Handle;
    }
}
