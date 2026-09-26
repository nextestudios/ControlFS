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

## Privacy

Everything stays on your PC. Settings live in `%LOCALAPPDATA%\ControlFS\settings.json`. Passwords are never saved or logged. No network access for core features.

## Troubleshooting

- **No controller detected:** plug it in and press a button; the header shows the active pad. Keyboard and mouse always work.
- **Windows SmartScreen warning:** the build isn't code-signed yet ([policy](CODE_SIGNING.md)).
- **"Format recognized but not supported":** only ZIP is supported for now.

## Building from source

Requires the .NET SDK in `global.json`.

```bash
dotnet build ControlFS.slnx
dotnet test ControlFS.slnx
```

The app runs only on Windows 11 x64. Portable package: `.\build\Publish-ControlFS.ps1 -Version 0.1.0-alpha.1`.
More in [build-and-release.md](build-and-release.md) (Portuguese).
