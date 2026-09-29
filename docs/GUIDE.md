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
| LT / RT | Page up / down; with 2+ tabs, previous / next tab | Cursor to start / end |
| Start | App menu | Done |
| Select / View | Search | Symbols |
| R3 (press right stick) | List ↔ grid | — |
| L3 (press left stick) | With two panes: switch the active pane (Tab on the keyboard) | — |
| Right stick (tilt) | Scroll the active list, menu, text, zoomed image or dialog | — |

**Back** closes the open menu first, then clears the selection, then goes back in history, then to the home screen. Leaving the app always asks for confirmation, starting on "Cancel".

**Right stick:** tilt it to scroll the active content: the list or grid moves one row per step (the focus follows, so it never leaves the screen and never wraps), menus move one option, the text preview scrolls lines (sideways: columns), a zoomed image pans, and long dialogs and About scroll their body. Only the surface on top scrolls: with a menu or preview open, the list behind it doesn't move. A short flick scrolls one step, a stronger tilt scrolls faster, and holding it speeds up gradually; back in the center it stops at once. Pressing the stick (R3) switches list ↔ grid without scrolling. Controllers without an SDL gamepad mapping (the ones set up in "Controles sem perfil…") don't scroll with a stick; the D-pad and triggers still do everything.

Only one controller drives the app at a time: the first one to press a button. Pressing a button on another controller (while the active one isn't holding anything) makes it the active one. With the window in the background, input is ignored. The menu has "Confirm with: bottom/right button" and the label style: **automatic** (default: follows the family of the controller in use: Xbox, PlayStation, Nintendo or generic) or fixed to generic, Xbox, PlayStation or Nintendo.

The bottom bar shows the buttons of the controller in use (for example `A Open` on Xbox, with the Xbox face colors: A green, B red, X blue, Y yellow; `✕ Open` on PlayStation) and switches as soon as you use another controller. When you use the keyboard, it shows keyboard keys instead (`Enter Open`, `Esc Back`) until you press a controller button again. The bar always stays one line: when the prompts don't fit (handhelds, large Windows text), the less essential ones are left out from the end — **Abrir**, **Voltar**, **Ações** and **Menu** always stay, and everything else still works and is in the menus.

The on-screen keyboard opens with the focus on the first letter (`q`), not on the number row. It has the text field on top, four character rows, a function row (`⇧` Shift · `ABC` letters · `@#:` symbols · space · `⌫`) and a bottom row (cursor `◀ ▶` · `…` more · Cancel · **Done**). `…` opens accents, **Select all** (`Sel. tudo`), **Clear** and the PT-BR/EN switch. Selected text is highlighted and underlined: typing replaces it, `⌫` deletes it and `◀ ▶` just drop the selection (Ctrl+A on a physical keyboard selects everything). Rename opens with the name before the extension already selected, so typing `novo` on `example-file.zip` gives `novo.zip`. Shift: one press capitalizes the next letter, a second press locks caps (`⇪`), a third turns it off. Keys that a field doesn't accept (e.g. `\ / : * ? " < > |` in file names) are dimmed. Only the focused key has an outline; **Done** is marked by its accent-colored text. On handhelds the prompts under the keyboard show only the essentials (Select, Delete, Shift, Symbols, Done, Cancel); LB/RB and LT/RT still move the cursor. Holding West, LB/RB, or South on `⌫ ◀ ▶` repeats (it speeds up the longer you hold); Done and the other keys never repeat. The cursor is the accent-colored bar in the text field; LT/RT (or Home/End) jump to the start or end, and moving it never changes the text, page or Shift.

**Suggestions:** in name, search and path fields a strip above the keys suggests matching names (case and accents don't matter: `rel` finds `Relatório 2026`): names you typed before and names in the current folder; for paths, your favorites and recent folders. Press Up from the first row to reach the strip, Left/Right to pick, South to use it (it replaces the text), Down to go back to the keys; Up again continues to the last row. Everything stays on this PC; password fields never show or store suggestions. Menu → Configurações (settings) → **Sugestões do teclado** turns them off and erases the typed history.

**Gyro aiming (experimental, off by default):** Menu → Configurações → **Mira por giroscópio no teclado** lets a controller with a gyroscope (DualSense, DualShock 4, Switch Pro…) point at keys on the on-screen keyboard: turn or tilt the controller and the focus follows; South types as usual, the D-pad keeps working and **R3** recenters. The sensor is only turned on while this setting is on. Not yet tested on real hardware (see `docs/decisions/0007`).

Every essential action is reachable through menus (Start / North). On a joystick mapped with only directions, Confirm and Back, **hold Confirm** (0.6 s) to open Actions and **hold Back** to open the Menu; short presses still confirm and go back, and the bottom bar shows "(segure)" ("hold") on those buttons.

### Menus and dialogs

Every menu and dialog opens in the same dark panel over the dimmed screen. The header says what it is about (for an item's actions: its icon, name and type). Options are full-width rows with an icon next to the text, grouped under small headings when that helps (e.g. **Organizar**, **Exibição**). The focused option is filled in cyan with dark bold text; D-pad/stick moves it, South chooses, East closes (or answers the dialog's safe choice). Options that delete data are red with a warning symbol and never start focused: sensitive confirmations start on **Cancel**. Unavailable options are dimmed; focusing one tells you why. The buttons you can press in the panel are shown at its bottom, with the glyphs of the controller in use. While a panel is open, nothing behind it reacts to the controller, keyboard or mouse. With Windows transparency effects off or high contrast on, the panel is solid instead of frosted.

Item actions (North) and the app Menu (Start) open with the most used actions as a **grid of tiles** at the top — icon with a short label underneath, readable from the couch (e.g. **Abrir**, **Recortar**, **Copiar**, **Colar**, **Renomear**, **Propriedades**, **Excluir**; in the Menu, **Colar**, **Nova pasta**, **Nova aba**, **Atualizar**, **Caminho**, **Operações**, **Configurações**, **Início**). The other actions follow in a compact list; the line under the focused tile or list row says its full name and what it does (or why it's unavailable). In the grid, **Left/Right** move between tiles and stop at the edges, **Down** from the last row goes into the list and **Up** from the top of the list goes back to the tile you came from; a red tile deletes and is always last. Clicking a tile chooses it. The menu opens on the first available tile (on an archive, on **Extrair para**, extract to). In an item's menu, what applies to the open folder — adding it to favorites, **Colar** (paste), **Nova pasta aqui** (new folder here), **Abrir terminal aqui** (open terminal here) — sits in the **Nesta pasta (name)** group, named after the folder and kept apart from the item's actions.

In dialogs, the footer says what each button does. With the focus on the back option itself (**Cancelar**, **Fechar**), the footer shows the next option on Right instead of repeating the same thing on both buttons.

All settings live in Menu → **Configurações** (settings), grouped under **Exibição** (view, density, details panel, sort, hidden items), **Busca e privacidade** (search in subfolders, recents, keyboard suggestions), **Controles** (confirm button, button labels, Fluidez (controller read rate), Leve em segundo plano (light in the background), gyro aiming (experimental), active controller, controller test, controllers without a profile) and **ControlFS** (updates). In each group, short settings are **tiles** in a grid with the icon, a short name and the current value (e.g. **Ocultos** · escondidos, hidden items · hidden). Values are states, never verbs, and on/off settings say **ligado**/**desligado** (on/off); the same setting has the same name and value everywhere. The line under the focused tile explains what the setting does instead of repeating its value. Settings with long descriptions or that open another screen stay in a list below. In the grid, Left/Right move between tiles, Down from the last row continues to the group's list (and from there to the next group's grid) and Up goes back to the tile you came from. The panel keeps the same width: moving focus never widens or narrows it, and long descriptions wrap. Changing a setting keeps Configurações open with the new value and focus on the same setting, so you can change several in a row; Back closes it.

### Controller test

Menu → Configurações → **Teste de controles…** (controller test) lists the connected controllers (name, type, family, VID:PID, gamepad or joystick without a profile, and which one is active) and shows, for every button you press, the physical control and the action it performs in ControlFS. Nothing runs on this screen: hold Confirm for 1 s to copy a report (no personal data) and hold Back for 1 s to leave; on the keyboard, Enter and Esc.

### Active controller

By default, any controller takes over when you press a button on it (except inside sensitive confirmations). Menu → Configurações → **Controle ativo…** (active controller) lists the connected controllers with their family, type, VID:PID and whether they look physical or virtual; press South/A on one and only that controller drives ControlFS until you pick **Automático** again or it disconnects. The keyboard always works.

Remappers such as Steam Input and DS4Windows expose the physical controller *and* a virtual copy (e.g. "Steam Virtual Gamepad" or an emulated Xbox 360 controller), so each button could arrive twice. ControlFS shows a notice when it sees this and marks the likely copy in the menu: choose one of them there.

### Joysticks without a profile

Some generic USB pads, arcade sticks and adapters aren't recognized as gamepads. ControlFS detects them but they can't move around until you map them:

1. Hold any button on the joystick for 2 seconds (or, with the keyboard or another controller, Menu → Configurações → **Controllers without a profile…** → Configure).
2. Let go of everything for a second while ControlFS measures each axis at rest.
3. Press what you want for **Up, Down, Left, Right, Confirm and Back**, one at a time, releasing between steps. D-pads, sticks (including inverted axes) and buttons all work; an input already used is refused.
4. Optional buttons follow (Actions, Menu, Mark, regions, pages, Search). Mapping **Actions** and **Menu** is recommended; without them, holding Confirm opens Actions and holding Back opens the Menu. Press the joystick's Back button (or Enter) to skip one.
5. **Test** the new mapping: the joystick already drives the screen; choose **Save profile**, **Redo a step…** or **Cancel without saving**.

Keyboard: Esc cancels without saving, ← redoes the previous step, Enter skips an optional step. With no input for 20 seconds the wizard cancels itself. If the joystick already has a profile, saving asks before replacing it (starting on "Cancel"); cancelling at any point leaves the saved profile untouched. The profile applies again whenever that joystick is connected, including after restarting ControlFS. Menu → Configurações → Controllers without a profile also exports a profile to a folder and imports one (`.json`, up to 64 KB, validated; anything unexpected is refused).

### Phone as a controller

No controller or keyboard at hand? Use your phone (Android or iPhone, current Chrome or Safari), with no app to install and no account:

1. Menu → **Conectar celular…** (connect phone). A QR code appears with the address under it. The phone must be on the same Wi-Fi or wired network as the PC.
2. Point the phone's camera at the code and open the link. The first time, **Windows Firewall** may ask about ControlFS: allow it on **private networks** (if you deny it, the page won't open; allow it later in Windows Security → Firewall → Allow an app).
3. The PC shows the phone's IP address and a 6-digit code; check that the phone shows the same code and choose **Permitir** (allow). Choose **Recusar** (refuse) if you don't recognize it.
4. The page has a D-pad, a swipe area that scrolls, **Abrir** (open), **Voltar** (back), **Marcar** (mark), **Ações** (actions), **Menu**, **Buscar** (search), **Lista/Grade** (list/grid), region and page buttons. When a text field is open in ControlFS (search, rename, go to path…), type in the phone's field with its own keyboard; **OK** finishes, like OK on the on-screen keyboard.

Important confirmations (delete, replace, undo) are answered on the PC: from the phone only **Voltar** works there. Menu → **Desconectar celular** (disconnect phone) or **Desconectar** on the phone ends the session; locking the phone, closing the page, losing the network or closing ControlFS does too. Each QR code works for 2 minutes and for one connection, so connecting again means Menu → Conectar celular again. If the PC has several networks (e.g. Wi-Fi and cable, or virtual adapters), **Usar outra rede do PC** switches the address. The link is direct and encrypted; don't use it on networks you don't trust (public Wi-Fi). Details: [security model](security-model.md#celular-como-controle-223) and [privacy](PRIVACY.md).

## First launch: welcome and guided tutorial

The first time ControlFS opens, a full-screen **welcome** walks you through five steps: welcome; how the controller works (the real buttons of the controller in use — Open, Back, Actions, Menu, Search, Mark, L1/R1 for the top bar, L2/R2 for tabs, R3 for the view; press any button on your controller and the prompts switch to it); the basics, applied at once (confirm with the bottom or right button, button labels, theme, list/grid, Fluidez); privacy (everything stays on this PC; the update check is optional; the phone link is off until you open it); and **Quer fazer o tutorial guiado?** — **Começar tutorial** or **Agora não**.

| Control | Welcome |
|---|---|
| D-pad / left stick | Move between options |
| South (A / ✕) | Choose, or change the focused setting |
| East (B / ○) or L1 | Previous step |
| R1 | Next step |
| Start / Menu | Skip everything (no confirmation) |

Keyboard (arrows, Enter, Esc, F10) and mouse (click an option or a prompt) work too, and Narrator reads each step. The **guided tutorial** then runs on the real screen, read-only: a callout shows the exact button of your controller and the region it's about (list, top bar or footer) is highlighted; it advances only when you actually do it — move the focus, open a folder, go back, open and close Actions (its name changes with the context: Extrair in an archive, Filtros in a search), enter the top bar with L1/R1 and open a shortcut, switch list/grid with R3, search with Select/View, and open the Menu and Configurações. **Mark** (West) opens the tutorial options: continue, **Voltar passo** (previous step) or **Pular tutorial** (skip); with the mouse, use the callout's buttons. Both end on Home with the first card focused. See them again from Menu → **Ajuda e tutorial…** (guided tutorial, welcome, About) or Configurações → **Rever boas-vindas**. If you already used ControlFS before this version, the welcome doesn't open on its own.

**More from the team.** Once, right after the welcome (and the tutorial, if you take it), one screen presents the team's other apps: **NextBoost PRO** (a Windows 10/11 optimizer for gaming) and **Console Mode** (turns your PC into a console: focuses the TV, switches audio, opens Steam Big Picture, Playnite or Xbox). Left/Right choose, South opens the app's site in your browser, East closes. It never comes back on its own; **Menu → Mais da equipe** (or Ajuda e tutorial → Mais da equipe) shows it again, with each app's platform; new apps from the team appear in the same list. ControlFS itself doesn't go online for this: the logos are in the package and a link only opens when you choose it.

## Top bar: path and quick access

The top bar is the same on every screen. On the left is the path: a root button (**Locais** on the home screen, the Recycle Bin and search results; **Meu computador**, "This PC", on folders and archives) followed by the real segments of where you are, for example `Meu computador › C:\ › Users › ana › Downloads`, or `Locais › Início` on the home screen. On the right is quick access: **Favoritos** (favorites), **Recentes** (recent), the Windows folders that exist on this PC (Downloads, Documents, Desktop, Pictures, Videos, Music), **Meu computador** and **Lixeira** (Recycle Bin), with the Windows icons. The bar is one region with a single focus, and its shoulder glyphs (LB/RB, L1/R1 or L/R, following the controller in use) sit at both ends. From the list, **LB** focuses the folder above and **RB** the first shortcut (on the home screen both go to Favoritos). In the bar, **LB/RB** and **Left/Right** move to the previous/next target, **LT/RT** jump to the first/last item of the part you are in, and **South** opens: a folder shortcut opens in the current tab (Back returns), Favoritos and Recentes open a list to choose from, **Meu computador** shows the drives in the current tab (Back returns), the **Meu computador** root does the same and the **Locais** root goes home. The last segment is the current folder: it is only the location label, so focus skips it (and it never reloads); the shortcut matching the current folder is highlighted and skipped as well. **Down** or **East** go back to the list, with the focus where it was.

The current path is shown as segments at the top. **LB** moves focus from the list to the path bar (on the folder above the current one); **Left/Right** (or LB/RB) pick a segment and **South** goes there, focusing the folder you came from. **Down** or **East** go back to the list. Inside an archive, the archive file is its own segment after a `▸`, so the disk part and the inside of the archive are easy to tell apart. Long paths collapse the middle into `…`, which opens the hidden folders. Keyboard: Ctrl+← / Ctrl+→. The same list is in Menu → **Go to folder above…**.

To jump anywhere, use Menu → **Go to path…** (also in the folder picker's Start menu): the on-screen keyboard opens with the current folder selected, so typing replaces it. Path characters (`\ / :`) are on the symbols page (Select/View); with a physical keyboard you can type or paste (Ctrl+V) a path, with or without quotes, and variables such as `%USERPROFILE%` work. **Done** goes there; a file's path opens its folder with the file focused. A missing or invalid path shows the error and keeps the keyboard open so you can fix it.

## Smoothness

ControlFS draws at your display's refresh rate (60, 120, 144 Hz…), as set in Windows. With Menu → Configurações → **Fluidez: máxima** (the default) it reads the controller ~125 times per second, enough for a 120 Hz screen; **economia de bateria** reads it less often, which saves power on handhelds. A 60 Hz screen can't show more than 60 frames per second: to get 120, set the display to 120 Hz in Windows (Settings → Display → Advanced display).

With no controller connected, ControlFS only checks a few times per second whether one arrived (keyboard and mouse don't need polling); fast reading starts as soon as a controller connects.

**Leve em segundo plano** (light in the background, Menu → Configurações, on by default): when the window is minimized or behind another window — a game, for example — ControlFS yields the processor to it (below-normal priority and Windows 11 efficiency mode, shown as a leaf in Task Manager), reads controllers only to notice connections, stops checking drives and, after 5 seconds, gives unused memory back to Windows. Coming back to the window restores everything at once (the first moment may be a little slower while Windows brings memory back). Copies and extractions keep running, just slower, and audio or video playing in ControlFS keeps normal priority. Turn it off if you want background operations at full speed.

## Title bar and full screen

The top of the window has no white Windows title bar: the header with the ControlFS logo goes all the way up and uses the theme colors, and minimize, maximize and close sit in its top-right corner with the same colors (close turns red on hover; with a Windows high-contrast theme they use the system colors). Drag the empty part of the header to move the window, double-click it to maximize or restore, and hover maximize for Windows 11 snap layouts.

**Full screen**: the button next to minimize, **F11**, Menu → **Tela cheia** (a tile) or Menu → Configurações → **Tela cheia**. The same button, F11 or Menu → **Sair da tela cheia** goes back to the window as it was (maximized or not). The choice is saved, so ControlFS opens full screen next time. There's no controller shortcut (every button already has a job); Esc is Back, so it doesn't leave full screen. A video always plays full screen; F11 during a video leaves full screen just for that video, and closing it returns the window to your choice.

## Theme and accent color

Menu → Configurações → **Tema** (theme) switches between **automático** (the default: follows Windows' app mode in Settings → Personalization → Colors, live), **escuro** (dark) and **claro** (light). **Cor de destaque** (accent color) cycles through cyan, blue, green, amber, magenta and orange: it colors the focus ring, the focused option, the text cursor and highlighted symbols. Both apply at once, and Configurações stays open so you can compare. Every combination is checked for contrast: text and the focused option stay readable (≥ 4.5:1), and the focus ring stands out from the background (≥ 3:1). Marked items, warnings and dangerous actions keep their own colors and symbols, whatever the accent.

## Tabs

Each tab keeps its own folder, history, marked items and focus. With two or more tabs, the tab strip shows in the header next to the logo (with one tab it would only repeat the path, so it is hidden). To reach it, enter the top bar (LB or RB) and press **Up**; there, **LB/RB** (or Left/Right) switch tabs and **North** offers **New tab** (the current folder in a new tab), **Close tab** and the list of tabs. **South**, **Down** or **East** go back to the list. Menu → **Abas** (tabs) does the same without the strip, and North on a folder has **Open in new tab**. Up to 8 tabs; clicking a tab switches to it. Two tabs with the same name show the folder they are in, e.g. **Fotos (Viagem)** and **Fotos (Casamento)**. Menu → **Nova aba** (new tab) opens the current folder in a new tab from anywhere. **With two or more tabs, LT/RT (L2/R2) switch to the previous/next tab** while browsing (wrapping around at the ends); with a single tab they keep paging the list and jumping between Home sections. L1/R1 always stay on the top bar, and inside menus, the keyboard and previews the triggers belong to them.

With two or more tabs open, the next launch of ControlFS brings back the same tabs, in the same order and on the tab that was active (archives and searches come back at the folder they came from). If a tab's folder is gone or its drive is disconnected, the tab shows as "(indisponível)" (unavailable) and displays Home with a notice; opening another place in it reuses it. Menu → Configurações → **Restaurar abas ao abrir** (restore tabs on launch) turns this off (and erases the saved list).

Closed a tab by mistake? Menu → **Reabrir aba fechada** (reopen closed tab, or North on the tab strip) brings it back at the same position, with its folder and history; repeat to reopen earlier ones (up to 10 per session). **Duplicar aba** (duplicate tab, same menu) opens a copy next to it: same folder, same focused item and the same history, without the marks.

## Two panes

Menu → **Dois painéis** (two panes) shows two folders side by side, each pane with its own location, history, marks and focus. The active pane has the cyan outline and the "ATIVO" (active) title, and the top bar shows its path; the other one is dimmed, with its path in its title. **L3** (press the left stick; **Tab** on the keyboard) switches the active pane without changing marks or starting anything; clicking the other pane does the same. Tabs stay in the left pane (with the right pane active, LT/RT page the list). In **Actions** (North), **Copiar para o outro painel** (copy to the other pane), **Mover para o outro painel** (move) and, on an archive, **Extrair para o outro painel** (extract) show the source and destination before running; with both panes in the same folder they are unavailable and say why. Handhelds and narrow windows show a single pane (the choice is saved and comes back on a larger screen).

## The list

The focused item has a highlight ring and shows its full name (up to three lines); other long names end in "…". Marked items get a stripe on the left, a checked box and "Marked"; cut items get scissors and "Cut" and are dimmed until pasted; password-protected archive entries show a lock. None of these rely on color alone.

North → **Select all (N)** marks every item in the folder or archive (never drives, special folders or blocked entries); **Clear selection (N)** unmarks them, as does East.

The list sits in a card with a column header: mark box, **Nome** (name), **Tipo** (type), **Tamanho** (size) and **Modificado em** (modified). The column the folder is sorted by has an arrow (↑ ascending, ↓ descending); change it in Menu → Configurações → **Sort by** / **Order** or, with a mouse, by clicking the column title (again to reverse). Dates show as "Hoje, 14:32" (today), "Ontem, 18:05" (yesterday) or "25/09/2026, 20:11". Opening a folder starts on its first item; Back and up focus the folder you came from again. On the home screen, Windows folders show as "Pasta do sistema" (system folder) with the real size of everything inside ("Calculando…" while summing). The focused row has a blue fill, a cyan border and a highlighted chevron; marked rows show a checked box, an amber bar and "Marcado" (focus never marks: only X/West, Mark all or, with a mouse, the header box).

On the right of the list is the **details panel** for the focused item: a large icon (or the image thumbnail), name, type and real data — for a folder, the path, how many items and how much space are inside (summed in the background, only for the focused folder, with "Calculando…"); for a file, size and date; for an image, format and dimensions; for a ZIP or 7z archive, how many files it holds (without extracting); for a drive, file system, capacity, free space and the usage bar. With marked items, it shows how many and their total size. The grid has the same panel on its right (in folders, home, This PC, search, archives and the Recycle Bin): it follows the focused tile without taking focus, and the grid uses fewer columns to make room. With marked items the panel also says whether the focused item is one of them; when focus is on the top bar it only summarizes the marked items ("Nenhum item em foco"). On handhelds (1280×720/800) and narrow windows the panel steps aside by default; Actions → Properties shows the same data. Menu → Configurações → **Painel de detalhes: visível/oculto** (details panel: shown/hidden) toggles it; the list and the grid each remember their own choice (saved like the view and density), and on a handheld showing it gives the grid fewer columns instead of covering it.

Menu → Configurações → **List density** switches between **comfortable** (tall rows, for the TV) and **compact** (short rows, more items on screen), with the same columns. In a narrow window the type column goes first. The choice is saved. On a handheld (1280×720/800) comfortable rows are shorter (about 64 px), and the very first launch there starts in **compact**; once you pick a density, ControlFS never changes it. Long paths in the details panel lose their middle (`C:\…\2026\Relatórios trimestrais`), keeping the drive and the last folders.

Menu → Configurações → **View** (or **R3**, pressing the right stick, or **Ctrl+G** on the keyboard) switches between the **list** and a **grid** of cards (large icon, name, type and size, states and, for folders, a chevron), in folders, search results, archives, the Recycle Bin and the home screen. Columns follow the width left for the grid: 3 at 1080p (2 with the details panel beside it), 2 on a handheld (1 if you show the panel there), 1 in a narrow window, 4 on a 4K TV (one more in compact density). In the grid the D-pad and stick move up, down, left and right between tiles: left/right continue onto the previous/next row at the ends, and down onto a shorter last row lands on its last item. The triggers page one screen of rows. Since left no longer goes to the parent folder in the grid, use Back or the path bar (LB). Density applies to the grid too (compact = smaller tiles), and switching views keeps the focused item. The R3 prompt names the action: **Ver em grade** (view as grid) in the list, **Ver em lista** (view as list) in the grid.

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

Press **Select/View** (or Ctrl+F) inside a folder, type part of the name on the on-screen keyboard and press **Done**. Case and accents don't matter ("relatorio" finds "Relatório"). Results appear as they are found; the line above the list says whether the list is **partial** (still searching or cancelled), **complete**, or stopped at the 10,000-result limit, and how many folders could not be read (no permission). North → **Other search actions** → **Skipped folders** lists them. Nothing is indexed: only the folder you are in is read, when you search.

On the **home screen**, **Select/View** searches the main folders (Downloads, Documents, Desktop, Pictures, Videos and Music) at once, with the same rules; **East/B** cancels and, on the results, returns home. In **Meu computador** (This PC), search looks in the focused drive.

**Empty lists** say what to do next: "Lixeira vazia" (Recycle Bin empty), "Compactado vazio" (empty archive), "Pasta vazia — Y Ações: Colar / Nova pasta" (empty folder — paste / new folder; paste only when something is copied) and, for a search with no results, the filters and a new search, with the active controller's buttons.

**Errors** show in plain language with what to do (not found, access denied, in use by another program, disk full, path too long, device not ready). The original Windows text stays on a **Detalhes técnicos** (technical details) line and in the local log, for reporting a problem.

- **Subfolders:** included by default. Change it in Menu → Configurações → "Search in subfolders" (for the next search) or North → **Other search actions** → "Subfolders" on the results (searches again).
- **East/B** while searching stops it and keeps the partial results; East/B again goes back to the folder.
- **South/A** on a result opens its folder with the item focused; Back returns to the results.
- **Filters:** North on the results opens the filters. **South** toggles a type (Folders, Images, Videos, Music and audio, Documents, Archives, Executables; several types combine) or steps through **Size** and **Modified** ranges; the menu stays open so you can pick several. The list updates right away without searching again, the line above the list shows "N of M results (filters: …)", and the filters stay on for later searches in this session until **Clear filters**.
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

Mark items with West (or focus one) → North → **Compress…**. Choose the name (on-screen keyboard), ZIP, TAR.GZ or 7z (smaller, slower to create; opens in 7-Zip and in the current Windows 11 File Explorer) and the compression level, then **Compress**. The archive is written to a temporary file and only appears when finished; an existing file is never overwritten (the name gets "(2)"). Links and junctions inside folders are skipped and listed in the result. RAR can't be created (proprietary format).

## Operations center

Menu → **Operations** lists every copy, move, delete, extraction and compression of the session in two groups: **Em andamento** (in progress: an icon for the state — running, paused, waiting for you — plus the percentage and items, updating while the menu is open) and **Histórico** (history: finished ones, with a success, warning or error icon). Select one to see its progress or result, or to cancel it while it runs. While anything runs, a thin bar along the bottom edge of the title strip shows the overall progress, and the title strip shows the current operation with its percentage.

**When an operation finishes:** a copy, move or delete without any problem doesn't open a dialog: a notice in the bottom-right corner says what happened ("3 itens copiados · Menu → Desfazer") and fades after about 3 seconds. Warnings, failures and cancels still open the result dialog. Either way the result stays in Menu → Operations. An operation that **failed**, **finished with warnings** or was **cancelled** offers **Retry**: ControlFS plans the original request again from scratch (items that no longer exist at the source are left out; what already reached the destination goes through the usual conflict questions). Password-protected archives ask for the password again.

When only some items failed or were not processed (for example after a cancel or a locked file), the result dialog and the operation's details also offer **Retry failed items (N)**: only those items run again, each into the folder it was meant to reach; what already succeeded is never copied, moved or extracted again, and the new result lists only the retried items. Entries blocked for security are never retried.

**Pause and resume:** copies, moves and deletes show **Pause** in their details while running; the operation stops at the next safe point (between items, and between blocks inside a file being copied), so disk activity stops within about a second. **Resume** continues from the current item; **Cancel** also works while paused and removes the partial copy. A paused operation holds the queue: the next operations wait until it resumes or is cancelled. Pausing only lasts while ControlFS is open. Extraction and compression can't pause safely yet, so they don't offer it. Moving within the same drive and deleting a whole folder (to the Recycle Bin or permanently) are single steps and finish before the pause takes effect.

**Undo and redo:** Menu → **Undo: …** reverses the most recent reversible operation of this session, and the notice shown when a copy or move finishes points to it. Only operations with a safe inverse qualify: a **rename** goes back to the old name, a **move** goes back to where the items were, a **copy** is removed (to the Recycle Bin when the drive has one) only if it is still unchanged and the original still exists, and items sent to the **Recycle Bin** are restored. Before touching anything, ControlFS checks every item; if something changed since (the old name or place is taken, the copy was edited, the item left the Recycle Bin), nothing is done and the reason is shown. Permanent deletes, replacements, folder merges and operations that finished with warnings are never offered. **Redo: …** repeats the undone operation. Undo covers operations from this session only.

**History:** finished operations (and renames) are kept in `history.json` in the data folder (`%LOCALAPPDATA%\ControlFS`, or `ControlFS_Data` in portable mode), so Menu → Operations also lists operations from earlier launches, newest first, with date, source, destination and per-item outcome counts. It keeps the 200 most recent operations and up to 100 items each (problems first). It records what happened, never file contents or archive passwords, and by itself doesn't mean an operation can be undone. **Clear history…** at the end of the list erases the record (no file is touched).

If ControlFS is closed in the middle of an operation (crash, power loss), the next launch removes the hidden temporaries it had created (`.controlfs-staging-*`, `.controlfs-copy-*.part`) and shows a notice saying so. Only items ControlFS registered before creating them are removed; nothing is deleted just because of its name.

## Image preview

**South** on a JPG, PNG, GIF, BMP or WebP image opens it inside ControlFS (North → **Open with the default app** still opens it in Windows). **Left/Right** or **LB/RB** go to the previous/next image in list order; **RT** zooms in and **LT** zooms out; while zoomed, the D-pad moves around the image and **South** fits it back to the screen; **East/B** closes, leaving the focus on the last image viewed. The panel's prompts only list what works right now, with your controller's buttons; after 4 seconds without input they fade (Close stays visible) and any button brings them back. Images are decoded in the background (the screen never freezes) and reduced to at most 4096 px on the long side; only the first frame of an animated GIF is shown and phone photos are turned upright. Before decoding, ControlFS checks the real format from the file's content (not the extension) and refuses files over 100 MB or over 80 megapixels with a clear message. Nothing is ever executed. Images inside archives aren't previewed: extract them first. WebP needs the Windows WebP codec (built into Windows 11 and recent Windows 10).

## PDF preview

**South** on a `.pdf` shows it inside ControlFS (North → **Visualizar PDF** does the same; **Open with the default app** still opens it in Windows). **Left/Right** or **LB/RB** go to the previous/next page; **RT** zooms in and **LT** zooms out; while zoomed, the D-pad moves around the page and **South** fits it back to the screen; **East/B** closes, leaving the focus on the PDF. The header shows the page number and the zoom. A password-protected PDF says so: **South** opens the on-screen keyboard (masked; the password is never saved or suggested) and a wrong password lets you try again. Pages are drawn by Windows (`Windows.Data.Pdf`) in the background, one at a time, as pictures: links, attachments, forms and scripts never open or run. ControlFS checks that the file really is a PDF (by its content) and refuses files over 200 MB; only the first 5,000 pages can be browsed, and a page that takes more than 20 seconds to open or draw shows an error instead of freezing. PDFs inside archives aren't previewed.

## Audio playback

**South** on an MP3, WAV, WMA, M4A, AAC, FLAC, OGG or Opus file plays it inside ControlFS (North → **Ouvir aqui** does the same; **Open with the default app** is still there). **South** pauses and resumes (at the end it starts over), **Left/Right** skip back/forward 10 seconds, **LB/RB** one minute, **Up/Down** change the volume and **North** mutes/unmutes. The panel shows whether it's playing, the elapsed and total time, a progress bar and the volume. **East/B** closes: the sound stops right away and the focus stays on the file. Playback uses only the codecs installed in Windows (Media Foundation): MP3, WAV, WMA, M4A/AAC and FLAC work on Windows 10/11; OGG and Opus need the Microsoft Store media extensions (Web Media Extensions). A file Windows can't decode shows a clear message instead of playing. The file is read locally as a stream, never sent anywhere, and an executable renamed to `.mp3` is refused. Audio inside archives isn't played.

## Video player

**South** on an MP4, M4V, MOV, WMV, AVI, MKV, WebM or 3GP file, from the list or the grid, plays it full screen (North → **Assistir aqui** does the same; **Open with the default app** is still there). The window goes full screen while the video is open and comes back when you leave (if you had chosen full screen yourself, it stays; F11 during the video leaves full screen just for that video). An overlay with the title, elapsed and remaining time, a progress bar, the volume and the subtitle/audio track state appears on any button and fades after 3 seconds of playback; while paused it stays.

- **South:** pause/resume (at the end, watch again).
- **Left/Right:** 10 seconds back/forward; **LB/RB:** 1 minute; **LT/RT:** 5% of the video (the timeline). Presses add up: the target time shows in large type and as a white mark on the bar, and the jump happens when you stop pressing (**South** jumps right away, **Back** cancels). It never goes before the start or past the end.
- **Up/Down:** volume. **North:** subtitles (off, embedded tracks, or an `.srt`/`.vtt` file with the same name next to the video), audio track (when there's more than one), mute, start over and **Esquecer onde parei** (forget where I stopped). Options the video doesn't have are disabled with the reason.
- **Back:** hides the overlay first; pressed again, returns to the list with the focus on the video.

Subtitles use the Windows caption style (Settings → Accessibility → Captions). When you leave a video partway (more than 30 seconds in and more than 30 seconds before the end), ControlFS remembers where you stopped; opening the same file again asks **Continuar de 12:34** (resume) or **Começar do início** (start over). The position belongs to that exact file (its path, size and date): another file with the same name, or the same file after being edited, starts from the beginning. Only a digest and the seconds are stored, never the name; Configurações → **Apagar onde os vídeos pararam** erases them all.

Playback uses only the codecs installed in Windows (Media Foundation). H.264/AAC MP4 and M4V, WMV and most AVI files play on any Windows 10/11; HEVC (H.265), VP9, AV1, MKV and WebM may need the video extensions from the Microsoft Store. A file Windows can't play shows a clear message and **Back** returns to the list. Videos inside archives aren't played. Nothing is streamed, and no DRM-protected media is supported.

## Text preview

**South** on a text file (.txt, .md, .log, .json, .xml, .csv, .ini, .yaml, source code…) opens it read-only inside ControlFS; for any other file, North → **Visualizar como texto** (view as text). That includes scripts such as .ps1 or .bat, which South would ask to run: reading them never executes anything. **Up/Down** scroll one line, **LT/RT** a page, **LB/RB** jump to the start/end, **Left/Right** shift long lines sideways, **South** switches between a fixed-width and a proportional font, **East/B** closes. The encoding is detected (UTF-8 with or without BOM, UTF-16, otherwise the Windows ANSI code page) and shown with the line count. Only the first 2 MB and 10,000 lines are read; a larger file shows a clear "partial preview" notice. Binary files are refused with a message. Files inside archives aren't previewed.

**Editing:** for a text file up to 1 MB shown in full, **North** in the preview starts editing (the header says "Editando"). The focused line is highlighted: **Up/Down** (and LT/RT, LB/RB) pick the line, **South** opens it on the on-screen keyboard (Concluir puts it back), **North** offers insert a line below/above, delete the line, undo, save and leave editing, and **Start** saves. Saving asks first, showing how many lines changed and the name of the backup; then the file is swapped in one step (written to a temporary file in the same folder and replaced at once, so it's never left half-written), keeping its encoding, BOM, line endings and Windows attributes. The previous version stays next to it as `name.controlfs.bak` (replaced on each save). **Back** without changes leaves editing; with changes it asks before discarding, starting on **Continuar editando**. If another program changed the file after you started, saving says so and only replaces it if you choose **Substituir mesmo assim**. Binary files, read-only files, files over 1 MB or over 10,000 lines, and files whose encoding wouldn't be written back identically can't be edited here (the reason is shown); use the default app.

## Disk images (ISO, IMG, VHD, VHDX)

North on an `.iso`, `.img`, `.vhd` or `.vhdx` file → **Montar imagem** (mount image) uses Windows' own mounting, the same as Explorer's "Mount" (no extra drivers), and opens the new drive when it appears. ISO/IMG images are mounted read-only; VHD/VHDX virtual hard disks need ControlFS running as administrator, as in Windows. If an image is already mounted, ControlFS just opens its drive. Errors are explained (not a disk image, damaged, compressed or sparse file, in use by another program, permission).

To eject it, go to **Meu computador** (This PC) or the home screen, North on the drive → **Desmontar imagem…** (unmount image). This works for images mounted by ControlFS or by Windows. The confirmation starts on **Cancelar** and warns that programs with files open on the drive lose access to them; the image file itself is never changed. Tabs showing that drive go back to This PC.

## Opening files with Windows

**South** on a file that isn't an archive, a previewable image, a PDF, a playable audio or video file or a text file opens it in the default Windows program. North on a file also offers **Open with…** and **Show in File Explorer**. The other program may not work with the controller: come back with Alt+Tab or the system button. Programs and scripts (.exe, .msi, .bat, .ps1, .lnk…) ask first, starting on **Cancel**. Nothing is ever opened automatically after extracting.

**Game shortcuts and shortcut icons.** Steam game shortcuts (`.url` files that open `steam://…`, like the ones Steam puts on the Desktop) show the game's title without `.url` (e.g. "Valheim"), the type **Jogo da Steam** and the icon the shortcut declares (the game's own `.ico` in the local Steam folder); if that icon is missing, ControlFS looks for the same file in the local Steam installation, and otherwise shows a game symbol. The details panel and Properties keep the real file name, what the shortcut opens (`steam://rungameid/…`) and its real type. **South** (or North → **Jogar…**) asks first, starting on **Cancel**, and then hands the shortcut file itself to Windows, which passes it to Steam; without Steam installed you get a readable error. Ordinary website shortcuts (`https://…`) stay regular `.url` files. Windows `.lnk` shortcuts show their own icon (the one they declare, or their target program's) instead of a blank document. Icons are only read from local paths on fixed drives: a shortcut that points its icon at a network share (`\\server\…`), a web address or a link is shown with the generic symbol and never touched.

## Terminal

North → **Abrir terminal aqui…** (open terminal here) opens **Windows Terminal** in the current folder, or **Windows PowerShell** if Windows Terminal isn't installed. It asks first, starting on **Cancelar**, because the terminal is a Windows program outside ControlFS: it doesn't work with the controller and needs a keyboard. **Abrir terminal e o teclado virtual do Windows** also opens Windows' on-screen keyboard (ControlFS' own keyboard only works inside ControlFS). Nothing runs by itself: the terminal just opens in the folder, waiting for you. The terminal is external on purpose; see `docs/decisions/0009`.

## Updates

The **installed** version updates itself:

1. Once a day at most, it asks GitHub for the latest release of `nextestudios/ControlFS` (stable only, or also pre-releases if you are on one).
2. It downloads the installer in the background and only accepts it if the **release manifest is signed with the project key** and the file's **SHA-256 and size** match. Same or older versions are refused.
3. It offers **Install and restart**. Postpone it and it installs silently when you quit. Nothing happens while a copy or extraction is running.

Menu → Configurações → **Updates**: check now, automatic check on/off, install on quit on/off, pre-releases (automatic / yes / no).
The **portable** version only tells you a new version exists; download it from the release page.

## Screen readers

With Narrator (or another UI Automation screen reader) on, the app announces where the focus is and the focused item as you move with the controller or keyboard: the home screen, folder, menu, dialog or on-screen keyboard when you enter it, then just the item as you move (name, type, size and position such as "3 of 20"). States are spoken in words: marked, cut, blocked (with the reason), password-protected, unavailable (with the reason). Notices (the pop-up in the bottom-right corner) and the line above the list are read without moving the focus. Nothing depends on sound, vibration or color alone.

## Privacy

Everything stays on your PC. Settings and logs live in `%LOCALAPPDATA%\ControlFS` (installed) or in `ControlFS_Data` next to `ControlFS-Portable-x64.exe` (portable). Passwords are never saved or logged. Core features never use the network. The only internet access is the update check, which sends nothing but a `ControlFS/<version>` User-Agent to GitHub and can be turned off. Connecting a phone (above) listens on your local network only while you use it.

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
