# Changelog

English (US) release notes, mirroring CHANGELOG.md (Brazilian Portuguese). Before publishing a version, add a `## [VERSION]` section to **both** files: the release workflow uses the section matching the tag and fails if either is missing.

## [Unreleased]
### What's new
- **New on-screen keyboard layout**: text field on top, four character rows, a function row (`⇧`, `ABC`, `@#:`, space, `⌫`) and a bottom row with the cursor, `…` (accents, language and clear) and a wide **Done**. (#41)
- Button labels automatically follow the family of the controller in use (Xbox, PlayStation, Nintendo or generic), detected from the type SDL reports and the vendor. Pressing a button on another controller hands control to it and switches the labels without restarting. The menu still lets you pin a style. (#32)
- **Retry** an operation that failed, finished with warnings or was cancelled: Menu → Operations → pick the operation → Retry. The original request is planned again from scratch. (#18)

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
