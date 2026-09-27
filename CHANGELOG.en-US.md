# Changelog

English (US) release notes, mirroring CHANGELOG.md (Brazilian Portuguese). Before publishing a version, add a `## [VERSION]` section to **both** files: the release workflow uses the section matching the tag and fails if either is missing.

## [Unreleased]
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
- **Select all** and **Clear selection** in the actions menu (North), with counts; drives, special folders and blocked entries are never selected. (#23)

### Security
- Extraction, copy and move destinations are checked **by handle** on Windows: destination folders stay pinned while each file is put in place, so another program can't swap them for a junction to write outside the destination. Remaining limits in `docs/security-model.md`. (#81)

### Improvements
- **Clearer focus that always lands somewhere valid:** the list uses the same highlight ring as menus and dialogs; after deleting or moving items focus goes to the next remaining item (or the previous one); opening a folder scrolls the focused item into view right away; and the physical keyboard keeps working right after a menu or dialog closes. (#31)
- **Clearer list:** focused, marked and cut items each have their own shape (ring; stripe + checked box + "Marked"; scissors + "Cut"), readable without relying on color. Type, size and date always appear in the same order, and the focused item shows its full name. New **list density** in the menu (comfortable, or compact with columns), saved between sessions. (#28)

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
