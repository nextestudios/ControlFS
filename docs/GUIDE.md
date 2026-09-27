# ControlFS guide

🇧🇷 [Guia em português](GUIDE.pt-BR.md)

## Controls

Buttons follow **physical position** (SDL3 convention), not printed letters.

| Position | Action | On-screen keyboard |
|---|---|---|
| D-pad / left stick | Move | Move between keys |
| South | Open / confirm | Press key |
| East | Back / close | Cancel without applying |
| West | Mark item | Backspace |
| North | Item actions | Shift |
| LB / RB | Path bar (LB) / tabs (RB) | Move cursor |
| LT / RT | Page up / down | Cursor to start / end |
| Start | App menu | Done |
| Select / View | Search | Symbols |

**Back** closes the open menu first, then clears the selection, then goes back in history, then to the home screen. Leaving the app always asks for confirmation, starting on "Cancel".

Only one controller drives the app at a time: the first one to press a button. Pressing a button on another controller (while the active one isn't holding anything) makes it the active one. With the window in the background, input is ignored. The menu has "Confirm with: bottom/right button" and the label style: **automatic** (default: follows the family of the controller in use: Xbox, PlayStation, Nintendo or generic) or fixed to generic, Xbox, PlayStation or Nintendo.

The bottom bar shows the buttons of the controller in use (for example `A Open` on Xbox, `✕ Open` on PlayStation) and switches as soon as you use another controller. When you use the keyboard, it shows keyboard keys instead (`Enter Open`, `Esc Back`) until you press a controller button again.

The on-screen keyboard has the text field on top, four character rows, a function row (`⇧` Shift · `ABC` letters · `@#:` symbols · space · `⌫`) and a bottom row (cursor `◀ ▶` · `…` more · Cancel · **Done**). `…` opens accents, **Select all** (`Sel. tudo`), **Clear** and the PT-BR/EN switch. Selected text is highlighted and underlined: typing replaces it, `⌫` deletes it and `◀ ▶` just drop the selection (Ctrl+A on a physical keyboard selects everything). Rename opens with the name before the extension already selected, so typing `novo` on `example-file.zip` gives `novo.zip`. Shift: one press capitalizes the next letter, a second press locks caps (`⇪`), a third turns it off. Keys that a field doesn't accept (e.g. `\ / : * ? " < > |` in file names) are dimmed. Holding West, LB/RB, or South on `⌫ ◀ ▶` repeats (it speeds up the longer you hold); Done and the other keys never repeat. The cursor is the accent-colored bar in the text field; LT/RT (or Home/End) jump to the start or end, and moving it never changes the text, page or Shift.

Every essential action is reachable through menus (Start / North). On a joystick mapped with only directions, Confirm and Back, **hold Confirm** (0.6 s) to open Actions and **hold Back** to open the Menu; short presses still confirm and go back, and the bottom bar shows "(segure)" ("hold") on those buttons.

### Controller test

Menu → **Teste de controles…** (controller test) lists the connected controllers (name, type, family, VID:PID, gamepad or joystick without a profile, and which one is active) and shows, for every button you press, the physical control and the action it performs in ControlFS. Nothing runs on this screen: hold Confirm for 1 s to copy a report (no personal data) and hold Back for 1 s to leave; on the keyboard, Enter and Esc.

### Active controller

By default, any controller takes over when you press a button on it (except inside sensitive confirmations). Menu → **Controle ativo…** (active controller) lists the connected controllers with their family, type, VID:PID and whether they look physical or virtual; press South/A on one and only that controller drives ControlFS until you pick **Automático** again or it disconnects. The keyboard always works.

Remappers such as Steam Input and DS4Windows expose the physical controller *and* a virtual copy (e.g. "Steam Virtual Gamepad" or an emulated Xbox 360 controller), so each button could arrive twice. ControlFS warns in the bottom bar when it sees this and marks the likely copy in the menu: choose one of them there.

### Joysticks without a profile

Some generic USB pads, arcade sticks and adapters aren't recognized as gamepads. ControlFS detects them but they can't move around until you map them:

1. Hold any button on the joystick for 2 seconds (or, with the keyboard or another controller, Menu → **Controllers without a profile…** → Configure).
2. Let go of everything for a second while ControlFS measures each axis at rest.
3. Press what you want for **Up, Down, Left, Right, Confirm and Back**, one at a time, releasing between steps. D-pads, sticks (including inverted axes) and buttons all work; an input already used is refused.
4. Optional buttons follow (Actions, Menu, Mark, regions, pages, Search). Mapping **Actions** and **Menu** is recommended; without them, holding Confirm opens Actions and holding Back opens the Menu. Press the joystick's Back button (or Enter) to skip one.
5. **Test** the new mapping: the joystick already drives the screen; choose **Save profile**, **Redo a step…** or **Cancel without saving**.

Keyboard: Esc cancels without saving, ← redoes the previous step, Enter skips an optional step. With no input for 20 seconds the wizard cancels itself. If the joystick already has a profile, saving asks before replacing it (starting on "Cancel"); cancelling at any point leaves the saved profile untouched. The profile applies again whenever that joystick is connected, including after restarting ControlFS. Menu → Controllers without a profile also exports a profile to a folder and imports one (`.json`, up to 64 KB, validated; anything unexpected is refused).

## Path bar

The current path is shown as segments at the top. **LB** moves focus from the list to the path bar (on the folder above the current one); **Left/Right** pick a segment and **South** goes there, focusing the folder you came from. **RB**, **Down** or **East** go back to the list. Inside an archive, the archive file is its own segment after a `▸`, so the disk part and the inside of the archive are easy to tell apart. Long paths collapse the middle into `…`, which opens the hidden folders. Keyboard: Ctrl+← / Ctrl+→. The same list is in Menu → **Go to folder above…**.

To jump anywhere, use Menu → **Go to path…** (also in the folder picker's Start menu): the on-screen keyboard opens with the current folder selected, so typing replaces it. Path characters (`\ / :`) are on the symbols page (Select/View); with a physical keyboard you can type or paste (Ctrl+V) a path, with or without quotes, and variables such as `%USERPROFILE%` work. **Done** goes there; a file's path opens its folder with the file focused. A missing or invalid path shows the error and keeps the keyboard open so you can fix it.

## Tabs

The tab strip sits above the path. Each tab keeps its own folder, history, marked items and focus. **RB** moves focus from the list to the strip; there, **LB/RB** (or Left/Right) switch tabs and **North** offers **New tab** (the current folder in a new tab) and **Close tab**. **South**, **Down** or **East** go back to the list. North on a folder also has **Open in new tab**. Up to 8 tabs; clicking a tab switches to it.

## The list

The focused item has a highlight ring and shows its full name (up to three lines); other long names end in "…". Marked items get a stripe on the left, a checked box and "Marked"; cut items get scissors and "Cut" and are dimmed until pasted; password-protected archive entries show a lock. None of these rely on color alone.

North → **Select all (N)** marks every item in the folder or archive (never drives, special folders or blocked entries); **Clear selection (N)** unmarks them, as does East.

Menu → **List density** switches between **comfortable** (two lines per item, for the TV) and **compact** (one line with type, size and date columns). The choice is saved.

Menu → **View** (or **Ctrl+G** on the keyboard) switches between the **list** and a **grid** of large icons, for folders and the home screen alike. In the grid the D-pad and stick move up, down, left and right between tiles: left/right continue onto the previous/next row at the ends, and down onto a shorter last row lands on its last item. The triggers page one screen of rows. Since left no longer goes to the parent folder in the grid, use Back or the path bar (LB). Density applies to the grid too (compact = smaller tiles), and switching views keeps the focused item.

**Folder size:** North on a folder or drive → **Properties** → **Calculate size** adds up every file inside it (hidden ones included, like Explorer's "Size"), showing the running total while it works. **East** cancels at once and keeps the partial value. Junctions and links are never followed (they are counted separately), and folders that could not be read are listed instead of silently skipped.

## Search

Press **Select/View** (or Ctrl+F) inside a folder, type part of the name on the on-screen keyboard and press **Done**. Case and accents don't matter ("relatorio" finds "Relatório"). Results appear as they are found; the bottom bar says whether the list is **partial** (still searching or cancelled), **complete**, or stopped at the 10,000-result limit, and how many folders could not be read (no permission). North → **Other search actions** → **Skipped folders** lists them. Nothing is indexed: only the folder you are in is read, when you search.

- **Subfolders:** included by default. Change it in Menu → "Search in subfolders" (for the next search) or North → **Other search actions** → "Subfolders" on the results (searches again).
- **East/B** while searching stops it and keeps the partial results; East/B again goes back to the folder.
- **South/A** on a result opens its folder with the item focused; Back returns to the results.
- **Filters:** North on the results opens the filters. **South** toggles a type (Folders, Images, Videos, Music and audio, Documents, Archives, Executables; several types combine) or steps through **Size** and **Modified** ranges; the menu stays open so you can pick several. The list updates right away without searching again, the bottom bar shows "N of M results (filters: …)", and the filters stay on for later searches in this session until **Clear filters**.
- Search never enters junctions, symbolic links or other reparse points (the link itself can show up as a result).

## Recent folders and files

The home screen shows **Recent** once you have opened something: the last 10 folders you visited and the last 10 files or archives you opened, newest first. Choose an item to go back to it (a file opens as if you pressed South on it in its folder). North on **Recent** offers **Clear recent** and **Turn off recent**; Menu → **Recent: remember/don't remember** turns it back on. The lists are stored only in your local settings and never sent anywhere; turning the feature off also erases them.

## Favorites

Press **North** on a folder (or anywhere inside one, for "this folder") and choose **Add to favorites**. Favorites come first on the home screen and in the folder picker (Start → "Go to another place"), so they are one press away when copying, moving or extracting. On the home screen, North on a favorite offers **Move favorite up/down** and **Remove from favorites**. A favorite whose folder is missing (for example, an unplugged drive) is shown as unavailable and kept until you remove it.

The home screen lists drives with their type (local, USB, optical, network), label, letter and free space. Plugging in or removing a USB stick updates the list within a couple of seconds, without restarting; a favorite on that stick becomes available again.

## Recycle Bin

Home → **Recycle Bin** lists what was deleted to the Windows Recycle Bin, with the original folder and the deletion date (the date shown on each item is when it was deleted). **South/A** or **North** on an item offers **Restore** (back to the folder it came from, recreated if needed; if something with the same name is already there, nothing is overwritten) and **Delete permanently…**, which always asks first with the focus on **Cancel**. Mark several items with **West/X** to restore or delete them together. An item whose recorded original location is invalid can only be deleted for good.

## Extracting

On an archive the bottom bar shows **South Explore** (opens it read-only), **West Mark** and **North Extract…**: North opens the actions menu already on **Extract to "name"**, so North then South extracts.

Inside an archive, the header shows the format, number of files, uncompressed size and how many entries are password-protected or blocked. Each file shows its size and how much it takes compressed (e.g. "compactado: 540 KB (45%)"); password-protected entries have a lock and blocked ones state the reason (South/A shows it in full). South/A explores folders, West marks for extraction and, with entries marked, the bottom bar shows **North Extract selection (N)**: the menu opens on that option, so North then South extracts only what you marked.

- **Extract to "name"**: creates a new folder next to the archive (never reuses an existing one: "name (2)").
- **Extract here**: into the archive's folder; name conflicts ask you.
- **Extract to…**: pick a folder inside the app (you can create one there).
- Inside an open archive, mark entries with West and use **Extract selection**.
- **Several archives at once**: mark them with West and press North → **Extract each to its own folder (N)** (the first option, so North then South). Each one goes into a new folder named after it (contents never mix; repeated names get "(2)"), is queued as its own operation and, at the end, a summary shows each result. Marked items that aren't archives are left out; password-protected ones ask for the password in their turn.
- **Test integrity** (North on an archive, or inside it): reads every entry and checks its CRC without writing anything, with progress and cancel in Menu → Operations. The result shows how many entries matched, which ones failed (by name) and how many have no checksum to compare (TAR, GZ, ZIP AES AE-2). It is not a virus scan.

Before starting you see source, destination, entries and conflict policy. Conflicts start on **Skip (keep existing)**; **Replace** asks again. The archive is never deleted and nothing extracted is ever opened or run.

Blocked entries (unsafe names like `../`, links, reserved Windows names) show a ⚠ and the reason.

## Compressing

Mark items with West (or focus one) → North → **Compress…**. Choose the name (on-screen keyboard), ZIP or TAR.GZ and the compression level, then **Compress**. The archive is written to a temporary file and only appears when finished; an existing file is never overwritten (the name gets "(2)"). Links and junctions inside folders are skipped and listed in the result. RAR can't be created (proprietary format).

## Operations center

Menu → **Operations** lists every copy, move, delete, extraction and compression of the session. Select one to see its progress or result, or to cancel it while it runs. An operation that **failed**, **finished with warnings** or was **cancelled** offers **Retry**: ControlFS plans the original request again from scratch (items that no longer exist at the source are left out; what already reached the destination goes through the usual conflict questions). Password-protected archives ask for the password again.

When only some items failed or were not processed (for example after a cancel or a locked file), the result dialog and the operation's details also offer **Retry failed items (N)**: only those items run again, each into the folder it was meant to reach; what already succeeded is never copied, moved or extracted again, and the new result lists only the retried items. Entries blocked for security are never retried.

**Pause and resume:** copies, moves and deletes show **Pause** in their details while running; the operation stops at the next safe point (between items, and between blocks inside a file being copied), so disk activity stops within about a second. **Resume** continues from the current item; **Cancel** also works while paused and removes the partial copy. A paused operation holds the queue: the next operations wait until it resumes or is cancelled. Pausing only lasts while ControlFS is open. Extraction and compression can't pause safely yet, so they don't offer it. Moving within the same drive and deleting a whole folder (to the Recycle Bin or permanently) are single steps and finish before the pause takes effect.

**History:** finished operations (and renames) are kept in `history.json` in the data folder (`%LOCALAPPDATA%\ControlFS`, or `ControlFS_Data` in portable mode), so Menu → Operations also lists operations from earlier launches, newest first, with date, source, destination and per-item outcome counts. It keeps the 200 most recent operations and up to 100 items each (problems first). It records what happened, never file contents or archive passwords, and by itself doesn't mean an operation can be undone. **Clear history…** at the end of the list erases the record (no file is touched).

If ControlFS is closed in the middle of an operation (crash, power loss), the next launch removes the hidden temporaries it had created (`.controlfs-staging-*`, `.controlfs-copy-*.part`) and says so in the bottom bar. Only items ControlFS registered before creating them are removed; nothing is deleted just because of its name.

## Image preview

**South** on a JPG, PNG, GIF, BMP or WebP image opens it inside ControlFS (North → **Open with the default app** still opens it in Windows). **Left/Right** or **LB/RB** go to the previous/next image in list order; **RT** zooms in and **LT** zooms out; while zoomed, the D-pad moves around the image and **South** fits it back to the screen; **East/B** closes, leaving the focus on the last image viewed. Images are decoded in the background (the screen never freezes) and reduced to at most 4096 px on the long side; only the first frame of an animated GIF is shown and phone photos are turned upright. Before decoding, ControlFS checks the real format from the file's content (not the extension) and refuses files over 100 MB or over 80 megapixels with a clear message. Nothing is ever executed. Images inside archives aren't previewed: extract them first. WebP needs the Windows WebP codec (built into Windows 11 and recent Windows 10).

## Text preview

**South** on a text file (.txt, .md, .log, .json, .xml, .csv, .ini, .yaml, source code…) opens it read-only inside ControlFS; for any other file, North → **Visualizar como texto** (view as text). That includes scripts such as .ps1 or .bat, which South would ask to run: reading them never executes anything. **Up/Down** scroll one line, **LT/RT** a page, **LB/RB** jump to the start/end, **Left/Right** shift long lines sideways, **South** switches between a fixed-width and a proportional font, **East/B** closes. The encoding is detected (UTF-8 with or without BOM, UTF-16, otherwise the Windows ANSI code page) and shown with the line count. Only the first 2 MB and 10,000 lines are read; a larger file shows a clear "partial preview" notice. Binary files are refused with a message. Files inside archives aren't previewed.

## Opening files with Windows

**South** on a file that isn't an archive, a previewable image or a text file opens it in the default Windows program. North on a file also offers **Open with…** and **Show in File Explorer**. The other program may not work with the controller: come back with Alt+Tab or the system button. Programs and scripts (.exe, .msi, .bat, .ps1, .lnk…) ask first, starting on **Cancel**. Nothing is ever opened automatically after extracting.

## Updates

The **installed** version updates itself:

1. Once a day at most, it asks GitHub for the latest release of `nextestudios/ControlFS` (stable only, or also pre-releases if you are on one).
2. It downloads the installer in the background and only accepts it if the **release manifest is signed with the project key** and the file's **SHA-256 and size** match. Same or older versions are refused.
3. It offers **Install and restart**. Postpone it and it installs silently when you quit. Nothing happens while a copy or extraction is running.

Menu → **Updates**: check now, automatic check on/off, install on quit on/off, pre-releases (automatic / yes / no).
The **portable** version only tells you a new version exists; download it from the release page.

## Screen readers

With Narrator (or another UI Automation screen reader) on, the app announces where the focus is and the focused item as you move with the controller or keyboard: the home screen, folder, menu, dialog or on-screen keyboard when you enter it, then just the item as you move (name, type, size and position such as "3 of 20"). States are spoken in words: marked, cut, blocked (with the reason), password-protected, unavailable (with the reason). Bottom-bar messages are read without moving the focus. Nothing depends on sound, vibration or color alone.

## Privacy

Everything stays on your PC. Settings and logs live in `%LOCALAPPDATA%\ControlFS` (installed) or in `ControlFS_Data` next to `ControlFS-Portable-x64.exe` (portable). Passwords are never saved or logged. Core features never use the network. The only network access is the update check, which sends nothing but a `ControlFS/<version>` User-Agent to GitHub and can be turned off.

## Troubleshooting

- **The app doesn't open:** send us `logs\startup.log` and `logs\crash.log` from the data folder above (they contain no passwords or file contents).
- **No controller detected:** plug it in and press a button; the header shows the active pad. Keyboard and mouse always work.
- **Windows SmartScreen warning:** the build isn't code-signed yet ([policy](CODE_SIGNING.md)).
- **"Format recognized but not supported":** only ZIP is supported for now.
- **Update failed / "signature invalid":** the app refused a file it couldn't verify. Try again later or download the installer from the release page.

## Building from source

Requires the .NET SDK in `global.json`.

```bash
dotnet build ControlFS.slnx
dotnet test ControlFS.slnx
```

The app runs only on Windows 11 x64. Installer + portable (needs Inno Setup 6): `.\build\Publish-ControlFS.ps1 -Version 0.1.0-alpha.3`.
More in [build-and-release.md](build-and-release.md) (Portuguese).
