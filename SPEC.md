# Overmark: design

**Overmark** is a Windows screenshot tool that combines ShareX-style capture (crosshairs, magnifier, capture last region) with a Snipping Tool–style editor where you pick the colour and thickness *before* drawing. It also records the screen to MP4, without sound. It's free and open source under the MIT licence.

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
| Ctrl+Shift+PrtScn | Record the screen: opens the overlay in record mode. While recording, it stops the recording instead. |

On first run, if Windows' *"Use the Print screen key to open screen capture"* setting is on, Overmark explains why it should be off and offers to open that setting.

## Capture overlay

- Freezes every monitor as soon as the key is pressed. Typically visible about 125 ms after the key press (the first capture after starting is slower).
- Thin white crosshair lines (with a faint dark edge, so they show on any background) through the pointer, the full width and height of the screen (can be turned off).
- **Magnifier:** a white card beside the pointer: a round lens of zoomed pixels on a grid, with the centre pixel outlined, and under it the pointer's position (e.g. `3444, 800`) and the colour of the pixel under it, as hex with a swatch (e.g. `E3E6EA`). It flips sides near screen edges (can be turned off).
- Drag to select a region, with a live size readout in a dark pill above it (`W × H at X, Y`). The selection has a white outline and thick white corner marks, all just outside it so they never cover captured pixels; the rest of the screen is dimmed.
- **Window snap:** the window under the pointer is highlighted, and a click (without dragging) selects exactly that window.
- **Capture on release** (default): letting go of the mouse, or clicking a window, captures straight away.
- **Fine adjustment:** the arrow keys move the pointer 1 px (Shift: 10 px), before and during a drag. With *capture on release* turned off, releasing the mouse shows handles instead: drag the edges or corners, nudge with the arrow keys (Ctrl+arrows resize), then Enter or double-click captures.
- Esc cancels. Right-click drops the current selection, or cancels if there isn't one.
- **Toolbar** at the bottom centre of the monitor the pointer is on (just above the taskbar), until a selection starts. It's a white bar of buttons, each an icon over a short label, with its key in the corner:
  - **Photo** / **Video** (R): whether whatever is picked next (region, window, last region or full screen) is captured as a screenshot or recorded as a video. The chosen one is filled in: dark for Photo, red for Video. R switches between them.
  - **Last** (L): captures the previous region or window capture again. Hovering it previews that area with a dashed outline. Greyed out until there is one.
  - **Full** (F): the whole monitor.
  - **Settings**: cancels the capture and opens Settings.
  - **Esc**: cancels.

## After a capture

- The image is copied to the clipboard and saved as a PNG in the save folder.
- A **preview** pops up bottom-right of the monitor the pointer is on: a white card with the picture (with a close button in its corner), a heading (*Copied and saved*), the size and file name, and a list of actions. It stays about 5 seconds (adjustable; 0 shows a plain Windows notification instead), stays while the mouse is over it, and never takes focus from the app being used.
  - Click the picture, or **Edit**, to open the editor (brought to the front).
  - Drag it into Outlook, Teams, Word, a folder, etc.
  - **Pin to the screen**.
  - **Delete** it, for a capture taken by mistake: the file goes to the Recycle Bin (so it can be put back), and the clipboard is emptied if it still holds the capture. The row is red, and turns pink under the mouse.

## Editor

**Layout:** light, with the picture on a dotted grey background.
- **Title bar** (Overmark's own, white): icon, file name, an *Unsaved* pill while there are unsaved changes, and brief messages such as *Saved*; then Delete · Pin · Copy · **Save** and the minimise, maximise and close buttons. Windows still handles dragging, double-clicking to maximise, snapping to screen edges, resizing, the shadow and (on Windows 11) the rounded corners.
- **Hint pill** above the picture: the selected tool's name and a one-line reminder of how it works, plus that tool's on/off options (Arrowhead for curves, Box behind text for text).
- **Zoom pill** at the top right: Fit, and the zoom level, which goes to actual size when clicked.
- **Dock** along the bottom, with everything for drawing: undo · redo · the tools · the **loaded pen** · colours · thickness slider. It wraps onto two rows when the window is too narrow. The selected tool is dark, with a dot in the pen's colour.
  - Line, Arrow and Curve share one dock button, and Rectangle and Circle another. It shows the tool used last from its group (the arrow, to start with), and a small corner mark says there are more. Clicking it picks that tool; clicking it again while one of the group is picked opens a flyout above it listing all of them (icon, name and key). The keys pick any of them directly.
  - The **loaded pen** shows what the next mark will look like: a sample stroke in the current colour and thickness (wide and see-through for the highlighter, "Aa" at the text size for text), the colour's name, and the thickness.

**Colour and thickness**
- 8 preset colours plus a custom colour picker. Keys 1–8 pick the presets, 9 the custom colour.
- Thickness from 1 to 30 px on a slider (clicking anywhere on it jumps there), with the loaded pen showing the result. [ and ] change it by 1 (Shift: by 5). The highlighter draws four times as wide.
- Picked **before** drawing and applied to the next mark only; marks already drawn never change. Text that's still being typed isn't drawn yet, so it does take colour, size and box changes.
- For the text tool, the thickness sets the text size: four times the thickness (minimum 10 px), so the default thickness of 5 gives 20 px text. The loaded pen shows the text size while the text tool is selected.
- The last-used tool, colour and thickness are remembered.

**Tools**

| Tool | Key | Behaviour |
|---|---|---|
| Move | V | Click a mark to select it (a dashed outline), drag to move it; the arrow keys nudge it 1 px (Shift: 10), Delete removes it, Esc lets go. Rectangles and circles are grabbed by their outline. For a dimension: drag its text to move just the text, the round handle above the text to turn it, its dimension line to move it in or out (the measured points stay put), or an extension line to move the whole dimension. Only this tool moves marks: with any other tool, pressing on a mark draws as usual |
| Freehand | P | Drag to draw; lightly smoothed. A click makes a dot |
| Line | L | Drag from start to end. Shift snaps to 0°, 45° or 90° |
| Arrow | A | Like Line, with an arrowhead that scales with thickness |
| Curve | C | Drag a straight line, then drag the handle at its middle to bend it; Enter or clicking elsewhere finishes it. A toggle adds an arrowhead |
| Rectangle | R | Outline only. Shift makes a square |
| Circle | O | An ellipse, outline only. Shift makes a perfect circle |
| Highlighter | H | Wide translucent stroke; text underneath stays readable. Shift makes it straight |
| Text | T | Click and type; the first line is centred on the click. Enter finishes, Shift+Enter starts a new line, Esc drops it, clicking elsewhere finishes it and starts the next. Segoe UI Semibold. A toggle puts it on a box (white, or near-black behind light colours) outlined in the text colour |
| Eraser | E | Click or drag across marks; each mark touched is removed whole (empty rectangles only by their outline) |
| Dimension | D | Like SolidWorks' smart dimension; see below. Shift snaps the second point to 0°, 45° or 90° from the first |
| Crop | X | Drag a box, then Enter |

- **Moving, not restyling:** marks can be moved (the Move tool) but not recoloured, resized or edited after drawing; the eraser and undo fix mistakes.
- **Undo/redo:** Ctrl+Z / Ctrl+Y, covering drawing, erasing (one step per eraser drag), moving (one step per drag or nudge), deleting and cropping.

**Dimensions**

- Click the two points to measure between (or drag from one to the other), then move the pointer to place the dimension line and click. Where the pointer is picks what it measures: above or below the points, the horizontal distance; to the left or right, the vertical distance; anywhere else, the true (aligned) distance. A label beside the pointer shows which, and the length in pixels.
- Holding Shift snaps the second point to 0°, 45° or 90° from the first, as for a line, whether it's clicked or dragged to; the guide line from the first point shows where it will go. Shift doesn't change the third click: the dimension line's direction is already set by what it measures.
- The third click opens a text box on the dimension, holding the measured length (e.g. `214 px`) selected, so typing replaces it. Enter (or clicking elsewhere) finishes it; if the text is deleted the dimension is kept without text. Esc drops the whole dimension at any stage.
- Drawn in the current colour: extension lines from just off each point to a little past the dimension line, and a dimension line with slim arrowheads. The lines are half the thickness slider's value (at least 1 px) and the text is four times it, like the text tool.
- The text sits in a gap in the middle of the dimension line. If it's too long to fit between the arrowheads, it moves just outside the line instead. When the extension lines are too close together for the arrows, the arrows go outside, pointing in.
- With the Move tool, the text can be dragged anywhere. Near the dimension line, or the line carried on past the arrows, it snaps onto it and the line breaks round it (and reaches out to it if it's beyond the arrows). Anywhere else it stays where it's dropped, with a thin leader line back to the nearest point of the dimension line.
- The text can be turned with its handle. It snaps to level, upright, along the dimension line and across it (within 8°); Shift turns it freely. It never ends up upside down.
- **Zoom:** fits the window to start with; Ctrl+scroll zooms around the pointer; Ctrl+0 fits, Ctrl+1 is actual size.
- **Saving:** only Save or Ctrl+S writes to disk, overwriting the capture file with the annotated version. Closing with unsaved changes asks first. The window title starts with `*` while there are unsaved changes.
- **Copy** (Ctrl+C) and **Pin** use the current image, crop and marks included. Pinning minimises the editor. (A Drag button was tried and removed; dragging into other apps is done from the preview popup.)
- **Delete** (the bin button in the title bar, red under the mouse) asks first (*No* is the default), then moves the capture file to the Recycle Bin and closes the editor without asking about unsaved changes. It has no keyboard shortcut, as Delete removes the selected mark with the Move tool.
- **Flatten:** marks are merged into the image when it's copied, saved or pinned. There are no project files.
- **Opening:** click the preview, use *Edit last capture* in the tray menu, or run `Overmark.exe --edit <file>` (a lone file path also works, as Windows' "Open with" passes it).

## Pin to screen

- **One pin at a time:** pinning something new replaces the old pin.
- **Title strip** along the top, white with rounded top corners: a move grip, "Pinned", a see-through slider and a close ✕ (red under the mouse). A thin dark outline round the pin marks its edges on any background.
- **Move** by dragging the strip or the image. Overmark moves it itself rather than using Windows' title-bar dragging, so a double-click or dragging to a screen edge can't maximise it.
- **See-through:** the slider (or Shift+scroll) fades the image down to 20%, so what's underneath shows through; the strip stays solid.
- **Zoom:** Ctrl+scroll, 25% to 400%; 100% is one image pixel per screen pixel.
- **Close:** the ✕, Esc, or right-click → Close. Right-click also offers Copy, Save as… and Edit.
- **Placement:** from the preview, exactly over the spot it was captured from; from the editor, centred on the screen. Always kept on screen so the strip stays reachable.

## Screen recording

- **No sound.** Video only, as H.264 in an MP4 file, made by Windows' own encoder (Media Foundation, through `MediaTranscoder`), which uses the graphics card when it can. Nothing extra to install, except on Windows "N" editions, which need the Media Feature Pack; Overmark says so if the encoder is missing.
- **Picking the area** works exactly as for a screenshot, in record mode (see the overlay toolbar). The area is trimmed to even dimensions, as H.264 needs. Areas bigger than 3840 × 2160 are scaled down to fit.
- **Frames** are copied from the screen with GDI at a steady 15, 30 or 60 a second (30 by default), on their own thread. If the encoder falls behind, frames are skipped rather than letting the video drift. The mouse pointer is drawn in (can be turned off).
- **While recording:**
  - A 2 px red frame just outside the area. It ignores the mouse, so clicks go through to whatever is underneath.
  - A small white **bar** just below the area (or above it, or inside its bottom edge when there's no room): a red dot (hollow while paused), the elapsed time, **Pause**/**Resume**, a dark **Stop** button and a red **Discard**. It never takes focus.
  - Both are excluded from all screen captures (`WDA_EXCLUDEFROMCAPTURE`), so they never appear in the video, or in screenshots taken meanwhile.
  - The tray menu's *Record screen* item reads *Stop recording*.
- **Pause** leaves no gap: the video continues from where it paused. The time shown excludes pauses.
- **Stop** (the button, the record hotkey, the tray item, or quitting Overmark) finishes the file. Then:
  - It's saved in the save folder, named like screenshots but `.mp4`.
  - It's copied to the clipboard as a file, so Ctrl+V pastes it into Teams, Outlook or Explorer.
  - The preview pops up with the first frame, length, size and file name. Clicking the picture (or **Play**) plays the video in the default player; dragging it drops the file; **Delete** sends it to the Recycle Bin. There's no pin action.
- **Discard** stops and deletes the file.
- One recording at a time. Screenshots still work while recording.

## Settings

The settings window (tray menu → *Settings…*, or **Settings** on the capture toolbar) edits a copy; nothing changes until Save. It's light like the rest of Overmark: each group of settings is a white card on a pale grey background, and Save (dark) and Cancel sit in a white bar along the bottom.

- **Hotkeys:** click a box and press the keys. While recording, Overmark's keyboard hook hands key presses to the window, so PrtScn can be recorded even though Overmark owns it. Recording stops as soon as the box loses focus, or after 10 seconds.
- **Capture on release**, or adjust the selection first.
- **Crosshair lines** and **magnifier** on or off.
- **Save folder** (default `Pictures\Screenshots`) and **file name pattern** (a .NET date/time format, default `yyyy-MM-dd_HH-mm-ss`), with a live example.
- **Recording:** frames a second (15, 30 or 60) and whether to show the mouse pointer.
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
  - Code signing, so the installer shows a publisher instead of an "unknown publisher" warning. First choice: free open-source signing from SignPath Foundation (needs automated builds, which the GitHub Actions release workflow provides, and a published code-signing policy; the certificate names SignPath Foundation as publisher). Azure Artifact Signing only accepts individuals in the US and Canada; a traditional OV certificate costs a few hundred a year. Since 2024 no certificate, EV included, skips SmartScreen outright: reputation builds with downloads. Updates installed by Overmark itself don't trigger SmartScreen, only the first install from a browser.
  - Antivirus false positives are possible, because watching the keyboard for PrtScn is also what keyloggers do. Signing, open source and submitting releases to Microsoft for analysis all help.
  - A winget listing (`winget install Overmark`), and possibly the Microsoft Store later (which signs and updates apps itself, but needs an MSIX package).
  - A name check (domains and trademarks) for "Overmark".

## Not included

Sound in recordings, blur/pixelate, numbered steps, OCR, uploads, capture history, scrolling capture, and editing marks after drawing other than moving them.

## Build phases

1. **Foundation:** tray app, single instance, settings file, per-monitor DPI, PrtScn takeover. *Done.*
2. **Capture overlay:** freeze, crosshairs, magnifier, window snap, fine adjustment, last region, full screen, clipboard and auto-save, toolbar. *Done.* HDR still to do.
3. **Editor drawing:** pick-first colour and thickness, the drawing tools, Shift-snap, undo/redo, zoom, copy and save. *Done.*
4. **Eraser and output:** eraser, crop, drag-out, the preview popup, pins. *Done.*
5. **Settings and installer:** settings window, start with Windows, Velopack installer and auto-update, first GitHub release. *Done except the first release.*
6. **Shakedown:** daily use by early users, fixing whatever gets in the way. Added along the way: the circle tool, crosshair and magnifier switches, screen recording without sound, the text tool, deleting a capture from the preview, dimensions, the Move tool, and a lighter redesign of the editor (own title bar, one dock for drawing, the loaded pen), the capture toolbar, the magnifier, the preview, the recording bar, Settings and pins.
7. **Wider release:** signing, GitHub Actions builds, README screenshots, winget.
