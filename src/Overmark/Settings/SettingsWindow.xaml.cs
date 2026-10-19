using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Overmark.Capture;
using Overmark.Hotkeys;
using Overmark.Platform;

namespace Overmark.Settings;

/// <summary>Edits a copy of the settings; nothing changes until Save is clicked.</summary>
internal partial class SettingsWindow : Window
{
    private static readonly Brush RecordingBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)));
    private static readonly Brush NormalBorderBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xDA, 0xDF, 0xE5)));

    private readonly AppSettings _settings;
    private readonly IHotkeyRecorder _hotkeys;
    private readonly Action<AppSettings> _save;
    private readonly Dictionary<HotkeyAction, Button> _hotkeyBoxes;

    // Recording stops on its own after this long, so the keyboard can never stay captured.
    private readonly DispatcherTimer _recordingTimeout = new() { Interval = TimeSpan.FromSeconds(10) };
    private HotkeyAction? _recording;

    // True when the key being recorded is added to the action's hotkeys rather than replacing them.
    private bool _recordingExtra;

    public SettingsWindow(AppSettings settings, IHotkeyRecorder hotkeys, Action<AppSettings> save, Action openSettingsFile)
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height;
        _settings = settings;
        _hotkeys = hotkeys;
        _save = save;
        _hotkeyBoxes = new()
        {
            [HotkeyAction.RegionCapture] = RegionCaptureBox,
            [HotkeyAction.LastRegion] = LastRegionBox,
            [HotkeyAction.FullScreen] = FullScreenBox,
            [HotkeyAction.Record] = RecordBox,
        };

        foreach (var (action, box) in _hotkeyBoxes)
        {
            box.Click += (_, _) => StartRecording(action, extra: false);
        }

        RegionCaptureAdd.Click += (_, _) => StartRecording(HotkeyAction.RegionCapture, extra: true);
        LastRegionAdd.Click += (_, _) => StartRecording(HotkeyAction.LastRegion, extra: true);
        FullScreenAdd.Click += (_, _) => StartRecording(HotkeyAction.FullScreen, extra: true);
        RecordAdd.Click += (_, _) => StartRecording(HotkeyAction.Record, extra: true);

        RegionCaptureClear.Click += (_, _) => ClearHotkey(HotkeyAction.RegionCapture);
        LastRegionClear.Click += (_, _) => ClearHotkey(HotkeyAction.LastRegion);
        FullScreenClear.Click += (_, _) => ClearHotkey(HotkeyAction.FullScreen);
        RecordClear.Click += (_, _) => ClearHotkey(HotkeyAction.Record);
        _recordingTimeout.Tick += (_, _) => StopRecording();
        Deactivated += (_, _) => StopRecording();
        Closed += (_, _) => StopRecording();

        CaptureOnRelease.IsChecked = !settings.AdjustBeforeCapture;
        (settings.DrawFromCentre ? DrawFromCentre : DrawFromCorner).IsChecked = true;
        ShowCrosshairBox.IsChecked = settings.ShowCrosshair;
        ShowMagnifierBox.IsChecked = settings.ShowMagnifier;
        (settings.RecordFrameRate switch { 15 => Fps15, 60 => Fps60, _ => Fps30 }).IsChecked = true;
        RecordCursorBox.IsChecked = settings.RecordCursor;
        SaveFolderBox.Text = settings.SaveFolder ?? "";
        SaveFolderBox.TextChanged += (_, _) => ShowSaveFolderHint();
        BrowseButton.Click += (_, _) => BrowseForFolder(SaveFolderBox, "Where should screenshots be saved?");
        RecordingFolderBox.Text = settings.RecordingFolder ?? "";
        RecordingFolderBox.TextChanged += (_, _) => ShowSaveFolderHint();
        BrowseRecordingButton.Click += (_, _) => BrowseForFolder(RecordingFolderBox, "Where should recordings be saved?");
        PatternBox.Text = settings.FileNamePattern ?? "";
        PatternBox.TextChanged += (_, _) => ShowPatternExample();
        PopupSlider.Value = Math.Clamp(Math.Round(settings.PopupSeconds), PopupSlider.Minimum, PopupSlider.Maximum);
        PopupSlider.ValueChanged += (_, _) => ShowPopupText();
        StartWithWindowsBox.IsChecked = StartWithWindows.IsEnabled();
        CheckForUpdatesBox.IsChecked = settings.CheckForUpdates;
        WindowsSettingButton.Click += (_, _) => SnippingKeySetting.OpenWindowsSetting();
        Activated += (_, _) => ShowPrintScreenStatus(); // refreshed after a visit to Windows Settings
        VersionRun.Text = $"Overmark {AppInfo.Version}";
        OpenFileLink.Click += (_, _) => openSettingsFile();
        SaveButton.Click += (_, _) => Save();
        CancelButton.Click += (_, _) => Close();

        ShowHotkeys();
        ShowSaveFolderHint();
        ShowPatternExample();
        ShowPopupText();
        ShowPrintScreenStatus();
    }

    private void StartRecording(HotkeyAction action, bool extra)
    {
        StopRecording();
        _recording = action;
        _recordingExtra = extra;
        _hotkeyBoxes[action].Content = extra ? "Press the extra keys…" : "Press the keys…";
        _hotkeyBoxes[action].BorderBrush = RecordingBrush;
        _hotkeys.StartRecording(OnKeyRecorded);
        _recordingTimeout.Start();
    }

    private void OnKeyRecorded(Hotkey hotkey)
    {
        if (_recording is not { } action)
        {
            return;
        }

        // Esc on its own keeps the old hotkey.
        if (!(hotkey.Modifiers == ModifierKeys.None && KeyInterop.KeyFromVirtualKey(hotkey.VirtualKey) == Key.Escape))
        {
            var hotkeys = _recordingExtra ? ValidHotkeys(action) : new List<Hotkey>();
            if (!hotkeys.Contains(hotkey))
            {
                hotkeys.Add(hotkey);
            }

            _settings.Hotkeys.Set(action, HotkeyBindings.Join(hotkeys));
        }

        StopRecording();
    }

    private void StopRecording()
    {
        _hotkeys.StopRecording();
        _recordingTimeout.Stop();
        _recording = null;
        ShowHotkeys();
    }

    private void ClearHotkey(HotkeyAction action)
    {
        StopRecording();
        _settings.Hotkeys.Set(action, "None");
        ShowHotkeys();
    }

    private void ShowHotkeys()
    {
        foreach (var (action, box) in _hotkeyBoxes)
        {
            var hotkeys = ValidHotkeys(action);
            box.Content = hotkeys.Count > 0 ? HotkeyBindings.Describe(hotkeys) : "None";
            box.BorderBrush = NormalBorderBrush;
        }

        HotkeyBindings.FromSettings(_settings.Hotkeys, out var problems);
        HotkeyProblems.Text = string.Join(Environment.NewLine, problems);
        HotkeyProblems.Visibility = problems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The action's hotkeys as written in the settings, leaving out any that aren't valid.</summary>
    private List<Hotkey> ValidHotkeys(HotkeyAction action)
    {
        var hotkeys = new List<Hotkey>();
        foreach (var entry in HotkeyBindings.Split(_settings.Hotkeys.Get(action)))
        {
            if (Hotkey.TryParse(entry, out var hotkey) && !hotkeys.Contains(hotkey))
            {
                hotkeys.Add(hotkey);
            }
        }

        return hotkeys;
    }

    private void BrowseForFolder(TextBox box, string title)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            InitialDirectory = !string.IsNullOrWhiteSpace(box.Text) ? box.Text
                : !string.IsNullOrWhiteSpace(SaveFolderBox.Text) ? SaveFolderBox.Text
                : FileNamer.DefaultFolder,
        };
        if (dialog.ShowDialog(this) == true)
        {
            box.Text = dialog.FolderName;
        }
    }

    private void ShowSaveFolderHint()
    {
        var screenshots = string.IsNullOrWhiteSpace(SaveFolderBox.Text) ? FileNamer.DefaultFolder : SaveFolderBox.Text.Trim();
        SaveFolderHint.Text = string.IsNullOrWhiteSpace(SaveFolderBox.Text) ? $"Empty, so screenshots go to {FileNamer.DefaultFolder}." : "";
        RecordingFolderHint.Text = string.IsNullOrWhiteSpace(RecordingFolderBox.Text) ? $"Empty, so recordings go to {screenshots} too." : "";
    }

    private void ShowPatternExample() =>
        PatternExample.Text = "Example: " + Path.GetFileName(FileNamer.NextPath("", PatternBox.Text, DateTime.Now, _ => false));

    private void ShowPopupText() => PopupText.Text = PopupSlider.Value switch
    {
        0 => "Off (notification instead)",
        1 => "1 second",
        var seconds => $"{seconds:0} seconds",
    };

    private void ShowPrintScreenStatus()
    {
        var on = SnippingKeySetting.IsEnabled() == true;
        PrintScreenStatus.Text = on
            ? "Windows is set to open Snipping Tool with Print Screen. Turn that off so the key goes to Overmark."
            : "Windows' own Print Screen shortcut is off, so the key goes to Overmark.";
        WindowsSettingButton.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Save()
    {
        StopRecording();
        _settings.AdjustBeforeCapture = CaptureOnRelease.IsChecked != true;
        _settings.DrawFromCentre = DrawFromCentre.IsChecked == true;
        _settings.ShowCrosshair = ShowCrosshairBox.IsChecked == true;
        _settings.ShowMagnifier = ShowMagnifierBox.IsChecked == true;
        _settings.RecordFrameRate = Fps15.IsChecked == true ? 15 : Fps60.IsChecked == true ? 60 : 30;
        _settings.RecordCursor = RecordCursorBox.IsChecked == true;
        _settings.SaveFolder = SaveFolderBox.Text.Trim();
        _settings.RecordingFolder = RecordingFolderBox.Text.Trim();
        _settings.FileNamePattern = string.IsNullOrWhiteSpace(PatternBox.Text) ? FileNamer.DefaultPattern : PatternBox.Text;
        _settings.PopupSeconds = PopupSlider.Value;
        _settings.CheckForUpdates = CheckForUpdatesBox.IsChecked == true;

        try
        {
            StartWithWindows.Set(StartWithWindowsBox.IsChecked == true);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Warn($"Couldn't change start with Windows: {ex.Message}");
            MessageBox.Show(this, $"Couldn't change \"Start with Windows\":\n\n{ex.Message}", "Overmark", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        _save(_settings);
        Close();
    }

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
