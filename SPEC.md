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
- Runs in the notification area, one copy at a time. The tray menu has Region capture, Last region, Full screen, Open editor, Edit last capture, Display info, Settings and Quit.
  - Double-clicking the icon brings up the editor, so *Open editor* is in bold (as Windows shows the double-click item). See *Opening* under Editor.
  - Starting Overmark when it's already running (its Start menu shortcut, say) brings up the editor too, and so does starting it the first time, unless it was started at sign-in.
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

Changeable in Settings. Each action can have more than one hotkey (e.g. PrtScn and Win+Shift+S, for laptops whose PrtScn key sends a Snipping Tool shortcut unless Fn is held).

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
- Drag to select a region, from corner to corner or (a setting) out from the centre, with a live size readout in a dark pill above it (`W × H at X, Y`). The selection has a white outline and thick white corner marks, all just outside it so they never cover captured pixels; the rest of the screen is dimmed.
- **Window snap:** the window under the pointer is highlighted, and a click (without dragging) selects exactly that window.
- **Capture on release** (default): letting go of the mouse, or clicking a window, captures straight away.
- **Fine adjustment:** the arrow keys move the pointer 1 px (Shift: 10 px), before and during a drag. With *capture on release* turned off, releasing the mouse shows handles instead: drag the edges or corners, nudge with the arrow keys (Ctrl+arrows resize), then Enter or double-click captures.
- Esc cancels. Right-click drops the current selection, or cancels if there isn't one.
- **Toolbar** at the bottom centre of the monitor the pointer is on (just above the taskbar), until a selection starts. It's a white bar of buttons, each an icon over a short label, with its key in the corner:
  - **Photo** / **Video** (R): whether whatever is picked next (region, window, last region or full screen) is captured as a screenshot or recorded as a video. The chosen one is filled in: dark for Photo, red for Video. R switches between them.
  - **Corner** / **Centre** (C): whether a drag starts at a corner of the selection or at its centre (the same choice as in Settings, and the one picked here is saved). Dark when chosen. C switches between them, even part-way through a drag.
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
  - **Remove background and copy**: copies the capture again with its white background see-through, as the editor's button does (pure white touching the edges goes, its edges softened). The saved file stays as it is. The preview then shows the result, the heading says *Background removed and copied* (or that there was no white background), and the row goes.
  - **Delete** it, for a capture taken by mistake: the file goes to the Recycle Bin (so it can be put back), and the clipboard is emptied if it still holds the capture. The row is red, and turns pink under the mouse.

## Editor

**Layout:** light, with the picture on a dotted grey background.
- **Title bar** (Overmark's own, white): icon, file name, an *Unsaved* pill while there are unsaved changes, and brief messages such as *Saved*; then Delete · Pin · Copy · **Save** and the minimise, maximise and close buttons. Windows still handles dragging, double-clicking to maximise, snapping to screen edges, resizing, the shadow and (on Windows 11) the rounded corners.
- **No capture open** (the editor brought up before anything has been captured today, or after the last capture is deleted): the picture, the pills and the dock make way for a note on the dotted background, *No screenshot open*, saying how to take one (with the region capture hotkey), with an **Open an image…** button and *or paste one (Ctrl+V), or drop one here*. Delete, Pin, Copy, Save and Open folder are hidden and the editor's keys do nothing, apart from Ctrl+O and Ctrl+V; today's captures still show. The next capture opens in it.
- **Hint pill** above the picture: the selected tool's name and a one-line reminder of how it works, plus that tool's on/off options (Arrowhead for curves, Box behind text for text).
- **Zoom pill** at the top right: Fit, and the zoom level, which goes to actual size when clicked.
- **Today's captures** down the left side: every screenshot and recording Overmark has saved today, and every image opened or pasted into the editor, newest first, each a thumbnail with what kind it was (Region, Window, Last region, Full screen, Video, Opened or Pasted) and the time. A **+** in the list's heading opens an image (see *Images from elsewhere*). The one being edited has a dark ring. A video shows a play symbol and its length.
  - Clicking a screenshot shows it in the same window, in place of the current one (the window stays put, so nothing flickers; the picture is fitted to the window again and undo starts afresh). Unsaved marks aren't asked about: they're kept as a draft (see *Editable for a day* below), and the capture shows an *Unsaved* tag in the list. One that's already open in another editor window is brought to the front instead. Clicking a video plays it in the default player.
  - The list updates as new captures come in, and a capture's thumbnail updates when it's saved with marks.
  - An **x** in a capture's corner (always shown on the one being edited, on the others while the pointer is over them) takes it off the list, like closing a tab. The file stays where it is, and opening the capture again (from the preview, the tray or `--edit`) puts it back on the list.
    - Unsaved changes are asked about first, as when closing the editor: *Yes* saves them into the file, *No* throws them away, *Cancel* keeps it. For a capture that isn't open, that means its draft; for one open in another editor window, that window closes as usual.
    - Closing the capture being edited moves on to the next one down the list (or the one above, at the bottom), or closes the editor if no screenshots are left. Deleting it (the bin button) moves on in the same way, except that with none left the editor stays open, with no capture.
  - **Open folder** at the bottom shows the current capture in File Explorer.
  - The switch at the left of the title bar hides or shows the list; the choice is remembered.
  - Only what Overmark saved or opened is listed (not other files in the folder), and anything since deleted or moved is left out. The list is kept in `%AppData%\Overmark\history.json`, which holds the last two weeks. *Edit last capture* skips images opened from elsewhere.
- **Dock** along the bottom, with everything for drawing: undo · redo · the tools · the **loaded pen** · colours · thickness slider. It wraps onto two rows when the window is too narrow. The selected tool is dark, with a dot in the pen's colour.
  - Line, Arrow and Curve share one dock button, Rectangle and Circle another, and the redact tools, Solid box and Pixelate, a third (its icon is lines of text with the middle one under a solid bar, or pixelated). It shows the tool used last from its group (the arrow, the rectangle and the solid box, to start with), and a small corner mark says there are more. Clicking it picks that tool; clicking it again while one of the group is picked opens a flyout above it listing all of them (icon, name and key). The keys pick any of them directly.
  - The **loaded pen** shows what the next mark will look like: a sample stroke in the current colour and thickness (wide and see-through for the highlighter, "Aa" at the text size for text, a box in the colour or a mosaic for the redact tool), the colour's name (or *Pixelate*), and the thickness (or the text size, the block size, or *solid box*).

**Colour and thickness**
- 8 preset colours plus a custom colour picker. Keys 1–8 pick the presets, 9 the custom colour.
- Thickness from 1 to 30 px on a slider (clicking anywhere on it jumps there), with the loaded pen showing the result. [ and ] change it by 1 (Shift: by 5), as do the dots either side of the slider: the small one on the left makes it thinner, the large one on the right thicker. The highlighter draws four times as wide.
- Picked **before** drawing and applied to the next mark only; marks already drawn keep theirs, unless they're selected with the Move tool. Then picking a colour or thickness changes those marks too (as well as the pen), and the dots, [ and ] step from the marks' own thickness rather than the pen's (if they're all as thick). Text that's still being typed isn't drawn yet, so it does take colour, size and box changes.
- For the text tool, the thickness sets the text size: four times the thickness (minimum 10 px), so the default thickness of 5 gives 20 px text. The loaded pen shows the text size while the text tool is selected.
- The last-used tool, colour and thickness are remembered.

**Tools**

| Tool | Key | Behaviour |
|---|---|---|
| Move | V | Click a mark to select it (a dashed outline, with square handles where it can be reshaped; see *Changing marks*), drag to move it; the arrow keys nudge it 1 px (Shift: 10), Delete removes it, Esc lets go. Several marks can be selected at once (see *Selecting several*). Ctrl+C copies the selection and Ctrl+V pastes a copy (see *Copying marks*). Picking a colour or thickness changes the selected marks. Double-clicking text, or a dimension, changes its words (see below). Rectangles and circles are grabbed by their outline. For a dimension: drag its text to move just the text, the round handle above the text to turn it, its dimension line to move it in or out (the measured points stay put), or an extension line to move the whole dimension. Only this tool moves marks: with any other tool, pressing on a mark draws as usual |
| Freehand | P | Drag to draw; lightly smoothed. A click makes a dot |
| Line | L | Drag from start to end. Shift snaps to 0°, 45° or 90° |
| Arrow | A | Like Line, with an arrowhead that scales with thickness |
| Curve | C | Drag a straight line, then drag the handle at its middle to bend it; Enter or clicking elsewhere finishes it. A toggle adds an arrowhead |
| Rectangle | R | Outline only. Shift makes a square |
| Circle | O | An ellipse, outline only. Shift makes a perfect circle |
| Highlighter | H | Wide translucent stroke; text underneath stays readable. Shift makes it straight |
| Solid box | M | Redact: drag a box over what should be hidden (names, email addresses, figures), filled with the pen's colour. See Pixelate for the rest |
| Pixelate | Shift+M | Redact like Solid box, but the box shows the picture under it in blocks of its average colours, three times the thickness across (15 px at the default thickness, never under 6 px), counted from the box's top-left. For both redact tools: It covers whole pixels (its edges are rounded outwards, and it moves and resizes in whole pixels), so nothing shows at its edges, and it works from the picture alone: marks under it are covered, not pixelated. Shift makes a square. Anywhere inside counts for erasing and grabbing. Saving, copying and pinning flatten it in, so the hidden pixels aren't in what's shared (the editable copy in `%AppData%` keeps the original for a day, as for any capture) |
| Text | T | Click and type; the first line is centred on the click. Enter finishes, Shift+Enter starts a new line, Esc drops it, clicking elsewhere finishes it and starts the next. Segoe UI Semibold. A toggle puts it on a box (white, or near-black behind light colours) outlined in the text colour |
| Eraser | E | Click or drag across marks; each mark touched is removed whole (empty rectangles only by their outline) |
| Dimension | D | Like SolidWorks' smart dimension; see below. Shift snaps the second point to 0°, 45° or 90° from the first |
| Crop | X | Drag a box, then Enter |
| Remove white background | B | A button, not a tool. Pure white (#FFFFFF) connected to the edges of the cropped picture becomes see-through; white enclosed by the picture stays. Solid pixels touching the removed white are softened (white blended out as alpha) so anti-aliased edges leave no white fringe. Marks are untouched. The editor shows a checkerboard behind see-through parts; save, copy (PNG; the plain clipboard bitmap is flattened onto white) and pin keep the transparency |

- **Esc** drops anything part-done (a mark being drawn, a curve waiting to be bent, a crop box, text being typed, a dimension being placed) and picks the Move tool, so the next click selects rather than draws. Text whose words were being changed is left as it was. With the Move tool already picked, Esc lets go of the selection; with a flyout open, it closes the flyout.
- **Changing marks:** with the Move tool, a mark can be moved, reshaped, recoloured and given another thickness (for text, another size; for a pixelated redact box, another block size).
- **Selecting several:** with the Move tool, Shift+click or Ctrl+click adds a mark to the selection, or takes it out if it's already in. Dragging from an empty spot draws a selection box (dashed, lightly tinted), and every mark wholly inside it is selected as it's dragged; with Shift or Ctrl, they're added to what was already selected, otherwise they replace it, and a box round nothing (or a plain click on an empty spot) selects nothing. Ctrl+A picks the Move tool and selects every mark. Each selected mark has its dashed outline; the handles (and a dimension's turning handle) only show while one mark is selected on its own.
  - Dragging any of the selected marks moves them all, as one undo step; pressing one of them and letting go without moving selects just it. The arrow keys nudge them all, Delete removes them all, and picking a colour or thickness changes them all (a pixelated box keeps its colours), each as one undo step. The dots, [ and ] step from the marks' own thickness if they're all as thick, otherwise from the pen's.
  - A dimension that's one of several moves whole, whichever part of it is pressed.
- **Copying marks:** Ctrl+C copies whatever was picked or drawn last: the marks selected with the Move tool (as they are now, moved or recoloured), or the mark just drawn or whose words were just changed. Once the picture is picked instead (the capture is opened or switched to, its tile in today's captures is clicked, the Move tool clicks an empty spot, or Esc lets go of the selection), Ctrl+C copies the picture as before; so it does once the marks are no longer there (undone, erased, deleted). The Copy button always copies the picture. The status says what was copied (*Arrow copied (Ctrl+V pastes it)*, *3 marks copied (Ctrl+V pastes them)*).
  - Marks go on the clipboard in Overmark's own format (`Overmark.Marks`, JSON in the shape editable copies use), so they paste onto this capture, another one, or one in another editor window, and stay there after Overmark closes. Other apps see nothing to paste. A mark of a kind this version doesn't know is left out.
  - Ctrl+V with marks on the clipboard and a capture open pastes them on top, in the order they were drawn, as one undo step, and picks the Move tool with the copies selected. They land 10 on-screen pixels down and to the right of where they were copied from, arranged as they were; pasting the same copies again lands each lot a step further on, starting again when something else is copied. They're moved back inside the crop together if any would land outside it (to the crop's top-left corner if they're too big to fit).
  - A pasted dimension is an ordinary one: it never sets the picture's scale. If it said what it measured (or its typed length set the scale where it was copied), it says what it measures in this picture's scale; words typed over it stay.
- **Reshaping:** a selected mark shows small white square handles (outlined in the accent red) where it can be reshaped, and a press on one drags it, keeping the distance the press was from the handle's centre. Lines and arrows: their two ends. Curves: their two ends, and a round handle halfway along that bends it, like the one it had while it was drawn (moving an end keeps the curve passing through that point). Rectangles, circles and redact boxes: the four corners and the middle of each side; the opposite corner or side stays put, and dragging past it turns the box inside out. Dimensions: the two measured points; the dimension keeps measuring the same way, its line stays where it is, and if its text still says what it measured it measures again. Shift snaps as when drawing: ends to 0°, 45° or 90° from the other end, corners to a square or circle. Freehand, highlighter and text have no handles. Handles are found before the mark itself (the nearest, if they're close), so a selected mark's end can't be grabbed to move the whole mark; one that isn't selected yet moves whole, as before. The pointer shows which way a handle resizes. A double-click on a handle doesn't start changing a dimension's words.
- **Changing words:** double-clicking text with the Move tool (or a dimension, anywhere on it) opens it for typing, as when it was drawn, starting with what it says (selected, so typing replaces it). It keeps its own colour and size unless they're changed while it's open. Enter or clicking elsewhere keeps the change, Esc leaves it as it was, and deleting all of a text's words removes it (a dimension stays, without text). A dimension's turned text is typed turned.
- **Undo/redo:** Ctrl+Z / Ctrl+Y, covering drawing, erasing (one step per eraser drag), moving and reshaping (one step per drag or nudge), recolouring, changing thickness (one step per drag of the slider), changing words, deleting, pasting, cropping and removing the white background. Dimensions that follow a change of scale undo with the change that caused it.

**Dimensions**

- Click the two points to measure between (or drag from one to the other), then move the pointer to place the dimension line and click. Where the pointer is picks what it measures: above or below the points, the horizontal distance; to the left or right, the vertical distance; anywhere else, the true (aligned) distance. A label beside the pointer shows which, and the length in pixels.
- Holding Shift snaps the second point to 0°, 45° or 90° from the first, as for a line, whether it's clicked or dragged to; the guide line from the first point shows where it will go. Shift doesn't change the third click: the dimension line's direction is already set by what it measures.
- The third click opens a text box on the dimension, holding the measured length (e.g. `214 px`, or in the picture's scale once there is one, below) selected, so typing replaces it. Enter (or clicking elsewhere) finishes it; if the text is deleted the dimension is kept without text. Esc drops the whole dimension at any stage.
- Drawn in the current colour: extension lines from just off each point to a little past the dimension line, and a dimension line with slim arrowheads. The lines are half the thickness slider's value (at least 1 px) and the text is four times it, like the text tool.
- The text sits in a gap in the middle of the dimension line. If it's too long to fit between the arrowheads, it moves just outside the line instead. When the extension lines are too close together for the arrows, the arrows go outside, pointing in.
- With the Move tool, the text can be dragged anywhere. Near the dimension line, or the line carried on past the arrows, it snaps onto it and the line breaks round it (and reaches out to it if it's beyond the arrows). Anywhere else it stays where it's dropped, with a thin leader line back to the nearest point of the dimension line.
- The text can be turned with its handle. It snaps to level, upright, along the dimension line and across it (within 8°); Shift turns it freely. It never ends up upside down.
- **Real units:** changing a dimension's text (new, or by double-clicking it) to a length with a unit sets the picture's **scale** from it: a number (with a point or comma for decimals) and mm, cm, m, µm (or um), in, ft, " or ', with or without a space, e.g. `50 mm`, `2.5in`, `3"`. Anything else (a bare number, `Ø50 mm`, `50 mm REF`) doesn't. Leaving the text as it was (pressing Enter on the measured length, say) doesn't set it either.
  - Dimensions whose text says what they measure in pixels (or in the old scale) then say it in the new one, as part of the same undo step: in the typed unit, to as many decimal places as were typed, with a space or not as typed (`50.0 mm` makes `37.4 mm`; `2in` makes `1in`). New dimensions start that way, and so does the label beside the pointer while one is placed. Text that's been typed over is never changed.
  - Only one dimension sets the scale: the latest to have a length typed into it. One that set it before keeps its words but stops setting it. Moving the points of the one that sets it changes the scale (the others follow when it's let go); deleting or erasing it, or changing its text to something that isn't a length, goes back to pixels. Undo brings back the scale along with the dimension.
  - While the dimension tool is picked and there's a scale, the hint pill shows it on the right, e.g. *1 mm = 4.28 px*. Setting or removing it says so in the title bar.
  - Kept with the marks in the editable copy (which dimension sets the scale), so it lasts as long as they do.
- **Zoom:** fits the window to start with; Ctrl+scroll zooms around the pointer; Ctrl+0 fits, Ctrl+1 is actual size.
- **Saving:** only Save or Ctrl+S writes to disk, overwriting the capture file with the annotated version, in the file's own format: PNG, or JPEG (quality 95) or BMP for an image opened from elsewhere, flattened onto white as they can't be see-through. Closing with unsaved changes asks first. The window title starts with `*` while there are unsaved changes.
- **Copy** (and Ctrl+C, unless a mark was picked or drawn last; see *Copying marks*) and **Pin** use the current image, crop and marks included. Pinning minimises the editor. (A Drag button was tried and removed; dragging into other apps is done from the preview popup.)
- **Delete** (the bin button in the title bar, red under the mouse) asks first (*No* is the default), then moves the capture file to the Recycle Bin, unsaved changes and all, and moves on to the next capture as closing it does (see *Today's captures*). If the next one is open in another editor window, that window comes to the front and this one closes; with none left, the editor stays open with no capture. It has no keyboard shortcut, as Delete removes the selected mark with the Move tool.
- **Flatten:** marks are merged into the image when it's copied, saved or pinned. There are no project files; instead, captures stay editable for a day:
- **Editable for a day:** alongside the flattened PNG, Overmark keeps an editable copy of each capture that has marks (or a crop): the original pixels, the marks and the crop, in `%AppData%\Overmark\edits`. Opening the capture again within 24 hours of its last edit brings the marks back as marks, so they can still be moved, erased and recoloured; the status reads *Marks still editable*. Undo history doesn't carry over.
  - Each capture can have a **saved** copy (matching what Save wrote to the file) and a **draft** (unsaved marks, kept when switching to another capture from today's captures). The draft wins while it lasts, and opens as unsaved.
  - Closing the window still asks about unsaved changes: *Yes* saves (replacing the draft), *No* throws the unsaved marks away, draft included.
  - A copy is only used while the capture file is exactly as Overmark left it (same time and size); a file changed by anything else opens as it is.
  - Copies are deleted 24 hours after the capture's last edit (checked when Overmark starts and whenever a copy is kept), when the capture is deleted, or when it's saved with nothing left to edit.
- **Bringing up the editor:** double-clicking the tray icon, *Open editor* in the tray menu, or starting Overmark from its shortcut. The editor that's open comes to the front as it is (restored if minimised); otherwise one opens on today's newest screenshot, or with no capture if there's none today. *Edit last capture* with nothing to edit opens it with no capture too.
- **Images from elsewhere:** Ctrl+O (or the **+** above today's captures, or *Open an image…* when no capture is open) picks an image file; Ctrl+V pastes one (unless text is being typed, when it pastes text, or a mark has been copied, when it pastes the mark); or one can be dropped anywhere on the editor. Each opens in the editor as if picked from today's captures (unsaved marks on the current one are kept as a draft) and goes on the list.
  - A file (opened, dropped, or copied in File Explorer and pasted; the first image if there are several) opens as it is, and goes on the list as *Opened*. Save writes back over it in its own format. PNG, JPEG and BMP are written back; anything else Windows can read (GIF, TIFF, WebP, and HEIC with its codec) opens as a PNG copy in the screenshots folder instead, leaving the original alone, as saving over it would lose what the editor can't keep (a GIF's animation, a TIFF's other pages) or there's no encoder.
  - A picture (copied from a browser, Office or another app, or dragged in from one) is saved as a new PNG in the screenshots folder, named as captures are, and goes on the list as *Pasted*. The clipboard's PNG is used when there is one, keeping see-through parts; otherwise the plain bitmap, as opaque.
  - Photos are turned the way their camera recorded them being held (EXIF orientation), and unusual pixel formats (CMYK, greys, 16 bits a channel, palettes) become ordinary 32-bit colour. The file is read all at once, so it isn't kept locked.
  - One that can't be read says why in a message over the editor.
- **Opening:** click the preview, pick it from today's captures, use *Edit last capture* in the tray menu (which remembers the last capture across restarts), or run `Overmark.exe --edit <file>` (a lone file path also works, as Windows' "Open with" passes it). If an editor is already open, the capture shows in it (as if picked from today's captures, so unsaved marks are kept as a draft) and the window comes to the front, rather than a second window opening. Only while that editor is waiting on a question (such as whether to save or delete) does the capture open in a window of its own.

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

- **Hotkeys:** click a box and press the keys; this replaces the action's hotkeys. **Add** records another hotkey for the same action, and **Clear** removes them all. In settings.json an action's hotkeys are separated by commas: `"regionCapture": "PrintScreen, Win+Shift+S"`. While recording, Overmark's keyboard hook hands key presses to the window, so PrtScn can be recorded even though Overmark owns it. Recording stops as soon as the box loses focus, or after 10 seconds.
- **Capture on release**, or adjust the selection first.
- **Drag a selection from its Corner** (default) **or Centre:** with Centre, the press is the middle of the selection and the drag pulls out a corner, so it grows evenly both ways (a small cross marks the centre while dragging). Near a screen edge the far side stops at the edge. Handles afterwards still move one edge at a time.
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

Sound in recordings, numbered steps, OCR, uploads, a history browser beyond today's captures, scrolling capture, and changing the shape of freehand and highlighter marks after drawing them.

## Build phases

1. **Foundation:** tray app, single instance, settings file, per-monitor DPI, PrtScn takeover. *Done.*
2. **Capture overlay:** freeze, crosshairs, magnifier, window snap, fine adjustment, last region, full screen, clipboard and auto-save, toolbar. *Done.* HDR still to do.
3. **Editor drawing:** pick-first colour and thickness, the drawing tools, Shift-snap, undo/redo, zoom, copy and save. *Done.*
4. **Eraser and output:** eraser, crop, drag-out, the preview popup, pins. *Done.*
5. **Settings and installer:** settings window, start with Windows, Velopack installer and auto-update, first GitHub release. *Done except the first release.*
6. **Shakedown:** daily use by early users, fixing whatever gets in the way. Added along the way: the circle tool, crosshair and magnifier switches, centre-out selection, screen recording without sound, the text tool, deleting a capture from the preview, dimensions, the Move tool, the redact tool, reshaping marks by their handles, dimensions in real units, opening, pasting and dropping images from elsewhere, and a lighter redesign of the editor (own title bar, one dock for drawing, the loaded pen), the capture toolbar, the magnifier, the preview, the recording bar, Settings and pins.
7. **Wider release:** signing, GitHub Actions builds, README screenshots, winget.
