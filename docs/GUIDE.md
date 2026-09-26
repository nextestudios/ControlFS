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

## Updates

The **installed** version updates itself:

1. Once a day at most, it asks GitHub for the latest release of `nextestudios/ControlFS` (stable only, or also pre-releases if you are on one).
2. It downloads the installer in the background and only accepts it if the **release manifest is signed with the project key** and the file's **SHA-256 and size** match. Same or older versions are refused.
3. It offers **Install and restart**. Postpone it and it installs silently when you quit. Nothing happens while a copy or extraction is running.

Menu → **Updates**: check now, automatic check on/off, install on quit on/off, pre-releases (automatic / yes / no).
The **portable** version only tells you a new version exists; download it from the release page.

## Privacy

Everything stays on your PC. Settings live in `%LOCALAPPDATA%\ControlFS\settings.json`. Passwords are never saved or logged. Core features never use the network. The only network access is the update check, which sends nothing but a `ControlFS/<version>` User-Agent to GitHub and can be turned off.

## Troubleshooting

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

The app runs only on Windows 11 x64. Installer + portable (needs Inno Setup 6): `.\build\Publish-ControlFS.ps1 -Version 0.1.0-alpha.2`.
More in [build-and-release.md](build-and-release.md) (Portuguese).
