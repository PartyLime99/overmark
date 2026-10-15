namespace Overmark.Hotkeys;

/// <summary>Captures the next key press, e.g. so the settings window can record a new hotkey.</summary>
internal interface IHotkeyRecorder
{
    /// <summary>Sends key presses to <paramref name="recorder"/> instead of acting on them, until <see cref="StopRecording"/>.</summary>
    void StartRecording(Action<Hotkey> recorder);

    void StopRecording();
}
