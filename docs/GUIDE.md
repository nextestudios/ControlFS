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
| LB / RB | Top bar (path + quick access): from the list, LB = folder above, RB = first shortcut; in the bar, previous / next target | Move cursor |
| LT / RT | Page up / down | Cursor to start / end |
| Start | App menu | Done |
| Select / View | Search | Symbols |
| R3 (press right stick) | List ↔ grid | — |
| Right stick (tilt) | Scroll the active list, menu, text, zoomed image or dialog | — |

**Back** closes the open menu first, then clears the selection, then goes back in history, then to the home screen. Leaving the app always asks for confirmation, starting on "Cancel".

**Right stick:** tilt it to scroll the active content: the list or grid moves one row per step (the focus follows, so it never leaves the screen and never wraps), menus move one option, the text preview scrolls lines (sideways: columns), a zoomed image pans, and long dialogs and About scroll their body. Only the surface on top scrolls: with a menu or preview open, the list behind it doesn't move. A short flick scrolls one step, a stronger tilt scrolls faster, and holding it speeds up gradually; back in the center it stops at once. Pressing the stick (R3) switches list ↔ grid without scrolling. Controllers without an SDL gamepad mapping (the ones set up in "Controles sem perfil…") don't scroll with a stick; the D-pad and triggers still do everything.

Only one controller drives the app at a time: the first one to press a button. Pressing a button on another controller (while the active one isn't holding anything) makes it the active one. With the window in the background, input is ignored. The menu has "Confirm with: bottom/right button" and the label style: **automatic** (default: follows the family of the controller in use: Xbox, PlayStation, Nintendo or generic) or fixed to generic, Xbox, PlayStation or Nintendo.

The bottom bar shows the buttons of the controller in use (for example `A Open` on Xbox, with the Xbox face colors: A green, B red, X blue, Y yellow; `✕ Open` on PlayStation) and switches as soon as you use another controller. When you use the keyboard, it shows keyboard keys instead (`Enter Open`, `Esc Back`) until you press a controller button again.

The on-screen keyboard has the text field on top, four character rows, a function row (`⇧` Shift · `ABC` letters · `@#:` symbols · space · `⌫`) and a bottom row (cursor `◀ ▶` · `…` more · Cancel · **Done**). `…` opens accents, **Select all** (`Sel. tudo`), **Clear** and the PT-BR/EN switch. Selected text is highlighted and underlined: typing replaces it, `⌫` deletes it and `◀ ▶` just drop the selection (Ctrl+A on a physical keyboard selects everything). Rename opens with the name before the extension already selected, so typing `novo` on `example-file.zip` gives `novo.zip`. Shift: one press capitalizes the next letter, a second press locks caps (`⇪`), a third turns it off. Keys that a field doesn't accept (e.g. `\ / : * ? " < > |` in file names) are dimmed. Holding West, LB/RB, or South on `⌫ ◀ ▶` repeats (it speeds up the longer you hold); Done and the other keys never repeat. The cursor is the accent-colored bar in the text field; LT/RT (or Home/End) jump to the start or end, and moving it never changes the text, page or Shift.

**Suggestions:** in name, search and path fields a strip above the keys suggests matching names (case and accents don't matter: `rel` finds `Relatório 2026`): names you typed before and names in the current folder; for paths, your favorites and recent folders. Press Up from the first row to reach the strip, Left/Right to pick, South to use it (it replaces the text), Down to go back to the keys; Up again continues to the last row. Everything stays on this PC; password fields never show or store suggestions. Menu → Configurações (settings) → **Sugestões do teclado** turns them off and erases the typed history.

**Gyro aiming (experimental, off by default):** Menu → Configurações → **Mira por giroscópio no teclado** lets a controller with a gyroscope (DualSense, DualShock 4, Switch Pro…) point at keys on the on-screen keyboard: turn or tilt the controller and the focus follows; South types as usual, the D-pad keeps working and **R3** recenters. The sensor is only turned on while this setting is on. Not yet tested on real hardware (see `docs/decisions/0007`).

Every essential action is reachable through menus (Start / North). On a joystick mapped with only directions, Confirm and Back, **hold Confirm** (0.6 s) to open Actions and **hold Back** to open the Menu; short presses still confirm and go back, and the bottom bar shows "(segure)" ("hold") on those buttons.

### Menus and dialogs

Every menu and dialog opens in the same dark panel over the dimmed screen. The header says what it is about (for an item's actions: its icon, name and type). Options are full-width rows with an icon next to the text, grouped under small headings when that helps (e.g. **Organizar**, **Exibição**). The focused option is filled in cyan with dark bold text; D-pad/stick moves it, South chooses, East closes (or answers the dialog's safe choice). Options that delete data are red with a warning symbol and never start focused: sensitive confirmations start on **Cancel**. Unavailable options are dimmed; focusing one tells you why. The buttons you can press in the panel are shown at its bottom, with the glyphs of the controller in use. While a panel is open, nothing behind it reacts to the controller, keyboard or mouse. With Windows transparency effects off or high contrast on, the panel is solid instead of frosted.

Item actions (North) and the app Menu (Start) open with the most used actions as a **grid of tiles** at the top — icon with a short label underneath, readable from the couch (e.g. **Abrir**, **Recortar**, **Copiar**, **Colar**, **Renomear**, **Propriedades**, **Excluir**; in the Menu, **Colar**, **Nova pasta**, **Nova aba**, **Atualizar**, **Caminho**, **Operações**, **Configurações**, **Início**). The other actions follow in a compact list; the line under the focused tile or list row says its full name and what it does (or why it's unavailable). In the grid, **Left/Right** move between tiles and stop at the edges, **Down** from the last row goes into the list and **Up** from the top of the list goes back to the tile you came from; a red tile deletes and is always last. Clicking a tile chooses it.

All settings live in Menu → **Configurações** (settings), grouped under **Exibição** (view, density, details panel, sort, hidden items), **Busca e privacidade** (search in subfolders, recents, keyboard suggestions), **Controles** (confirm button, button labels, Fluidez (controller read rate), gyro aiming (experimental), active controller, controller test, controllers without a profile) and **ControlFS** (updates). Changing a setting keeps Configurações open with the new value, so you can change several in a row; Back closes it.

### Controller test

Menu → Configurações → **Teste de controles…** (controller test) lists the connected controllers (name, type, family, VID:PID, gamepad or joystick without a profile, and which one is active) and shows, for every button you press, the physical control and the action it performs in ControlFS. Nothing runs on this screen: hold Confirm for 1 s to copy a report (no personal data) and hold Back for 1 s to leave; on the keyboard, Enter and Esc.

### Active controller

By default, any controller takes over when you press a button on it (except inside sensitive confirmations). Menu → Configurações → **Controle ativo…** (active controller) lists the connected controllers with their family, type, VID:PID and whether they look physical or virtual; press South/A on one and only that controller drives ControlFS until you pick **Automático** again or it disconnects. The keyboard always works.

Remappers such as Steam Input and DS4Windows expose the physical controller *and* a virtual copy (e.g. "Steam Virtual Gamepad" or an emulated Xbox 360 controller), so each button could arrive twice. ControlFS warns in the bottom bar when it sees this and marks the likely copy in the menu: choose one of them there.

### Joysticks without a profile

Some generic USB pads, arcade sticks and adapters aren't recognized as gamepads. ControlFS detects them but they can't move around until you map them:

1. Hold any button on the joystick for 2 seconds (or, with the keyboard or another controller, Menu → Configurações → **Controllers without a profile…** → Configure).
2. Let go of everything for a second while ControlFS measures each axis at rest.
3. Press what you want for **Up, Down, Left, Right, Confirm and Back**, one at a time, releasing between steps. D-pads, sticks (including inverted axes) and buttons all work; an input already used is refused.
4. Optional buttons follow (Actions, Menu, Mark, regions, pages, Search). Mapping **Actions** and **Menu** is recommended; without them, holding Confirm opens Actions and holding Back opens the Menu. Press the joystick's Back button (or Enter) to skip one.
5. **Test** the new mapping: the joystick already drives the screen; choose **Save profile**, **Redo a step…** or **Cancel without saving**.

Keyboard: Esc cancels without saving, ← redoes the previous step, Enter skips an optional step. With no input for 20 seconds the wizard cancels itself. If the joystick already has a profile, saving asks before replacing it (starting on "Cancel"); cancelling at any point leaves the saved profile untouched. The profile applies again whenever that joystick is connected, including after restarting ControlFS. Menu → Configurações → Controllers without a profile also exports a profile to a folder and imports one (`.json`, up to 64 KB, validated; anything unexpected is refused).

## Top bar: path and quick access

The top bar is the same on every screen. On the left is the path: a root button (**Locais** on the home screen, the Recycle Bin and search results; **Meu computador**, "This PC", on folders and archives) followed by the real segments of where you are, for example `Meu computador › C:\ › Users › ana › Downloads`, or `Locais › Início` on the home screen. On the right is quick access: **Favoritos** (favorites), **Arquivos recentes** (recent), the Windows folders that exist on this PC (Downloads, Documents, Desktop, Pictures, Videos, Music), **Meu computador** and **Lixeira** (Recycle Bin), with the Windows icons. The bar is one region with a single focus, and its shoulder glyphs (LB/RB, L1/R1 or L/R, following the controller in use) sit at both ends. From the list, **LB** focuses the folder above and **RB** the first shortcut (on the home screen both go to Favoritos). In the bar, **LB/RB** and **Left/Right** move to the previous/next target, **LT/RT** jump to the first/last item of the part you are in, and **South** opens: a folder shortcut opens in the current tab (Back returns), Favoritos and Arquivos recentes open a list to choose from, **Meu computador** shows the drives in the current tab (Back returns), the **Meu computador** root does the same and the **Locais** root goes home. The last segment is the current folder: it is only the location label, so focus skips it (and it never reloads); the shortcut matching the current folder is highlighted and skipped as well. **Down** or **East** go back to the list, with the focus where it was.

The current path is shown as segments at the top. **LB** moves focus from the list to the path bar (on the folder above the current one); **Left/Right** (or LB/RB) pick a segment and **South** goes there, focusing the folder you came from. **Down** or **East** go back to the list. Inside an archive, the archive file is its own segment after a `▸`, so the disk part and the inside of the archive are easy to tell apart. Long paths collapse the middle into `…`, which opens the hidden folders. Keyboard: Ctrl+← / Ctrl+→. The same list is in Menu → **Go to folder above…**.

To jump anywhere, use Menu → **Go to path…** (also in the folder picker's Start menu): the on-screen keyboard opens with the current folder selected, so typing replaces it. Path characters (`\ / :`) are on the symbols page (Select/View); with a physical keyboard you can type or paste (Ctrl+V) a path, with or without quotes, and variables such as `%USERPROFILE%` work. **Done** goes there; a file's path opens its folder with the file focused. A missing or invalid path shows the error and keeps the keyboard open so you can fix it.

## Smoothness

ControlFS draws at your display's refresh rate (60, 120, 144 Hz…), as set in Windows. With Menu → Configurações → **Fluidez: máxima** (the default) it also reads the controller on every frame, so a 120 Hz screen gets 120 controller reads per second, in step with what you see. **Economia de bateria** reads it on a slower timer, which saves power on handhelds. A 60 Hz screen can't show more than 60 frames per second: to get 120, set the display to 120 Hz in Windows (Settings → Display → Advanced display).

## Tabs

Each tab keeps its own folder, history, marked items and focus. With two or more tabs, the tab strip shows in the header next to the logo (with one tab it would only repeat the path, so it is hidden). To reach it, enter the top bar (LB or RB) and press **Up**; there, **LB/RB** (or Left/Right) switch tabs and **North** offers **New tab** (the current folder in a new tab), **Close tab** and the list of tabs. **South**, **Down** or **East** go back to the list. Menu → **Abas** (tabs) does the same without the strip, and North on a folder has **Open in new tab**. Up to 8 tabs; clicking a tab switches to it. Menu → **Nova aba** (new tab) opens the current folder in a new tab from anywhere. **With two or more tabs, LT/RT (L2/R2) switch to the previous/next tab** while browsing (wrapping around at the ends); with a single tab they keep paging the list and jumping between Home sections. L1/R1 always stay on the top bar, and inside menus, the keyboard and previews the triggers belong to them.

With two or more tabs open, the next launch of ControlFS brings back the same tabs, in the same order and on the tab that was active (archives and searches come back at the folder they came from). If a tab's folder is gone or its drive is disconnected, the tab shows as "(indisponível)" (unavailable) and displays Home with a notice; opening another place in it reuses it. Menu → Configurações → **Restaurar abas ao abrir** (restore tabs on launch) turns this off (and erases the saved list).

Closed a tab by mistake? Menu → **Reabrir aba fechada** (reopen closed tab, or North on the tab strip) brings it back at the same position, with its folder and history; repeat to reopen earlier ones (up to 10 per session). **Duplicar aba** (duplicate tab, same menu) opens a copy next to it: same folder, same focused item and the same history, without the marks.

## The list

The focused item has a highlight ring and shows its full name (up to three lines); other long names end in "…". Marked items get a stripe on the left, a checked box and "Marked"; cut items get scissors and "Cut" and are dimmed until pasted; password-protected archive entries show a lock. None of these rely on color alone.

North → **Select all (N)** marks every item in the folder or archive (never drives, special folders or blocked entries); **Clear selection (N)** unmarks them, as does East.

The list sits in a card with a column header: mark box, **Nome** (name), **Tipo** (type), **Tamanho** (size) and **Modificado em** (modified). The column the folder is sorted by has an arrow (↑ ascending, ↓ descending); change it in Menu → Configurações → **Sort by** / **Order** or, with a mouse, by clicking the column title (again to reverse). Dates show as "Hoje, 14:32" (today), "Ontem, 18:05" (yesterday) or "25/09/2026, 20:11". Opening a folder starts on its first item; Back and up focus the folder you came from again. On the home screen, Windows folders show as "Pasta do sistema" (system folder) with the real size of everything inside ("Calculando…" while summing). The focused row has a blue fill, a cyan border and a highlighted chevron; marked rows show a checked box, an amber bar and "Marcado" (focus never marks: only X/West, Mark all or, with a mouse, the header box).

On the right of the list is the **details panel** for the focused item: a large icon (or the image thumbnail), name, type and real data — for a folder, the path, how many items and how much space are inside (summed in the background, only for the focused folder, with "Calculando…"); for a file, size and date; for an image, format and dimensions; for a ZIP or 7z archive, how many files it holds (without extracting); for a drive, file system, capacity, free space and the usage bar. With marked items, it shows how many and their total size. The grid has the same panel on its right (in folders, home, This PC, search, archives and the Recycle Bin): it follows the focused tile without taking focus, and the grid uses fewer columns to make room. With marked items the panel also says whether the focused item is one of them; when focus is on the top bar it only summarizes the marked items ("Nenhum item em foco"). On handhelds (1280×720/800) and narrow windows the panel steps aside by default; Actions → Properties shows the same data. Menu → Configurações → **Mostrar/Ocultar painel de detalhes** (Show/Hide details panel) toggles it; the list and the grid each remember their own choice (saved like the view and density), and on a handheld showing it gives the grid fewer columns instead of covering it.

Menu → Configurações → **List density** switches between **comfortable** (tall rows, for the TV) and **compact** (short rows, more items on screen), with the same columns. In a narrow window the type column goes first. The choice is saved.

Menu → Configurações → **View** (or **R3**, pressing the right stick, or **Ctrl+G** on the keyboard) switches between the **list** and a **grid** of cards (large icon, name, type and size, states and, for folders, a chevron), in folders, search results, archives, the Recycle Bin and the home screen. Columns follow the width left for the grid: 3 at 1080p (2 with the details panel beside it), 2 on a handheld (1 if you show the panel there), 1 in a narrow window, 4 on a 4K TV (one more in compact density). In the grid the D-pad and stick move up, down, left and right between tiles: left/right continue onto the previous/next row at the ends, and down onto a shorter last row lands on its last item. The triggers page one screen of rows. Since left no longer goes to the parent folder in the grid, use Back or the path bar (LB). Density applies to the grid too (compact = smaller tiles), and switching views keeps the focused item.

**Home screen in the grid:** the home screen becomes cards in sections. **Pastas principais** (main folders: Downloads, Documents, Desktop, Pictures, Videos, Music) show the Windows icon, the real path and how many items and how much space are inside (everything, subfolders included), for example "124 itens • 5,2 GB". The sum runs in the background, one folder at a time, showing "Calculando…" (calculating) meanwhile; it never freezes the screen, leaving the home screen stops it and the result is kept for 10 minutes. Huge folders stop after 20 seconds and show what was counted with a "+" ("250.000 itens+ • 48 GB+"). **Unidades e dispositivos** (drives and devices) show the name, a usage bar and "X livres de Y" (X free of Y) with the file system (NTFS, exFAT…). Network drives mapped in Windows and File Explorer's **Network locations** ("Add a network location") show here with the network symbol and the share (e.g. "filmes (Z:)", "Rede · \\nas\filmes"), without a usage bar: ControlFS doesn't contact the server until you open the place, so a NAS that is off never holds up Home. A remembered but disconnected drive shows as "desconectada" (disconnected); opening it lets Windows try to reconnect (Back cancels the wait). ControlFS stores no network passwords: open a share that needs a login in File Explorer first. Favorites get their own section at the top, and **Outros locais** (other places) holds Recent and the Recycle Bin. Columns follow the width left next to the details panel (3 folders side by side at 1080p without the panel, 2 on a handheld, 1 in a narrow window; more on a large TV). The D-pad moves between cards and across sections; the triggers jump to the start of the previous/next section. In the list, the home screen shows the same places as rows, with the real size of the main folders.

**Meu computador** (This PC, from quick access or the path root) opens the drives in a tab, as cards with the usage bar in the grid and as rows in the list. South opens the drive (Back returns with the focus on it) and North offers open in File Explorer, open in a new tab, favorite and Properties (with capacity, free, used and file system).

**Folder size:** North on a folder or drive → **Properties** → **Calculate size** adds up every file inside it (hidden ones included, like Explorer's "Size"), showing the running total while it works. **East** cancels at once and keeps the partial value. Junctions and links are never followed (they are counted separately), and folders that could not be read are listed instead of silently skipped.

**Disk usage:** North on a folder or drive (or on empty space for the current folder) → **Analisar uso do disco** (analyze disk usage) reads the whole tree once in the background, showing the running total; **East** cancels at once. The result lists the subfolders and then the largest files, each from biggest to smallest, with size and share of the folder (e.g. "Jogos — 12 GB (45%)"). South on a folder drills into it without reading the disk again, East goes back up a level, South on a file opens its folder with the file focused, and **Abrir esta pasta** opens the level you're on. Same rules as folder size: junctions and links are never followed and unreadable folders are listed, so the totals match Explorer's "Size".

**Git status:** Menu → Configurações → **Status do Git** (off by default). In a folder inside a Git repository, the line above the list shows the branch and how many items changed ("GIT · ramo main · 3 itens com mudanças"), and items show "Git: modificado" (modified), "novo (não rastreado)" (untracked), "adicionado", "renomeado", "conflito" or, for folders, "com mudanças" (something inside changed). It is read-only (no Git operations), doesn't need Git installed and never runs anything configured in the repository; the status arrives after the list, so browsing never waits for it.

## Batch rename

Mark the items (X/West, or North → Select all), then North → **Batch rename…**. The dialog shows a live preview (current name → new name, problems first) and the options of the chosen **Mode** (South on "Mode" cycles through them):

- **Numbering:** base name, start number and digits (`Photo 001.jpg`, `Photo 002.jpg`… in list order; the base name starts as the folder's name).
- **Find and replace:** text to find and its replacement, with **Match case** off by default.
- **Prefix and suffix:** text added before and after the name (the suffix goes before the extension).
- **Case:** lowercase, UPPERCASE or Title Case.

Text fields open the on-screen keyboard; empty is allowed (e.g. no prefix). File extensions never change. If a new name is invalid, repeats another marked item's new name or is already used in the folder (hidden items included), the item shows the reason and the whole batch is blocked until the pattern is fixed: nothing is overwritten and nothing is renamed in a chain. **Start** (or the **Rename N items** option) applies it through the Operations center with a per-item result; Menu → **Undo** puts every name back.

## Search

Press **Select/View** (or Ctrl+F) inside a folder, type part of the name on the on-screen keyboard and press **Done**. Case and accents don't matter ("relatorio" finds "Relatório"). Results appear as they are found; the bottom bar says whether the list is **partial** (still searching or cancelled), **complete**, or stopped at the 10,000-result limit, and how many folders could not be read (no permission). North → **Other search actions** → **Skipped folders** lists them. Nothing is indexed: only the folder you are in is read, when you search.

- **Subfolders:** included by default. Change it in Menu → Configurações → "Search in subfolders" (for the next search) or North → **Other search actions** → "Subfolders" on the results (searches again).
- **East/B** while searching stops it and keeps the partial results; East/B again goes back to the folder.
- **South/A** on a result opens its folder with the item focused; Back returns to the results.
- **Filters:** North on the results opens the filters. **South** toggles a type (Folders, Images, Videos, Music and audio, Documents, Archives, Executables; several types combine) or steps through **Size** and **Modified** ranges; the menu stays open so you can pick several. The list updates right away without searching again, the bottom bar shows "N of M results (filters: …)", and the filters stay on for later searches in this session until **Clear filters**.
- Search never enters junctions, symbolic links or other reparse points (the link itself can show up as a result). OneDrive folders with files on demand are searched like normal folders: only names are read, so nothing is downloaded (OneDrive may fetch the list of a folder that was never opened).

## Recent folders and files

The home screen shows **Recent** once you have opened something: the last 10 folders you visited and the last 10 files or archives you opened, newest first. Choose an item to go back to it (a file opens as if you pressed South on it in its folder). North on **Recent** offers **Clear recent** and **Turn off recent**; Menu → Configurações → **Recent: remember/don't remember** turns it back on. The lists are stored only in your local settings and never sent anywhere; turning the feature off also erases them.

## Favorites

Press **North** on a folder (or anywhere inside one, for "this folder") and choose **Add to favorites**. Favorites come first on the home screen and in the folder picker (Start → "Go to another place"), so they are one press away when copying, moving or extracting. On the home screen, North on a favorite offers **Move favorite up/down** and **Remove from favorites**. A favorite whose folder is missing (for example, an unplugged drive) is shown as unavailable and kept until you remove it.

The home screen lists drives with their type (local, USB, optical, network), label, letter and free space. Plugging in or removing a USB stick updates the list within a couple of seconds, without restarting; a favorite on that stick becomes available again.

## Recycle Bin

Home → **Recycle Bin** lists what was deleted to the Windows Recycle Bin, with the original folder and the deletion date (the date shown on each item is when it was deleted). **South/A** or **North** on an item offers **Restore** (back to the folder it came from, recreated if needed; if something with the same name is already there, nothing is overwritten) and **Delete permanently…**, which always asks first with the focus on **Cancel**. Mark several items with **West/X** to restore or delete them together. An item whose recorded original location is invalid can only be deleted for good.

## Extracting

On an archive the bottom bar shows **South Explore** (opens it read-only), **West Mark** and **North Extract…**: North opens the actions menu already on **Extract to "name"**, so North then South extracts.

Inside an archive, the header shows the format, number of files, uncompressed size and how many entries are password-protected or blocked. Each file shows its size and how much it takes compressed (e.g. "compactado: 540 KB (45%)"); password-protected entries have a lock and blocked ones state the reason (South/A shows it in full). South/A explores folders, West marks for extraction and, with entries marked, the bottom bar shows **North Extract selection (N)**: the menu opens on that option, so North then South extracts only what you marked.

- **Extract to "name"**: creates a new folder next to the archive (never reuses an existing one: "name (2)").
- **Extract here**: into the archive's folder; name conflicts ask you.
- **Extract to…**: pick a folder inside the app (you can create one there).
- Inside an open archive, mark entries with West and use **Extract selection**.
- **Several archives at once**: mark them with West and press North → **Extract each to its own folder (N)** (the first option, so North then South). Each one goes into a new folder named after it (contents never mix; repeated names get "(2)"), is queued as its own operation and, at the end, a summary shows each result. Marked items that aren't archives are left out; password-protected ones ask for the password in their turn.
- **Split archives** (`name.7z.001`, `name.zip.001`, `name.part1.rar`, `name.rar` + `name.r00`, `name.z01` + `name.zip`): open or extract from **any** volume; ControlFS gathers every volume in the same folder. If one is missing, nothing is extracted and the message names the missing volumes. Marking several volumes of the same set for a batch extraction counts as one archive. Split TAR/TAR.GZ is not supported.
- **Test integrity** (North on an archive, or inside it): reads every entry and checks its CRC without writing anything, with progress and cancel in Menu → Operations. The result shows how many entries matched, which ones failed (by name) and how many have no checksum to compare (TAR, GZ, ZIP AES AE-2). It is not a virus scan.

Before starting you see source, destination, entries and conflict policy. Conflicts start on **Skip (keep existing)**; **Replace** asks again. The archive is never deleted and nothing extracted is ever opened or run.

Blocked entries (unsafe names like `../`, links, reserved Windows names) show a ⚠ and the reason.

## Compressing

Mark items with West (or focus one) → North → **Compress…**. Choose the name (on-screen keyboard), ZIP or TAR.GZ and the compression level, then **Compress**. The archive is written to a temporary file and only appears when finished; an existing file is never overwritten (the name gets "(2)"). Links and junctions inside folders are skipped and listed in the result. RAR can't be created (proprietary format).

## Operations center

Menu → **Operations** lists every copy, move, delete, extraction and compression of the session. Select one to see its progress or result, or to cancel it while it runs. An operation that **failed**, **finished with warnings** or was **cancelled** offers **Retry**: ControlFS plans the original request again from scratch (items that no longer exist at the source are left out; what already reached the destination goes through the usual conflict questions). Password-protected archives ask for the password again.

When only some items failed or were not processed (for example after a cancel or a locked file), the result dialog and the operation's details also offer **Retry failed items (N)**: only those items run again, each into the folder it was meant to reach; what already succeeded is never copied, moved or extracted again, and the new result lists only the retried items. Entries blocked for security are never retried.

**Pause and resume:** copies, moves and deletes show **Pause** in their details while running; the operation stops at the next safe point (between items, and between blocks inside a file being copied), so disk activity stops within about a second. **Resume** continues from the current item; **Cancel** also works while paused and removes the partial copy. A paused operation holds the queue: the next operations wait until it resumes or is cancelled. Pausing only lasts while ControlFS is open. Extraction and compression can't pause safely yet, so they don't offer it. Moving within the same drive and deleting a whole folder (to the Recycle Bin or permanently) are single steps and finish before the pause takes effect.

**Undo and redo:** Menu → **Undo: …** reverses the most recent reversible operation of this session, and the result dialog of a copy or move also offers **Undo**. Only operations with a safe inverse qualify: a **rename** goes back to the old name, a **move** goes back to where the items were, a **copy** is removed (to the Recycle Bin when the drive has one) only if it is still unchanged and the original still exists, and items sent to the **Recycle Bin** are restored. Before touching anything, ControlFS checks every item; if something changed since (the old name or place is taken, the copy was edited, the item left the Recycle Bin), nothing is done and the reason is shown. Permanent deletes, replacements, folder merges and operations that finished with warnings are never offered. **Redo: …** repeats the undone operation. Undo covers operations from this session only.

**History:** finished operations (and renames) are kept in `history.json` in the data folder (`%LOCALAPPDATA%\ControlFS`, or `ControlFS_Data` in portable mode), so Menu → Operations also lists operations from earlier launches, newest first, with date, source, destination and per-item outcome counts. It keeps the 200 most recent operations and up to 100 items each (problems first). It records what happened, never file contents or archive passwords, and by itself doesn't mean an operation can be undone. **Clear history…** at the end of the list erases the record (no file is touched).

If ControlFS is closed in the middle of an operation (crash, power loss), the next launch removes the hidden temporaries it had created (`.controlfs-staging-*`, `.controlfs-copy-*.part`) and says so in the bottom bar. Only items ControlFS registered before creating them are removed; nothing is deleted just because of its name.

## Image preview

**South** on a JPG, PNG, GIF, BMP or WebP image opens it inside ControlFS (North → **Open with the default app** still opens it in Windows). **Left/Right** or **LB/RB** go to the previous/next image in list order; **RT** zooms in and **LT** zooms out; while zoomed, the D-pad moves around the image and **South** fits it back to the screen; **East/B** closes, leaving the focus on the last image viewed. The panel's prompts only list what works right now, with your controller's buttons; after 4 seconds without input they fade (Close stays visible) and any button brings them back. Images are decoded in the background (the screen never freezes) and reduced to at most 4096 px on the long side; only the first frame of an animated GIF is shown and phone photos are turned upright. Before decoding, ControlFS checks the real format from the file's content (not the extension) and refuses files over 100 MB or over 80 megapixels with a clear message. Nothing is ever executed. Images inside archives aren't previewed: extract them first. WebP needs the Windows WebP codec (built into Windows 11 and recent Windows 10).

## PDF preview

**South** on a `.pdf` shows it inside ControlFS (North → **Visualizar PDF** does the same; **Open with the default app** still opens it in Windows). **Left/Right** or **LB/RB** go to the previous/next page; **RT** zooms in and **LT** zooms out; while zoomed, the D-pad moves around the page and **South** fits it back to the screen; **East/B** closes, leaving the focus on the PDF. The header shows the page number and the zoom. A password-protected PDF says so: **South** opens the on-screen keyboard (masked; the password is never saved or suggested) and a wrong password lets you try again. Pages are drawn by Windows (`Windows.Data.Pdf`) in the background, one at a time, as pictures: links, attachments, forms and scripts never open or run. ControlFS checks that the file really is a PDF (by its content) and refuses files over 200 MB; only the first 5,000 pages can be browsed, and a page that takes more than 20 seconds to open or draw shows an error instead of freezing. PDFs inside archives aren't previewed.

## Audio playback

**South** on an MP3, WAV, WMA, M4A, AAC, FLAC, OGG or Opus file plays it inside ControlFS (North → **Ouvir aqui** does the same; **Open with the default app** is still there). **South** pauses and resumes (at the end it starts over), **Left/Right** skip back/forward 10 seconds, **LB/RB** one minute, **Up/Down** change the volume and **North** mutes/unmutes. The panel shows whether it's playing, the elapsed and total time, a progress bar and the volume. **East/B** closes: the sound stops right away and the focus stays on the file. Playback uses only the codecs installed in Windows (Media Foundation): MP3, WAV, WMA, M4A/AAC and FLAC work on Windows 10/11; OGG and Opus need the Microsoft Store media extensions (Web Media Extensions). A file Windows can't decode shows a clear message instead of playing. The file is read locally as a stream, never sent anywhere, and an executable renamed to `.mp3` is refused. Audio inside archives isn't played.

## Text preview

**South** on a text file (.txt, .md, .log, .json, .xml, .csv, .ini, .yaml, source code…) opens it read-only inside ControlFS; for any other file, North → **Visualizar como texto** (view as text). That includes scripts such as .ps1 or .bat, which South would ask to run: reading them never executes anything. **Up/Down** scroll one line, **LT/RT** a page, **LB/RB** jump to the start/end, **Left/Right** shift long lines sideways, **South** switches between a fixed-width and a proportional font, **East/B** closes. The encoding is detected (UTF-8 with or without BOM, UTF-16, otherwise the Windows ANSI code page) and shown with the line count. Only the first 2 MB and 10,000 lines are read; a larger file shows a clear "partial preview" notice. Binary files are refused with a message. Files inside archives aren't previewed.

## Disk images (ISO, IMG, VHD, VHDX)

North on an `.iso`, `.img`, `.vhd` or `.vhdx` file → **Montar imagem** (mount image) uses Windows' own mounting, the same as Explorer's "Mount" (no extra drivers), and opens the new drive when it appears. ISO/IMG images are mounted read-only; VHD/VHDX virtual hard disks need ControlFS running as administrator, as in Windows. If an image is already mounted, ControlFS just opens its drive. Errors are explained (not a disk image, damaged, compressed or sparse file, in use by another program, permission).

To eject it, go to **Meu computador** (This PC) or the home screen, North on the drive → **Desmontar imagem…** (unmount image). This works for images mounted by ControlFS or by Windows. The confirmation starts on **Cancelar** and warns that programs with files open on the drive lose access to them; the image file itself is never changed. Tabs showing that drive go back to This PC.

## Opening files with Windows

**South** on a file that isn't an archive, a previewable image, a PDF, a playable audio file or a text file opens it in the default Windows program. North on a file also offers **Open with…** and **Show in File Explorer**. The other program may not work with the controller: come back with Alt+Tab or the system button. Programs and scripts (.exe, .msi, .bat, .ps1, .lnk…) ask first, starting on **Cancel**. Nothing is ever opened automatically after extracting.

**Game shortcuts and shortcut icons.** Steam game shortcuts (`.url` files that open `steam://…`, like the ones Steam puts on the Desktop) show the game's title without `.url` (e.g. "Valheim"), the type **Jogo da Steam** and the icon the shortcut declares (the game's own `.ico` in the local Steam folder); if that icon is missing, ControlFS looks for the same file in the local Steam installation, and otherwise shows a game symbol. The details panel and Properties keep the real file name, what the shortcut opens (`steam://rungameid/…`) and its real type. **South** (or North → **Jogar…**) asks first, starting on **Cancel**, and then hands the shortcut file itself to Windows, which passes it to Steam; without Steam installed you get a readable error. Ordinary website shortcuts (`https://…`) stay regular `.url` files. Windows `.lnk` shortcuts show their own icon (the one they declare, or their target program's) instead of a blank document. Icons are only read from local paths on fixed drives: a shortcut that points its icon at a network share (`\\server\…`), a web address or a link is shown with the generic symbol and never touched.

## Updates

The **installed** version updates itself:

1. Once a day at most, it asks GitHub for the latest release of `nextestudios/ControlFS` (stable only, or also pre-releases if you are on one).
2. It downloads the installer in the background and only accepts it if the **release manifest is signed with the project key** and the file's **SHA-256 and size** match. Same or older versions are refused.
3. It offers **Install and restart**. Postpone it and it installs silently when you quit. Nothing happens while a copy or extraction is running.

Menu → Configurações → **Updates**: check now, automatic check on/off, install on quit on/off, pre-releases (automatic / yes / no).
The **portable** version only tells you a new version exists; download it from the release page.

## Screen readers

With Narrator (or another UI Automation screen reader) on, the app announces where the focus is and the focused item as you move with the controller or keyboard: the home screen, folder, menu, dialog or on-screen keyboard when you enter it, then just the item as you move (name, type, size and position such as "3 of 20"). States are spoken in words: marked, cut, blocked (with the reason), password-protected, unavailable (with the reason). Bottom-bar messages are read without moving the focus. Nothing depends on sound, vibration or color alone.

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
