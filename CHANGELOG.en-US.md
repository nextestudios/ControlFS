# Changelog

English (US) release notes, mirroring CHANGELOG.md (Brazilian Portuguese). Before publishing a version, add a `## [VERSION]` section to **both** files: the release workflow uses the section matching the tag and fails if either is missing.

## [Unreleased]

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
