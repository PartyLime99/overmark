# Overmark

A Windows screenshot tool: ShareX-style capture (crosshairs, magnifier, capture last region) with a simple editor where you pick the colour and thickness *before* you draw. It also records the screen to MP4 (no sound).

Free and open source under the [MIT licence](LICENSE). The full plan is in [SPEC.md](SPEC.md).

## Status

**Early use.** Capturing, the editor, pins, screen recording, settings and the installer with automatic updates all work. Next: signing and a wider release.

## Capturing

Press **PrtScn** and the screen freezes, with crosshairs and a magnifier. Then:

- **Drag** to select a region, or **click** a window. Letting go captures it.
- Before or during a drag, the **arrow keys** move the pointer one pixel at a time (Shift: 10).
- **Esc** or right-click cancels.
- The **toolbar** at the bottom has **Photo** / **Video** (R switches), **Corner** / **Centre** (C switches; Centre drags out from the middle), **Last** region (L), **Full** screen (F), **Settings** and **Esc** to cancel.

Each capture is copied to the clipboard and saved as a PNG in `Pictures\Screenshots`. A preview pops up in the corner: **click** the picture (or **Edit**) to edit, **drag** it into Outlook, Teams or a folder, **pin** it so it floats on top of everything, **remove the white background and copy** it (for pasting a CAD model onto a coloured slide, say; the saved file stays as it is), or **delete** it if it was a mistake (it goes to the Recycle Bin, and off the clipboard).

| Key | Action |
|---|---|
| PrtScn | Region capture |
| Shift+PrtScn | Capture the last region again |
| Ctrl+PrtScn | Full screen (the monitor under the pointer) |
| Ctrl+Shift+PrtScn | Record the screen, or stop recording |

## Recording

Press **Ctrl+Shift+PrtScn** (or **Video** on the capture toolbar), then pick the area just as for a screenshot: drag a region, click a window, or press L or F. Recording starts straight away.

While recording, a thin red frame marks the area and a small bar beside it shows the time, with **Pause**, **Stop** and **Discard**. Neither appears in the video. Press Ctrl+Shift+PrtScn again (or **Stop recording** in the tray menu) to stop.

The video is saved as an MP4 (no sound) next to your screenshots and copied to the clipboard as a file, so Ctrl+V pastes it into Teams, Outlook or a folder. A preview pops up: **click** it to play, **drag** it somewhere, or **delete** it. The frame rate (15, 30 or 60 a second) and whether the mouse pointer shows are in Settings.

## Editor

Click the preview after a capture to draw on it. Double-clicking Overmark's tray icon, or starting Overmark from the Start menu, brings up the editor; if you haven't captured anything yet, it says how to. **Edit last capture** in the tray menu opens the last capture, and `Overmark.exe --edit <file>` opens any image.

To mark up an image from somewhere else, press **Ctrl+O** (or the **+** at the top of Today), **paste** it with Ctrl+V (a picture copied from a browser or another app, or an image file copied in File Explorer), or **drop** it on the editor. A pasted picture is saved as a new PNG in your screenshots folder. An image file opens as it is, and Save writes back over it in its own format (a JPEG stays a JPEG), except for kinds the editor can't write back, such as GIF or WebP, which open as a PNG copy in your screenshots folder. Photos are turned the right way up.

Down the left side is **Today**: everything you've captured (or opened, or pasted) today, newest first. Click a screenshot to switch to it, or a recording to play it. Unsaved marks aren't lost when you switch: they're kept as a draft (tagged *Unsaved* in the list) and are there when you come back. The **x** on a capture takes it off the list (the file stays put), asking first if it has unsaved changes; closing the one you're editing moves on to the next. **Open folder** shows the capture in File Explorer, and the button at the left of the title bar hides or shows the list.

Marks stay **editable for a day**: reopen a capture within 24 hours of editing it and its marks can still be moved, reshaped, recoloured, made thicker or thinner, erased and have their text changed, even after saving. (The PNG always has them flattened in, for sharing.)

Everything for drawing is in the dock along the bottom: undo and redo, the tools, the **loaded pen** (a sample of what the next mark will look like), the colours and the thickness slider, with a small dot and a large one either side of it that make it 1 px thinner or thicker. Pick the colour and thickness **first**; they apply to the next mark only (to change a mark you've already drawn, select it with the move tool, below). Line, Arrow and Curve share one dock button, as do Rectangle and Circle, and Solid box and Pixelate: it shows the one you used last, and clicking it again opens a small menu with the others (their keys work as always).

| Key | Tool or action |
|---|---|
| P / L / A / C / R / O / H / T | Freehand, Line, Arrow, Curve, Rectangle, Circle, Highlighter, Text |
| M / Shift+M | Redact with a solid box / pixelated blocks |
| D | Dimension |
| V | Move: select a mark and drag it, or drag its handles to reshape it; arrow keys nudge it, Delete removes it; double-click text to change it |
| E / X | Eraser (removes whole marks) / Crop (drag a box, then Enter) |
| B | Remove the white background (makes it see-through) |
| Shift (while drawing) | Straight angles for lines, arrows, curves and a dimension's two points; squares and circles; straight highlighter |
| 1–8, 9 | The palette colours; your custom colour |
| [ / ] | Thinner / thicker (Shift: by 5), like the dots either side of the slider |
| Enter / Esc | Finish a curve, crop, text or dimension / drop it and go back to the move tool |
| Ctrl+Z / Ctrl+Y | Undo / redo |
| Ctrl+C / Ctrl+S | Copy / save over the capture file |
| Ctrl+O / Ctrl+V | Open an image / paste one |
| Ctrl+0 / Ctrl+1 / Ctrl+scroll | Fit / actual size / zoom |

To draw a **curve**, drag a straight line, then drag the round handle in its middle to bend it. The **Arrowhead** switch in the pill above the picture adds an arrowhead.

To add **text**, pick the text tool (T), click where it should go and type. Enter finishes it, Shift+Enter starts a new line, and clicking somewhere else finishes it and starts another there. The thickness slider sets the size (the default gives 20 px text), and the **Box behind text** switch in the pill above the picture puts the text on a box so it stands out on a busy background. The colour, size and box can still be changed while you're typing.

To **hide** names, email addresses, figures and the like, pick a redact tool and drag a box over them. The redact button in the dock holds two, like Rectangle and Circle: **Solid box** (M) covers them in the pen's colour (press 7 for black first), and **Pixelate** (Shift+M) blurs the picture under the box into blocks (the thickness sets the block size: 15 px at the default). The button shows the one you used last; click it again to pick the other. Saving, copying and pinning flatten the box in, so what was underneath is gone from what you share.

To add a **dimension** (like SolidWorks' smart dimension), pick the dimension tool (D) and click the two points to measure between (hold Shift to keep them level, upright or at 45°). Then move the pointer: above or below the points gives a horizontal dimension, to the left or right a vertical one, and anywhere else an aligned one (the true distance). Click to place it, type the text (it starts as the length in pixels, selected so typing replaces it) and press Enter. Text too long for the gap moves just outside the line by itself.

To measure in **real units**, type a length with a unit into one dimension, such as `50 mm` or `2.5 in` (mm, cm, m, µm, in, ft, " and ' all work). That sets the picture's scale: the other dimensions that still showed their length in pixels show it in that unit instead, to as many decimal places as you typed, and new ones start that way. The pill above the picture shows the scale (e.g. *1 mm = 4.28 px*) while the dimension tool is picked. Typing a length into another dimension changes the scale, and deleting that dimension (or changing its text to something that isn't a length) goes back to pixels. Text you've typed yourself is never changed.

To **move** marks, pick the move tool (V). Click a mark to select it and drag it; the arrow keys nudge it and Delete removes it. A selected mark shows small square **handles** where it can be reshaped: drag the ends of a line or arrow, the ends or middle of a curve (to bend it again), the corners and sides of a rectangle, circle or redact box (Shift on a corner keeps it square), or a dimension's two measured points (a dimension still showing its measured length measures again). While it's selected, picking a colour or thickness changes it (the dots, [ and ] step from its own thickness). **Double-click** text, or a dimension, to change what it says: type, then press Enter (Esc leaves it as it was). For a dimension, drag its text to put it anywhere (near the line it snaps on; elsewhere it gets a leader line), drag the round handle above the text to turn it (Shift turns it freely), drag the dimension line to move it in or out, or drag an extension line to move the whole thing. Other tools never move marks, so you can draw on top of them.

To lift a picture off a white background (a CAD model, say), click the **checkerboard** button in the dock (B). Pure white that touches the edges of the picture becomes see-through, shown as a checkerboard; white inside the picture, like a label, stays. The edges next to it are softened, so they don't leave a white fringe on a dark background. Saving keeps the transparency (PNG), and copying puts it on the clipboard for apps that take PNG, such as Word and PowerPoint. Ctrl+Z brings the white back.

The **Pin** button in the title bar floats the image on top of everything. To drop a capture into an email, chat or folder, drag the preview that pops up after capturing. The **bin** button deletes the capture: after you confirm, the file goes to the Recycle Bin and the editor moves on to the next capture in Today (or shows none, if that was the last).

## Pins

A pin is a capture floating on top of everything; there's only ever one, so pinning something new replaces it. Drag its title strip (or the image) to move it, use the strip's slider or Shift+scroll to make it see-through, Ctrl+scroll to zoom, and the ✕ or Esc to close it. Right-click for Copy, Save as, Edit and Close. Pinning from the editor minimises the editor.

## Settings

Open **Settings…** from the tray menu (or Settings on the capture toolbar):

- **Hotkeys:** click a box and press the keys you want. **Add** gives the same action a second key as well, which helps on laptops whose PrtScn key sends Win+Shift+S unless Fn is held: add both and either works.
- **Capture as soon as I let go of the mouse** (on by default); turn it off to adjust the selection with handles and press Enter.
- **Drag a selection from its Corner or Centre:** with Centre, press on the middle of what you want and drag out to a corner, like a centre rectangle in CAD.
- **Crosshair lines** and the **magnifier**, each of which can be turned off.
- **Save folder** and **file name pattern**, with a live example.
- **Recording:** frames a second, and whether the mouse pointer shows.
- **How long the preview stays** in the corner (0 shows a plain notification instead).
- **Start with Windows** and **check for updates automatically**.

Everything is stored in `%AppData%\Overmark\settings.json`, which can also be edited by hand (changes apply when it's saved). The log is in `%AppData%\Overmark\logs`.

## Installing

Run `Overmark-win-Setup.exe` from the latest [release](https://github.com/PartyLime99/overmark/releases). It installs for the current user only (no admin rights needed), adds Overmark to the Start menu, and updates itself from GitHub releases. Uninstall it from Windows Settings → Apps.

## Building

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) on Windows 10 or 11.

```
dotnet build
dotnet test
dotnet run --project src/Overmark
```

The app icon is generated by `tools/make-icon.ps1`.

**Releases** are built on GitHub: in the Actions tab, run the **Release** workflow and enter the new version number. It runs the tests, makes a self-contained build, packs it with [Velopack](https://velopack.io) and publishes it as a GitHub release, which installed copies then update to.

To build the installer locally instead, run `tools/release.ps1 -Version 1.2.3` (it writes to `releases\`). Setting `OVERMARK_SNAPSHOTS` to a folder before `dotnet test` renders the popup, recording bar, editor and capture overlay there as PNGs, without showing them on screen.
