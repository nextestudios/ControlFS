# ControlFS

A **file manager for Windows made for the controller**, with a **built-in extractor**: browse, organize and unzip files from the couch, on a TV-connected PC or a Windows handheld. Open source, local, no login, no telemetry.

🇧🇷 [Leia em português](README.pt-BR.md)

![Windows](https://img.shields.io/badge/Windows-11%20x64-blue)
![Version](https://img.shields.io/github/v/release/nextestudios/ControlFS?include_prereleases&label=version&color=brightgreen)
![License](https://img.shields.io/github/license/nextestudios/ControlFS)
![CI](https://github.com/nextestudios/ControlFS/actions/workflows/ci.yml/badge.svg)

> **Pre-alpha.** The first vertical journey works and is covered by automated tests on real files, but the app has **not been validated on real Windows hardware or with physical controllers yet**. See [PROGRESS.md](PROGRESS.md) (Portuguese).

## Download

Get **[0.1.0-alpha.2](https://github.com/nextestudios/ControlFS/releases/tag/v0.1.0-alpha.2)** (pre-release):

- **`ControlFS-Setup-x64.exe`** (recommended): per-user install, no admin, **updates itself automatically** (verified, signed updates).
- **`ControlFS-Portable-x64.zip`**: unzip anywhere and run `ControlFS.exe`; it tells you about new versions, replacing it is manual.

Windows 11 x64. Not code-signed yet, so SmartScreen may warn ([policy](docs/CODE_SIGNING.md)). On 0.1.0-alpha.1? Run the installer once; updates are automatic from then on.

## How it works

1. Open the app: the home screen lists your folders (Downloads, Documents…) and drives.
2. Browse with the D-pad or stick; **South** opens, **East** goes back, **North** shows actions.
3. On a `.zip`, **South** opens it read-only; **North → Extract** unpacks it to a dedicated folder, here, or a folder you pick inside the app.

| Controller (position) | Does | Keyboard |
|---|---|---|
| D-pad / left stick | Move | Arrows |
| South (A / ✕) | Open / confirm | Enter |
| East (B / ○) | Back / close | Esc |
| West (X / □) | Mark item | Space |
| North (Y / △) | Item actions | F2 |
| LT / RT | Page up / down | PgUp / PgDn |
| Start | App menu | F10 |
| — | Full screen | F11 |

Buttons follow **physical position**, so a Nintendo layout doesn't flip confirm and back. You can switch to "confirm with the right button" in the menu.

## Features

- Real folders and drives, history, sorting, hidden items, marking, properties
- Own **on-screen keyboard** (Portuguese/English, accents, symbols, cursor, masked passwords) usable with only directions + confirm + back
- **Create folder** with Windows naming rules
- **ZIP:** browse without extracting; extract all or a selection; password (ZipCrypto); conflicts (skip / keep both / replace with confirmation); progress and per-item results
- **Automatic, verified updates** (installed version): daily check, background download, "Install and restart" or install on quit; signed manifest + SHA-256; can be turned off ([how it works](docs/GUIDE.md#updates))
- **Safe extraction:** nothing is written outside the destination, links are blocked, name collisions are refused, size limits, temporary staging, CRC check ([security model](docs/security-model.md))

**Formats today: ZIP only.** AES, ZIP64, 7z, RAR, TAR and GZ are detected and reported as not supported yet ([matrix](docs/archive-support.md)).

## Roadmap

**Next:** run on Windows with real controllers, ZIP64 and AES, rename/delete (Recycle Bin), copy/move · **Later:** two panes, search, favorites, 7z/RAR/TAR/GZ, unknown-controller wizard, light theme. Details in [docs/roadmap.md](docs/roadmap.md).

## More

- [Guide](docs/GUIDE.md): controls, extraction, privacy, troubleshooting, building from source
- **Feedback:** [this form](https://github.com/nextestudios/ControlFS/issues/new?template=feedback.yml)
- Changelog: [English](CHANGELOG.en-US.md) · [português](CHANGELOG.md)

[MIT License](LICENSE) · [Third-party notices](THIRD_PARTY_NOTICES.md) · [Code signing policy](docs/CODE_SIGNING.md) · [Security](SECURITY.md)
