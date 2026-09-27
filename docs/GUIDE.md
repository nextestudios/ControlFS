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
| Start | App menu | Done |
| Select | (search, not yet) | Symbols |

**Back** closes the open menu first, then clears the selection, then goes back in history, then to the home screen. Leaving the app always asks for confirmation, starting on "Cancel".

Only one controller drives the app at a time: the first one to press a button. Pressing a button on another controller (while the active one isn't holding anything) makes it the active one. With the window in the background, input is ignored. The menu has "Confirm with: bottom/right button" and the label style: **automatic** (default: follows the family of the controller in use: Xbox, PlayStation, Nintendo or generic) or fixed to generic, Xbox, PlayStation or Nintendo.

The bottom bar shows the buttons of the controller in use (for example `A Open` on Xbox, `✕ Open` on PlayStation) and switches as soon as you use another controller. When you use the keyboard, it shows keyboard keys instead (`Enter Open`, `Esc Back`) until you press a controller button again.

The on-screen keyboard has the text field on top, four character rows, a function row (`⇧` Shift · `ABC` letters · `@#:` symbols · space · `⌫`) and a bottom row (cursor `◀ ▶` · `…` more · Cancel · **Done**). `…` opens accents, **Clear** and the PT-BR/EN switch. Shift: one press capitalizes the next letter, a second press locks caps (`⇪`), a third turns it off. Keys that a field doesn't accept (e.g. `\ / : * ? " < > |` in file names) are dimmed. Holding West, LB/RB, or South on `⌫ ◀ ▶` repeats (it speeds up the longer you hold); Done and the other keys never repeat.

Every essential action is reachable through menus (Start / North), so a pad with only a D-pad and two buttons still works.

## Favorites

Press **North** on a folder (or anywhere inside one, for "this folder") and choose **Add to favorites**. Favorites come first on the home screen and in the folder picker (Start → "Go to another place"), so they are one press away when copying, moving or extracting. On the home screen, North on a favorite offers **Move favorite up/down** and **Remove from favorites**. A favorite whose folder is missing (for example, an unplugged drive) is shown as unavailable and kept until you remove it.

## Extracting

On an archive the bottom bar shows **South Explore** (opens it read-only), **West Mark** and **North Extract…**: North opens the actions menu already on **Extract to "name"**, so North then South extracts.

- **Extract to "name"**: creates a new folder next to the archive (never reuses an existing one: "name (2)").
- **Extract here**: into the archive's folder; name conflicts ask you.
- **Extract to…**: pick a folder inside the app (you can create one there).
- Inside an open archive, mark entries with West and use **Extract selection**.

Before starting you see source, destination, entries and conflict policy. Conflicts start on **Skip (keep existing)**; **Replace** asks again. The archive is never deleted and nothing extracted is ever opened or run.

Blocked entries (unsafe names like `../`, links, reserved Windows names) show a ⚠ and the reason.

## Compressing

Mark items with West (or focus one) → North → **Compress…**. Choose the name (on-screen keyboard), ZIP or TAR.GZ and the compression level, then **Compress**. The archive is written to a temporary file and only appears when finished; an existing file is never overwritten (the name gets "(2)"). Links and junctions inside folders are skipped and listed in the result. RAR can't be created (proprietary format).

## Operations center

Menu → **Operations** lists every copy, move, delete, extraction and compression of the session. Select one to see its progress or result, or to cancel it while it runs. An operation that **failed**, **finished with warnings** or was **cancelled** offers **Retry**: ControlFS plans the original request again from scratch (items that no longer exist at the source are left out; what already reached the destination goes through the usual conflict questions). Password-protected archives ask for the password again.

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
