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
| B | Grid mode: Home cards ("Pastas principais" with real counts/sizes, "Unidades e dispositivos" with usage bars), folder tiles, This PC | planned |
| C | List mode: column header, friendly dates, details panel | planned |

Legend for the matrix: **Where** is the place in the new shell (after the phase in brackets). **Test** names the automated
test that protects the behavior (`File::Method`, unit tests under `tests/ControlFS.UnitTests`, Windows-only ones under
`tests/ControlFS.WindowsIntegrationTests`), `UIA` for `build/Test-UiAutomation.ps1` (Smoke workflow), `Screens` for the
`--render-screens` captures (Smoke workflow) or **Manual** with the `docs/TESTING.md` section. "Manual only" rows are
hardware, visual or timing checks that CI can't prove.

## Shell

```
┌ [ControlFS logo with text] [tabs · RB]                         [controller · operation] ┐  (system title bar above)
├ [LB] [Locais|Meu computador] › segment › … › current │ Favoritos · Arquivos recentes · known folders · Meu computador · Lixeira
├ badge line (archive summary, search, recycle bin, picker title) — only when there is something to say
├ CONTENT: Grid (B) or List (+ details panel, C)
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

| Input | Home | Browser (list region) | Top bar focused | Tab strip focused |
|---|---|---|---|---|
| LB / Ctrl+← | top bar, first quick-access item (A2) | top bar, parent folder segment (#30) | back to content | previous tab |
| RB / Ctrl+→ | — | tab strip (#50) | back to content | next tab |
| Left / Right | grid: move; list: — (Right opens) | grid: move; list: up / open | move across breadcrumb then quick access | switch tab |
| LT / RT | page | page | first / last item of the segment | first / last tab |
| South | open place | open item | go to segment / open quick access | back to content |
| North | place actions | item actions | full path menu (segments only) | new/close tab |
| East | exit dialog | back (selection → search → history → Home) | back to content | back to content |
| Down | move | move | back to content | back to content |
| Start / F10 | app menu | app menu | app menu | app menu |
| Select / Ctrl+F | — | search (disk folders) | — | — |
| R3 / Ctrl+G | Lista ↔ Grade | Lista ↔ Grade | — | — |

## Design tokens (A1)

`src/ControlFS.App/Resources/Theme.cs`. Sizes still scale with `LayoutProfile` (#36: compact / regular / large).

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
| `Danger` | `#FF7A6E` | always with "⚠" text, never color alone |
| `Radius` | 8 | cards, rows, chips |
| `FocusRing` | 2 px (× space scale) | same thickness focused or not (no layout jump) |
| `MotionFast` / `MotionFocus` / `MotionPanel` | 120 / 160 / 200 ms | only `BrushTransition` on focus fills for now |
| Xbox faces | A `#2EB34A`, B `#E5393F`, X `#2C7FE8`, Y `#F4C42F` | glyph body; Xbox family only (other families keep their shapes) |

## Regression matrix

### Input, controllers and prompts

| Feature | Where | Shortcut / flow | Test |
|---|---|---|---|
| Semantic actions only (screens never see buttons) | all | `InputAction` → `AppController.Handle` | all journey tests (`Driver`) |
| Default map by physical position; confirm/back convention swaps behavior and prompts | Menu → "Confirmar com" | South/East | `InputRouterTests::East_confirms_convention_swaps_behavior_by_position`, `PromptJourneyTests` |
| R3 (right stick click) → Lista/Grade | footer "R Lista/Grade" | R3, Ctrl+G, Menu → Exibição | `PromptJourneyTests` (prompt + button), `GridViewJourneyTests`, `DensityJourneyTests` |
| Confirm/menus never repeat; navigation repeats with acceleration | — | hold | `InputRouterTests::Confirm_fires_once_per_press_and_never_repeats`, `::Navigation_repeats_after_initial_delay_with_acceleration` |
| Held button latched across context change | — | — | `InputRouterTests::Held_button_is_latched_on_context_change_until_released`, `::Latched_navigation_does_not_repeat_into_new_context`, `::Context_change_triggered_by_a_repeat_keeps_the_control_latched` |
| Active device, hot swap, sensitive-context lock | footer header status | press on another controller | `InputRouterTests::Another_device_takes_over_only_with_a_new_press_while_the_active_one_is_idle`, `::Sensitive_context_blocks_automatic_device_takeover` |
| Suspension when the window loses focus | — | Alt+Tab | `InputRouterTests::Suspended_router_ignores_input_and_resume_requires_new_press`; Manual (TESTING "Antes de cada release") |
| Stick deadzone, hysteresis, diagonals | — | left stick | `StickNormalizerTests` (5 tests) |
| Active controller menu, duplicates (Steam Input/DS4Windows) | Menu → Controle ativo | Start | `ActiveControllerJourneyTests`, `InputRouterTests::Explicitly_selected_device_is_the_only_one_routed_until_automatic_or_removed`; Manual "Controle ativo e duplicatas (#80)" |
| Controller test screen + report | Menu → Teste de controles… | hold South/East | `ControllerTestJourneyTests`; Manual "Teste de controles (#78)" |
| Raw joystick wizard, profiles import/export, two-button long press | Menu → Controles sem perfil… | — | `ControllerMappingWizardTests` (5), `ControllerMappingJourneyTests` (3), `ControllerProfileSerializerTests` (3); Manual "Joystick sem perfil (#79)" |
| Family detection and label style (automatic/generic/Xbox/PS/Nintendo) | Menu → Legendas | — | `ControllerFamilyTests` (3), `PromptJourneyTests` |
| Dynamic footer prompts, hot swap, keyboard keys when typing on a physical keyboard | footer | — | `PromptJourneyTests`, `HintJourneyTests` (4), `JourneyTests::Footer_hints_only_show_actions_that_work_in_context` |
| Footer order (A1): Confirm, Back, Mark, Actions, Menu, Search, Lista/Grade, LB, RB (screens only; modals keep theirs) | footer | — | `PromptJourneyTests` |
| Xbox face colors in glyphs (A green, B red, X blue, Y yellow) | footer, glyph gallery | — | Screens (`glyphs/`); Manual "Glifos dos botões" |
| Glyphs per family (PS shapes, Nintendo A/B by position, generic dots, never fake Xbox) | footer | — | `ControllerFamilyTests::Glyph_letters_and_spoken_names_follow_position_per_family`, `InputRouterTests::Glyphs_follow_physical_position_not_letters`; Screens |
| Physical keyboard map | all | arrows, Enter, Esc, Backspace, Space, F2/Menu key, F10, PgUp/PgDn, Home/End (typing), Ctrl+←/→, Ctrl+F, Ctrl+G, Ctrl+A/Ctrl+V (typing), F11 | UIA (F10, arrows, PageDown, Esc, Ctrl+F); Manual for the rest |
| Mouse/touch: item, segment, tab, menu option, dialog option, key | all | click/tap → same action as South | Manual "Barra de caminho (#30)", "Abas (#50)" |
| Guide/Home button never mapped | — | — | `SdlControlMapping` (code review) |

### Screens and navigation

| Feature | Where | Shortcut / flow | Test |
|---|---|---|---|
| Home with places (favorites, Recentes, known folders, drives, Lixeira) | Home content (B: cards) | South opens, North actions | `FavoritesJourneyTests`, `RecentsJourneyTests`, `RecycleBinJourneyTests`, `DriveJourneyTests` |
| Real known folders (Downloads via Known Folder API) and drives with type/label/free space | Home, top bar (A2) | — | `WindowsBehaviorTests::Downloads_comes_from_known_folder_api`, `::Drives_are_listed_as_places`; Manual "Tipos de unidade (#25)" |
| Drive plugged in while open, focus kept | Home | — | `DriveJourneyTests`, `DriveWatcherIntegrationTests` |
| Browse real folders, open, up (Left in list), history Back with focus restore | content | South/Right, Left, East | `JourneyTests::Back_semantics_selection_then_history_then_home_then_confirmed_exit`, `BreadcrumbJourneyTests` |
| Stale listing never overwrites a newer navigation | — | — | `JourneyTests::Late_listing_response_does_not_overwrite_newer_navigation` |
| Focus by identity, survives resort/removal; focus ≠ selection | content | — | `StateTests` (5) |
| Breadcrumb / path bar with archive boundary and collapse, root chip (Locais / Meu computador) | top bar left segment (A2) | LB, Left/Right, South, North = full path | `BreadcrumbJourneyTests` (3), `TopBarJourneyTests`; Manual "Barra de caminho (#30)" |
| Quick access: Favoritos, Arquivos recentes, known folders, Meu computador, Lixeira | top bar right segment (A2) | LB then Right, South | `TopBarJourneyTests`; Manual "Barra superior e cabeçalho (redesenho, fase A2)" |
| Tabs (8 max), each with its own folder/history/marks/focus | header tab strip (A2) | RB, LB/RB, North new/close, "Abrir em nova aba" | `TabsJourneyTests`; Manual "Abas (#50)" |
| Go to path (typed/pasted, quotes, %VARS%) | Menu → Ir para caminho… | Start | `GoToPathJourneyTests`, `TypedPathTests` |
| Go to folder above… | Menu | Start | `BreadcrumbJourneyTests` (same menu as the `…` segment) |
| Go home | Menu → Ir para o início; top bar root chip (A2) | Start / LB | `JourneyTests::Back_semantics_…` |
| Favorites: add/remove/reorder, missing kept until removed, first on Home and in the picker | Home, item actions, top bar (A2) | North → Adicionar aos favoritos | `FavoritesJourneyTests` |
| Recents: bounded, persisted, clear, turn off | Home "Recentes", Menu → Recentes, top bar (A2) | North on Recentes | `RecentsJourneyTests` |
| Recycle Bin: list, restore (never overwrite), permanent delete asks on Cancel | Home, top bar (A2) | South/North on an item | `RecycleBinJourneyTests`, `RecycleBinIntegrationTests` (2), `UndoJourneyTests::Undo_of_a_recycle_…` |
| Search (on-screen keyboard, streaming, partial/complete, skipped folders, cancel keeps partial) | results in the current mode | Select/View, Ctrl+F | `SearchJourneyTests` (2), `SearchIntegrationTests` (3); Manual "Busca (#46)" |
| Search filters (type/size/date), subfolders toggle | North on results | — | `SearchFilterJourneyTests` |
| OneDrive files-on-demand folders searched without downloading | — | — | `SearchIntegrationTests::Reparse_tag_…`; Manual "OneDrive sob demanda (#126)" |
| Grid view with 2D navigation, persisted; switching keeps focus | Menu → Exibição, R3, Ctrl+G | — | `GridViewJourneyTests` |
| Density comfortable/compact, persisted | Menu → Densidade da lista | — | `DensityJourneyTests` |
| Sort by name/type/size/date, ascending/descending, natural sort | Menu → Ordenar por / Ordem (C: column header) | Start | `StateTests::Natural_sort_orders_numbers_numerically`, `::Focus_survives_resort_by_identity` |
| Hidden items show/hide (persisted) | Menu → Itens ocultos | Start | Manual "Lista: estados e densidade (#28)" |
| Refresh | Menu → Atualizar | Start | journey tests that call Refresh indirectly (`FileOperationJourneyTests`) |
| Folder picker (copy/move/extract destination, create folder, other places, go to path, cancel) | full-screen picker, same shell | Start = Escolher esta pasta… | `FileOperationJourneyTests::Copy_to_a_folder_with_the_picker_and_keep_both_on_conflict` |
| Exit confirmation starts on Cancel | Home East, Menu → Sair | East | `JourneyTests::Back_semantics_…`, UIA |

### Item actions (North menus)

| Feature | Where | Test |
|---|---|---|
| Folder: Abrir, Abrir no Explorador, Abrir em nova aba, Renomear, Copiar, Recortar, Copiar para…, Mover para…, Compactar…, Excluir…, favoritos, Colar, Nova pasta aqui, Marcar todos / Limpar marcação, Propriedades | Y Ações | `RenameJourneyTests`, `ClipboardJourneyTests`, `FileOperationJourneyTests`, `ShellAndCompressJourneyTests`, `DeleteJourneyTests`, `SelectAllJourneyTests`, `TabsJourneyTests` |
| File: Abrir compactado, Extrair para "x" / aqui / para…, Testar integridade, Visualizar imagem / como texto, Executar… / Abrir com o aplicativo padrão, Abrir com…, Mostrar no Explorador, file ops as above, Propriedades | Y Ações (opens on "Extrair para" for archives) | `HintJourneyTests::Archive_file_offers_explore_mark_and_extract_…`, `ShellAndCompressJourneyTests` (5), `ImagePreviewJourneyTests`, `TextPreviewJourneyTests`, `JourneyTests::Test_integrity_…` |
| Marked items: Extrair cada um (batch), Copiar/Recortar/Copiar para/Mover para/Compactar/Excluir N, Marcar todos, Limpar marcação | Y Operações (N) | `BatchExtractionJourneyTests`, `FileOperationJourneyTests::Move_marked_items_to_a_folder`, `SelectAllJourneyTests` |
| Inside an archive: Extrair tudo para "x" / aqui / para…, Extrair seleção (N) para "x" / para…, Testar integridade, Informações do compactado | Y Extrair… | `ArchiveBrowserJourneyTests` (2), `JourneyTests::Archive_is_browsed_read_only_and_back_returns_to_disk` |
| Home place: Abrir, favoritos, Mover favorito para cima/baixo; Recentes: Abrir, Limpar, Desligar | Y Ações on Home | `FavoritesJourneyTests`, `RecentsJourneyTests` |
| Recycle Bin: Restaurar, Excluir permanentemente…, Propriedades, Marcar todos, Atualizar | Y Ações in Lixeira | `RecycleBinJourneyTests` |
| Search: filters, Outras ações (Mostrar na pasta, Nova busca, Subpastas, Pastas puladas, Cancelar busca, Propriedades) | Y Filtros | `SearchFilterJourneyTests`, `SearchJourneyTests` |
| Properties with folder size on demand (cancel keeps partial, junctions not followed) | Y → Propriedades (C: details panel shows the same real data) | `FolderSizeJourneyTests`, `FolderSizeIntegrationTests` |
| Tab strip: Nova aba, Fechar aba | North on the strip | `TabsJourneyTests` |
| Path bar: full path menu | North on a segment | `BreadcrumbJourneyTests` |

### App menu (Start)

Every entry stays in the Menu (Start/F10); nothing moves out without a replacement in the same PR.

| Entry | Test |
|---|---|
| Colar, Nova pasta, Atualizar, Ir para pasta acima…, Ir para caminho… | `ClipboardJourneyTests`, `JourneyTests::Vertical_journey_…`, `GoToPathJourneyTests` |
| Ordenar por, Ordem | `StateTests` (sorting); Manual |
| Busca em subpastas | `SearchJourneyTests` |
| Recentes: lembrar/não lembrar | `RecentsJourneyTests` |
| Itens ocultos | Manual "Lista: estados e densidade (#28)" |
| Exibição: lista/grade | `GridViewJourneyTests` |
| Densidade da lista | `DensityJourneyTests` |
| Operações (N ativas) → operation → Pausar/Continuar/Cancelar operação/Tentar de novo; Limpar histórico… | `PauseJourneyTests`, `FileOperationJourneyTests::Retry_…` (2), `JourneyTests::Retry_failed_items_of_a_cancelled_extraction_…`, `HistoryJourneyTests` (2) |
| Desfazer / Refazer | `UndoJourneyTests` (4) |
| Confirmar com | `PromptJourneyTests`, `InputRouterTests` |
| Legendas | `PromptJourneyTests`, `ControllerFamilyTests` |
| Teste de controles…, Controle ativo, Controles sem perfil… | `ControllerTestJourneyTests`, `ActiveControllerJourneyTests`, `ControllerMappingJourneyTests` |
| Atualizações (Instalar e reiniciar, Verificar agora, automático, instalar ao sair, pré-lançamento) | `UpdateFlowTests` (6), `UpdateServiceTests` (12) |
| Esvaziar área de transferência | `ClipboardJourneyTests` |
| Sobre o ControlFS | `AboutJourneyTests` |
| Ir para o início, Sair | `JourneyTests::Back_semantics_…`, UIA |

### File operations and Central de Operações

| Feature | Test |
|---|---|
| Copy/move byte-identical, folder into itself refused, conflicts never silent (skip/keep both/replace, merge only when chosen), links not followed, cancel leaves nothing partial, pause/resume mid-file | `FileOperationServiceTests` (11), `FileOperationIntegrationTests` (2) |
| Delete to Recycle Bin / permanent (junction removed, target kept) | `DeleteJourneyTests`, `DeleteIntegrationTests` (2) |
| Rename (stem preselected, extension change asks, collisions refused, case-only) | `RenameJourneyTests` (2), `RenameIntegrationTests` |
| Create folder (accented, invalid name keeps keyboard with the reason) | `JourneyTests::Vertical_journey_…`, `::Invalid_folder_name_keeps_keyboard_open_with_reason` |
| Clipboard copy/cut/paste across folders and tabs | `ClipboardJourneyTests` (2) |
| Compress (zip/tar.gz, name typed, existing never overwritten, links not followed) | `ShellAndCompressJourneyTests::Compress_…` (2), `ArchiveCreatorTests` (4) |
| Operations center: progress, cancel, results, errors, retry, history persisted without passwords | `PauseJourneyTests`, `HistoryJourneyTests`, `FileOperationJourneyTests`; header status text (Manual) |
| Undo/redo with checks | `UndoJourneyTests` (4) |
| Leftover cleanup after a crash | `LeftoverCleanupTests` (2); Manual "Limpeza após queda" |
| Open with Windows, executables ask first starting on Cancel, leaving-app warning | `ShellAndCompressJourneyTests`, `ShellIntegrationTests` (3) |

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
| Never auto-execute | `ShellAndCompressJourneyTests::Executables_require_explicit_confirmation_starting_on_cancel` |

### Previews, keyboard, dialogs

| Feature | Where | Test |
|---|---|---|
| Image preview (zoom, pan, next/previous, bomb refusal, EXIF) | modal over the shell | `ImagePreviewJourneyTests`, `ImagePreviewPolicyTests` (3); Manual "Visualização de imagens (#57)" |
| Text preview (encodings, limits, binary refused) | modal | `TextPreviewJourneyTests`, `TextPreviewTests` (2); Manual "(#58)" |
| On-screen keyboard: PT-BR/EN, shift/caps, numbers, symbols, accents, space, backspace (repeat), clear, caret, selection, OK/cancel, name/path/password fields, masking/reveal, controller navigation | modal; footer shows Selecionar/Apagar/…/Concluir/Cancelar | `VirtualKeyboardTests` (13), `HintJourneyTests::On_screen_keyboard_…`, UIA |
| Dialogs name the focused choice; destructive dialogs start on the safe option | modal | `HintJourneyTests::Dialogs_and_menus_…`, UIA |
| Updates (installed/portable, signature, SHA, relaunch, notifications) | Menu → Atualizações; header status | `UpdateServiceTests` (12), `UpdateFlowTests` (6), `ReleaseVersionTests` |
| About (version, license, source) | Menu → Sobre | `AboutJourneyTests` |

### Accessibility, layout and window

| Feature | Test |
|---|---|
| Narrator announces context then item, states in words, live status line | `ScreenReaderJourneyTests`; Manual "Narrador (#40)" |
| Responsive tiers (compact/regular/large), Windows text scale | `LayoutBreakpointsTests` (3); Screens (720p, 800p, 800p+150% text, 1080p, 1080p@150%, 4K@100/200/300%); Manual "Layout responsivo (#36)" |
| Shell icons (known folders, drives, file associations), cached, async | `IconRequestTests` (2), `ShellIconIntegrationTests`; Manual "Ícones do Windows (#24)" |
| Full screen (F11) and windowed (min/max/close/resize/move) | Manual "Antes de cada release" |
| Settings persisted (view, density, favorites, recents, hidden, labels, convention, updates) and migrated | `DensityJourneyTests`, `GridViewJourneyTests`, `ControllerFamilyTests::Settings_v1_…`, `FavoritesJourneyTests`, `RecentsJourneyTests` |

## Notes for phases B and C

- `AppController.IsGrid`, `SetGridLayout(columns, rows)` and `GridNavigation` already give 2D focus with the same columns
  the view shows. Cards must publish their real column count the same way (`UpdateGridMetrics`).
- The footer label for ChangeView is computed from `IsGrid` (target view); don't hardcode it in views.
- Keep `EntryRowTemplate.Fill` as the single place that turns a `FileEntry` into text; the details panel should reuse
  `EntryText` so list, grid, details and Narrator say the same thing.
- Recursive sizes/counts on Home cards must reuse the folder-size walker (#55: never follows junctions, cancellable)
  off the UI thread, with "Calculando…" until done.
- Every new visual state must keep the rule "focus = cyan ring + fill (+ halo); marked = amber bar + check + text".
