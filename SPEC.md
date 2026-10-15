# Overmark: design

**Overmark** is a Windows screenshot tool that combines ShareX-style capture (crosshairs, magnifier, capture last region) with a Snipping Tool–style editor where you pick the colour and thickness *before* drawing. It's free and open source under the MIT licence. Screen recording is deliberately left out.

This document describes how Overmark behaves and why. The [README](README.md) is the short user guide.

---

## Platform and install

- Windows 10/11. C# / WPF on .NET 10 (long-term support).
- Installed with `Overmark-win-Setup.exe`, built with [Velopack](https://velopack.io) by `tools/release.ps1`. It's about 82 MB because it carries its own copy of .NET, so nothing else needs installing. A portable zip is made too.
  - Per-user install: no admin rights needed.
  - Start menu shortcut and a normal uninstaller.
  - Automatic updates from GitHub Releases: checked one minute after starting and every 6 hours, downloaded quietly, and installed the next time Overmark starts. They can be turned off in Settings.
- Runs in the notification area, one copy at a time. The tray menu has Region capture, Last region, Full screen, Edit last capture, Display info, Settings and Quit.
- No telemetry or analytics. The update check is the only network traffic.
- **Hotkey conflicts:** apps such as OneDrive, Dropbox, ShareX, Greenshot or Lightshot may already own PrtScn. Overmark watches the keyboard with a low-level hook, so it gets its hotkeys before those apps do and the key never reaches them.
  - While an admin window is focused, Windows hides keys from normal apps. As a backup, Overmark also registers each hotkey the standard way, which only works for keys no other app has claimed.
  - *Display info* (tray menu) shows which hotkeys have that backup.

## Displays

- Any number of monitors in any arrangement, including monitors left of or above the main one.
- Mixed resolutions and scaling (100–300%). Overmark is per-monitor DPI aware: captures are always at full physical resolution and pixel-exact, and the overlay uses one window per monitor so each gets its own scaling.
- Monitors can be plugged in, unplugged or rescaled while it runs.
- **Not done yet:** HDR monitors. Captures use a standard screen copy, which can look washed out on an HDR display; tone-mapping needs an HDR monitor to build and test.

## Hotkeys

Changeable in Settings.

| Key | Action |
|---|---|
| PrtScn | Region capture (opens the overlay) |
| Shift+PrtScn | Capture the last region again straight away. Remembered across restarts; if there's no previous region, it opens the overlay. |
| Ctrl+PrtScn | Full screen: the monitor under the pointer |

On first run, if Windows' *"Use the Print screen key to open screen capture"* setting is on, Overmark explains why it should be off and offers to open that setting.

## Capture overlay

- Freezes every monitor as soon as the key is pressed. Typically visible about 125 ms after the key press (the first capture after starting is slower).
- Dashed crosshair lines through the pointer, the full width and height of the screen.
- **Magnifier:** a circle beside the pointer showing zoomed pixels on a grid, with the centre pixel outlined and a coordinate label underneath (e.g. `X: 3444 Y: 800`). It flips sides near screen edges.
- Drag to select a region, with a live size readout (`W × H`).
- **Window snap:** the window under the pointer is highlighted, and a click (without dragging) selects exactly that window.
- **Capture on release** (default): letting go of the mouse, or clicking a window, captures straight away.
- **Fine adjustment:** the arrow keys move the pointer 1 px (Shift: 10 px), before and during a drag. With *capture on release* turned off, releasing the mouse shows handles instead: drag the edges or corners, nudge with the arrow keys (Ctrl+arrows resize), then Enter or double-click captures.
- Esc cancels. Right-click drops the current selection, or cancels if there isn't one.
- **Toolbar** at the top of the monitor the pointer is on, until a selection starts:
  - **Last region** (L): captures the previous region or window capture again. Hovering it previews that area with a dashed outline. Greyed out until there is one.
  - **Full screen** (F): the whole monitor.
  - **Settings** (gear): cancels the capture and opens Settings.
  - **Cancel** (×).

## After a capture

- The image is copied to the clipboard and saved as a PNG in the save folder.
- A **preview** pops up bottom-right of the monitor the pointer is on, showing the image, its size and file name. It stays about 5 seconds (adjustable; 0 shows a plain Windows notification instead), stays while the mouse is over it, and never takes focus from the app being used.
  - Click it to open the editor (brought to the front).
  - Drag it into Outlook, Teams, Word, a folder, etc.
  - Pin it to the screen.

## Editor

**Toolbar:** tools · colour swatches · thickness slider · undo/redo · zoom · Drag · Pin · Copy · Save

**Colour and thickness**
- 8 preset colours plus a custom colour picker. Keys 1–8 pick the presets, 9 the custom colour.
- Thickness from 1 to 30 px on a slider, with a dot showing the current colour and thickness. [ and ] change it by 1 (Shift: by 5). The highlighter draws four times as wide.
- Picked **before** drawing and applied to the next mark only; marks already drawn never change.
- The last-used tool, colour and thickness are remembered.

**Tools**

| Tool | Key | Behaviour |
|---|---|---|
| Freehand | P | Drag to draw; lightly smoothed. A click makes a dot |
| Line | L | Drag from start to end. Shift snaps to 0°, 45° or 90° |
| Arrow | A | Like Line, with an arrowhead that scales with thickness |
| Curve | C | Drag a straight line, then drag the handle at its middle to bend it; Enter or clicking elsewhere finishes it. A toggle adds an arrowhead |
| Rectangle | R | Outline only. Shift makes a square |
| Highlighter | H | Wide translucent stroke; text underneath stays readable. Shift makes it straight |
| Eraser | E | Click or drag across marks; each mark touched is removed whole (empty rectangles only by their outline) |
| Crop | X | Drag a box, then Enter |

- **Draw-only:** marks can't be selected, moved or restyled after drawing; the eraser and undo fix mistakes.
- **Undo/redo:** Ctrl+Z / Ctrl+Y, covering drawing, erasing (one step per eraser drag) and cropping.
- **Zoom:** fits the window to start with; Ctrl+scroll zooms around the pointer; Ctrl+0 fits, Ctrl+1 is actual size.
- **Saving:** only Save or Ctrl+S writes to disk, overwriting the capture file with the annotated version. Closing with unsaved changes asks first. The window title starts with `*` while there are unsaved changes.
- **Copy** (Ctrl+C), **Drag** (press and drag onto another app) and **Pin** all use the current image, crop and marks included. Dragging hands over the saved file, or a copy with the edits if there are unsaved changes. Pinning minimises the editor.
- **Flatten:** marks are merged into the image when it's copied, saved, dragged or pinned. There are no project files.
- **Opening:** click the preview, use *Edit last capture* in the tray menu, or run `Overmark.exe --edit <file>` (a lone file path also works, as Windows' "Open with" passes it).

## Pin to screen

- **One pin at a time:** pinning something new replaces the old pin.
- **Title strip** along the top: a move grip, "Pinned", a see-through slider and a close ✕.
- **Move** by dragging the strip or the image. Overmark moves it itself rather than using Windows' title-bar dragging, so a double-click or dragging to a screen edge can't maximise it.
- **See-through:** the slider (or Shift+scroll) fades the image down to 20%, so what's underneath shows through; the strip stays solid.
- **Zoom:** Ctrl+scroll, 25% to 400%; 100% is one image pixel per screen pixel.
- **Close:** the ✕, Esc, or right-click → Close. Right-click also offers Copy, Save as… and Edit.
- **Placement:** from the preview, exactly over the spot it was captured from; from the editor, centred on the screen. Always kept on screen so the strip stays reachable.

## Settings

The settings window (tray menu → *Settings…*, or the gear on the capture toolbar) edits a copy; nothing changes until Save.

- **Hotkeys:** click a box and press the keys. While recording, Overmark's keyboard hook hands key presses to the window, so PrtScn can be recorded even though Overmark owns it. Recording stops as soon as the box loses focus, or after 10 seconds.
- **Capture on release**, or adjust the selection first.
- **Save folder** (default `Pictures\Screenshots`) and **file name pattern** (a .NET date/time format, default `yyyy-MM-dd_HH-mm-ss`), with a live example.
- **Preview duration**, or 0 for a plain notification.
- **Start with Windows:** the standard per-user Run registry entry, so it also shows in Task Manager's Startup apps. Started this way, Overmark starts quietly.
- **Check for updates automatically.**
- The state of Windows' own Print Screen setting, with a button to open it.

Settings are stored in `%AppData%\Overmark\settings.json`, which can also be edited by hand (changes apply as soon as it's saved). The log is in `%AppData%\Overmark\logs`, because `%LocalAppData%\Overmark` belongs to the installer.

## Release

- **Free and open source** under the MIT licence, in a public GitHub repository. Installers and updates come from GitHub Releases.
- **Unsigned for now.** On first install, Windows shows "Windows protected your PC"; click *More info → Run anyway*.
- **Planned before a wider release:**
  - Release builds made by GitHub Actions directly from the public code, so anyone can verify what's in the installer (free signing services require this).
  - Code signing, so the installer shows a publisher instead of a warning: first choice is free open-source signing from SignPath Foundation; otherwise Azure Artifact Signing or a traditional certificate. Even signed, SmartScreen can warn until a release builds up download history.
  - Antivirus false positives are possible, because watching the keyboard for PrtScn is also what keyloggers do. Signing, open source and submitting releases to Microsoft for analysis all help.
  - A winget listing (`winget install Overmark`), and possibly the Microsoft Store later (which signs and updates apps itself, but needs an MSIX package).
  - A name check (domains and trademarks) for "Overmark".

## Not included

Screen recording, text labels, ellipses, blur/pixelate, numbered steps, OCR, uploads, capture history, scrolling capture, and selecting or moving marks after drawing.

## Build phases

1. **Foundation:** tray app, single instance, settings file, per-monitor DPI, PrtScn takeover. *Done.*
2. **Capture overlay:** freeze, crosshairs, magnifier, window snap, fine adjustment, last region, full screen, clipboard and auto-save, toolbar. *Done.* HDR still to do.
3. **Editor drawing:** pick-first colour and thickness, the drawing tools, Shift-snap, undo/redo, zoom, copy and save. *Done.*
4. **Eraser and output:** eraser, crop, drag-out, the preview popup, pins. *Done.*
5. **Settings and installer:** settings window, start with Windows, Velopack installer and auto-update, first GitHub release. *Done except the first release.*
6. **Shakedown:** daily use by early users, fixing whatever gets in the way.
7. **Wider release:** signing, GitHub Actions builds, README screenshots, winget.
