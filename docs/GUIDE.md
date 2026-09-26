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
| LB / RB | — | Move cursor |
| LT / RT | Page up / down | — |
| Start | App menu | OK |
| Select | (search, not yet) | Symbols |

**Back** closes the open menu first, then clears the selection, then goes back in history, then to the home screen. Leaving the app always asks for confirmation, starting on "Cancel".

Only one controller drives the app at a time: the first one to press a button. With the window in the background, input is ignored. The menu has "Confirm with: bottom/right button" and the label style (generic, Xbox, PlayStation, Nintendo).

Every essential action is reachable through menus (Start / North), so a pad with only a D-pad and two buttons still works.

## Extracting

- **Extract to "name"**: creates a new folder next to the archive (never reuses an existing one: "name (2)").
- **Extract here**: into the archive's folder; name conflicts ask you.
- **Extract to…**: pick a folder inside the app (you can create one there).
- Inside an open archive, mark entries with West and use **Extract selection**.

Before starting you see source, destination, entries and conflict policy. Conflicts start on **Skip (keep existing)**; **Replace** asks again. The archive is never deleted and nothing extracted is ever opened or run.

Blocked entries (unsafe names like `../`, links, reserved Windows names) show a ⚠ and the reason.

## Compressing

Mark items with West (or focus one) → North → **Compress…**. Choose the name (on-screen keyboard), ZIP or TAR.GZ and the compression level, then **Compress**. The archive is written to a temporary file and only appears when finished; an existing file is never overwritten (the name gets "(2)"). Links and junctions inside folders are skipped and listed in the result. RAR can't be created (proprietary format).

## Opening files with Windows

**South** on a file that isn't an archive opens it in the default Windows program. North on a file also offers **Open with…** and **Show in File Explorer**. The other program may not work with the controller: come back with Alt+Tab or the system button. Programs and scripts (.exe, .msi, .bat, .ps1, .lnk…) ask first, starting on **Cancel**. Nothing is ever opened automatically after extracting.

## Updates

The **installed** version updates itself:

1. Once a day at most, it asks GitHub for the latest release of `nextestudios/ControlFS` (stable only, or also pre-releases if you are on one).
2. It downloads the installer in the background and only accepts it if the **release manifest is signed with the project key** and the file's **SHA-256 and size** match. Same or older versions are refused.
3. It offers **Install and restart**. Postpone it and it installs silently when you quit. Nothing happens while a copy or extraction is running.

Menu → **Updates**: check now, automatic check on/off, install on quit on/off, pre-releases (automatic / yes / no).
The **portable** version only tells you a new version exists; download it from the release page.

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
