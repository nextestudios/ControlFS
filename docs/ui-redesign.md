# UI redesign: inventory and regression contract

The maintainer's redesign turns the single list screen into a shared shell (header, top navigation bar, content,
footer) with two official content modes: **Grid** (visual, fast navigation) and **List** (information, with a details
panel). It is a refactor of the existing UI, not a new window: `AppController`, the semantic actions and every backend
stay the same.

This file is the contract for the redesign phases. **Nothing in the matrix below may disappear, stop working, become
mouse-only or lose its test.** A later phase that moves a feature updates its "Where" cell in the same pull request.

| Phase | Scope | Status |
|---|---|---|
| A1 | This inventory, design tokens, footer (prompt colors, order, Lista/Grade), R3 → ChangeView | done |
| A2 | Shared header (logo on every screen, tabs next to it) and top navigation bar (breadcrumb + quick access) | done |
| B1 | Grid Home cards ("Pastas principais" with real counts/sizes, "Unidades e dispositivos" with usage bars, Favoritos, Outros locais), This PC view | done |
| B2 | Grid tiles restyled as cards (icon · name · type/size · states or "em <pasta>" · chevron) in folders, search, archives and Recycle Bin; columns from the available width | done |
| C1 | List mode: rows in a card with column header (mark box · Nome · Tipo · Tamanho · Modificado em), sort arrow, friendly dates, real sizes of main folders on Home, new-location focus rule, taller footer/top bar at 1080p+ | done |
| C2 | Details panel on the right of the list (folder, file, image thumbnail, archive, drive, archive entries, Recycle Bin items, marked summary), async and only for the focused item while visible; collapses on handhelds/narrow windows | done |
| M (#172) | One modal system for every menu, dialog, keyboard, picker menu, operation/result, About, controller screens and preview: dimmed page, frosted panel (solid fallback), header with icon/title/context, full-width option rows with icons, filled focus, section headings, prompts inside the panel | done |

Legend for the matrix: **Where** is the place in the new shell (after the phase in brackets). **Test** names the automated
test that protects the behavior (`File::Method`, unit tests under `tests/ControlFS.UnitTests`, Windows-only ones under
`tests/ControlFS.WindowsIntegrationTests`), `UIA` for `build/Test-UiAutomation.ps1` (Smoke workflow), `Screens` for the
`--render-screens` captures (Smoke workflow) or **Manual** with the `docs/TESTING.md` section. "Manual only" rows are
hardware, visual or timing checks that CI can't prove.

## Shell

```
┌ [ControlFS logo with text] [tabs, only with 2+]                  [controller · operation] ┐  (system title bar above)
├ [L1] [Locais|Meu computador] › segment › … › current │ Favoritos · Arquivos recentes · known folders · Meu computador · Lixeira [R1]
├ badge line (archive summary, search, recycle bin, picker title) — only when there is something to say
├ CONTENT: Grid (B: Home = card sections; elsewhere tiles) or List, each + details panel (C; grid #177)
└ status line · prompts: A Abrir · B Voltar · X Marcar · Y Ações · Menu · Buscar · R Lista/Grade
```

- **Window chrome** stays the system title bar (min/max/close, resize, move, F11 full screen). The reference draws the
  logo inside the title bar; doing that needs `ExtendsContentIntoTitleBar` plus pass-through regions for the tab strip,
  which can't be verified without a Windows desktop session, so the logo sits in the first content row instead.
- **Header**: official logo with text (`assets/controlfs-logo-text-900.png`, copied as `controlfs-logo.png`) on every
  screen; the icon-only logo stays for the exe, taskbar and installer. Right side: active controller and running
  operation/update/clipboard status (unchanged texts).
- **Top navigation bar (A2)**: left segment = breadcrumb with a root chip (`Locais > Início` on Home; `Meu computador >
  C: > …` on disk and archive paths; `Locais > Lixeira` / `Locais > Busca: …` elsewhere); right segment = quick access.
  Icons come from `IIconProvider` (Windows shell icons for known folders, This PC and the Recycle Bin; Segoe Fluent
  Icons glyphs for Favorites/Recent, never emoji).
- **Footer**: prompts from `AppController.Prompts` (`ControllerPromptProvider`), status line above them.

### Controller model of the shell

| Input | Home | Browser (list region) | Top bar focused | Tab strip focused (2+ tabs) |
|---|---|---|---|---|
| LB / L1 / Ctrl+← | top bar, first quick-access item (A2) | top bar, parent folder segment (#30) | previous target (#176) | previous tab |
| RB / R1 / Ctrl+→ | top bar, first quick-access item (#176) | top bar, first quick-access item (#176; was: tab strip) | next target (#176) | next tab |
| Left / Right | grid: move; list: — (Right opens) | grid: move; list: up / open | previous / next target (current folder and active shortcut skipped) | switch tab |
| Up | move | move | tab strip, when 2+ tabs (#176) | — |
| LT / RT | page | page | first / last target of the segment | first / last tab |
| South | open place | open item | go to segment / open quick access (never the current folder) | back to content |
| North | place actions | item actions | full path menu (segments only) | new/close/switch tab |
| East | exit dialog | back (selection → search → history → Home) | back to content | back to content |
| Down | move | move | back to content | back to content |
| Start / F10 | app menu | app menu | app menu | app menu |
| Select / Ctrl+F | — | search (disk folders) | — | — |
| R3 / Ctrl+G | Lista ↔ Grade | Lista ↔ Grade | — | — |
| L3 / Tab | other pane (two panes, #56) | other pane (two panes, #56) | — | — |

## Design tokens (A1)

`src/ControlFS.App/Resources/Theme.cs`. Sizes still scale with `LayoutProfile` (#36: compact / regular / large).
Colors come from `ControlFS.Core.Appearance.ThemePalettes` (#37): the values below are the **dark** theme with the default
**cyan** accent; the light theme and the other accents are in the same file, and `Theme.Apply` recolors the shared brushes
live. Contrast targets for every theme × accent: `ThemeContrastTests`.

| Token | Value | Use |
|---|---|---|
| `Background` | `#06101A` | window |
| `Surface` | `#0A1623` | header, top bar, footer |
| `SurfaceRaised` (card) | `#0F1D2A` | cards, menus, dialogs, keys |
| `AccentSoft` (card focused) | `#123B5A` | focused item fill |
| `Border` | `#17364D` | separators, card borders |
| `Accent` (focus) | `#11C7FF` | focus border, caret, highlighted symbols |
| `Primary` | `#079CFF` | usage bars, active location |
| `Text` / `TextMuted` / `TextDisabled` | `#F5F8FC` / `#A4B4C8` / `#607286` | primary / secondary / muted text |
| `FocusGlow` | `#11C7FF` at 33% | 2 px halo around the focus ring (`Theme.WithGlow`) |
| `Selected` | `#F2C14E` | marked items (mark bar, check, "Marcado"): focus ≠ marking |
| `Danger` | `#FF7A6E` | always with a warning symbol ("⚠" text, or the warning icon in modals), never color alone |
| `Radius` | 8 | cards, rows, chips |
| `FocusRing` | 2 px (× space scale) | same thickness focused or not (no layout jump) |
| `MotionFast` / `MotionFocus` / `MotionPanel` | 120 / 160 / 200 ms | only `BrushTransition` on focus fills for now |
| Xbox faces | A `#2EB34A`, B `#E5393F`, X `#2C7FE8`, Y `#F4C42F` | glyph body; Xbox family only (other families keep their shapes) |

### Modal tokens (#172)

| Token | Value | Use |
|---|---|---|
| `ModalScrim` | `#02070C` at 71% (91% when solid) | page behind any modal |
| `ModalPanel()` | acrylic, tint `#0C1B2A` at 82%, fallback `#0D1C2B` | panel material; solid brush when `Theme.SolidSurfaces` |
| `ModalEdge` / `ModalDivider` / `ModalInset` | white at 22% / 14% / 10% | thin light edge, separators, info and field boxes |
| `ModalRadius` / `RowRadius` | 22 / 12 (× layout scale) | panel / option rows, keys, info boxes |
| `FocusFill` + `FocusText` | `#11C7FF` + `#03101A` (≈9:1) | focused option and key: filled, SemiBold, scaled 1.025 (keys 1.06) |
| `DangerFill` | `#FF8A7F` | focused destructive option (dark text); unfocused: red text + trailing warning symbol |
| `DisabledFill` | `#2A3E52` | focused unavailable option (muted text + "Indisponível: reason") |
| `Success` / `Warning` / `Danger` | `#4CD98A` / `#F2C14E` / `#FF7A6E` | header badge tone (result, warning, error) |
| `MotionModal` | 120 ms, opacity only | new modal only; off with Windows animations off; never delays input |

Icons: `ActionIcon` (Core) is the only map from meaning to symbol (`ActionIcons.Glyph`, Segoe Fluent Icons / MDL2);
`ActionIcons.IsDestructive` (Delete, DeleteForever, Erase) drives the red style and the "never initial focus" rule
(`AppController.SafeInitialFocus`). `MenuItem.Icon`/`Section`, `DialogOption.Icon`, `Modal.Icon`/`Subtitle` are set by
`AppController`; views never pick icons. Menus (#193): `MenuItem.Placement = Quick` (set by `AppController`) puts an
option in the quick-action grid (≤ 4 tiles per row, destructive tiles last, `ShortLabel` under the icon, full label for
Narrator/UIA); the rest is a compact list (40 px rows, detail only on the focused row; with a grid, groups are separated by a divider
only, without titles). Menu panel: a fixed width per menu (#227, `MenuModal.PanelWidth`: 540 px with a grid, 460 without; min = max, both capped by the window), so focus and the focused row's detail never resize it (long text wraps); `MenuPadding` 20, compact header. Configurações uses per-section grids (#227, `sectionGrids: true`): short independent settings are tiles in their group's grid showing icon, `ShortLabel` and `MenuItem.Value` (the current value); long-description settings and submenus stay list rows under the grid. `MenuItem.KeepOpen` (Configurações) applies a setting and keeps the
menu open (`MenuModal.Reload`). The retained panel (#191) swaps only the tile/row that loses and gains focus. `Theme.SolidSurfaces` comes from Windows transparency effects off or high
contrast (`UISettings.AdvancedEffectsEnabled`, `AccessibilitySettings.HighContrast`); `Theme.ReduceMotion` from
`UISettings.AnimationsEnabled`.

## Regression matrix

### Input, controllers and prompts

| Feature | Where | Shortcut / flow | Test |
|---|---|---|---|
| Semantic actions only (screens never see buttons) | all | `InputAction` → `AppController.Handle` | all journey tests (`Driver`) |
| Default map by physical position; confirm/back convention swaps behavior and prompts | Menu → Configurações → "Confirmar com" | South/East | `InputRouterTests::East_confirms_convention_swaps_behavior_by_position`, `PromptJourneyTests` |
| R3 (right stick click) → Lista/Grade | footer "R Lista/Grade" | R3, Ctrl+G, Menu → Configurações → Exibição | `PromptJourneyTests` (prompt + button), `GridViewJourneyTests`, `DensityJourneyTests` |
| Confirm/menus never repeat; navigation repeats with acceleration | — | hold | `InputRouterTests::Confirm_fires_once_per_press_and_never_repeats`, `::Navigation_repeats_after_initial_delay_with_acceleration` |
| Held button latched across context change | — | — | `InputRouterTests::Held_button_is_latched_on_context_change_until_released`, `::Latched_navigation_does_not_repeat_into_new_context`, `::Context_change_triggered_by_a_repeat_keeps_the_control_latched` |
| Active device, hot swap, sensitive-context lock | footer header status | press on another controller | `InputRouterTests::Another_device_takes_over_only_with_a_new_press_while_the_active_one_is_idle`, `::Sensitive_context_blocks_automatic_device_takeover` |
| Suspension when the window loses focus | — | Alt+Tab | `InputRouterTests::Suspended_router_ignores_input_and_resume_requires_new_press`; Manual (TESTING "Antes de cada release") |
| Stick deadzone, hysteresis, diagonals | — | left stick | `StickNormalizerTests` (5 tests) |
| Active controller menu, duplicates (Steam Input/DS4Windows) | Menu → Configurações → Controle ativo | Start | `ActiveControllerJourneyTests`, `InputRouterTests::Explicitly_selected_device_is_the_only_one_routed_until_automatic_or_removed`; Manual "Controle ativo e duplicatas (#80)" |
| Controller test screen + report | Menu → Configurações → Teste de controles… | hold South/East | `ControllerTestJourneyTests`; Manual "Teste de controles (#78)" |
| Raw joystick wizard, profiles import/export, two-button long press | Menu → Configurações → Controles sem perfil… | — | `ControllerMappingWizardTests` (5), `ControllerMappingJourneyTests` (3), `ControllerProfileSerializerTests` (3); Manual "Joystick sem perfil (#79)" |
| Family detection and label style (automatic/generic/Xbox/PS/Nintendo) | Menu → Configurações → Legendas | — | `ControllerFamilyTests` (3), `PromptJourneyTests` |
| Gyro aiming on the on-screen keyboard (#77, experimental, off by default): pointer layer over key focus, no wrap, D-pad re-anchors, R3 recenters; sensor only enabled with the setting | Menu → Configurações → Mira por giroscópio no teclado | turn/tilt controller; R3 | `GyroPointerTests` (2), `GyroKeyboardJourneyTests`; Manual "Mira por giroscópio (#77)" |
| Light/dark theme (automatic follows Windows live) and accent color presets, applied live and persisted; contrast checked for every combination | Menu → Configurações → Tema / Cor de destaque | — | `ThemeContrastTests`, `AppearanceJourneyTests`; Screens `7-light-*`, `7d`, `7e`; Manual "Tema claro e cor de destaque (#37)" |
| Stable menu width + per-section settings grids (#227): tiles with label and value, 2D navigation between grids and lists | Menu → Configurações | D-pad; click a tile | `ModalSystemJourneyTests::Settings_grids_per_section_navigate_in_two_dimensions_with_the_lists_between_them`; Screens `4`/`4a`, `4b`/`4c` (same modal width); Manual "Largura estável e grades em Configurações (#227)" |
| Right-stick scrolling (#175): active surface only (list/grid rows, menus, text lines/columns, zoomed image, dialog/About body); deadzone + hysteresis, proportional rate, sustained acceleration, instant stop; R3 press and context change latch until center; never steals the active device | all lists and modals | tilt right stick | `AnalogScrollerTests` (3), `InputRouterTests::Right_stick_scroll_never_takes_over_…`, `RightStickScrollJourneyTests`; Manual "Rolagem com o analógico direito (#175)" |
| Dynamic footer prompts, hot swap, keyboard keys when typing on a physical keyboard | footer | — | `PromptJourneyTests`, `HintJourneyTests` (4), `JourneyTests::Footer_hints_only_show_actions_that_work_in_context` |
| Footer order (A1): Confirm, Back, Mark, Actions, Menu, Search, Lista/Grade, L1, R1 (screens only; modals keep theirs); L1/R1 glyphs at the ends of the top bar while the list has focus (#176) | footer, top bar | — | `PromptJourneyTests` |
| Xbox face colors in glyphs (A green, B red, X blue, Y yellow) | footer, glyph gallery | — | Screens (`glyphs/`); Manual "Glifos dos botões" |
| Glyphs per family (PS shapes, Nintendo A/B by position, generic dots, never fake Xbox) | footer | — | `ControllerFamilyTests::Glyph_letters_and_spoken_names_follow_position_per_family`, `InputRouterTests::Glyphs_follow_physical_position_not_letters`; Screens |
| Physical keyboard map | all | arrows, Enter, Esc, Backspace, Space, F2/Menu key, F10, PgUp/PgDn, Home/End (typing), Ctrl+←/→, Ctrl+F, Ctrl+G, Ctrl+A/Ctrl+V (typing), F11 | UIA (F10, arrows, PageDown, Esc, Ctrl+F); Manual for the rest |
| Mouse/touch: item, segment, tab, menu option, dialog option, key | all | click/tap → same action as South | Manual "Barra de caminho (#30)", "Abas (#50)" |
| Guide/Home button never mapped | — | — | `SdlControlMapping` (code review) |

### Screens and navigation

| Feature | Where | Shortcut / flow | Test |
|---|---|---|---|
| Home with places (favorites, Recentes, known folders, drives, Lixeira) | List: rows as before. Grid (B1): sections Favoritos · Pastas principais · Unidades e dispositivos · Outros locais (Recentes, Lixeira) | South opens, North actions, 2D per section, LT/RT = section | `FavoritesJourneyTests`, `RecentsJourneyTests`, `RecycleBinJourneyTests`, `DriveJourneyTests`, `HomeGridJourneyTests`; Screens `1c`, `1d`; Manual "Início em grade e Meu computador (redesenho, fase B1)" |
| Real item count and recursive size on main folder cards ("Calculando…", async, cancelled when leaving Home, one folder at a time, 20 s budget with "+", cached 10 min) | Home grid (B1); Home list "Tamanho" column (C1) | — | `HomeGridJourneyTests::Main_folder_cards_show_real_counts_…` |
| Drive cards: label, usage bar, "X livres de Y", file system (pt-BR numbers) | Home grid, This PC (B1) | — | `HomeGridJourneyTests::Home_sections_…`; Manual (B1 section) |
| This PC (Meu computador): drives in a browser tab, with history; drive menu and properties (capacity, free, used, file system) | quick access / path root → tab (B1; was: Home on the first drive) | South, Back, North | `HomeGridJourneyTests::Home_sections_…`, `TopBarJourneyTests`; Screens `1e`, `1f` |
| Real known folders (Downloads via Known Folder API) and drives with type/label/free space | Home, top bar (A2) | — | `WindowsBehaviorTests::Downloads_comes_from_known_folder_api`, `::Drives_are_listed_as_places`; Manual "Tipos de unidade (#25)" |
| Network locations Windows already knows: mapped drives (connected or "desconectada", share shown, no volume query) and "Locais de rede" shortcuts to UNC shares; network symbol only (no Shell icon request); never probed on the UI thread (#27) | Home "Unidades e dispositivos", Meu computador | South opens (async, Back cancels) | `NetworkLocationIntegrationTests` (2), `NetworkPathPolicyTests`; Manual "Locais de rede (#27)" |
| Drive plugged in while open, focus kept | Home | — | `DriveJourneyTests`, `DriveWatcherIntegrationTests` |
| Browse real folders, open, up (Left in list), history Back with focus restore | content | South/Right, Left, East | `JourneyTests::Back_semantics_selection_then_history_then_home_then_confirmed_exit`, `BreadcrumbJourneyTests` |
| Stale listing never overwrites a newer navigation | — | — | `JourneyTests::Late_listing_response_does_not_overwrite_newer_navigation` |
| Focus by identity, survives resort/removal; focus ≠ selection | content | — | `StateTests` (5) |
| Breadcrumb / path bar with archive boundary and collapse, root chip (Locais / Meu computador); the current folder is a label, never a target (#176) | top bar left segment (A2) | L1, L1/R1, Left/Right, South, North = full path | `BreadcrumbJourneyTests` (3), `TopBarJourneyTests` (2); Manual "Barra de caminho (#30)", "Barra superior com L1/R1 (#176)" |
| Quick access: Favoritos, Arquivos recentes, known folders, Meu computador (B1: opens This PC), Lixeira; the active shortcut is skipped (#176) | top bar right segment (A2) | R1 (or L1 then Right), South | `TopBarJourneyTests` (2), `HomeGridJourneyTests`; Manual "Barra superior e cabeçalho (redesenho, fase A2)" |
| Tabs (8 max), each with its own folder/history/marks/focus; strip only with 2+ tabs, no standalone prompt (#176) | header tab strip (A2) | Up from the top bar, L1/R1, North new/close/switch, Menu → Abas, "Abrir em nova aba" | `TabsJourneyTests`, `ModalSystemJourneyTests`; Screens `2g-folder-tabs`; Manual "Abas (#50)" |
| Open tabs (2+) restored on launch, missing folder = "(indisponível)" tab showing Home with a notice, checked off the UI thread (#51) | settings `OpenTabs`/`ActiveOpenTab` | automatic; Menu → Configurações → "Restaurar abas ao abrir" turns it off | `TabsJourneyTests::Open_tabs_are_restored_on_the_next_launch_…` |
| Reopen closed tab: last 10 closed tabs (location + history, no marks), back at their position | Menu → "Reabrir aba fechada", Menu → Abas, North on the strip (#52) | — | `TabsJourneyTests::A_closed_tab_reopens_at_its_place_…` |
| Duplicate tab: same location, focus and a copy of the history, no marks (search/Lixeira → origin folder) | North on the strip, Menu → Abas (#53) | — | `TabsJourneyTests::A_duplicated_tab_copies_location_history_and_focus_…` |
| Go to path (typed/pasted, quotes, %VARS%) | Menu → Ir para caminho… | Start | `GoToPathJourneyTests`, `TypedPathTests` |
| Go to folder above… | Menu | Start | `BreadcrumbJourneyTests` (same menu as the `…` segment) |
| Go home | Menu → Ir para o início; top bar root chip "Locais" (A2) | Start / LB | `JourneyTests::Back_semantics_…`, `TopBarJourneyTests` |
| Favorites: add/remove/reorder, missing kept until removed, first on Home and in the picker | Home, item actions, top bar (A2) | North → Adicionar aos favoritos | `FavoritesJourneyTests` |
| Recents: bounded, persisted, clear, turn off | Home "Recentes", Menu → Configurações → Recentes, top bar (A2) | North on Recentes | `RecentsJourneyTests` |
| Recycle Bin: list, restore (never overwrite), permanent delete asks on Cancel | Home, top bar (A2) | South/North on an item | `RecycleBinJourneyTests`, `RecycleBinIntegrationTests` (2), `UndoJourneyTests::Undo_of_a_recycle_…` |
| Search (on-screen keyboard, streaming, partial/complete, skipped folders, cancel keeps partial) | results in the current mode | Select/View, Ctrl+F | `SearchJourneyTests` (2), `SearchIntegrationTests` (3); Manual "Busca (#46)" |
| Search filters (type/size/date), subfolders toggle | North on results | — | `SearchFilterJourneyTests` |
| OneDrive files-on-demand folders searched without downloading | — | — | `SearchIntegrationTests::Reparse_tag_…`; Manual "OneDrive sob demanda (#126)" |
| Grid view with 2D navigation, persisted; switching keeps focus and marks (no re-read; also inside archives and in search results: `ListModeJourneyTests::Switching_views_…`); cards with responsive columns (B2: comfortable 3 at 1080p, 2 handheld, 1 narrow, 4 on 4K TV; compact one more; counted from the width left by the details panel, #177: 2 at 1080p with it) | Menu → Configurações → Exibição, R3, Ctrl+G | — | `GridViewJourneyTests`, `HomeGridJourneyTests::Home_sections_…` |
| Density comfortable/compact, persisted (C1: tall/short rows with the same columns; the type column drops first when narrow) | Menu → Configurações → Densidade da lista | — | `DensityJourneyTests`; Screens `3-folder-compact` |
| Sort by name/type/size/date, ascending/descending, natural sort | Menu → Configurações → Ordenar por / Ordem; list column header shows the arrow and sorts on click (C1) | Start; mouse on a column title | `StateTests::Natural_sort_orders_numbers_numerically`, `::Focus_survives_resort_by_identity`, `ListModeJourneyTests::Column_header_follows_…`; Screens `2c` |
| Details panel: folder (path, recursive count/size, 250 ms debounce, 20 s budget, cached 10 min, cancelled on focus change), file, image (header + thumbnail through `IImageDecoder` with `PreviewLimits`), archive (format by content; file count only for ZIP/7z, 3 s budget), drive (file system, capacity, free, used, usage bar), archive entries, Recycle Bin items, marked summary | List and grid (incl. Home/This PC cards), right side (C2, grid #177); automatic = shown where it fits (list: name stays legible; grid: ≥ 2 columns), hidden on handheld/narrow (`MainWindow.DetailsLayout`) | Menu → Configurações → Mostrar/Ocultar painel de detalhes (per view, `AppSettings.ListDetails`/`GridDetails`, null = automatic) | — | `DetailsPanelJourneyTests` (4); Screens `1-home`, `1c`, `2d`, `2e`, `2f`, `3c`, `3d`; Manual "Painel de detalhes (redesenho, fase C2)", "Painel de detalhes na grade (#177)" |
| List rows: mark box (focus ≠ marking), icon, name, type ("Pasta do sistema" for Windows folders), size (real sums on Home), friendly date, chevron; compact density with the same columns | content, List (C1) | X marks; mouse on the header box = Marcar todos / Limpar | `ListModeJourneyTests::Friendly_dates_…`, `::Column_header_…`; Screens `1-home`, `2-folder`, `3-folder-compact`; Manual "Lista em colunas (redesenho, fase C1)" |
| Opening another location focuses its first item; back/up/refresh restore the item | content | South, Right, East, Left | `ListModeJourneyTests::Opening_another_location_…`, `JourneyTests::Back_semantics_…` |
| Hidden items show/hide (persisted) | Menu → Configurações → Itens ocultos | Start | Manual "Lista: estados e densidade (#28)" |
| Refresh | Menu → Atualizar | Start | journey tests that call Refresh indirectly (`FileOperationJourneyTests`) |
| Folder picker (copy/move/extract destination, create folder, other places, go to path, cancel) | full-screen picker, same shell | Start = Escolher esta pasta… | `FileOperationJourneyTests::Copy_to_a_folder_with_the_picker_and_keep_both_on_conflict` |
| Exit confirmation starts on Cancel | Home East, Menu → Sair | East | `JourneyTests::Back_semantics_…`, UIA |

### Item actions (North menus)

Since #193 the frequent actions are quick-grid tiles (folder: Abrir, Recortar, Copiar, Renomear, Compactar, Colar,
Propriedades, Excluir; file: Abrir/Executar/Jogar, the same file ops; marked items: Recortar, Copiar, Compactar, Excluir; drive: Abrir,
Nova aba, Propriedades, Atualizar); everything else stays in the list below. Initial focus is unchanged (first item passed
by `AppController`, "Extrair para" on archives). 2D grid: `ModalSystemJourneyTests::Quick_action_grid_…`.

| Feature | Where | Test |
|---|---|---|
| Folder: Abrir, Abrir no Explorador, Abrir em nova aba, Renomear, Copiar, Recortar, Copiar para…, Mover para…, Compactar…, Excluir…, favoritos, Colar, Nova pasta aqui, Marcar todos / Limpar marcação, Propriedades | Y Ações | `RenameJourneyTests`, `ClipboardJourneyTests`, `FileOperationJourneyTests`, `ShellAndCompressJourneyTests`, `DeleteJourneyTests`, `SelectAllJourneyTests`, `TabsJourneyTests` |
| File: Abrir compactado, Extrair para "x" / aqui / para…, Testar integridade, Visualizar imagem / como texto, Executar… / Abrir com o aplicativo padrão, Abrir com…, Mostrar no Explorador, file ops as above, Propriedades | Y Ações (opens on "Extrair para" for archives) | `HintJourneyTests::Archive_file_offers_explore_mark_and_extract_…`, `ShellAndCompressJourneyTests` (5), `ImagePreviewJourneyTests`, `TextPreviewJourneyTests`, `JourneyTests::Test_integrity_…` |
| Marked items: Extrair cada um (batch), Renomear em lote…, Copiar/Recortar/Copiar para/Mover para/Compactar/Excluir N, Marcar todos, Limpar marcação | Y Operações (N) | `BatchExtractionJourneyTests`, `FileOperationJourneyTests::Move_marked_items_to_a_folder`, `SelectAllJourneyTests`, `BatchRenameJourneyTests` |
| Inside an archive: Extrair tudo para "x" / aqui / para…, Extrair seleção (N) para "x" / para…, Testar integridade, Informações do compactado | Y Extrair… | `ArchiveBrowserJourneyTests` (2), `JourneyTests::Archive_is_browsed_read_only_and_back_returns_to_disk` |
| Home place: Abrir, favoritos, Mover favorito para cima/baixo; Recentes: Abrir, Limpar, Desligar | Y Ações on Home | `FavoritesJourneyTests`, `RecentsJourneyTests` |
| Recycle Bin: Restaurar, Excluir permanentemente…, Propriedades, Marcar todos, Atualizar | Y Ações in Lixeira | `RecycleBinJourneyTests` |
| Search: filters, Outras ações (Mostrar na pasta, Nova busca, Subpastas, Pastas puladas, Cancelar busca, Propriedades) | Y Filtros | `SearchFilterJourneyTests`, `SearchJourneyTests` |
| Properties with folder size on demand (cancel keeps partial, junctions not followed) | Y → Propriedades; List (C2) and grid (#177): details panel shows the real data of the focused item | `FolderSizeJourneyTests`, `FolderSizeIntegrationTests` |
| Disk usage analysis (#72): Y Ações on a folder/drive (or current folder) → Analisar uso do disco; ranked folders then files, drill down/up, open a file's folder, cancel with Back | `DiskUsageJourneyTests`, `FolderSizeIntegrationTests::Disk_usage_totals_match_…` |
| Git status (#75): Configurações → Status do Git (off by default); badge line "GIT · ramo …", row state "Git: modificado/novo…", read after the list | `GitStatusJourneyTests` |
| Open terminal here (#76): Y Ações → "Esta pasta" → Abrir terminal aqui…; notice starts on Cancelar; optional Windows on-screen keyboard | `TerminalJourneyTests` (2) |
| Two panes (#56): left = active tab, right = own `PaneState`; active pane outlined in cyan with "ATIVO" title, the other dimmed with its path; L3/Tab or click switches without touching marks; tabs stay on the left; single pane on handhelds/narrow windows (setting kept); details panel hidden | Menu → "Dois painéis"; L3/Tab | `DualPaneJourneyTests` (2); Screens `2h-dual-pane`, `2i-dual-pane-right`; Manual "Dois painéis (#56)" |
| Copiar/Mover para o outro painel (quick tiles), Extrair para o outro painel (archives), with source/destination summary; unavailable with a reason when both panes show the same folder (#56) | Y Ações (two panes) | `DualPaneJourneyTests` |
| Tab strip: Nova aba, Duplicar aba, Fechar aba, Reabrir aba fechada, Ir para a aba (2+) | North on the strip; Menu → Abas | `TabsJourneyTests` |
| Mount/unmount disk images (#73, #74): Y Ações on .iso/.img/.vhd/.vhdx → Montar imagem (opens the new drive); Y on a mounted drive (This PC, Home) → Desmontar imagem… (confirmation starts on Cancelar) | `DiskImageJourneyTests`, `DiskImageIntegrationTests` |
| Path bar: full path menu | North on a segment | `BreadcrumbJourneyTests` |

### App menu (Start)

Every entry stays reachable from the Menu (Start/F10); nothing moves out without a replacement in the same PR. Since
#193 the Menu has a quick grid (Colar, Nova pasta, Nova aba, Atualizar, Ir para caminho, Operações, Configurações, Ir
para o início) and a short list (Ir para pasta acima, Abas, Desfazer/Refazer, Esvaziar área de transferência, Sobre,
Sair); every setting moved to **Menu → Configurações** (rows marked "→ Configurações"). `ModalSystemJourneyTests::
Settings_live_in_Configuracoes_…` checks every moved entry is there; `Driver.ChooseMenu` looks inside Configurações for them.

| Entry | Test |
|---|---|
| Colar, Nova pasta, Atualizar, Ir para pasta acima…, Ir para caminho… | `ClipboardJourneyTests`, `JourneyTests::Vertical_journey_…`, `GoToPathJourneyTests` |
| Configurações (grouped: Exibição, Busca e privacidade, Controles, ControlFS; toggles keep it open) | `ModalSystemJourneyTests::Settings_live_in_Configuracoes_…`; Screens `4b-settings` |
| → Configurações: Ordenar por, Ordem | `StateTests` (sorting), `ListModeJourneyTests`; Manual |
| → Configurações: Busca em subpastas | `SearchJourneyTests` |
| → Configurações: Recentes: lembrar/não lembrar | `RecentsJourneyTests` |
| → Configurações: Itens ocultos | Manual "Lista: estados e densidade (#28)" |
| → Configurações: Exibição: lista/grade | `GridViewJourneyTests` |
| → Configurações: Densidade da lista | `DensityJourneyTests` |
| Operações (N ativas) → operation → Pausar/Continuar/Cancelar operação/Tentar de novo; Limpar histórico… | `PauseJourneyTests`, `FileOperationJourneyTests::Retry_…` (2), `JourneyTests::Retry_failed_items_of_a_cancelled_extraction_…`, `HistoryJourneyTests` (2) |
| Desfazer / Refazer | `UndoJourneyTests` (4) |
| → Configurações: Confirmar com | `PromptJourneyTests`, `InputRouterTests` |
| → Configurações: Legendas | `PromptJourneyTests`, `ControllerFamilyTests` |
| → Configurações: Fluidez (máxima / economia de bateria) | Manual "Fluidez máxima (leitura por quadro)" |
| → Configurações: Teste de controles…, Controle ativo, Controles sem perfil… | `ControllerTestJourneyTests`, `ActiveControllerJourneyTests`, `ControllerMappingJourneyTests` |
| → Configurações: Atualizações (Instalar e reiniciar, Verificar agora, automático, instalar ao sair, pré-lançamento) | `UpdateFlowTests` (6), `UpdateServiceTests` (12) |
| Esvaziar área de transferência | `ClipboardJourneyTests` |
| Sobre o ControlFS | `AboutJourneyTests` |
| Ir para o início (grid tile "Início"), Sair (last list row) | `JourneyTests::Back_semantics_…`, UIA |

### File operations and Central de Operações

| Feature | Test |
|---|---|
| Copy/move byte-identical, folder into itself refused, conflicts never silent (skip/keep both/replace, merge only when chosen), links not followed, cancel leaves nothing partial, pause/resume mid-file | `FileOperationServiceTests` (11), `FileOperationIntegrationTests` (2) |
| Delete to Recycle Bin / permanent (junction removed, target kept) | `DeleteJourneyTests`, `DeleteIntegrationTests` (2) |
| Rename (stem preselected, extension change asks, collisions refused, case-only) | `RenameJourneyTests` (2), `RenameIntegrationTests` |
| Batch rename (#71): Y Operações (N) → Renomear em lote…; numbering, find/replace, prefix/suffix, case; live preview = result; conflicts block before disk; Start applies; undoable | `BatchRenameJourneyTests` (2) |
| Create folder (accented, invalid name keeps keyboard with the reason) | `JourneyTests::Vertical_journey_…`, `::Invalid_folder_name_keeps_keyboard_open_with_reason` |
| Clipboard copy/cut/paste across folders and tabs | `ClipboardJourneyTests` (2) |
| Compress (zip/tar.gz/7z, name typed, existing never overwritten, links not followed; "Formato" cycles ZIP → TAR.GZ → 7z, #67) | `ShellAndCompressJourneyTests::Compress_…` (2), `ArchiveCreatorTests` (5), `SevenZipInteropTests` (Windows: 7-Zip tests and extracts the result) |
| Operations center: progress, cancel, results, errors, retry, history persisted without passwords | `PauseJourneyTests`, `HistoryJourneyTests`, `FileOperationJourneyTests`; header status text (Manual) |
| Undo/redo with checks | `UndoJourneyTests` (4) |
| Leftover cleanup after a crash | `LeftoverCleanupTests` (2); Manual "Limpeza após queda" |
| Open with Windows, executables ask first starting on Cancel, leaving-app warning | `ShellAndCompressJourneyTests`, `ShellIntegrationTests` (3) |
| Steam game shortcut: South/Jogar… asks first (starts on Cancel, shows the game and `steam://` target), then the `.url` file itself goes to the Shell; readable error without Steam | `SteamShortcutJourneyTests`, `ShortcutIconIntegrationTests::Opening_a_steam_shortcut_without_steam_…` |

### Archives and security

| Feature | Test |
|---|---|
| Browse read-only, header summary (format, files, size, passwords, blocked) | `ArchiveBrowserJourneyTests` (2), `JourneyTests::Archive_is_browsed_read_only_…` |
| Extract all / partial / here / to… / dedicated folder never reused | `SafeExtractorTests` (20), `JourneyTests` |
| Password prompt + retry, wrong password recognized (ZipCrypto, AES AE-1/AE-2, 7z, RAR5) | `JourneyTests::Password_flow_retries_after_wrong_password`, `FormatTests` (6), `SafeExtractorTests::Zipcrypto_…` |
| Conflicts: skip / keep both / replace (second confirmation) / apply to all / cancel | `JourneyTests::Extract_here_twice_…`, `::Conflict_dialog_back_skips_…`, `SafeExtractorTests::Conflicts_…`, `::Apply_to_remaining_…`, `::Cancel_at_conflict_…` |
| Containment, traversal, links/junctions, collisions, limits, staging, CRC, MOTW, long paths | `SafeExtractorTests`, `ArchivePathPolicyTests`, `WindowsNameRulesTests` (3), `JunctionRaceTests` (3), `WindowsBehaviorTests` (6), `Zip64Tests` (3), `LinkDetectionTests`, `ReparseTagsTests` |
| Integrity test (no writes, cancel) | `ArchiveTestTests` (3), `JourneyTests::Test_integrity_…` |
| Batch extract | `BatchExtractionJourneyTests` |
| Split archives (.7z.001, .zip.001, .partN.rar, .rar + .r00, .z01 + .zip): open/extract from any volume, missing volumes named and nothing extracted, one set counts once in a batch (#65) | `VolumeTests` (3), `BatchExtractionJourneyTests::Marked_volumes_of_one_split_archive_are_extracted_once` |
| Never auto-execute | `ShellAndCompressJourneyTests::Executables_require_explicit_confirmation_starting_on_cancel` |

### Previews, keyboard, dialogs

| Feature | Where | Test |
|---|---|---|
| Image preview (zoom, pan, next/previous, bomb refusal, EXIF) | modal over the shell | `ImagePreviewJourneyTests`, `ImagePreviewPolicyTests` (3); Manual "Visualização de imagens (#57)" |
| Image preview prompts (#171): only available actions (no Anterior/Próxima at the ends, Ajustar only when zoomed), controller family glyphs, fade after 4 s without input (Close stays), any input reveals and still acts; footer size constant | image preview footer | `ImagePreviewJourneyTests::Hints_fade_…`; Manual "Legendas da visualização de imagens (#171)" |
| Audio playback (#60): South on audio / North → Ouvir aqui; South play/pause (restart at end), Left/Right ±10 s, LB/RB ±1 min (bounded), Up/Down volume, North mute, East closes and stops; disguised executables refused; status polled per frame, redraw only on visible change | modal over the shell | `AudioPreviewJourneyTests`, `MediaPlayerIntegrationTests` (real MediaPlayer, generated WAV); Manual "Áudio (#60)" |
| Video player (#61, #170): South on video / North → Assistir aqui, from list and grid; full-window layer under the modal layer (menus/dialogs on top keep the video), window full screen while open; overlay (title, elapsed/remaining, bar, volume, subtitle/audio state, prompts) fades after 3 s playing, any input reveals; South play/pause, Left/Right ±10 s, LB/RB ±1 min, LT/RT 5% with a previewed target (commit after 700 ms, South now, Back cancels), Up/Down volume, North options (subtitles embedded/sidecar, audio track, mute, restart, forget); Back hides overlay then closes; resume/start over per file (digest of path+size+date); Configurações → Apagar onde os vídeos pararam | full-window video layer (`VideoPlayerView`) | `VideoPlayerJourneyTests` (3); Screen `mv-video-player`; Manual "Vídeo (#61, #170)" |
| Text editing from the preview (#62): North in the text preview → edit mode (focused line band, header "Editando"); Up/Down/LT/RT/LB/RB pick the line, South edits it on the on-screen keyboard, North line menu (insert below/above, delete, undo, save, leave), Start saves with confirmation (atomic replace + `.controlfs.bak`), Back asks before discarding, changed-on-disk warning; refuses binary/read-only/>1 MB/non-round-trip | text preview modal | `TextEditJourneyTests` (3), `TextEditDocumentTests` (2); Manual "Edição de texto (#62)" |
| Text preview (encodings, limits, binary refused) | modal | `TextPreviewJourneyTests`, `TextPreviewTests` (2); Manual "(#58)" |
| PDF preview (#59): South on .pdf / North → Visualizar PDF; LB/RB and Left/Right pages, RT/LT zoom, pan while zoomed, South fits, East closes; password on the masked keyboard with retry; content check, 200 MB / 5,000 pages / 3072 px / 20 s limits | modal over the shell | `PdfPreviewJourneyTests` (3), `PdfRendererIntegrationTests` (Windows.Data.Pdf, page 1 of a generated PDF); Screen `mp-pdf-preview`; Manual "Visualização de PDF (#59)" |
| On-screen keyboard: PT-BR/EN, shift/caps, numbers, symbols, accents, space, backspace (repeat), clear, caret, selection, OK/cancel, name/path/password fields, masking/reveal, controller navigation | modal; footer shows Selecionar/Apagar/…/Concluir/Cancelar | `VirtualKeyboardTests` (13), `HintJourneyTests::On_screen_keyboard_…`, UIA |
| Dialogs name the focused choice; destructive dialogs start on the safe option | modal | `HintJourneyTests::Dialogs_and_menus_…`, UIA |
| Modal system (#172): every menu option and dialog button has an icon; destructive ones flagged, red + warning symbol, never the initial focus; input never reaches the screen under a modal (buttons, list clicks, tabs); nested modals close one at a time and focus returns; prompts and status inside the panel; solid panel with transparency off/high contrast | every modal | `ModalSystemJourneyTests` (4), UIA; Screens `m1`–`m9`, `icons/action-icons`; Manual "Modais (#172)" |
| Updates (installed/portable, signature, SHA, relaunch, notifications) | Menu → Configurações → Atualizações; header status | `UpdateServiceTests` (12), `UpdateFlowTests` (6), `ReleaseVersionTests` |
| About (version, license, source) | Menu → Sobre | `AboutJourneyTests` |
| Phone as a controller (#223): Menu → Conectar celular… shows a QR code (URL, PC network, "Usar outra rede do PC" with 2+ networks, Cancelar); the phone's IP + 6-digit code ask Permitir/Recusar on the PC (sensitive, starts on Recusar); the phone page sends semantic actions and text to the on-screen keyboard; only Back during sensitive confirmations; Menu → Desconectar celular; header shows "Celular conectado (IP)" | Menu (list, section ControlFS); dialog modal with the QR code; header status | `PhoneJourneyTests` (2), `PhoneChannelTests` (7), `PhoneLinkIntegrationTests` (2, real listener); Screens `m10-phone-pairing`, `m10b-phone-allow`; Manual "Celular como controle (#223)" |

### Accessibility, layout and window

| Feature | Test |
|---|---|
| Narrator announces context then item, states in words, live status line | `ScreenReaderJourneyTests`; Manual "Narrador (#40)" |
| Responsive tiers (compact/regular/large), Windows text scale | `LayoutBreakpointsTests` (3); Screens (720p, 800p, 800p+150% text, 1080p, 1080p@150%, 4K@100/200/300%); Manual "Layout responsivo (#36)" |
| Shell icons (known folders, drives, file associations), cached, async | `IconRequestTests` (2), `ShellIconIntegrationTests`; Manual "Ícones do Windows (#24)" |
| Shortcut icons: Steam `.url` games show their title (no `.url`), "Jogo da Steam" and the icon the shortcut declares (local only, cache keyed by path + date + size); `.lnk` its own icon; game/link glyph as fallback. Details/Properties keep the real name and type | `ShortcutTests` (7), `SteamShortcutJourneyTests`, `ShortcutIconIntegrationTests` (4); Screens `6-shortcuts-list`, `6b-shortcuts-grid`; Manual "Atalhos de jogos da Steam e .lnk (#168)" |
| Full screen (F11) and windowed (min/max/close/resize/move) | Manual "Antes de cada release" |
| Settings persisted (view, density, favorites, recents, hidden, labels, convention, updates) and migrated | `DensityJourneyTests`, `GridViewJourneyTests`, `ControllerFamilyTests::Settings_v1_…`, `FavoritesJourneyTests`, `RecentsJourneyTests` |

## Notes for phases B and C

- B1: the Home grid is `HomeView` (non-virtualized card sections), not the `GridView`; the folder `GridView` keeps
  `EntryRowTemplate` tiles until B2. Card texts come from `AppController.DescribePlace` (also read by Narrator);
  numbers from `EntryText` (pt-BR). Folder stats: `AppController.FolderStatsFor(path)`; drives: `FileEntry.Volume`.
- B2: folder/search/archive/Recycle Bin grid cards are still the virtualized `GridView` with `EntryRowTemplate.TileXaml`
  (uniform size); `UpdateGridMetrics` picks columns from `TileSize(density).MinWidth` and stretches cards to fill the row.
- The details panel (C) can reuse `FolderStatsFor` for known folders and `EntryText.DriveUsage` for drives.

- `AppController.IsGrid`, `SetGridLayout(columns, rows)` and `GridNavigation` already give 2D focus with the same columns
  the view shows. Cards must publish their real column count the same way (`UpdateGridMetrics`).
- The footer label for ChangeView is computed from `IsGrid` (target view); don't hardcode it in views.
- Keep `EntryRowTemplate.Fill` as the single place that turns a `FileEntry` into text; the details panel should reuse
  `EntryText` so list, grid, details and Narrator say the same thing.
- Row/tile titles and the Narrator use `EntryText.DisplayName` (Steam games by title); the details panel title and
  Properties use the real `FileEntry.Name`.
- Recursive sizes/counts on Home cards must reuse the folder-size walker (#55: never follows junctions, cancellable)
  off the UI thread, with "Calculando…" until done.
- Every new visual state must keep the rule "focus = cyan ring + fill (+ halo); marked = amber bar + check + text".
