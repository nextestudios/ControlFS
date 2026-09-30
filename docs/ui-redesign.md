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
┌ [ControlFS logo with text] [tabs, only with 2+]   [controller · operation] [⛶][_][□][X] ┐  (header = title bar, #230)
├ [L1] [Locais|Meu computador] › segment › … › current │ Favoritos · Recentes · known folders · Meu computador · Lixeira [R1]
├ badge line (archive summary, git, picker title, search summary, items without permission) — only when there is something to say
├ CONTENT: Grid (B: Home = card sections; elsewhere tiles) or List, each + details panel (C; grid #177)
│                                                         [toast: notice, fades after 3 s] ┘ (over the content)
└ prompts: A Abrir · B Voltar · X Marcar · Y Ações · Menu · Buscar · R Ver em grade   (constant height)
```

- **Window chrome (#230)**: the header *is* the title bar, like the reference and Discord. `TitleBarView` sets
  `AppWindow.TitleBar.ExtendsContentIntoTitleBar`; the system caption buttons (min/max/close, snap layouts, double-click
  to maximize) stay, colored from the theme tokens (`Background`/`Text`/`TextMuted`/`Border`/`AccentSoft`; close hover is
  the system red; null = system colors under high contrast), recolored live with the theme. The drag region
  (`InputNonClientPointerSource`, Caption) is the header minus its top-right button corner; the tab strip and the full
  screen button are Passthrough. With a modal/video on screen or in full screen nothing drags (the whole header is
  Passthrough), because a tall panel can overlap the header. Regions are recomputed on header/tabs size, DPI, window
  size and presenter changes. The header reserves the caption inset (`RightInset`) plus the full screen button on the
  right. Windows without title bar customization keep the system bar. Captures (`--render-screens`) can't show the real
  caption buttons: they reserve 3 × 46 px and draw look-alike glyphs.
  No separate logo row (and no plate behind the logo: the light theme uses the dark-name variant): logo, tabs, controller/device and operation status all live in this strip (status at `FontBody`,
  one line each, so the strip height stays the logo's). The full screen button shows "exit full screen" while active.
- **Header**: official logo with text (`assets/controlfs-logo-text-900.png`, copied as `controlfs-logo.png`) on every
  screen; the icon-only logo stays for the exe, taskbar and installer. Right side: active controller and running
  operation/update/clipboard status (unchanged texts).
- **Top navigation bar (A2)**: left segment = breadcrumb with a root chip (`Locais > Início` on Home; `Meu computador >
  C: > …` on disk and archive paths; `Locais > Lixeira` / `Locais > Busca: …` elsewhere); right segment = quick access.
  Icons come from `IIconProvider` (Windows shell icons for known folders, This PC and the Recycle Bin; Segoe Fluent
  Icons glyphs for Favorites/Recent, never emoji).
- **Footer**: prompts from `AppController.Prompts` (`ControllerPromptProvider`) only. Its height never depends on a
  message (it used to grow 59 → 124 px with a status line and shrink the list).
- **Status toast** (`StatusToastView`): `AppController.StatusMessage` shows as a pop-up in the bottom-right corner of the
  content at `FontBody` (icon + up to 3 lines, max ~45% of the width), never focusable or clickable, a polite live region
  for Narrator. `StatusSerial` restarts it even for the same text; it stays 3 s (the next input doesn't clear it early),
  then fades (≤ 200 ms `OpacityTransition`, none with reduced motion) and a timer collapses it — nothing depends on the
  animation. When the focused list/grid row would be under it, it moves to the top-right corner. With a modal open the
  message is in the modal's own footer and the toast hides. Captures (pinned layout) mirror `StatusMessage` with no timer.
- **Operation progress**: while `OperationQueue.Progress` is non-null (any active operation; mean of each one's
  `Fraction`, by bytes or items, unknown = 0) a 4 px accent bar runs along the bottom edge of the header (no height
  change); the header's operation text shows the percentage. Progress reaches the UI through `CoalescingProgress`
  (≤ ~10 updates/s, latest wins; late reports after the end are ignored) and `ProgressEstimator` (fraction never goes
  backwards, ETA only with ≥ 3 s of work, ≥ 5% done and a known total, always labelled "estimativa"; no total =
  indeterminate: current activity, no % or ETA). Operation details (Menu → Operações → row) list source, destination,
  current item, %, items, data, speed and ETA, refreshed live while open (#257).

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
| Select / Ctrl+F | search in the main folders | search (disk folders; This PC: focused drive) | — | — |
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
`AppController`; views never pick icons. Each meaning has its own glyph; the only repeats are the documented groups in
`ActionIcons.SharedGlyphs` (trash, undo/restore, dismiss X, again, play, controller), guarded by
`ActionIconTests::Each_meaning_has_its_own_glyph_except_the_documented_shared_ones` (UX audit: Copy/CopyTo/KeepBoth,
Settings/ControllerSetup, Info/Properties/About, Replace/SortOrder, Archive/Compress, Operations vs Refresh, "Abas…"
and "Reabrir aba fechada" now have distinct glyphs; the gallery is the smoke capture `icons/action-icons.png`). Menus (#193): `MenuItem.Placement = Quick` (set by `AppController`) puts an
option in the quick-action grid (≤ 4 tiles per row, destructive tiles last, `ShortLabel` under the icon, full label for
Narrator/UIA); the rest is a compact list (40 px rows, detail only on the focused row; with a grid, groups are separated by a divider
only, without titles). Menu panel: a fixed size class per menu (#227, `MenuModal.Size`: Medium 540 px with a grid, Compact 460 without; a fixed `Width`, capped by the window), and the description of the focused option (tile caption or row detail, or why it is unavailable) lives in one fixed area between the list and the footer whose height is that of the longest description of the menu, so focus never resizes the panel (long text wraps); `MenuPadding` 20, compact header. Configurações uses per-section grids (#227, `sectionGrids: true`): short independent settings are tiles in their group's grid showing icon, `ShortLabel` and `MenuItem.Value` (the current value); long-description settings and submenus stay list rows under the grid. `MenuItem.KeepOpen` (Configurações) applies a setting and keeps the
menu open (`MenuModal.Reload`). The retained panel (#191) swaps only the tile/row that loses and gains focus. `Theme.SolidSurfaces` comes from Windows transparency effects off or high
contrast (`UISettings.AdvancedEffectsEnabled`, `AccessibilitySettings.HighContrast`); `Theme.ReduceMotion` from
`UISettings.AnimationsEnabled`.

### Modal sizing (#227)

Every modal picks a **size class** (`Modal.Size`, `ModalSize`): the panel has a fixed `Width` per class (× the layout scale,
capped by the window minus the margins, so 720p, handhelds and 4K all fit), never derived from the focused option, the
selected value, a variant, a message or the longest label. The default of a new modal is **Standard**. Nothing forces one
universal size on unrelated dialogs: a short confirmation stays Compact.

| Class | Width | Used by |
|---|---|---|
| Compact | 460 | list-only menus, short confirmations |
| Medium | 540 | menus with a quick-action grid (app menu, item actions, Configurações) |
| Standard | 640 | dialogs with information/options, About, audio player |
| Wide | 960 | on-screen keyboard, "Mais da equipe", controller test, mapping wizard |
| Fill | window − margins | image/PDF/text preview, video, welcome |

Height rules (the panel is at most the window minus the margins; the body scrolls inside those bounds):

- **Reserve, don't resize.** Content that can change while the modal is open has its space reserved by overlapping every
  possible text in one grid cell (`TextSlot`, `InfoLines` reserve, `WeightStable`): the invisible copies only measure.
  The focus fill is bold, so an unfocused label also measures itself bold (a label that wraps only when focused would grow
  its row).
- **Menus**: one description area (`MenuModal.AllDescriptions`); no per-row detail inside the list.
- **Dialogs**: `DialogModal.LineReserve` reserves every text of a variable info line (Compress: each format description,
  each compression and the numbered file name; extraction: both destination texts). Size is Compact when the dialog is a
  short confirmation (≤ 2 lines, no toggles, no progress, no QR code), otherwise Standard, decided once.
- **Footer**: the status line has one reserved line (`StatusLineHeight`); a notice arriving while a modal is open fills it
  (messages beyond two lines are ellipsized).
- **Keyboard**: the validation-message line is reserved, the field reserves two lines for paths, and the suggestion strip has
  its place while suggestions are on.
- **Wizard / controller test**: step headline, detail, feedback, the options-or-hint area, the device list (2 rows), the
  last 8 presses and the notice have reserved space.

Inventory (every modal type; the render report proves the rule for the marked ones):

| Modal | Class | Height | Proof |
|---|---|---|---|
| Option picker (#261: Formato, Compressão, Tema, Destaque, Ordenar por, Legendas, Tamanho, Modo…) | Medium | every choice and its description always visible (no description area, no per-focus growth); the current one is marked | Screens `p1`/`p1b` (group `picker-theme`, width and height), `p3`/`p3b` (`picker-format`), all pickers same width (`picker-width`), `p1c`/`p3c` (the modal underneath returns at its size) |
| Menu (Início), item actions, path menu, drive/tab/operation menus, folder picker menu, "Locais" | Medium (with grid) / Compact | reserved description area; body scrolls | Screens `4`/`4a` (group `menu-app`), `m1`/`m1b` (`menu-actions`) |
| Configurações | Medium | reserved description area; tiles and rows measure bold | Screens `4b`–`4e` (group `settings`: tile, long row, long value, wrapped label) |
| Compactar (ZIP / TAR.GZ / 7z / RAR — RAR only enabled with the user's WinRAR, #258) | Standard | `LineReserve`: identical for every format and compression | Screens `q1`, `q1b`, `q1c`, `p3c` (group `compress`, width and height) |
| Extrair (summary) | Standard | `LineReserve` for the destination | Screens `m3`/`m3b` (group `extract-summary`) |
| Confirmations (delete, exit, run, terminal, unmount, discard…) | Compact | content only; long names wrap | Screens `m2`/`m2b`/`q2` (group `confirm-delete`, width) |
| Results, conflict, batch, history and operation details, errors, disk usage, "Continuar o vídeo?" | Standard (Compact when short) | content only | Screens `m5b`, `m5c`, `m7` |
| File-operation progress | Standard | values reserve their lines; body scrolls | Manual (#257 reworks the progress views) |
| On-screen keyboard (name, path, password, search) | Wide | reserved error line, field lines, suggestion strip | Screens `m4`/`m5` (group `keyboard-password`), `5-keyboard` |
| Sobre | Standard | content only | Screens `m9` |
| Conectar celular (QR), Permitir este celular? | Standard / Compact | QR fits 30% of the height | Screens `m10`, `m10b` |
| Mais da equipe | Wide | content only | Screens `m11`, `m11b` |
| Teste de controles, assistente de mapeamento | Wide | reserved areas (see above) | Manual (Controles) |
| Image, PDF, text preview | Fill | box computed from the window | Screens `2*`, `mp-pdf-preview` |
| Áudio | Standard | content only | Manual |
| Video player | Fill (own layer) | full window | Screen `mv-video-player` |
| Welcome (onboarding) | Fill (own layer) | fixed 1240 content column, centered | Screens `o1`–`o4` |
| Tutorial callout | overlay, not a modal panel | max width by tier | Screens `o5`–`o7` |

The render harness (`--render-screens`) records the panel size of every capture that names a size group and fails the run
(`FALHA: … mudou de tamanho`, and `TAMANHO DIFERENTE` in `report.txt`) when two captures of the same group and target differ by
more than 1 px in width (and in height where the group asserts it: `menu-app`, `menu-actions`, `settings`, `compress`,
`extract-summary`, `keyboard-password`; confirmations compare width only because different names wrap differently).

### Option pickers (#261)

One pattern for every value with several alternatives that the row hides: activating the row opens a **picker** on top of the
current modal (`MenuModal` with `IsPicker`; no third modal type): every alternative is listed with an optional one-line
description under it (all visible at once), the current one marked with a filled radio icon **and** the text "atual" (never
color alone), the focus starts on the current one (`SafeInitialFocus`), confirming applies and returns to the modal underneath
exactly as it was (state and focus; the picker is a separate modal on the stack, so nothing underneath is rebuilt) and Back
returns without changing anything. Keyboard, mouse (click a row), controller and Narrator ("Escolha: Formato", "ZIP, …, opção
1 de 3, selecionada") come from the menu machinery; the footer prompts say **Escolher** and **Cancelar**.

API (`AppController.Choices.cs`): `Choice<T>(Value, Label, Description?)`, `ChoiceRow(label, current, choices, onPick, icon, …)`
for menus (Configurações, filters) and `ChoiceOption(label, () => current, choices, onPick, icon)` for dialogs (Compress,
batch rename). A new alternative is one more `Choice` in the declaring list: Compress formats live in
`AppController.CompressFormats` (the picker, the summary text and the height reservation all read it), so a new format is one
added line.

| Where | Pickers | Stays one press |
|---|---|---|
| Compactar | Formato, Compressão | Nome (keyboard), Compactar |
| Configurações (uma coluna larga de blocos só de ícone, #293; L1/R1 pulam de grupo; primeiro bloco do Menu) | Exibição, Densidade da lista, Tema, Cor de destaque, Ordenar por, Confirmar com, Legendas, Fluidez | Ordem, Painel de detalhes, Itens ocultos, Tela cheia, Busca em subpastas, Recentes, Restaurar abas, Sugestões do teclado, Segundo plano, Mira por giroscópio (on/off or symmetric two-way toggles); Controle ativo, Teste de controles, Controles sem perfil, Atualizações (they open their own screen) |
| Filtros da busca | Tamanho, Modificado | Tipos (checkboxes) |
| Renomear em lote | Modo, Converter para | Dígitos (1–6, a stepper), text fields |

The welcome (onboarding) keeps its own inline choices (one press advances the value; it is a full-screen flow with the
tutorial state machine watching it).

## Regression matrix

### Input, controllers and prompts

| Feature | Where | Shortcut / flow | Test |
|---|---|---|---|
| Semantic actions only (screens never see buttons) | all | `InputAction` → `AppController.Handle` | all journey tests (`Driver`) |
| Default map by physical position; confirm/back convention swaps behavior and prompts | Menu → Configurações → "Confirmar com" | South/East | `InputRouterTests::East_confirms_convention_swaps_behavior_by_position`, `PromptJourneyTests` |
| R3 (right stick click) → Lista/Grade | footer "R Ver em grade" / "R Ver em lista" (an action, not the current state) | R3, Ctrl+G, Menu → Configurações → Exibição | `PromptJourneyTests` (prompt + button), `GridViewJourneyTests`, `DensityJourneyTests` |
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
| Stable modal size (#227): size classes for every modal, fixed description area in menus, per-section settings grids (tiles with label and value, 2D navigation between grids and lists) | Menu → Configurações | D-pad; click a tile | `ModalSystemJourneyTests::Settings_grids_per_section_navigate_in_two_dimensions_with_the_lists_between_them`, `::Menu_size_class_and_reserved_descriptions_do_not_depend_on_the_focused_option`; Screens `4`/`4a`, `4b`–`4e`, `m1`–`m5`, `q1`–`q2` (same size per group, asserted by the render report); Manual "Largura estável e grades em Configurações (#227)" |
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
| Quick access: Favoritos, Recentes (same name as on Home), known folders, Meu computador (B1: opens This PC), Lixeira; the active shortcut is skipped (#176) | top bar right segment (A2) | R1 (or L1 then Right), South | `TopBarJourneyTests` (2), `HomeGridJourneyTests`; Manual "Barra superior e cabeçalho (redesenho, fase A2)" |
| Tabs (8 max), each with its own folder/history/marks/focus; strip only with 2+ tabs, no standalone prompt (#176) | header tab strip (A2) | Up from the top bar, L1/R1, North new/close/switch, Menu → Abas, "Abrir em nova aba" | `TabsJourneyTests`, `ModalSystemJourneyTests`; Screens `2g-folder-tabs`; Manual "Abas (#50)" |
| Tabs with the same name show their parent folder, e.g. "Fotos (Viagem)" (strip, Menu → Abas, Narrator) | header tab strip, Menu → Abas | — | `TabsJourneyTests::Tabs_with_the_same_folder_name_…` |
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
| Search from Home across the main folders (Downloads, Documentos, Área de trabalho, Imagens, Vídeos, Músicas; nested/duplicate roots skipped, links never followed, cancellable, "Buscar" prompt on Home, Back returns Home); This PC searches the focused drive | Home, This PC | Select/View, Ctrl+F | `SearchJourneyTests::Search_from_home_…`, `PromptJourneyTests` |
| Empty states: "Lixeira vazia", "Compactado vazio", "Pasta vazia — Y Ações: Colar / Nova pasta" (Colar only when it would work), "Nenhum resultado — Y Filtros · Select Nova busca" (button names follow the active controller) | list/grid empty text (`AppController.EmptyMessage`) | — | `ClipboardJourneyTests::Empty_folder_…`, `SearchJourneyTests::Search_from_home_…` |
| Readable errors: system exceptions map to one pt-BR category with one suggested action (não encontrado, acesso negado, em uso, disco cheio, caminho longo, dispositivo não pronto, genérico) via `UserErrors`; raw text only as "Detalhes técnicos" and in the local log | error dialogs, status line, keyboard, previews, operation results | — | `UserErrorsTests` |
| OneDrive files-on-demand folders searched without downloading | — | — | `SearchIntegrationTests::Reparse_tag_…`; Manual "OneDrive sob demanda (#126)" |
| Grid view with 2D navigation, persisted; switching keeps focus and marks (no re-read; also inside archives and in search results: `ListModeJourneyTests::Switching_views_…`); cards with responsive columns (B2: comfortable 3 at 1080p, 2 handheld, 1 narrow, 4 on 4K TV; compact one more; counted from the width left by the details panel, #177: 2 at 1080p with it) | Menu → Configurações → Exibição, R3, Ctrl+G | — | `GridViewJourneyTests`, `HomeGridJourneyTests::Home_sections_…` |
| Density comfortable/compact, persisted (C1: tall/short rows with the same columns; the type column drops first when narrow) | Menu → Configurações → Densidade da lista | — | `DensityJourneyTests`; Screens `3-folder-compact` |
| Sort by name/type/size/date, ascending/descending, natural sort | Menu → Configurações → Ordenar por / Ordem; list column header shows the arrow and sorts on click (C1) | Start; mouse on a column title | `StateTests::Natural_sort_orders_numbers_numerically`, `::Focus_survives_resort_by_identity`, `ListModeJourneyTests::Column_header_follows_…`; Screens `2c` |
| Details panel: folder (path, recursive count/size, 250 ms debounce, 20 s budget, cached 10 min, cancelled on focus change), file, image (header + thumbnail through `IImageDecoder` with `PreviewLimits`), archive (format by content; file count only for ZIP/7z, 3 s budget), drive (file system, capacity, free, used, usage bar), archive entries, Recycle Bin items, marked summary | List and grid (incl. Home/This PC cards), right side (C2, grid #177); automatic = shown where it fits (list: name stays legible; grid: ≥ 2 columns), hidden on handheld/narrow (`MainWindow.DetailsLayout`) | Menu → Configurações → Painel de detalhes: visível/oculto (per view, `AppSettings.ListDetails`/`GridDetails`, null = automatic) | — | `DetailsPanelJourneyTests` (4); Screens `1-home`, `1c`, `2d`, `2e`, `2f`, `3c`, `3d`; Manual "Painel de detalhes (redesenho, fase C2)", "Painel de detalhes na grade (#177)" |
| List rows: mark box (focus ≠ marking), icon, name, type ("Pasta do sistema" for Windows folders), size (real sums on Home), friendly date, chevron; compact density with the same columns | content, List (C1) | X marks; mouse on the header box = Marcar todos / Limpar | `ListModeJourneyTests::Friendly_dates_…`, `::Column_header_…`; Screens `1-home`, `2-folder`, `3-folder-compact`; Manual "Lista em colunas (redesenho, fase C1)" |
| Opening another location focuses its first item; back/up/refresh restore the item | content | South, Right, East, Left | `ListModeJourneyTests::Opening_another_location_…`, `JourneyTests::Back_semantics_…` |
| Hidden items show/hide (persisted) | Menu → Configurações → Itens ocultos | Start | Manual "Lista: estados e densidade (#28)" |
| Refresh | Menu → Atualizar | Start | journey tests that call Refresh indirectly (`FileOperationJourneyTests`) |
| Folder picker (copy/move/extract destination, create folder, other places, go to path, cancel) | full-screen picker, same shell | Start = Escolher esta pasta… | `FileOperationJourneyTests::Copy_to_a_folder_with_the_picker_and_keep_both_on_conflict` |
| Exit confirmation starts on Cancel | Home East, Menu → Sair | East | `JourneyTests::Back_semantics_…`, UIA |

### Item actions (North menus)

Since #193 the frequent actions are quick-grid tiles (folder: Abrir, Recortar, Copiar, Renomear, Compactar, Colar,
Propriedades, Excluir; file: Abrir/Executar/Jogar, the same file ops; marked items: Recortar, Copiar, Compactar, Excluir; drive: Abrir,
Nova aba, Propriedades, Atualizar); everything else stays in the list below. Initial focus: the first enabled,
non-destructive tile of the grid (it used to open on a list row below the grid); an explicit `FocusOn` still wins ("Extrair
para" on archives and marked archives, "Extrair tudo para" inside an archive, "Extrair seleção" with marked entries).
Actions about the open folder (add it to favorites, Colar, Nova pasta aqui, Abrir terminal aqui, Analisar uso do disco)
form the group "Nesta pasta (<nome>)", whose heading shows even under the grid (`MenuModal.TitledSection`).
2D grid, initial focus and the folder group: `ModalSystemJourneyTests::Quick_action_grid_…`.

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
| Open terminal here (#76): Y Ações → "Nesta pasta (<nome>)" → Abrir terminal aqui…; notice starts on Cancelar; optional Windows on-screen keyboard | `TerminalJourneyTests` (2) |
| Two panes (#56): left = active tab, right = own `PaneState`; active pane outlined in cyan with "ATIVO" title, the other dimmed with its path; L3/Tab or click switches without touching marks; tabs stay on the left; single pane on handhelds/narrow windows (setting kept); details panel hidden | Menu → "Dois painéis"; L3/Tab | `DualPaneJourneyTests` (2); Screens `2h-dual-pane`, `2i-dual-pane-right`; Manual "Dois painéis (#56)" |
| Copiar/Mover para o outro painel (quick tiles), Extrair para o outro painel (archives), with source/destination summary; unavailable with a reason when both panes show the same folder (#56) | Y Ações (two panes) | `DualPaneJourneyTests` |
| Tab strip: Nova aba, Duplicar aba, Fechar aba, Reabrir aba fechada, Ir para a aba (2+) | North on the strip; Menu → Abas | `TabsJourneyTests` |
| Mount/unmount disk images (#73, #74): Y Ações on .iso/.img/.vhd/.vhdx → Montar imagem (opens the new drive); Y on a mounted drive (This PC, Home) → Desmontar imagem… (confirmation starts on Cancelar) | `DiskImageJourneyTests`, `DiskImageIntegrationTests` |
| Path bar: full path menu | North on a segment | `BreadcrumbJourneyTests` |

### App menu (Start)

Every entry stays reachable from the Menu (Start/F10); nothing moves out without a replacement in the same PR. Since
#193 the Menu has a quick grid (Colar, Nova pasta, Nova aba, Atualizar, Ir para caminho, Operações, Configurações, Ir
para o início) and a short list (Ir para pasta acima, Abas, Desfazer/Refazer, Esvaziar área de transferência, Ajuda e
tutorial…, Sobre, Sair); every setting moved to **Menu → Configurações** (rows marked "→ Configurações"). `ModalSystemJourneyTests::
Settings_live_in_Configuracoes_…` checks every moved entry is there; `Driver.ChooseMenu` looks inside Configurações for them.

| Entry | Test |
|---|---|
| Colar, Nova pasta, Atualizar, Ir para pasta acima…, Ir para caminho… | `ClipboardJourneyTests`, `JourneyTests::Vertical_journey_…`, `GoToPathJourneyTests` |
| Configurações (grouped: Exibição, Busca e privacidade, Controles, ControlFS; toggles keep it open) | `ModalSystemJourneyTests::Settings_live_in_Configuracoes_…`; Screens `4b-settings` |
| Settings wording (UX audit): values are states ("ligado/desligado", "visíveis/escondidos", "incluídas/ignoradas"), one label per setting at every size ("Painel de detalhes: visível/oculto"); the caption under a focused tile explains it (`MenuModal.TileCaption`) instead of repeating the value | `ModalSystemJourneyTests::Settings_grids_per_section_…` |
| → Configurações: Ordenar por (picker), Ordem | `StateTests` (sorting), `ListModeJourneyTests`, `OptionPickerJourneyTests`; Manual |
| → Configurações: Busca em subpastas | `SearchJourneyTests` |
| → Configurações: Recentes: ligado/desligado | `RecentsJourneyTests` |
| → Configurações: Itens ocultos: visíveis/escondidos | Manual "Lista: estados e densidade (#28)" |
| → Configurações: Exibição: lista/grade (picker) | `GridViewJourneyTests`, `OptionPickerJourneyTests` |
| → Configurações: Densidade da lista (picker) | `DensityJourneyTests` |
| Option pickers (#261): every choice listed with the current one marked (icon + text), focus on the current, confirm applies and returns to the previous modal with its state and focus, Back changes nothing, mouse and Narrator, one-press toggles kept | Compactar (Formato, Compressão), Configurações, Filtros da busca, Renomear em lote | South / click; Back | `OptionPickerJourneyTests` (4), `ShellAndCompressJourneyTests::Format_picker_…`, `SearchFilterJourneyTests::Size_and_date_filters_open_a_picker_…`; Screens `p1`–`p3c`; Manual "Seletor de opções (#261)" |
| Operações (N ativas) → operation → Pausar/Continuar/Cancelar operação/Tentar de novo; Limpar histórico… | `PauseJourneyTests`, `FileOperationJourneyTests::Retry_…` (2), `JourneyTests::Retry_failed_items_of_a_cancelled_extraction_…`, `HistoryJourneyTests` (2) |
| Desfazer / Refazer | `UndoJourneyTests` (4) |
| → Configurações: Confirmar com (picker) | `PromptJourneyTests`, `InputRouterTests`, `OptionPickerJourneyTests` |
| → Configurações: Legendas (picker) | `PromptJourneyTests`, `ControllerFamilyTests`, `OptionPickerJourneyTests` |
| → Configurações: Fluidez (picker: máxima / economia) | `OptionPickerJourneyTests`; Manual "Fluidez máxima (leitura por quadro)" |
| → Configurações: Leve em segundo plano (tile "Segundo plano", group Controles, on by default): minimized/inactive window → BelowNormal + EcoQoS, controllers only watched for connections (1 s, SDL input events off), drive polling paused, memory trimmed once after 5 s; activation restores; media playing keeps normal priority. No controller connected → 250 ms poll (no 8 ms ticker) | `BackgroundModePolicyTests` (2), `InputCadenceTests` (2); Smoke "Measure performance" (`docs/performance.md`); Manual "Leve em segundo plano" |
| → Configurações: Teste de controles…, Controle ativo, Controles sem perfil… | `ControllerTestJourneyTests`, `ActiveControllerJourneyTests`, `ControllerMappingJourneyTests` |
| → Configurações: Atualizações (Instalar e reiniciar, Verificar agora, automático, instalar ao sair, pré-lançamento) | `UpdateFlowTests` (6), `UpdateServiceTests` (12) |
| Esvaziar área de transferência | `ClipboardJourneyTests` |
| Sobre o ControlFS | `AboutJourneyTests` |
| Ajuda e tutorial… (#231) → Tutorial guiado, Rever boas-vindas, Sobre o ControlFS | `TutorialJourneyTests::Tutorial_from_the_help_menu_…`, `OnboardingJourneyTests::Existing_users_…` |
| → Configurações: Rever boas-vindas (#231, group ControlFS) | `OnboardingJourneyTests` |
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
| Compress (zip/tar.gz/7z, name typed, existing never overwritten, links not followed; "Formato" and "Compressão" open option pickers, #261, ZIP / TAR.GZ / 7z, #67; same panel size for every format, #227) | `ShellAndCompressJourneyTests::Compress_…` (3), `ArchiveCreatorTests` (5), `SevenZipInteropTests` (Windows: 7-Zip tests and extracts the result) |
| Operations center: progress, cancel, results, errors, retry, history persisted without passwords; Menu → Operações grouped **Em andamento** (state icon, %, items; refreshed in place while open) / **Histórico** | `PauseJourneyTests`, `HistoryJourneyTests`, `FileOperationJourneyTests`; header status text and progress bar (Manual "Avisos e andamento") |
| Responsive UI and consistent progress (#257): progress flood is coalesced (no freeze after a copy), % / items / bytes / speed / ETA rules, indeterminate = activity only, cancel, Cut shows no transfer until Paste, compress/extract totals, live details | `ProgressJourneyTests` (9), `ProgressEstimatorTests` (6), `FileOperationIntegrationTests::Cross_volume_move_of_a_large_file_…`; Manual "Andamento e resposta (#257)" |
| Clean success of copy/move/delete = toast ("N itens copiados · Menu → Desfazer"), no dialog; warnings/failures/cancel keep the result dialog | `FileOperationJourneyTests::Copy_to_a_folder_…`, `UndoJourneyTests`, `PauseJourneyTests`, `DualPaneJourneyTests`, `ClipboardJourneyTests` (`Driver.WaitStatus`); Screens `3-folder-compact`, `3b-folder-grid-compact` (toast) |
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
| Legibility and handheld density (UX audit P1-7/P2-6/P2-7/P2-11/P2-12/P3): badge line, "PAINEL … · ATIVO" and the inactive pane title/path at `FontBody`; `FontCaption` = 16 px on the Compact tier; folder picker badge is a title row (`FontTitle`, "Copiar para… · escolha a pasta"); Compact-tier comfortable rows ~64 px (icon 40, title 20, state 16); first launch on the Compact tier starts in compact density (`SettingsLoadResult.FirstRun` + `AppController.ApplyFirstRunDensity`, never over a saved/session choice); footer never wraps (`MainWindow.FitFooter` drops non-essential prompts from the end, keeps Confirm/Back/Actions/Menu); path root chip always visible (current crumb label shrinks instead of scrolling); details-panel paths middle-ellipsized (`PathEllipsis.Middle`); Home known-folder cards drop the path on the Large tier (middle-ellipsized elsewhere); keyboard starts on the first letter row, only the focused key has an outline (Done = accent text), Compact tier shows 6–7 keyboard prompts (no cursor/start/end prompts); light theme: dark-name logo variant (`assets/controlfs-logo-text-light-900.png`, no plate), `KeyFill`/`KeyFunctionFill` tokens; unmarked focused item = empty box (`DetailsIcon.Unmarked`) | shell, details panel, Home cards, on-screen keyboard | `DensityJourneyTests::First_run_on_a_handheld_…`, `PathEllipsisTests`, `VirtualKeyboardTests::Vertical_navigation_…` (initial focus), `ThemeContrastTests` (key vs panel, accent on function keys), `DetailsPanelJourneyTests` (Unmarked); Screens `1c`, `2-folder`, `2b`, `2d`, `2h`, `3-folder-compact`, `5-keyboard`, `7-light-folder`, `7c`, `m8`; Manual "Legibilidade e densidade nos portáteis" |
| Dialogs name the focused choice; destructive dialogs start on the safe option; with the focus on the Back option itself (Cancelar/Fechar), the footer shows the next option on Right instead of repeating it on Confirm | modal | `HintJourneyTests::Dialogs_and_menus_…`, UIA |
| Operation result dialogs replace the "…iniciada" status line (the footer never contradicts "Extração concluída"); a recycled delete's status names Menu → Desfazer | result dialog; status line | `UndoJourneyTests::Undo_of_a_recycle_…` |
| Modal system (#172): every menu option and dialog button has an icon; destructive ones flagged, red + warning symbol, never the initial focus; input never reaches the screen under a modal (buttons, list clicks, tabs); nested modals close one at a time and focus returns; prompts and status inside the panel; solid panel with transparency off/high contrast | every modal | `ModalSystemJourneyTests` (4), UIA; Screens `m1`–`m9`, `icons/action-icons`; Manual "Modais (#172)" |
| Updates (installed/portable, signature, SHA, relaunch, notifications) | Menu → Configurações → Atualizações; header status | `UpdateServiceTests` (12), `UpdateFlowTests` (6), `ReleaseVersionTests` |
| About (version, license, source) | Menu → Sobre | `AboutJourneyTests` |
| Welcome / onboarding (#231): first launch only (fresh settings in the real window; never in tests, `--render-screens`, `--no-onboarding` or for settings from older versions), 5 full-screen steps (welcome, controller prompts of the active family, basics applied at once — Confirmar com, Legendas, Tema, Exibição, Fluidez —, privacy with the optional update check, tutorial invitation); D-pad moves, South chooses/changes, East or L1 previous step, R1 next, Start skips without confirmation; prompts tappable; Narrator reads step + focused option; completion saved (`OnboardingCompleted`); the automatic update check waits until it ends; drawing failure skips it; ends on Home with the first card focused | full-screen layer in the modal slot (`OnboardingView`); Menu → Ajuda e tutorial → Rever boas-vindas; Configurações → Rever boas-vindas | `OnboardingJourneyTests` (2); Screens `o1`–`o4`; Manual "Boas-vindas e tutorial guiado (#231)" |
| "Mais da equipe": one screen, once (`PromoSeen` saved as soon as it opens), when the app is idle after the welcome/tutorial (or on launch for settings that already completed the welcome); only in the real window (`OfferPromo`; never in tests, `--render-screens` or `--no-onboarding`); two cards (NextBoost PRO, Console Mode) with bundled logos, what each does and a button that opens the site in the default browser (`IShellService.OpenLink`, https only); Left/Right choose, South opens, East/Start closes, Down reaches Fechar; Narrator reads the card; no network by ControlFS | modal (`PromoModal`, `BuildPromo`); Menu → Mais da equipe (and Ajuda e tutorial → Mais da equipe); catalog `AppController.TeamApps` grows in two-column rows, empty catalog shows a notice | `PromoJourneyTests` (3), `ShellLinkIntegrationTests`; Screens `m11`, `m11b`; Manual "Mais da equipe" |
| Guided tutorial (#231): 8 steps on the real screen (move focus, open a folder, go back, open/close Ações, top bar L1/R1 + a shortcut, R3 list/grid, search, Menu → Configurações), each advancing only on the semantic event (`GuidedTutorial` state machine in Application); spotlight (dim + accent ring) on content / top bar / footer, callout with the exact prompt glyph, corner callout while a modal is open; West/Marcar opens Continuar / Voltar passo / Pular tutorial (never marks during the tutorial); mouse buttons on the callout; never changes files; Narrator reads each new step; ends with a summary on Home | overlay above the modal layer (`TutorialOverlayView`), only the callout is hit-testable; Menu → Ajuda e tutorial → Tutorial guiado; last onboarding step | `TutorialJourneyTests` (3); Screens `o5`–`o7`; Manual "Boas-vindas e tutorial guiado (#231)" |
| Phone as a controller (#223): Menu → Conectar celular… shows a QR code (URL, PC network, "Usar outra rede do PC" with 2+ networks, Cancelar); the phone's IP + 6-digit code ask Permitir/Recusar on the PC (sensitive, starts on Recusar); the phone page sends semantic actions and text to the on-screen keyboard; only Back during sensitive confirmations; Menu → Desconectar celular; header shows "Celular conectado (IP)" | Menu (list, section ControlFS); dialog modal with the QR code; header status | `PhoneJourneyTests` (2), `PhoneChannelTests` (7), `PhoneLinkIntegrationTests` (2, real listener); Screens `m10-phone-pairing`, `m10b-phone-allow`; Manual "Celular como controle (#223)" |

### Accessibility, layout and window

| Feature | Test |
|---|---|
| Narrator announces context then item, states in words, live status line | `ScreenReaderJourneyTests`; Manual "Narrador (#40)" |
| Responsive tiers (compact/regular/large), Windows text scale | `LayoutBreakpointsTests` (3); Screens (720p, 800p, 800p+150% text, 1080p, 1080p@150%, 4K@100/200/300%); Manual "Layout responsivo (#36)" |
| Shell icons (known folders, drives, file associations), cached, async | `IconRequestTests` (2), `ShellIconIntegrationTests`; Manual "Ícones do Windows (#24)" |
| Shortcut icons: Steam `.url` games show their title (no `.url`), "Jogo da Steam" and the icon the shortcut declares (local only, cache keyed by path + date + size); `.lnk` its own icon; game/link glyph as fallback. Details/Properties keep the real name and type | `ShortcutTests` (7), `SteamShortcutJourneyTests`, `ShortcutIconIntegrationTests` (4); Screens `6-shortcuts-list`, `6b-shortcuts-grid`; Manual "Atalhos de jogos da Steam e .lnk (#168)" |
| Full screen (#230): F11, title-bar button, Menu → Tela cheia tile, Configurações → Tela cheia; saved (`AppSettings.FullScreen`) and applied at startup; video forces full screen without changing the choice (F11 during a video leaves it for that video only); no controller shortcut (all buttons taken), Esc stays Back | `FullScreenJourneyTests`; Manual "Barra de título e tela cheia (#230)" |
| Theme-colored title bar (#230): header in the title bar, caption buttons in theme colors (live, inactive, high contrast), drag region excluding tabs/button, double-click maximize, snap layouts, windowed (min/max/close/resize/move) | Screens `0-title-bar-dark`, `0b-title-bar-light` (1080p); Manual "Barra de título e tela cheia (#230)", "Antes de cada release" |
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
- The footer label for ChangeView is computed from `IsGrid` (target view, as an action: "Ver em grade"/"Ver em lista"); don't hardcode it in views.
- Keep `EntryRowTemplate.Fill` as the single place that turns a `FileEntry` into text; the details panel should reuse
  `EntryText` so list, grid, details and Narrator say the same thing.
- Row/tile titles and the Narrator use `EntryText.DisplayName` (Steam games by title); the details panel title and
  Properties use the real `FileEntry.Name`.
- Recursive sizes/counts on Home cards must reuse the folder-size walker (#55: never follows junctions, cancellable)
  off the UI thread, with "Calculando…" until done.
- Every new visual state must keep the rule "focus = cyan ring + fill (+ halo); marked = amber bar + check + text".
