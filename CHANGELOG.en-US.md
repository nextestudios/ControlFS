# Changelog

English (US) release notes, mirroring CHANGELOG.md (Brazilian Portuguese). Before publishing a version, add a `## [VERSION]` section to **both** files: the release workflow uses the section matching the tag and fails if either is missing.

## [Unreleased]
### What's new
- **Recycle Bin** on the home screen: lists deleted items with their original folder and deletion date; South/A on an item offers **Restore** (back to the original folder, never overwriting) and **Delete permanently**, which always asks first with the focus on Cancel. Marking several works. An invalid original location recorded in the bin is refused. (#26)
- **Text preview**: South on .txt, .md, .log, .json, .xml, .csv, .ini, code etc. (or North → Visualizar como texto (view as text) on any file, scripts included, without running them) opens it read-only. Up/Down scroll, LT/RT page, LB/RB jump to the start/end, Left/Right shift long lines and South toggles a fixed-width/proportional font. Encoding detected (UTF-8, UTF-16, ANSI); only the first 2 MB and 10,000 lines, with a partial-preview notice; binary files are refused. (#58)
- **Smaller downloads**: the portable exe went from 97 MB to 72 MB and the installer from 65 MB to 48 MB by dropping the Windows App SDK AI/ML components the app doesn't use. (#85)
- **Grid view**: Menu → View (or Ctrl+G) switches between the list and a grid of large icons in folders and on the home screen. In the grid the D-pad moves in 2D, never skipping items and wrapping to the next row at the ends; the triggers page. Density applies to the grid (smaller tiles when compact), the choice is saved and switching views keeps the focused item. (#29)
- **Image preview** (JPG, PNG, GIF, BMP, WebP): South opens the image inside the app; Left/Right or LB/RB step through the folder's images, RT/LT zoom, the D-pad pans while zoomed and East/B closes. Decoding runs in the background and is reduced to 4096 px; the format is checked from the content and 100 MB / 80 megapixel limits apply before decoding. Nothing is executed; images inside archives aren't previewed. (#57)
- **Text selection** on the on-screen keyboard: **Select all** on the `…` page (or Ctrl+A), the selection is highlighted and underlined, typing replaces it, `⌫` deletes it and `◀ ▶` drop it. Rename opens with the name before the extension selected (`example-file.zip` → type `novo` → `novo.zip`). (#44)
- **Drive types**: local disk, USB stick, optical drive and network drive each get their own symbol and text on the home screen and in the folder picker (with label, letter and free/total space), and Narrator reads the type. Plugging in or removing a USB stick while the app is open updates the list without restarting and keeps the focus on the same place. (#25)
- **Active controller**: Menu → Controle ativo (active controller) lists the connected controllers (name, family, type, VID:PID, physical or virtual) and South/A on one makes it the only controller driving ControlFS until you switch back to automatic. When Steam Input or DS4Windows expose the physical controller and a virtual copy at the same time, the bottom bar warns about the duplicate (each button could act twice) and the menu shows which one looks like the copy. The controller test also flags virtual devices. (#80)
- **Recent** on the home screen: the last folders you visited and files/archives you opened (up to 10 of each), one press away with Home → Recent → South/A. They stay on this computer only, in your settings; North on "Recent" clears the lists, and Menu → "Recent" turns the feature off (which also erases what was stored). (#49)
- **Operation history**: Menu → Operations also lists what was done in earlier launches (copies, moves, deletes, extractions, compressions and renames), with date, source, destination and per-item outcomes. Stored as versioned JSON in the data folder with atomic writes, capped at the 200 most recent operations, never with passwords or file contents; **Clear history…** erases the record. (#20)
- **Test integrity** of an archive without extracting (North on an archive or inside it): reads every entry and checks its CRC without writing anything, with progress and cancel. The result names the failing entries and counts those without a checksum (TAR, GZ, ZIP AES AE-2). It is not a virus scan. (#66)
### Improvements
- **ZIP64 validated**: ZIP archives with entries over 4 GB or more than 65,535 entries extract with sizes and CRCs checked; the safety limits still apply. (#63)
- **AES-encrypted ZIP (WinZip AE-1/AE-2, 128/192/256-bit) validated**: without a password the app asks for one, the right password extracts identical content and a wrong one is now reported as "wrong password" (asks again) instead of "wrong password or corrupt data". (#64)
- **Tabs** in the browser: each tab keeps its own folder, history, marked items and focus. RB moves to the tab strip, LB/RB switch tabs and North creates or closes one; North on a folder has "Open in new tab". A copy or move refreshes every tab showing its source or destination. (#50)

## [0.4.0-alpha.1]
### What's new
- **New on-screen keyboard layout**: text field on top, four character rows, a function row (`⇧`, `ABC`, `@#:`, space, `⌫`) and a bottom row with the cursor, `…` (accents, language and clear) and a wide **Done**. (#41)
- Button labels automatically follow the family of the controller in use (Xbox, PlayStation, Nintendo or generic), detected from the type SDL reports and the vendor. Pressing a button on another controller hands control to it and switches the labels without restarting. The menu still lets you pin a style. (#32)
- **Retry** an operation that failed, finished with warnings or was cancelled: Menu → Operations → pick the operation → Retry. The original request is planned again from scratch. (#18)
- Controller prompts use **original vector glyphs** (face buttons, bumpers, triggers, Menu/Options/+/−, D-pad and sticks) instead of plain letters, crisp at any scale. No third-party images or logos. (#33)
- The bottom bar shows the **buttons of the controller in use** (e.g. `A Open` on Xbox, `✕ Open` on PlayStation) and updates instantly when you switch controllers or the confirm/back convention; with the keyboard it shows keys. Narrator reads the button and the action. (#34)
- **Context-sensitive** bottom bar: on an archive it shows Explore, Mark and Extract… (North opens the menu already on "Extract to"), with marked items Operations (N) and Cancel selection, on the on-screen keyboard Select/Delete/Done/Cancel and, in dialogs, the name of the focused option. Actions that don't work right now are hidden. (#35)
- **Hold to repeat** on the on-screen keyboard: holding West (backspace), LB/RB (cursor) or South on `⌫ ◀ ▶` repeats with acceleration; Done never repeats. (#42)
- **Favorites**: pin folders from the actions menu (North → "Add to favorites"). They come first on the home screen and in the folder picker's "Go to another place", can be reordered from the home screen and are saved with your settings (portable mode included). A favorite that went missing shows as unavailable and is only removed when you say so. (#48)
- **Retry failed items**: from the result of a copy, move, delete or extraction, run again only the items that failed or were not processed; what already succeeded is never redone. Items interrupted by a cancel are now reported as "not processed". (#19)
- **Native Windows icons** in the list (home, folders, picker and archives): file types, folders, special folders and drives. Icons load in the background without stalling scrolling and follow the display scale; no emoji left in the list. (#24)
- **Search by name** in the current folder: Select/View (or Ctrl+F) opens the on-screen keyboard and Done starts the search, with or without subfolders (explicit menu choice; no PC-wide index). Results appear as they are found, and the bottom bar says whether they are partial, complete or cancelled and how many folders were skipped for lack of permission. East/B cancels and keeps what was found; opening a result goes to its folder with the item focused. Search never follows junctions or links. (#46)
- **Visible cursor** on the on-screen keyboard (a steady accent-colored bar whose position Narrator reads) and **LT/RT** (or Home/End) to jump to the start or end of the text. (#43)
- **Mapping wizard for joysticks without a profile** (controllers Windows/SDL doesn't recognize as gamepads): hold any button on the joystick for 2 s (or Menu → Controllers without a profile) and press, one at a time, up, down, left, right, confirm, back and, optionally, the extra buttons. It measures each axis at rest (deadzone and inverted axes), lets you redo steps, cancels by itself after 20 s without input and has a test mode before saving. The profile applies on reconnect and after restarting the app; replacing an existing profile always asks first. Profiles can be exported and imported (versioned, validated JSON with no executable content). (#79)
- **Cleanup after a crash**: hidden temporaries left by an interrupted extraction, copy or compression (crash, power loss) are removed on the next launch, with a notice in the bottom bar. Only what ControlFS registered before creating it is removed; never by name pattern, and never from another ControlFS window that is still open. (#82)
- **Navigable path bar**: LB moves focus to the path segments, Left/Right pick one and South jumps straight to a folder above, focusing the folder you came from. Inside archives, the archive file is its own segment after `▸`; long paths collapse the middle into `…`. Also in Menu → Go to folder above…. (#30)
- **Controller test** (Menu → Teste de controles…): lists the connected controllers (name, SDL type, family, VID:PID, gamepad or joystick without a profile, which one is active) and shows every button/axis live with the action it produces. Holding Confirm copies a plain-text report (app, Windows and SDL versions, controllers and results; no personal data) to paste into the issue; holding Back or Esc leaves. Testing controllers no longer needs the .NET SDK. (#78)
- **D-pad and two-button** controllers mapped by the wizard without Actions/Menu: **hold Confirm** (0.6 s) to open Actions and **hold Back** to open the Menu; short presses are unchanged and the bottom bar shows "(segure)" ("hold") on those buttons. Joysticks with Actions and Menu mapped, and gamepads, are unchanged. (#79)
- **Select all** and **Clear selection** in the actions menu (North), with counts; drives, special folders and blocked entries are never selected. (#23)

### Security
- Extraction, copy and move destinations are checked **by handle** on Windows: destination folders stay pinned while each file is put in place, so another program can't swap them for a junction to write outside the destination. Remaining limits in `docs/security-model.md`. (#81)

### Improvements
- **Responsive layout** for handhelds (1280×720/800), desktops and 1080p/4K TVs: the layout tier follows the effective resolution, DPI and Windows text size. On small screens spacing tightens, bottom-bar prompts wrap instead of disappearing, and menus and dialogs never grow taller than the screen (the focused item always stays visible); on a 4K TV at 100% text, icons and focus grow to stay readable at ~3 m. (#36)
- **Clearer focus that always lands somewhere valid:** the list uses the same highlight ring as menus and dialogs; after deleting or moving items focus goes to the next remaining item (or the previous one); opening a folder scrolls the focused item into view right away; and the physical keyboard keeps working right after a menu or dialog closes. (#31)
- **Clearer list:** focused, marked and cut items each have their own shape (ring; stripe + checked box + "Marked"; scissors + "Cut"), readable without relying on color. Type, size and date always appear in the same order, and the focused item shows its full name. New **list density** in the menu (comfortable, or compact with columns), saved between sessions. (#28)

### Known limitations
- Not yet validated with physical controllers: run Menu → Teste de controles… and post the report on issue #78.

## [0.3.0-alpha.1]
### What's new
- **Rename** files and folders with the on-screen keyboard: the cursor starts before the extension, changing the extension asks first, and invalid or duplicate names are refused without touching the disk. (#103)
- **Copy, cut and paste** with ControlFS's own clipboard, plus **Copy to… / Move to…** with the in-app folder picker. Cut items are dimmed until pasted. (#101, #102)
- **Move** on the same drive is instant; across drives, ControlFS copies first and removes each original only after its copy succeeded. (#101)
- **Delete to the Windows Recycle Bin**, with a separate, always-confirmed "Delete permanently". Where there is no Recycle Bin the app says the delete is permanent; it never deletes permanently in silence. (#104)
- **Conflicts in every operation**: skip, keep both, replace (with confirmation) or, for folders, merge; "apply to the rest" only lasts for the current operation. (#101)
- Per-item results for every operation in the operations center. (#101)
- Official ControlFS icon and logo in the app, taskbar, shortcuts, installer and README. (#100)

### License
- Changed the project license from MIT to GNU AGPL v3.0 only (`AGPL-3.0-only`). Releases up to and including 0.2.0-alpha.1 remain available under MIT; this release and later ones are AGPL-3.0-only. See `docs/LICENSING.md`. (#9)

### Updating
- From 0.2.0-alpha.1: the installed app shows the notice; choose **Install and restart**. Your settings are kept.
- From 0.1.0-alpha.1 to alpha.4 (which didn't open): download the installer below once.

## [0.2.0-alpha.1]
### What's new
- Extract **7z, RAR (RAR4 and RAR5, including solid), TAR, TAR.GZ and GZ**, besides ZIP, with the same protections (contained paths, blocked links, conflicts, limits). Archives with protected files or file lists (RAR/7z) ask for the password on the on-screen keyboard. (#5)
- **Compress** marked items (or the focused one) to **ZIP or TAR.GZ**: name typed on the on-screen keyboard, fast/normal/maximum compression, progress in the operations center. Links and junctions are not followed and nothing is overwritten. (#5)
- **Open with Windows:** Confirm on a non-archive file opens it in the default program; the actions menu has "Open with…" and "Show in File Explorer". Programs and scripts (.exe, .msi, .bat, .ps1, .lnk…) ask for confirmation, starting on "Cancel". (#5)

### Limitations
- RAR can't be **created** (proprietary format); creating 7z isn't available yet. Split volumes aren't supported yet.


## [0.1.0-alpha.4]
### Fixes
- The 0.1.0-alpha.3 portable `ControlFS-Portable-x64.exe` closed on startup: it looked for its resource file by the executable's name. The file is now `resources.pri` and is found under any name. (#4)
- CI now opens the portable under its exact published name (it used to be renamed, which hid the bug). (#4)


## [0.1.0-alpha.3]
### Fixes
- The app closed right after opening (installed and portable) in 0.1.0-alpha.1 and 0.1.0-alpha.2: the app resource file (`ControlFS.pri`) was missing and WinUI couldn't find its styles. Fixed.

### What's new
- Single-file portable version, `ControlFS-Portable-x64.exe`: keeps settings and logs in the `ControlFS_Data` folder next to it (if that folder isn't writable, it uses `%LOCALAPPDATA%\ControlFS` and tells you).
- Local startup and crash log in `logs` inside the data folder, to diagnose problems (no passwords or file contents).
- Every release is now published only after CI actually opens the portable and the installed app on Windows and confirms the window shows up.

### Updating
- From 0.1.0-alpha.1 or 0.1.0-alpha.2: those versions didn't open, so they can't update themselves. Download and run `ControlFS-Setup-x64.exe` once.


## [0.1.0-alpha.2]
### What's new
- Windows installer (`ControlFS-Setup-x64.exe`): per-user install, no admin, Start menu shortcut and an optional desktop shortcut.
- Automatic updates in the installed version: the app checks once a day, downloads in the background and offers "Install and restart"; if you postpone, it installs when you quit. All of it can be turned off in Menu → Updates.
- Verified updates: the app only accepts an installer whose manifest is signed with the project key and whose SHA-256 and size match; same or older versions are refused.
- The portable version tells you when a new version exists (replacing it is manual).

### Fixes
- `SHA256SUMS.txt` now uses LF line endings, so `sha256sum -c` works on Linux and macOS.

### Updating from 0.1.0-alpha.1
- 0.1.0-alpha.1 had no updater: download and run `ControlFS-Setup-x64.exe` once. Updates are automatic from then on.


## [0.1.0-alpha.1]
### What's new
- First public release (pre-alpha): a controller-first file manager with a built-in ZIP extractor.
- Browse known folders (through the Windows API) and drives, with history, sorting, hidden items, marking and properties.
- Own on-screen keyboard (Portuguese/English, accents, symbols, cursor, masked passwords) usable with only directions, confirm and back.
- Create folders with Windows naming rules.
- ZIP: browse without extracting, extract all or a selection (dedicated folder, here, or "to…" with an in-app folder picker), ZipCrypto passwords, conflicts, progress and per-item results.
- Safe extraction: path containment, blocked links, refused name collisions, limits, staging and CRC checks.
- SDL3 input mapped by physical position with a switchable confirm/back convention; keyboard and mouse use the same model.

### Known limitations
- Not validated on Windows with physical controllers yet; not code-signed.
- ZIP only (no AES or ZIP64 yet). Copy, move, rename, delete, search and two panes don't exist yet.
