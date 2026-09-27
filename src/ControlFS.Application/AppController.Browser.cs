using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Input;
using ControlFS.Core.Models;
using ControlFS.Core.Policies;
using ControlFS.Core.Text;

namespace ControlFS.Application;

public sealed partial class AppController
{
    private void HandlePane(PaneState pane, InputAction action)
    {
        if (HandleRegionSwitch(pane, action)) return;
        var list = pane.List;
        if (IsGrid && GridNavigation.Move(list.FocusIndex, list.Items.Count, GridColumns, GridRowsPerPage, action) is { } cell)
        {
            list.FocusAt(cell);
            return;
        }
        switch (action)
        {
            case InputAction.NavigateUp: list.Move(-1); break;
            case InputAction.NavigateDown: list.Move(1); break;
            case InputAction.PageUp: list.Move(-PageSize); break;
            case InputAction.PageDown: list.Move(PageSize); break;
            case InputAction.NavigateLeft: GoUp(pane); break;
            case InputAction.NavigateRight:
                if (list.Focused is { IsContainer: true } folder) OpenEntry(pane, folder);
                break;
            case InputAction.Confirm:
                if (list.Focused is { } entry) OpenEntry(pane, entry);
                break;
            case InputAction.Back: Back(pane); break;
            case InputAction.ToggleSelection:
                if (pane.Mode == PaneMode.Browse && !pane.IsLoading && pane.Location is not SearchLocation)
                {
                    if (list.Focused is { IsBlocked: true } blocked) StatusMessage = blocked.BlockedReason;
                    else list.ToggleFocusedSelection();
                }
                break;
            case InputAction.OpenContextMenu:
                if (pane.Mode == PaneMode.PickFolder) ShowPickerMenu();
                else if (pane.ActiveSearch is { } search) ShowSearchFilters(pane, search);
                else ShowItemMenu(pane);
                break;
            case InputAction.Search:
                BeginSearch(pane);
                break;
            case InputAction.OpenAppMenu:
                if (pane.Mode == PaneMode.PickFolder) ShowPickerMenu();
                else ShowAppMenu();
                break;
            case InputAction.ChangeView: ToggleView(); break;
        }
    }

    /// <summary>A grade ocupa a tela toda: esquerda/direita andam entre blocos (Voltar e a barra de caminho sobem de pasta).</summary>
    public bool IsGrid => Settings.View == ViewMode.Grid;

    /// <summary>Colunas da grade e linhas visíveis (LB/RB/gatilhos paginam por tela), publicadas pela view ao medir.</summary>
    public int GridColumns { get; private set; } = 1;

    public int GridRowsPerPage { get; private set; } = 1;

    public void SetGridLayout(int columns, int rowsPerPage)
    {
        GridColumns = Math.Max(1, columns);
        GridRowsPerPage = Math.Max(1, rowsPerPage);
    }

    /// <summary>Alterna lista/grade (preferência salva). O foco continua no mesmo item: ele é guardado pela identidade.</summary>
    internal void ToggleView()
    {
        UpdateSettings(s => s with { View = s.View == ViewMode.Grid ? ViewMode.List : ViewMode.Grid });
        StatusMessage = $"Exibição em {ViewName(Settings.View)}.";
    }

    private static string ViewName(ViewMode view) => view == ViewMode.Grid ? "grade" : "lista";

    /// <summary>Alterna a densidade da lista (preferência salva): confortável para TV, compacta para ver mais itens.</summary>
    internal void ToggleDensity()
    {
        UpdateSettings(s => s with { Density = s.Density == ListDensity.Compact ? ListDensity.Comfortable : ListDensity.Compact });
        StatusMessage = $"Lista {DensityName(Settings.Density)}.";
    }

    private static string DensityName(ListDensity density) => density == ListDensity.Compact ? "compacta" : "confortável";

    private void OpenEntry(PaneState pane, FileEntry entry)
    {
        if (pane.IsLoading) return;
        if (pane.Location is SearchLocation)
        {
            RevealResult(pane, entry);
            return;
        }
        if (pane.Location is RecycleBinLocation)
        {
            ShowRecycleBinMenu(pane);
            return;
        }
        if (entry.IsBlocked)
        {
            ShowMessage("Entrada bloqueada", [("Nome", entry.Name), ("Motivo", entry.BlockedReason!)]);
            return;
        }
        switch (entry.Kind)
        {
            case EntryKind.Directory or EntryKind.Drive or EntryKind.KnownFolder when entry.FullPath is { } path:
                Track(NavigateAsync(pane, new PhysicalLocation(path), pushHistory: true));
                break;
            case EntryKind.ArchiveDirectory when pane.Location is ArchiveLocation archive:
                Track(NavigateAsync(pane, archive with { InnerPath = ArchiveTree.PathFromId(entry.Id) }, pushHistory: true));
                break;
            case EntryKind.ArchiveFile:
                ShowArchiveEntryInfo(entry);
                break;
            case EntryKind.File when entry.FullPath is { } file && pane.Mode == PaneMode.Browse:
                Track(OpenFileAsync(pane, entry, file));
                break;
        }
    }

    private async Task OpenFileAsync(PaneState pane, FileEntry entry, string path)
    {
        var format = await Task.Run(() => _archives.Detect(path));
        if (ArchiveFormats.CanExtract(format)) await OpenArchiveAsync(pane, path);
        else if (IsPreviewableImage(entry) && _imageDecoder is not null) OpenImagePreview(pane, entry);
        else if (OpensAsText(entry)) OpenTextPreview(pane, entry);
        else OpenExternally(entry, path);
    }

    internal async Task OpenArchiveAsync(PaneState pane, string archivePath, string? password = null)
    {
        var generation = ++pane.Generation;
        pane.LoadCts?.Cancel();
        var cts = pane.LoadCts = new CancellationTokenSource();
        pane.IsLoading = true;
        RaiseChanged();
        try
        {
            var info = await _archives.InspectAsync(archivePath, password, Limits, cts.Token);
            if (generation != pane.Generation) return;
            var tree = new ArchiveTree(info, Limits);
            if (pane.Location is { } current) PushHistory(pane, current);
            pane.Archive = tree;
            pane.ArchivePassword = password;
            pane.Location = new ArchiveLocation(archivePath, string.Empty);
            pane.List.SetItems(tree.Children(string.Empty), newLocation: true);
            if (pane.Mode == PaneMode.Browse) RecordRecentFile(archivePath);
            if (tree.BlockedCount > 0) StatusMessage = $"{tree.BlockedCount} entrada(s) com nome inseguro foram bloqueadas.";
        }
        catch (ArchiveAccessException ex) when (generation == pane.Generation && ex.Kind is OperationErrorKind.PasswordRequired or OperationErrorKind.WrongPassword)
        {
            // Cabeçalhos protegidos: sem a senha nem a lista de arquivos pode ser lida.
            AskArchivePassword(pane, archivePath, ex.Kind == OperationErrorKind.WrongPassword ? "Senha incorreta. Tente novamente." : null);
        }
        catch (ArchiveAccessException ex) when (generation == pane.Generation)
        {
            ShowMessage("Não foi possível abrir o compactado", [("Arquivo", Path.GetFileName(archivePath)), ("Motivo", ex.Message)]);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (generation == pane.Generation) pane.IsLoading = false;
            RaiseChanged();
        }
    }

    /// <summary>Navega com proteção de geração: uma resposta atrasada não atualiza a pasta errada.</summary>
    internal async Task NavigateAsync(PaneState pane, Location target, bool pushHistory, string? focusId = null)
    {
        var generation = ++pane.Generation;
        pane.LoadCts?.Cancel();
        var cts = pane.LoadCts = new CancellationTokenSource();
        pane.IsLoading = true;
        RaiseChanged();
        try
        {
            IReadOnlyList<FileEntry> entries;
            var inaccessible = 0;
            ArchiveTree? tree = null;
            switch (target)
            {
                case PhysicalLocation physical:
                    var listing = await _fs.ListAsync(physical.FullPath, Settings.ShowHidden, cts.Token);
                    entries = pane.Mode == PaneMode.PickFolder ? listing.Entries.Where(e => e.IsContainer).ToList() : listing.Entries;
                    inaccessible = listing.InaccessibleCount;
                    target = new PhysicalLocation(listing.Path);
                    break;
                case ArchiveLocation archive when pane.Archive is { } current && current.Info.ArchivePath == archive.ArchivePath && current.DirectoryExists(archive.InnerPath):
                    tree = current;
                    entries = current.Children(archive.InnerPath);
                    break;
                case ThisPcLocation when pane.Mode == PaneMode.Browse:
                    entries = await Task.Run(ThisPcEntries, cts.Token); // unidades de rede podem demorar a responder
                    break;
                case RecycleBinLocation when _recycleBin is { } bin && pane.Mode == PaneMode.Browse:
                    entries = RecycledEntries(await Task.Run(() => bin.List(cts.Token), cts.Token));
                    break;
                case SearchLocation search when pane.Search is { } state && state.Location == search:
                    // Voltar de um resultado aberto: os resultados guardados reaparecem (a busca não roda de novo).
                    entries = [.. state.VisibleResults()];
                    break;
                default:
                    return;
            }
            if (generation != pane.Generation) return;
            if (pushHistory && pane.Location is { } previous) PushHistory(pane, previous);
            var newLocation = pane.Location != target; // outra pasta: sem item pedido, o foco começa no primeiro
            pane.Archive = tree;
            if (tree is null) pane.ArchivePassword = null;
            pane.Location = target;
            pane.InaccessibleCount = inaccessible;
            pane.List.SetItems(entries, focusId, newLocation: newLocation);
            if (target is PhysicalLocation p)
            {
                pane.LastValidPhysical = p;
                if (pane.Mode == PaneMode.Browse) RecordVisit(p.FullPath);
            }
        }
        catch (FileOperationException ex) when (generation == pane.Generation)
        {
            ShowMessage("Não foi possível abrir a pasta", [("Pasta", target.DisplayPath), ("Motivo", ex.Message)]);
            if (pane.Location is null) FallbackAfterFailedOpen(pane);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (generation == pane.Generation) pane.IsLoading = false;
            RaiseChanged();
        }
    }

    private void FallbackAfterFailedOpen(PaneState pane)
    {
        if (pane.Mode == PaneMode.Browse) GoHome();
        else CancelPicker();
    }

    private static void PushHistory(PaneState pane, Location current)
    {
        pane.Back.Push((current, pane.List.FocusedId));
        pane.Forward.Clear();
    }

    internal void Refresh(PaneState pane, string? focusId = null)
    {
        if (pane.Location is SearchLocation search) StartSearch(pane, search);
        else if (pane.Location is { } location)
            Track(NavigateAsync(pane, location, pushHistory: false, focusId ?? pane.List.FocusedId));
    }

    /// <summary>
    /// Voltar: fecha modo transitório (seleção), depois retorna à localização anterior válida;
    /// sem histórico, sobe para a tela inicial (que só encerra com confirmação).
    /// </summary>
    private void Back(PaneState pane)
    {
        if (pane.List.SelectionCount > 0)
        {
            pane.List.ClearSelection();
            return;
        }
        if (pane.ActiveSearch is { IsRunning: true } running)
        {
            CancelSearch(running);
            return;
        }
        if (pane.IsLoading)
        {
            pane.LoadCts?.Cancel();
            pane.Generation++;
            pane.IsLoading = false;
            if (pane.Location is null) FallbackAfterFailedOpen(pane);
            return;
        }
        if (pane.Back.Count > 0)
        {
            var (location, focusId) = pane.Back.Pop();
            if (pane.Location is { } current) pane.Forward.Push((current, pane.List.FocusedId));
            // Compactado já fechado ou busca substituída por outra: não há o que restaurar, segue voltando.
            if ((location is ArchiveLocation && pane.Archive is null) || (location is SearchLocation old && pane.Search?.Location != old))
            {
                Back(pane);
                return;
            }
            Track(NavigateAsync(pane, location, pushHistory: false, focusId));
            return;
        }
        if (pane.Mode == PaneMode.PickFolder) CancelPicker();
        else GoHome();
    }

    private void GoUp(PaneState pane)
    {
        if (pane.IsLoading) return;
        switch (pane.Location)
        {
            case ArchiveLocation { InnerPath.Length: > 0 } archive:
                var parent = archive.InnerPath.Contains('/') ? archive.InnerPath[..archive.InnerPath.LastIndexOf('/')] : string.Empty;
                Track(NavigateAsync(pane, archive with { InnerPath = parent }, pushHistory: true, focusId: ArchiveTree.IdPrefix + archive.InnerPath + "/"));
                break;
            case ArchiveLocation archive:
                var folder = Path.GetDirectoryName(archive.ArchivePath);
                if (folder is not null) Track(NavigateAsync(pane, new PhysicalLocation(folder), pushHistory: true, focusId: Path.GetFileName(archive.ArchivePath)));
                break;
            case PhysicalLocation physical when _fs.GetParent(physical.FullPath) is { } up:
                Track(NavigateAsync(pane, new PhysicalLocation(up), pushHistory: true, focusId: Path.GetFileName(Path.TrimEndingDirectorySeparator(physical.FullPath))));
                break;
        }
    }

    // ---------- Menus ----------

    private void ShowItemMenu(PaneState pane)
    {
        if (pane.IsLoading) return;
        if (pane.Location is ArchiveLocation archive)
        {
            ShowArchiveMenu(pane, archive);
            return;
        }
        if (pane.Location is RecycleBinLocation)
        {
            ShowRecycleBinMenu(pane);
            return;
        }
        if (pane.Location is ThisPcLocation)
        {
            ShowDriveMenu(pane);
            return;
        }
        var marked = pane.List.SelectedEntries.Where(e => e.FullPath is not null).ToList();
        if (marked.Count > 0 && pane.Location is PhysicalLocation)
        {
            // Compactados marcados: "extrair cada um" vem primeiro, então Norte e depois Sul extraem o lote (#69).
            var markedArchives = MarkedArchives(marked);
            PushModal(new MenuModal($"{marked.Count} item(ns) marcado(s)",
            [
                .. markedArchives.Count == 0 ? Array.Empty<MenuItem>() :
                [
                    new MenuItem($"Extrair cada um para a própria pasta ({markedArchives.Count})", () => BeginBatchExtraction(pane, markedArchives),
                        Detail: markedArchives.Count < marked.Count ? "Itens que não são compactados ficam de fora." : null),
                ],
                new MenuItem($"Copiar {marked.Count} item(ns)", () => PutOnClipboard(pane, marked, FileOperationKind.Copy), FileOpsUnavailable),
                new MenuItem($"Recortar {marked.Count} item(ns)", () => PutOnClipboard(pane, marked, FileOperationKind.Move), FileOpsUnavailable),
                new MenuItem($"Copiar {marked.Count} item(ns) para…", () => BeginTransferTo(pane, marked, FileOperationKind.Copy), FileOpsUnavailable),
                new MenuItem($"Mover {marked.Count} item(ns) para…", () => BeginTransferTo(pane, marked, FileOperationKind.Move), FileOpsUnavailable),
                new MenuItem($"Compactar {marked.Count} item(ns)…", () => BeginCompress(pane, marked)),
                new MenuItem($"Excluir {marked.Count} item(ns)…", () => BeginDelete(pane, marked), FileOpsUnavailable),
                .. SelectionItems(pane),
            ]));
            return;
        }
        var entry = pane.List.Focused;
        if (entry is { Kind: EntryKind.File, FullPath: { } file })
        {
            Track(ShowFileMenuAsync(pane, entry, file));
            return;
        }
        var items = new List<MenuItem>();
        if (entry is { IsContainer: true }) items.Add(new MenuItem("Abrir", () => OpenEntry(pane, entry)));
        if (entry is { IsContainer: true, FullPath: { } folderPath })
            items.Add(new MenuItem("Abrir no Explorador de Arquivos", () => RunShell(s => s.Open(folderPath), external: true), ShellUnavailable));
        if (entry is { IsContainer: true, FullPath: { } tabPath } && pane.Mode == PaneMode.Browse)
            items.Add(new MenuItem("Abrir em nova aba", () => OpenInNewTab(tabPath), NewTabUnavailable));
        if (entry is { Kind: EntryKind.Directory, FullPath: not null } && pane.Location is PhysicalLocation)
        {
            items.Add(new MenuItem("Renomear…", () => BeginRename(pane, entry), FileOpsUnavailable));
            items.Add(new MenuItem("Copiar", () => PutOnClipboard(pane, [entry], FileOperationKind.Copy), FileOpsUnavailable));
            items.Add(new MenuItem("Recortar", () => PutOnClipboard(pane, [entry], FileOperationKind.Move), FileOpsUnavailable));
            items.Add(new MenuItem("Copiar para…", () => BeginTransferTo(pane, [entry], FileOperationKind.Copy), FileOpsUnavailable));
            items.Add(new MenuItem("Mover para…", () => BeginTransferTo(pane, [entry], FileOperationKind.Move), FileOpsUnavailable));
            items.Add(new MenuItem("Compactar…", () => BeginCompress(pane, [entry])));
            items.Add(new MenuItem("Excluir…", () => BeginDelete(pane, [entry]), FileOpsUnavailable));
        }
        if (entry is { IsContainer: true, FullPath: { } favoritePath }) items.Add(FavoriteToggleItem(favoritePath));
        if (pane.Location is PhysicalLocation current) items.Add(FavoriteToggleItem(current.FullPath, "esta pasta"));
        if (Clipboard is not null) items.Add(new MenuItem(PasteLabel, () => Paste(pane), PasteUnavailable(pane)));
        items.Add(new MenuItem("Nova pasta aqui", () => BeginCreateFolder(pane), pane.Location is PhysicalLocation ? null : "Disponível apenas em pastas do disco."));
        items.AddRange(SelectionItems(pane));
        if (entry is not null) items.Add(new MenuItem("Propriedades", () => ShowProperties(entry)));
        PushModal(new MenuModal(entry?.Name ?? "Ações", items));
    }

    /// <summary>Meu computador: só o que vale para uma unidade (nada de marcar, colar ou criar pasta aqui).</summary>
    private void ShowDriveMenu(PaneState pane)
    {
        var items = new List<MenuItem>();
        if (pane.List.Focused is { FullPath: { } path } drive)
        {
            items.Add(new MenuItem("Abrir", () => OpenEntry(pane, drive)));
            items.Add(new MenuItem("Abrir no Explorador de Arquivos", () => RunShell(s => s.Open(path), external: true), ShellUnavailable));
            items.Add(new MenuItem("Abrir em nova aba", () => OpenInNewTab(path), NewTabUnavailable));
            items.Add(FavoriteToggleItem(path));
            items.Add(new MenuItem("Propriedades", () => ShowProperties(drive)));
        }
        items.Add(new MenuItem("Atualizar", () => Refresh(pane)));
        PushModal(new MenuModal(pane.List.Focused?.Name ?? "Meu computador", items));
    }

    /// <summary>
    /// "Marcar todos (N)" e "Limpar marcação (N)", com as contagens. Unidades, pastas especiais e entradas bloqueadas
    /// nunca são marcadas. Só no navegador (o seletor de pasta não marca).
    /// </summary>
    private List<MenuItem> SelectionItems(PaneState pane)
    {
        var items = new List<MenuItem>();
        if (pane.Mode != PaneMode.Browse) return items;
        var list = pane.List;
        var selectable = list.SelectableCount;
        if (list.SelectionCount < selectable || selectable == 0)
            items.Add(new MenuItem($"Marcar todos ({selectable})", () =>
            {
                list.SelectAll();
                StatusMessage = $"{list.SelectionCount} item(ns) marcado(s).";
            }, selectable == 0 ? "Nada aqui pode ser marcado." : null));
        if (list.SelectionCount > 0)
            items.Add(new MenuItem($"Limpar marcação ({list.SelectionCount})", list.ClearSelection));
        return items;
    }

    private async Task ShowFileMenuAsync(PaneState pane, FileEntry entry, string file)
    {
        var format = await Task.Run(() => _archives.Detect(file));
        var items = new List<MenuItem>();
        if (ArchiveFormats.CanExtract(format))
        {
            var folder = Path.GetDirectoryName(file)!;
            var stem = ArchiveFormats.StemOf(file);
            items.Add(new MenuItem($"Abrir compactado ({ArchiveFormats.DisplayName(format)})", () => Track(OpenArchiveAsync(pane, file))));
            items.Add(new MenuItem($"Extrair para \"{stem}\"", () => BeginExtraction(file, folder, dedicated: true, null, string.Empty)));
            items.Add(new MenuItem("Extrair aqui", () => BeginExtraction(file, folder, dedicated: false, null, string.Empty)));
            items.Add(new MenuItem("Extrair para…", () => PickDestinationThenExtract(file, folder, null, string.Empty)));
            items.Add(TestIntegrityItem(file));
        }
        if (IsPreviewableImage(entry)) items.Add(new MenuItem("Visualizar imagem", () => OpenImagePreview(pane, entry), ImagePreviewUnavailable));
        else if (!ArchiveFormats.CanExtract(format))
            items.Add(new MenuItem("Visualizar como texto", () => OpenTextPreview(pane, entry), Detail: "Somente leitura; nada é executado."));
        items.Add(new MenuItem(ExecutableFiles.IsPotentiallyExecutable(file) ? "Executar…" : "Abrir com o aplicativo padrão",
            () => OpenExternally(entry, file), ShellUnavailable));
        items.Add(new MenuItem("Abrir com…", () => RunShell(s => s.OpenWith(file), external: true), ShellUnavailable,
            Detail: "Escolher o programa na caixa do Windows."));
        items.Add(new MenuItem("Mostrar no Explorador de Arquivos", () => RunShell(s => s.RevealInExplorer(file), external: true), ShellUnavailable));
        items.Add(new MenuItem("Renomear…", () => BeginRename(pane, entry), FileOpsUnavailable));
        items.Add(new MenuItem("Copiar", () => PutOnClipboard(pane, [entry], FileOperationKind.Copy), FileOpsUnavailable));
        items.Add(new MenuItem("Recortar", () => PutOnClipboard(pane, [entry], FileOperationKind.Move), FileOpsUnavailable));
        items.Add(new MenuItem("Copiar para…", () => BeginTransferTo(pane, [entry], FileOperationKind.Copy), FileOpsUnavailable));
        items.Add(new MenuItem("Mover para…", () => BeginTransferTo(pane, [entry], FileOperationKind.Move), FileOpsUnavailable));
        items.Add(new MenuItem("Compactar…", () => BeginCompress(pane, [entry])));
        items.Add(new MenuItem("Excluir…", () => BeginDelete(pane, [entry]), FileOpsUnavailable));
        if (pane.Location is PhysicalLocation current) items.Add(FavoriteToggleItem(current.FullPath, "esta pasta"));
        if (Clipboard is not null) items.Add(new MenuItem(PasteLabel, () => Paste(pane), PasteUnavailable(pane)));
        items.Add(new MenuItem("Nova pasta aqui", () => BeginCreateFolder(pane)));
        items.AddRange(SelectionItems(pane));
        items.Add(new MenuItem("Propriedades", () => ShowProperties(entry)));
        // Compactado: o rodapé anuncia "Extrair…" neste botão, então o menu abre em "Extrair para <nome>".
        PushModal(new MenuModal(entry.Name, items) { FocusIndex = ArchiveFormats.CanExtract(format) ? 1 : 0 });
    }

    private MenuItem TestIntegrityItem(string archivePath) => new("Testar integridade", () => BeginArchiveTest(archivePath),
        Detail: "Lê todas as entradas e confere o CRC sem extrair nada. Não é antivírus.");

    private void ShowArchiveMenu(PaneState pane, ArchiveLocation archive)
    {
        var path = archive.ArchivePath;
        var folder = Path.GetDirectoryName(path)!;
        var stem = ArchiveFormats.StemOf(path);
        var selected = pane.List.SelectedIds.Select(ArchiveTree.PathFromId).Where(p => p.Length > 0).ToList();
        var items = new List<MenuItem>
        {
            new($"Extrair tudo para \"{stem}\"", () => BeginExtraction(path, folder, dedicated: true, null, string.Empty)),
            new("Extrair tudo aqui (pasta do arquivo)", () => BeginExtraction(path, folder, dedicated: false, null, string.Empty)),
            new("Extrair tudo para…", () => PickDestinationThenExtract(path, folder, null, string.Empty)),
        };
        var noSelection = selected.Count == 0 ? "Marque entradas primeiro (botão de marcar)." : null;
        var selectionIndex = items.Count;
        items.Add(new MenuItem($"Extrair seleção ({selected.Count}) para \"{stem}\"", () => BeginExtraction(path, folder, dedicated: true, selected, archive.InnerPath), noSelection));
        items.Add(new MenuItem($"Extrair seleção ({selected.Count}) para…", () => PickDestinationThenExtract(path, folder, selected, archive.InnerPath), noSelection));
        items.AddRange(SelectionItems(pane));
        items.Add(TestIntegrityItem(path));
        items.Add(new MenuItem("Informações do compactado", () => ShowArchiveInfo(pane)));
        // Com entradas marcadas, o rodapé anuncia "Extrair seleção": o menu abre já nela (Norte e depois Sul extraem).
        PushModal(new MenuModal(Path.GetFileName(path) + " (somente leitura)", items) { FocusIndex = selected.Count > 0 ? selectionIndex : 0 });
    }

    private void ShowAppMenu()
    {
        var pane = Browser;
        var inBrowser = Screen == Screen.Browser;
        var sort = pane.List.Sort;
        var items = new List<MenuItem>
        {
            new(PasteLabel, () => Paste(pane), inBrowser ? PasteUnavailable(pane) : "Abra uma pasta do disco para colar."),
            new("Nova pasta", () => BeginCreateFolder(pane), inBrowser && pane.Location is PhysicalLocation ? null : "Abra uma pasta do disco primeiro."),
            new("Atualizar", () => Refresh(pane), inBrowser ? null : "Nada para atualizar na tela inicial."),
            new("Ir para pasta acima…", () => ShowPathMenu(pane), inBrowser && BuildBreadcrumbs(pane).Count > 1 ? null : "Não há pastas acima desta.",
                Detail: "Também pela barra de caminho (botão de ombro esquerdo)."),
            new("Ir para caminho…", () => BeginGoToPath(pane), Detail: "Digite ou cole o caminho de uma pasta (ex.: D:\\Jogos)."),
            new($"Ordenar por: {SortLabel(sort.Field)}", () =>
            {
                pane.List.SetSort(sort with { Field = (SortField)(((int)sort.Field + 1) % 4) });
            }, inBrowser ? null : "Abra uma pasta primeiro."),
            new($"Ordem: {(sort.Descending ? "decrescente" : "crescente")}", () => pane.List.SetSort(sort with { Descending = !sort.Descending }), inBrowser ? null : "Abra uma pasta primeiro."),
            new($"Busca em subpastas: {(SearchIncludesSubfolders ? "incluir" : "não incluir")}", () => SearchIncludesSubfolders = !SearchIncludesSubfolders,
                Detail: "Vale para a próxima busca (Select/View)."),
            new($"Recentes: {(Settings.RememberRecents ? "lembrar" : "não lembrar")}", ToggleRememberRecents,
                Detail: "Pastas e arquivos abertos, só neste computador. Desligar apaga as listas."),
            new($"Itens ocultos: {(Settings.ShowHidden ? "mostrar" : "esconder")}", () =>
            {
                UpdateSettings(s => s with { ShowHidden = !s.ShowHidden });
                if (inBrowser) Refresh(pane);
            }),
            new($"Exibição: {ViewName(Settings.View)}", ToggleView, Detail: "Lista ou grade de ícones grandes (também R3 ou Ctrl+G)."),
            new($"Densidade da lista: {DensityName(Settings.Density)}", ToggleDensity,
                Detail: "Confortável: duas linhas, para TV. Compacta: uma linha com tipo, tamanho e data; na grade, blocos menores."),
            new($"Operações ({Operations.ActiveCount} ativa(s))", ShowOperations, Operations.Items.Count == 0 && History.Entries.Count == 0 ? "Nenhuma operação registrada." : null),
            new($"Confirmar com: {(Settings.Convention == ConfirmBackConvention.SouthConfirms ? "botão inferior" : "botão direito")}", () =>
                UpdateSettings(s => s with { Convention = s.Convention == ConfirmBackConvention.SouthConfirms ? ConfirmBackConvention.EastConfirms : ConfirmBackConvention.SouthConfirms }),
                Detail: "Troca comportamento e legendas de confirmar/voltar."),
            new($"Legendas: {LabelStyleName(Settings.LabelStyle)}", () =>
                UpdateSettings(s => s with { LabelStyle = (ButtonLabelStyle)(((int)s.LabelStyle + 1) % 5) }),
                Detail: Settings.LabelStyle != ButtonLabelStyle.Automatic ? null
                    : ActiveController is { } family ? $"Seguem o controle em uso (agora: {FamilyName(family)})." : "Seguem o controle em uso."),
            new("Teste de controles…", ShowControllerTest, _diagnostics is null ? "Controles indisponíveis nesta compilação." : null,
                Detail: "Mostra cada botão e a ação que ele produz; copia um relatório para a issue #78."),
            new(ActiveControllerMenuLabel, ShowActiveControllerMenu, _diagnostics is null ? "Controles indisponíveis nesta compilação." : null,
                Detail: "Escolha qual controle comanda o ControlFS (ex.: Steam Input ou DS4Windows duplicando o controle)."),
            new("Controles sem perfil…", ShowControllersMenu, Detail: "Configurar joysticks que não são reconhecidos como gamepad."),
            new(UpdateMenuLabel, ShowUpdatesMenu, _updates is null ? "Atualizações indisponíveis nesta compilação." : null),
            new("Esvaziar área de transferência", ClearClipboard, Clipboard is null ? "A área de transferência está vazia." : null),
            new("Sobre o ControlFS", ShowAbout, Detail: $"Versão {AppVersion} · licença AGPL-3.0-only"),
            new("Ir para o início", GoHome, Screen == Screen.Home ? "Você já está no início." : null),
            new("Sair", ShowExitDialog),
        };
        items.InsertRange(items.FindIndex(i => i.Label.StartsWith("Operações (", StringComparison.Ordinal)) + 1, UndoMenuItems());
        PushModal(new MenuModal("Menu", items));
    }

    // ---------- Criar pasta ----------

    public void BeginCreateFolder(PaneState pane)
    {
        if (pane.Location is not PhysicalLocation location) return;
        var keyboard = new VirtualKeyboard(TextFieldKind.FileName, "Nome da nova pasta", "Nova pasta");
        KeyboardModal? modal = null;
        modal = new KeyboardModal(keyboard, async k =>
        {
            var name = k.Text;
            try
            {
                var created = await Task.Run(() => _fs.CreateDirectory(location.FullPath, name));
                CloseModal(modal!);
                await NavigateAsync(pane, location, pushHistory: false, focusId: created.Id);
                StatusMessage = $"Pasta \"{created.Name}\" criada.";
            }
            catch (FileOperationException ex)
            {
                k.Reopen(ex.Message);
            }
        });
        PushModal(modal);
    }

    // ---------- Seletor de pasta interno ----------

    internal void OpenFolderPicker(string title, string startPath, Action<string> onPicked, Action? onCancel = null)
    {
        PickerTitle = title;
        _pickerCallback = onPicked;
        _pickerCancel = onCancel;
        Picker.Back.Clear();
        Picker.Forward.Clear();
        Picker.Location = null;
        Screen = Screen.FolderPicker;
        Track(NavigateAsync(Picker, new PhysicalLocation(startPath), pushHistory: false));
    }

    private void ShowPickerMenu()
    {
        var here = Picker.Location as PhysicalLocation;
        var items = new List<MenuItem>
        {
            new("Escolher esta pasta", () => CompletePicker(here!.FullPath), here is null ? "Nenhuma pasta aberta." : null, here?.FullPath),
            new("Criar pasta aqui", () => BeginCreateFolder(Picker), here is null ? "Nenhuma pasta aberta." : null),
            new("Ir para pasta acima…", () => ShowPathMenu(Picker), BuildBreadcrumbs(Picker).Count > 1 ? null : "Não há pastas acima desta."),
            new("Ir para outro local", ShowPickerPlaces),
            new("Ir para caminho…", () => BeginGoToPath(Picker)),
            new("Cancelar escolha", CancelPicker),
        };
        PushModal(new MenuModal(PickerTitle, items));
    }

    private void ShowPickerPlaces()
    {
        var items = BuildPlaces().Where(p => p.FullPath is not null)
            .Select(p => new MenuItem(p.Name, () => Track(NavigateAsync(Picker, new PhysicalLocation(p.FullPath!), pushHistory: true)),
                p.IsBlocked ? "Favorito indisponível: a pasta não existe ou não está acessível." : null, p.IsBlocked ? p.FullPath : p.Detail))
            .ToList();
        PushModal(new MenuModal("Locais", items));
    }

    private void CompletePicker(string path)
    {
        var callback = _pickerCallback;
        _pickerCallback = null;
        _pickerCancel = null;
        Screen = Screen.Browser;
        callback?.Invoke(path);
    }

    private void CancelPicker()
    {
        var cancel = _pickerCancel;
        _pickerCallback = null;
        _pickerCancel = null;
        Picker.LoadCts?.Cancel();
        Picker.Generation++;
        Picker.IsLoading = false;
        Screen = Browser.Location is null ? Screen.Home : Screen.Browser;
        cancel?.Invoke();
    }

    // ---------- Informações ----------

    private void ShowProperties(FileEntry entry)
    {
        var lines = new List<(string, string)>
        {
            ("Nome", entry.Name),
            ("Caminho", entry.FullPath ?? "—"),
            ("Tipo", entry.Kind == EntryKind.Drive ? EntryText.TypeName(entry) : entry.IsContainer ? "Pasta" : entry.Extension.Length > 0 ? $"Arquivo {entry.Extension}" : "Arquivo"),
        };
        if (entry.Volume is { TotalBytes: > 0 } volume)
        {
            if (volume.FileSystem is { } fileSystem) lines.Add(("Sistema de arquivos", fileSystem));
            lines.Add(("Capacidade", $"{EntryText.Size(volume.TotalBytes)} ({volume.TotalBytes:N0} bytes)"));
            lines.Add(("Livre", EntryText.Size(volume.FreeBytes)));
            lines.Add(("Usado", $"{EntryText.Size(volume.UsedBytes)} ({volume.UsedFraction:P0})"));
        }
        if (entry.Size is long size) lines.Add(("Tamanho", $"{FormatBytes(size)} ({size:N0} bytes)"));
        if (entry.Modified is { } modified) lines.Add(("Modificado", modified.LocalDateTime.ToString("g")));
        var attributes = new List<string>();
        if (entry.IsReadOnly) attributes.Add("somente leitura");
        if (entry.IsHidden) attributes.Add("oculto");
        if (entry.IsSystem) attributes.Add("sistema");
        if (entry.IsReparsePoint) attributes.Add("link/ponto de nova análise");
        if (attributes.Count > 0) lines.Add(("Atributos", string.Join(", ", attributes)));
        if (entry.Detail is { } detail) lines.Add(("Detalhes", detail));
        var dialog = ShowMessage("Propriedades", lines);
        if (entry is { IsContainer: true, Kind: not EntryKind.ArchiveDirectory, FullPath: { } folder })
            dialog.Options.Insert(0, new DialogOption("Calcular tamanho", DialogOptionKind.Primary, () => StartFolderSize(dialog, lines, folder)));
    }

    private void ShowArchiveEntryInfo(FileEntry entry)
    {
        var lines = new List<(string, string)> { ("Nome", entry.Name), ("Caminho no compactado", ArchiveTree.PathFromId(entry.Id)) };
        lines.Add(("Tamanho", entry.Size is long s ? FormatBytes(s) : "desconhecido"));
        if (entry.Detail is { } d) lines.Add(("Compressão", d));
        if (entry.Modified is { } m) lines.Add(("Modificado", m.LocalDateTime.ToString("g")));
        if (entry.IsEncrypted) lines.Add(("Proteção", "protegido por senha"));
        ShowMessage("Entrada do compactado", lines, "Somente leitura. Use o menu de ações para extrair.");
    }

    private void ShowArchiveInfo(PaneState pane)
    {
        if (pane.Archive is not { } tree) return;
        var info = tree.Info;
        var lines = new List<(string, string)>
        {
            ("Arquivo", info.ArchivePath),
            ("Formato", ArchiveFormats.DisplayName(info.Format)),
            ("Entradas", info.Entries.Count.ToString()),
            ("Arquivos", tree.FileCount.ToString()),
            ("Tamanho descompactado", info.DeclaredTotalSize is long total ? FormatBytes(total) + " (declarado)" : "desconhecido"),
            ("Senha", tree.EncryptedCount > 0 ? $"{tree.EncryptedCount} entrada(s) protegida(s)" : info.HasEncryptedEntries ? "há entradas protegidas" : "não"),
            ("Bloqueadas (nome recusado)", tree.BlockedCount.ToString()),
            ("Bloqueadas (links/especiais)", tree.LinkCount.ToString()),
            ("Verificação", info.Capabilities.CanVerifyIntegrity ? "CRC durante a extração" : "indisponível"),
        };
        foreach (var limitation in info.Limitations) lines.Add(("Limitação", limitation));
        ShowMessage("Informações do compactado", lines);
    }

    internal static string FormatBytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.##} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.##} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0.##} KB",
        _ => $"{bytes} B",
    };

    private static string SortLabel(SortField field) => field switch
    {
        SortField.Type => "tipo",
        SortField.Size => "tamanho",
        SortField.Modified => "data",
        _ => "nome",
    };

    private static string LabelStyleName(ButtonLabelStyle style) => style switch
    {
        ButtonLabelStyle.Xbox => "Xbox",
        ButtonLabelStyle.PlayStation => "PlayStation",
        ButtonLabelStyle.Nintendo => "Nintendo",
        ButtonLabelStyle.Generic => "genéricas",
        _ => "automáticas",
    };

    private static string FamilyName(ControllerFamily family) => family switch
    {
        ControllerFamily.Xbox => "Xbox",
        ControllerFamily.PlayStation => "PlayStation",
        ControllerFamily.Nintendo => "Nintendo",
        _ => "genérico",
    };

    internal static string StateLabel(OperationState state) => state switch
    {
        OperationState.Queued => "na fila",
        OperationState.Planning => "planejando",
        OperationState.Running => "em andamento",
        OperationState.WaitingForUser => "aguardando decisão",
        OperationState.Paused => "pausada",
        OperationState.CancelRequested => "cancelando",
        OperationState.Cancelled => "cancelada",
        OperationState.Completed => "concluída",
        OperationState.CompletedWithWarnings => "concluída com avisos",
        OperationState.Failed => "falhou",
        _ => state.ToString(),
    };
}
