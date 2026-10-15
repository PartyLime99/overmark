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
    private static readonly Brush NormalBorderBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)));

    private readonly AppSettings _settings;
    private readonly IHotkeyRecorder _hotkeys;
    private readonly Action<AppSettings> _save;
    private readonly Dictionary<HotkeyAction, Button> _hotkeyBoxes;

    // Recording stops on its own after this long, so the keyboard can never stay captured.
    private readonly DispatcherTimer _recordingTimeout = new() { Interval = TimeSpan.FromSeconds(10) };
    private HotkeyAction? _recording;

    public SettingsWindow(AppSettings settings, IHotkeyRecorder hotkeys, Action<AppSettings> save, Action openSettingsFile)
    {
        InitializeComponent();
        _settings = settings;
        _hotkeys = hotkeys;
        _save = save;
        _hotkeyBoxes = new()
        {
            [HotkeyAction.RegionCapture] = RegionCaptureBox,
            [HotkeyAction.LastRegion] = LastRegionBox,
            [HotkeyAction.FullScreen] = FullScreenBox,
        };

        foreach (var (action, box) in _hotkeyBoxes)
        {
            box.Click += (_, _) => StartRecording(action);
        }

        RegionCaptureClear.Click += (_, _) => ClearHotkey(HotkeyAction.RegionCapture);
        LastRegionClear.Click += (_, _) => ClearHotkey(HotkeyAction.LastRegion);
        FullScreenClear.Click += (_, _) => ClearHotkey(HotkeyAction.FullScreen);
        _recordingTimeout.Tick += (_, _) => StopRecording();
        Deactivated += (_, _) => StopRecording();
        Closed += (_, _) => StopRecording();

        CaptureOnRelease.IsChecked = !settings.AdjustBeforeCapture;
        ShowCrosshairBox.IsChecked = settings.ShowCrosshair;
        ShowMagnifierBox.IsChecked = settings.ShowMagnifier;
        SaveFolderBox.Text = settings.SaveFolder ?? "";
        SaveFolderBox.TextChanged += (_, _) => ShowSaveFolderHint();
        BrowseButton.Click += (_, _) => BrowseForFolder();
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

    private void StartRecording(HotkeyAction action)
    {
        StopRecording();
        _recording = action;
        _hotkeyBoxes[action].Content = "Press the keys…";
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
            _settings.Hotkeys.Set(action, hotkey.ToString());
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
            var text = _settings.Hotkeys.Get(action);
            box.Content = Hotkey.TryParse(text, out var hotkey) ? hotkey.DisplayText : "None";
            box.BorderBrush = NormalBorderBrush;
        }

        HotkeyBindings.FromSettings(_settings.Hotkeys, out var problems);
        HotkeyProblems.Text = string.Join(Environment.NewLine, problems);
        HotkeyProblems.Visibility = problems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BrowseForFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Where should captures be saved?",
            InitialDirectory = string.IsNullOrWhiteSpace(SaveFolderBox.Text) ? FileNamer.DefaultFolder : SaveFolderBox.Text,
        };
        if (dialog.ShowDialog(this) == true)
        {
            SaveFolderBox.Text = dialog.FolderName;
        }
    }

    private void ShowSaveFolderHint() => SaveFolderHint.Text = string.IsNullOrWhiteSpace(SaveFolderBox.Text)
        ? $"Empty, so captures go to {FileNamer.DefaultFolder}."
        : "";

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
        _settings.ShowCrosshair = ShowCrosshairBox.IsChecked == true;
        _settings.ShowMagnifier = ShowMagnifierBox.IsChecked == true;
        _settings.SaveFolder = SaveFolderBox.Text.Trim();
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
