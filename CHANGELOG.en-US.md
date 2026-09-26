# Changelog

English (US) release notes, mirroring CHANGELOG.md (Brazilian Portuguese). Before publishing a version, add a `## [VERSION]` section to **both** files: the release workflow uses the section matching the tag and fails if either is missing.

## [Unreleased]

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
