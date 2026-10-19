using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Overmark.Capture;
using Overmark.Editor;
using Overmark.Hotkeys;
using Overmark.Pinning;
using Overmark.Platform;
using Overmark.Recording;
using Overmark.Settings;
using Overmark.Tray;

namespace Overmark;

/// <summary>Owns everything that lives for the whole run: settings, global hotkeys and the tray icon.</summary>
internal sealed class OvermarkHost : IDisposable
{
    private readonly SettingsStore _store;
    private readonly HotkeyService _hotkeys;
    private readonly TrayIcon _tray;
    private readonly CaptureController _captures;
    private readonly EditorLauncher _editors;
    private readonly PinBoard _pins = new();
    private readonly CaptureHistory _history = new(AppInfo.HistoryFile);
    private readonly RecordingController _recordings;
    private readonly Action _quit;
    private readonly FileSystemWatcher _settingsWatcher;
    private readonly DispatcherTimer _reloadTimer;
    private AppSettings _settings;
    private IReadOnlyDictionary<HotkeyAction, IReadOnlyList<Hotkey>> _bindings = new Dictionary<HotkeyAction, IReadOnlyList<Hotkey>>();
    private IReadOnlySet<Hotkey> _registered = new HashSet<Hotkey>();
    private SettingsWindow? _settingsWindow;

    /// <param name="quiet">True when started at sign-in: no "Overmark is running" notification.</param>
    public OvermarkHost(SettingsStore store, Dispatcher dispatcher, Action quit, bool quiet)
    {
        _store = store;
        _settings = store.Load(out var settingsProblem);
        if (settingsProblem is not null)
        {
            Log.Warn(settingsProblem);
        }

        _quit = quit;
        _tray = new TrayIcon(RunFromMenu, EditLastCapture, ShowDisplayInfo, OpenSettings, Quit);
        _editors = new EditorLauncher(() => _settings, RememberEditorSettings, _pins, _history, _tray);
        _recordings = new RecordingController(
            () => _settings, () => _bindings.TryGetValue(HotkeyAction.Record, out var stop) ? stop[0] : null, _history, _tray);
        _captures = new CaptureController(() => _settings, RememberLastRegion, RememberFromCentre, _editors.Open, OpenSettings, _pins, _recordings, _history, _tray);
        _hotkeys = new HotkeyService(dispatcher);
        _hotkeys.Triggered += Run;
        ApplyHotkeys();
        _ = new Updater(() => _settings, _tray, dispatcher);

        // Pick up hand edits to settings.json without a restart. Editors often write a file in several steps,
        // so wait for a short quiet spell before reloading.
        _reloadTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(300) };
        _reloadTimer.Tick += (_, _) => ReloadSettings();
        _settingsWatcher = new FileSystemWatcher(Path.GetDirectoryName(store.FilePath)!, Path.GetFileName(store.FilePath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        void ScheduleReload() => dispatcher.InvokeAsync(() =>
        {
            _reloadTimer.Stop();
            _reloadTimer.Start();
        });
        _settingsWatcher.Changed += (_, _) => ScheduleReload();
        _settingsWatcher.Created += (_, _) => ScheduleReload();
        _settingsWatcher.Renamed += (_, _) => ScheduleReload();
        _settingsWatcher.EnableRaisingEvents = true;

        Log.Info(BuildReport());
        if (settingsProblem is not null)
        {
            _tray.Notify("Settings were reset", settingsProblem);
        }
        else if (!quiet)
        {
            _tray.Notify("Overmark is running", HowToCapture());
        }

        dispatcher.InvokeAsync(CheckSnippingKey, DispatcherPriority.ApplicationIdle);
        dispatcher.InvokeAsync(_captures.Prepare, DispatcherPriority.ApplicationIdle);
        dispatcher.InvokeAsync(DragOut.CleanUp, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>Handles a command line passed in at startup or from a later launch.</summary>
    public void HandleCommand(IReadOnlyList<string> args)
    {
        if (CommandLine.EditPath(args) is not { } path)
        {
            _tray.Notify("Overmark is already running", $"It's in the notification area. {HowToCapture()}");
        }
        else if (File.Exists(path))
        {
            _editors.Open(path);
        }
        else
        {
            _tray.Notify("Couldn't open the file", $"{path} doesn't exist.");
        }
    }

    public void Dispose()
    {
        _settingsWatcher.Dispose();
        _reloadTimer.Stop();
        _hotkeys.Dispose();
        _captures.Dispose();
        _tray.Dispose();
    }

    private void Run(HotkeyAction action)
    {
        Log.Info($"{action.DisplayName()} triggered");
        _captures.Run(action);
    }

    // Gives the tray menu time to fade out so it isn't in the capture.
    private async void RunFromMenu(HotkeyAction action)
    {
        await Task.Delay(250);
        Run(action);
    }

    private void RememberLastRegion(ScreenRect region)
    {
        _settings.LastRegion = SavedRegion.From(region);
        SaveSettings();
    }

    private void RememberFromCentre(bool fromCentre)
    {
        _settings.DrawFromCentre = fromCentre;
        SaveSettings();
    }

    private void RememberEditorSettings(EditorSettings editor)
    {
        _settings.Editor = editor;
        SaveSettings();
    }

    private void OpenSettings()
    {
        if (_settingsWindow is { } open)
        {
            ForegroundWindow.Activate(open);
            return;
        }

        var window = new SettingsWindow(SettingsStore.Copy(_settings), _hotkeys, ApplyNewSettings, OpenSettingsFile);
        window.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow = window;
        window.Show();
        ForegroundWindow.Activate(window);
    }

    /// <summary>Takes the settings window's changes, keeping things it doesn't edit as they are now.</summary>
    private void ApplyNewSettings(AppSettings updated)
    {
        updated.LastRegion = _settings.LastRegion;
        updated.Editor = _settings.Editor;
        updated.SnippingKeyPromptDismissed = _settings.SnippingKeyPromptDismissed;
        _settings = updated;
        SaveSettings();
        ApplyHotkeys();
        Log.Info($"Settings saved from the settings window: {DescribeBindings()}");
    }

    private void SaveSettings()
    {
        try
        {
            _store.Save(_settings);
        }
        catch (IOException ex)
        {
            Log.Warn($"Couldn't save settings: {ex.Message}");
        }
    }

    private void EditLastCapture()
    {
        // The history remembers the last capture across restarts.
        if ((_captures.LastSavedPath ?? _history.LatestScreenshot()?.Path) is { } path && File.Exists(path))
        {
            _editors.Open(path);
        }
        else
        {
            _tray.Notify("Nothing to edit yet", "Take a capture first, then it can be edited.");
        }
    }

    // Editors with unsaved changes ask first; if the user cancels one, Overmark keeps running.
    // A recording in progress is finished and saved before quitting.
    private async void Quit()
    {
        if (!_editors.CloseAll())
        {
            return;
        }

        _pins.Close();
        await _recordings.StopAsync(keep: true);
        _quit();
    }

    private void ApplyHotkeys()
    {
        _bindings = HotkeyBindings.FromSettings(_settings.Hotkeys, out var problems);
        _registered = _hotkeys.Apply(_bindings);
        _tray.ShowHotkeys(_bindings);

        foreach (var problem in problems)
        {
            Log.Warn(problem);
        }

        if (problems.Count > 0)
        {
            _tray.Notify("Check your hotkey settings", string.Join(Environment.NewLine, problems));
        }
    }

    private void ReloadSettings()
    {
        _reloadTimer.Stop();
        if (!File.Exists(_store.FilePath) || _store.IsOurLastWrite())
        {
            return;
        }

        AppSettings? settings;
        string? error;
        try
        {
            if (!_store.TryRead(out settings, out error))
            {
                Log.Warn($"Ignoring settings change: {error}");
                _tray.Notify("Settings not applied", $"settings.json has an error, so the previous settings are still in use.\n{error}");
                return;
            }
        }
        catch (IOException ex)
        {
            // Usually the editor still has the file open; the next change event retries.
            Log.Warn($"Couldn't read settings: {ex.Message}");
            return;
        }

        var previous = _bindings;
        _settings = settings;
        ApplyHotkeys();
        if (!SameBindings(previous, _bindings))
        {
            Log.Info($"Hotkeys changed: {DescribeBindings()}");
            _tray.Notify("Hotkeys updated", DescribeBindings());
        }
    }

    private void CheckSnippingKey()
    {
        if (_settings.SnippingKeyPromptDismissed || SnippingKeySetting.IsEnabled() != true)
        {
            return;
        }

        var answer = MessageBox.Show(
            "Windows is set to open Snipping Tool when you press Print Screen.\n\n" +
            "Switch that off so Print Screen goes to Overmark. Open the Windows setting now?\n\n" +
            "(Turn off \"Use the Print screen key to open screen capture\".)",
            "Overmark", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
        {
            SnippingKeySetting.OpenWindowsSetting();
        }

        _settings.SnippingKeyPromptDismissed = true;
        _store.Save(_settings);
    }

    private void ShowDisplayInfo() =>
        MessageBox.Show(BuildReport(), "Overmark: display and hotkey info", MessageBoxButton.OK, MessageBoxImage.Information);

    /// <summary>Opens settings.json in Notepad, or failing that its default app, or failing that shows it in a folder.</summary>
    private void OpenSettingsFile()
    {
        var path = _store.FilePath;
        var folder = Path.GetDirectoryName(path)!;
        var notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
        ProcessStartInfo[] attempts =
        [
            new(notepad, $"\"{path}\"") { UseShellExecute = false, WorkingDirectory = folder },
            new(path) { UseShellExecute = true, WorkingDirectory = folder },
            new("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true },
        ];

        foreach (var attempt in attempts)
        {
            try
            {
                Process.Start(attempt);
                return;
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                Log.Warn($"Couldn't open settings with {attempt.FileName}: {ex.Message}");
            }
        }

        _tray.Notify("Couldn't open the settings file", path);
    }

    private string HowToCapture() =>
        _bindings.TryGetValue(HotkeyAction.RegionCapture, out var hotkeys)
            ? $"Press {HotkeyBindings.Describe(hotkeys)} to capture."
            : "Right-click the tray icon to capture.";

    private string DescribeBindings() =>
        string.Join(", ", _bindings.Select(b => $"{HotkeyBindings.Describe(b.Value)}: {b.Key.DisplayName()}"));

    private static bool SameBindings(
        IReadOnlyDictionary<HotkeyAction, IReadOnlyList<Hotkey>> a, IReadOnlyDictionary<HotkeyAction, IReadOnlyList<Hotkey>> b) =>
        a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var other) && other.SequenceEqual(pair.Value));

    private string BuildReport()
    {
        var report = new StringBuilder();
        report.AppendLine($"Overmark {AppInfo.Version}");
        report.AppendLine($"DPI awareness: {Displays.DescribeDpiAwareness()}");
        report.AppendLine();

        report.AppendLine("Monitors");
        var monitors = Displays.GetMonitors();
        for (var i = 0; i < monitors.Count; i++)
        {
            var monitor = monitors[i];
            var main = monitor.IsPrimary ? " (main)" : "";
            report.AppendLine($"  {i + 1}{main}: {monitor.Bounds}, scaling {monitor.ScalePercent}%");
        }

        report.AppendLine($"  Whole desktop: {Displays.VirtualScreen(monitors)}");
        report.AppendLine();

        report.AppendLine($"Hotkeys (keyboard hook {(_hotkeys.HookActive ? "active" : "NOT active")})");
        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            if (!_bindings.TryGetValue(action, out var hotkeys))
            {
                report.AppendLine($"  {action.DisplayName()}: off");
                continue;
            }

            foreach (var hotkey in hotkeys)
            {
                var note = _registered.Contains(hotkey)
                    ? ""
                    : " (another app or Windows also uses this key, so it won't work while an admin window is focused)";
                report.AppendLine($"  {hotkey.DisplayText}: {action.DisplayName()}{note}");
            }
        }

        report.AppendLine();
        report.AppendLine($"Settings: {_store.FilePath}");
        report.Append($"Log: {Log.FilePath}");
        return report.ToString();
    }
}
