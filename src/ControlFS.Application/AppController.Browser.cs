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
        if (ReferenceEquals(pane, Browser) && HandleTabTrigger(action)) return;
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
            ShowMessage("Entrada bloqueada", [("Nome", entry.Name), ("Motivo", entry.BlockedReason!)], icon: ActionIcon.Error);
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
        else if (IsPdf(entry) && PdfRenderer is not null) OpenPdfPreview(pane, entry);
        else if (IsPlayableAudio(entry) && MediaPlayer is not null) OpenAudioPreview(pane, entry);
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
            if (tree.BlockedCount > 0) StatusMessage = $"{Plural.Of(tree.BlockedCount, "entrada", "entradas")} com nome inseguro {Plural.Word(tree.BlockedCount, "foi bloqueada", "foram bloqueadas")}.";
        }
        catch (ArchiveAccessException ex) when (generation == pane.Generation && ex.Kind is OperationErrorKind.PasswordRequired or OperationErrorKind.WrongPassword)
        {
            // Cabeçalhos protegidos: sem a senha nem a lista de arquivos pode ser lida.
            AskArchivePassword(pane, archivePath, ex.Kind == OperationErrorKind.WrongPassword ? "Senha incorreta. Tente novamente." : null);
        }
        catch (ArchiveAccessException ex) when (generation == pane.Generation)
        {
            ShowMessage("Não foi possível abrir o compactado", [("Arquivo", Path.GetFileName(archivePath)), ("Motivo", ex.Message)], icon: ActionIcon.Error);
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
            RequestGitStatus(pane);
            if (target is PhysicalLocation p)
            {
                pane.LastValidPhysical = p;
                // Abas restauradas carregam em segundo plano: só a aba ativa entra nos recentes.
                if (pane.Mode == PaneMode.Browse && ReferenceEquals(pane, Browser)) RecordVisit(p.FullPath);
            }
        }
        catch (FileOperationException ex) when (generation == pane.Generation)
        {
            ShowMessage("Não foi possível abrir a pasta", [("Pasta", target.DisplayPath), ("Motivo", ex.Message)], icon: ActionIcon.Error);
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
        if (pane.Mode == PaneMode.PickFolder) CancelPicker();
        else if (ReferenceEquals(pane, Browser)) GoHome(); // uma aba em segundo plano não tira a aba ativa da tela
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
            var copy = new MenuItem($"Copiar {Plural.Of(marked.Count, "item", "itens")}", () => PutOnClipboard(pane, marked, FileOperationKind.Copy), FileOpsUnavailable,
                Icon: ActionIcon.Copy, Placement: MenuPlacement.Quick, ShortLabel: "Copiar");
            PushModal(new MenuModal($"{Plural.Of(marked.Count, "item", "itens")} {Plural.Word(marked.Count, "marcado", "marcados")}",
            [
                .. markedArchives.Count == 0 ? Array.Empty<MenuItem>() :
                [
                    new MenuItem($"Extrair cada um para a própria pasta ({markedArchives.Count})", () => BeginBatchExtraction(pane, markedArchives),
                        Detail: marked.Any(e => e.Kind != EntryKind.File || !ArchiveFormats.HasExtractableExtension(e.Name)) ? "Itens que não são compactados ficam de fora." : null, Icon: ActionIcon.Extract),
                ],
                new MenuItem($"Recortar {Plural.Of(marked.Count, "item", "itens")}", () => PutOnClipboard(pane, marked, FileOperationKind.Move), FileOpsUnavailable, Icon: ActionIcon.Cut, Placement: MenuPlacement.Quick, ShortLabel: "Recortar"),
                copy,
                new MenuItem("Renomear em lote…", () => BeginBatchRename(pane, marked), FileOpsUnavailable,
                    Detail: "Numeração, localizar e substituir, prefixo e sufixo ou maiúsculas, com prévia.", Icon: ActionIcon.Rename),
                new MenuItem($"Copiar {Plural.Of(marked.Count, "item", "itens")} para…", () => BeginTransferTo(pane, marked, FileOperationKind.Copy), FileOpsUnavailable, Icon: ActionIcon.CopyTo),
                new MenuItem($"Mover {Plural.Of(marked.Count, "item", "itens")} para…", () => BeginTransferTo(pane, marked, FileOperationKind.Move), FileOpsUnavailable, Icon: ActionIcon.MoveTo),
                new MenuItem($"Compactar {Plural.Of(marked.Count, "item", "itens")}…", () => BeginCompress(pane, marked), Icon: ActionIcon.Compress, Placement: MenuPlacement.Quick, ShortLabel: "Compactar"),
                new MenuItem($"Excluir {Plural.Of(marked.Count, "item", "itens")}…", () => BeginDelete(pane, marked), FileOpsUnavailable, Icon: ActionIcon.Delete, Placement: MenuPlacement.Quick, ShortLabel: "Excluir"),
                .. SelectionItems(pane),
            ]) { Icon = ActionIcon.SelectAll, Subtitle = "Ações para todos os itens marcados", FocusOn = markedArchives.Count == 0 ? copy : null });
            return;
        }
        var entry = pane.List.Focused;
        if (entry is { Kind: EntryKind.File, FullPath: { } file })
        {
            Track(ShowFileMenuAsync(pane, entry, file));
            return;
        }
        var items = new List<MenuItem>();
        if (entry is { IsContainer: true }) items.Add(new MenuItem("Abrir", () => OpenEntry(pane, entry), Icon: ActionIcon.OpenFolder, Section: "Abrir", Placement: MenuPlacement.Quick));
        if (entry is { IsContainer: true, FullPath: { } folderPath })
            items.Add(new MenuItem("Abrir no Explorador de Arquivos", () => RunShell(s => s.Open(folderPath), external: true), ShellUnavailable, Icon: ActionIcon.OpenExternal, Section: "Abrir"));
        if (entry is { IsContainer: true, FullPath: { } tabPath } && pane.Mode == PaneMode.Browse)
            items.Add(new MenuItem("Abrir em nova aba", () => OpenInNewTab(tabPath), NewTabUnavailable, Icon: ActionIcon.NewTab, Section: "Abrir"));
        if (entry is { Kind: EntryKind.Directory, FullPath: not null } && pane.Location is PhysicalLocation)
        {
            items.Add(new MenuItem("Recortar", () => PutOnClipboard(pane, [entry], FileOperationKind.Move), FileOpsUnavailable, Icon: ActionIcon.Cut, Section: "Organizar", Placement: MenuPlacement.Quick));
            items.Add(new MenuItem("Copiar", () => PutOnClipboard(pane, [entry], FileOperationKind.Copy), FileOpsUnavailable, Icon: ActionIcon.Copy, Section: "Organizar", Placement: MenuPlacement.Quick));
            items.Add(new MenuItem("Renomear…", () => BeginRename(pane, entry), FileOpsUnavailable, Icon: ActionIcon.Rename, Section: "Organizar", Placement: MenuPlacement.Quick));
            items.Add(new MenuItem("Copiar para…", () => BeginTransferTo(pane, [entry], FileOperationKind.Copy), FileOpsUnavailable, Icon: ActionIcon.CopyTo, Section: "Organizar"));
            items.Add(new MenuItem("Mover para…", () => BeginTransferTo(pane, [entry], FileOperationKind.Move), FileOpsUnavailable, Icon: ActionIcon.MoveTo, Section: "Organizar"));
            items.Add(new MenuItem("Compactar…", () => BeginCompress(pane, [entry]), Icon: ActionIcon.Compress, Section: "Organizar", Placement: MenuPlacement.Quick));
            items.Add(new MenuItem("Excluir…", () => BeginDelete(pane, [entry]), FileOpsUnavailable, Icon: ActionIcon.Delete, Section: "Organizar", Placement: MenuPlacement.Quick));
        }
        if (entry is { IsContainer: true, FullPath: { } favoritePath }) items.Add(FavoriteToggleItem(favoritePath, section: "Favoritos"));
        if (pane.Location is PhysicalLocation current) items.Add(FavoriteToggleItem(current.FullPath, "esta pasta", "Favoritos"));
        if (Clipboard is not null) items.Add(new MenuItem(PasteLabel, () => Paste(pane), PasteUnavailable(pane), Icon: ActionIcon.Paste, Section: "Esta pasta", Placement: MenuPlacement.Quick, ShortLabel: "Colar"));
        items.Add(new MenuItem("Nova pasta aqui", () => BeginCreateFolder(pane), pane.Location is PhysicalLocation ? null : "Disponível apenas em pastas do disco.", Icon: ActionIcon.NewFolder, Section: "Esta pasta"));
        items.AddRange(SelectionItems(pane));
        if (entry is { Kind: EntryKind.Directory or EntryKind.KnownFolder or EntryKind.Drive, FullPath: { } usagePath }) items.Add(DiskUsageItem(usagePath, "Informações"));
        else if (entry is null && pane.Location is PhysicalLocation usageHere) items.Add(DiskUsageItem(usageHere.FullPath, "Esta pasta"));
        if (entry is not null) items.Add(new MenuItem("Propriedades", () => ShowProperties(entry), Icon: ActionIcon.Properties, Section: "Informações", Placement: MenuPlacement.Quick));
        PushModal(new MenuModal(entry?.Name ?? "Ações", items) { Icon = entry is null ? ActionIcon.Folder : EntryIcon(entry), Subtitle = entry is null ? pane.Location?.DisplayPath : TypeNameOf(entry) });
    }

    /// <summary>Meu computador: só o que vale para uma unidade (nada de marcar, colar ou criar pasta aqui).</summary>
    private void ShowDriveMenu(PaneState pane, string? image = null, bool imageChecked = false)
    {
        if (!imageChecked && DeferForImage(pane.List.Focused?.FullPath, found => ShowDriveMenu(pane, found, imageChecked: true))) return;
        var items = new List<MenuItem>();
        if (pane.List.Focused is { FullPath: { } path } drive)
        {
            items.Add(new MenuItem("Abrir", () => OpenEntry(pane, drive), Icon: ActionIcon.OpenFolder, Placement: MenuPlacement.Quick));
            items.Add(new MenuItem("Abrir no Explorador de Arquivos", () => RunShell(s => s.Open(path), external: true), ShellUnavailable, Icon: ActionIcon.OpenExternal));
            items.Add(new MenuItem("Abrir em nova aba", () => OpenInNewTab(path), NewTabUnavailable, Icon: ActionIcon.NewTab, Placement: MenuPlacement.Quick, ShortLabel: "Nova aba"));
            items.Add(FavoriteToggleItem(path));
            items.Add(new MenuItem("Propriedades", () => ShowProperties(drive), Icon: ActionIcon.Properties, Placement: MenuPlacement.Quick));
            items.Add(DiskUsageItem(path));
            if (image is not null) items.Add(UnmountItem(path, image));
        }
        items.Add(new MenuItem("Atualizar", () => Refresh(pane), Icon: ActionIcon.Refresh, Placement: MenuPlacement.Quick));
        PushModal(new MenuModal(pane.List.Focused?.Name ?? "Meu computador", items) { Icon = pane.List.Focused is null ? ActionIcon.ThisPc : ActionIcon.Drive });
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
                StatusMessage = $"{Plural.Of(list.SelectionCount, "item", "itens")} {Plural.Word(list.SelectionCount, "marcado", "marcados")}.";
            }, selectable == 0 ? "Nada aqui pode ser marcado." : null, Icon: ActionIcon.SelectAll, Section: "Seleção"));
        if (list.SelectionCount > 0)
            items.Add(new MenuItem($"Limpar marcação ({list.SelectionCount})", list.ClearSelection, Icon: ActionIcon.ClearSelection, Section: "Seleção"));
        return items;
    }

    private async Task ShowFileMenuAsync(PaneState pane, FileEntry entry, string file)
    {
        var format = await Task.Run(() => _archives.Detect(file));
        var items = new List<MenuItem>();
        MenuItem? extract = null;
        if (ArchiveFormats.CanExtract(format))
        {
            var folder = Path.GetDirectoryName(file)!;
            var stem = ArchiveFormats.StemOf(file);
            items.Add(new MenuItem($"Abrir compactado ({ArchiveFormats.DisplayName(format)})", () => Track(OpenArchiveAsync(pane, file)), Icon: ActionIcon.Archive, Section: "Compactado"));
            extract = new MenuItem($"Extrair para \"{stem}\"", () => BeginExtraction(file, folder, dedicated: true, null, string.Empty), Icon: ActionIcon.Extract, Section: "Compactado");
            items.Add(extract);
            items.Add(new MenuItem("Extrair aqui", () => BeginExtraction(file, folder, dedicated: false, null, string.Empty), Icon: ActionIcon.Extract, Section: "Compactado"));
            items.Add(new MenuItem("Extrair para…", () => PickDestinationThenExtract(file, folder, null, string.Empty), Icon: ActionIcon.Extract, Section: "Compactado"));
            items.Add(TestIntegrityItem(file));
        }
        if (IsPreviewableImage(entry)) items.Add(new MenuItem("Visualizar imagem", () => OpenImagePreview(pane, entry), ImagePreviewUnavailable, Icon: ActionIcon.Image, Section: "Abrir"));
        else if (IsPlayableAudio(entry)) items.Add(new MenuItem("Ouvir aqui", () => OpenAudioPreview(pane, entry), MediaUnavailable, Detail: "Com os codecs do Windows; nada é executado.", Icon: ActionIcon.Audio, Section: "Abrir"));
        else if (IsPdf(entry)) items.Add(new MenuItem("Visualizar PDF", () => OpenPdfPreview(pane, entry), PdfPreviewUnavailable, Detail: "Só as páginas; links e anexos nunca abrem.", Icon: ActionIcon.Pdf, Section: "Abrir"));
        else if (!ArchiveFormats.CanExtract(format))
            items.Add(new MenuItem("Visualizar como texto", () => OpenTextPreview(pane, entry), Detail: "Somente leitura; nada é executado.", Icon: ActionIcon.Text, Section: "Abrir"));
        items.Add(new MenuItem(entry.IsSteamGame ? "Jogar…" : ExecutableFiles.IsPotentiallyExecutable(file) ? "Executar…" : "Abrir com o aplicativo padrão",
            () => OpenExternally(entry, file), ShellUnavailable, Icon: entry.IsSteamGame ? ActionIcon.Game : ExecutableFiles.IsPotentiallyExecutable(file) ? ActionIcon.Run : ActionIcon.Open, Section: "Abrir",
            Placement: MenuPlacement.Quick, ShortLabel: entry.IsSteamGame ? "Jogar" : ExecutableFiles.IsPotentiallyExecutable(file) ? "Executar" : "Abrir"));
        if (MountItem(file) is { } mount) items.Add(mount);
        items.Add(new MenuItem("Abrir com…", () => RunShell(s => s.OpenWith(file), external: true), ShellUnavailable,
            Detail: "Escolher o programa na caixa do Windows.", Icon: ActionIcon.OpenWith, Section: "Abrir"));
        items.Add(new MenuItem("Mostrar no Explorador de Arquivos", () => RunShell(s => s.RevealInExplorer(file), external: true), ShellUnavailable, Icon: ActionIcon.Reveal, Section: "Abrir"));
        items.Add(new MenuItem("Recortar", () => PutOnClipboard(pane, [entry], FileOperationKind.Move), FileOpsUnavailable, Icon: ActionIcon.Cut, Section: "Organizar", Placement: MenuPlacement.Quick));
        items.Add(new MenuItem("Copiar", () => PutOnClipboard(pane, [entry], FileOperationKind.Copy), FileOpsUnavailable, Icon: ActionIcon.Copy, Section: "Organizar", Placement: MenuPlacement.Quick));
        items.Add(new MenuItem("Renomear…", () => BeginRename(pane, entry), FileOpsUnavailable, Icon: ActionIcon.Rename, Section: "Organizar", Placement: MenuPlacement.Quick));
        items.Add(new MenuItem("Copiar para…", () => BeginTransferTo(pane, [entry], FileOperationKind.Copy), FileOpsUnavailable, Icon: ActionIcon.CopyTo, Section: "Organizar"));
        items.Add(new MenuItem("Mover para…", () => BeginTransferTo(pane, [entry], FileOperationKind.Move), FileOpsUnavailable, Icon: ActionIcon.MoveTo, Section: "Organizar"));
        items.Add(new MenuItem("Compactar…", () => BeginCompress(pane, [entry]), Icon: ActionIcon.Compress, Section: "Organizar", Placement: MenuPlacement.Quick));
        items.Add(new MenuItem("Excluir…", () => BeginDelete(pane, [entry]), FileOpsUnavailable, Icon: ActionIcon.Delete, Section: "Organizar", Placement: MenuPlacement.Quick));
        if (pane.Location is PhysicalLocation current) items.Add(FavoriteToggleItem(current.FullPath, "esta pasta", "Favoritos"));
        if (Clipboard is not null) items.Add(new MenuItem(PasteLabel, () => Paste(pane), PasteUnavailable(pane), Icon: ActionIcon.Paste, Section: "Esta pasta", Placement: MenuPlacement.Quick, ShortLabel: "Colar"));
        items.Add(new MenuItem("Nova pasta aqui", () => BeginCreateFolder(pane), Icon: ActionIcon.NewFolder, Section: "Esta pasta"));
        items.AddRange(SelectionItems(pane));
        items.Add(new MenuItem("Propriedades", () => ShowProperties(entry), Icon: ActionIcon.Properties, Section: "Informações", Placement: MenuPlacement.Quick));
        // Compactado: o rodapé anuncia "Extrair…" neste botão, então o menu abre em "Extrair para <nome>".
        PushModal(new MenuModal(entry.Name, items) { Icon = EntryIcon(entry, format), Subtitle = TypeNameOf(entry), FocusOn = extract });
    }

    private MenuItem TestIntegrityItem(string archivePath) => new("Testar integridade", () => BeginArchiveTest(archivePath),
        Detail: "Lê todas as entradas e confere o CRC sem extrair nada. Não é antivírus.", Icon: ActionIcon.Test, Section: "Compactado");

    private void ShowArchiveMenu(PaneState pane, ArchiveLocation archive)
    {
        var path = archive.ArchivePath;
        var folder = Path.GetDirectoryName(path)!;
        var stem = ArchiveFormats.StemOf(path);
        var selected = pane.List.SelectedIds.Select(ArchiveTree.PathFromId).Where(p => p.Length > 0).ToList();
        var items = new List<MenuItem>
        {
            new($"Extrair tudo para \"{stem}\"", () => BeginExtraction(path, folder, dedicated: true, null, string.Empty), Icon: ActionIcon.Extract, Section: "Extrair"),
            new("Extrair tudo aqui (pasta do arquivo)", () => BeginExtraction(path, folder, dedicated: false, null, string.Empty), Icon: ActionIcon.Extract, Section: "Extrair"),
            new("Extrair tudo para…", () => PickDestinationThenExtract(path, folder, null, string.Empty), Icon: ActionIcon.Extract, Section: "Extrair"),
        };
        var noSelection = selected.Count == 0 ? "Marque entradas primeiro (botão de marcar)." : null;
        var extractSelection = new MenuItem($"Extrair seleção ({selected.Count}) para \"{stem}\"", () => BeginExtraction(path, folder, dedicated: true, selected, archive.InnerPath), noSelection, Icon: ActionIcon.Extract, Section: "Extrair");
        items.Add(extractSelection);
        items.Add(new MenuItem($"Extrair seleção ({selected.Count}) para…", () => PickDestinationThenExtract(path, folder, selected, archive.InnerPath), noSelection, Icon: ActionIcon.Extract, Section: "Extrair"));
        items.AddRange(SelectionItems(pane));
        items.Add(TestIntegrityItem(path));
        items.Add(new MenuItem("Informações do compactado", () => ShowArchiveInfo(pane), Icon: ActionIcon.Info, Section: "Compactado"));
        // Com entradas marcadas, o rodapé anuncia "Extrair seleção": o menu abre já nela (Norte e depois Sul extraem).
        PushModal(new MenuModal(Path.GetFileName(path) + " (somente leitura)", items) { Icon = ActionIcon.Archive, FocusOn = selected.Count > 0 ? extractSelection : null });
    }

    /// <summary>
    /// Menu do app (Start): as ações mais usadas em blocos (Colar, Nova pasta, Nova aba, Atualizar, Ir para caminho,
    /// Operações, Configurações, Início) e o resto numa lista curta que termina em Sair. Os ajustes ficam em Configurações.
    /// </summary>
    private void ShowAppMenu()
    {
        var pane = Browser;
        var inBrowser = Screen == Screen.Browser;
        const MenuPlacement quick = MenuPlacement.Quick;
        List<MenuItem> items =
        [
            new(PasteLabel, () => Paste(pane), inBrowser ? PasteUnavailable(pane) : "Abra uma pasta do disco para colar.", Icon: ActionIcon.Paste, Placement: quick, ShortLabel: "Colar"),
            new("Nova pasta", () => BeginCreateFolder(pane), inBrowser && pane.Location is PhysicalLocation ? null : "Abra uma pasta do disco primeiro.", Icon: ActionIcon.NewFolder, Placement: quick),
            new("Nova aba", NewTabHere, NewTabUnavailable, Detail: "Abre a pasta atual numa aba nova. Com 2+ abas, L2/R2 trocam de aba.", Icon: ActionIcon.NewTab, Placement: quick),
            new("Atualizar", () => Refresh(pane), inBrowser ? null : "Nada para atualizar na tela inicial.", Icon: ActionIcon.Refresh, Placement: quick),
            new("Ir para caminho…", () => BeginGoToPath(pane), Detail: "Digite ou cole o caminho de uma pasta (ex.: D:\\Jogos).", Icon: ActionIcon.GoToPath, Placement: quick, ShortLabel: "Caminho"),
            new($"Operações ({Plural.Of(Operations.ActiveCount, "ativa", "ativas")})", ShowOperations, Operations.Items.Count == 0 && History.Entries.Count == 0 ? "Nenhuma operação registrada." : null,
                Icon: ActionIcon.Operations, Placement: quick, ShortLabel: "Operações"),
            new("Configurações…", ShowSettings, Detail: "Exibição, busca, privacidade, controles e atualizações.", Icon: ActionIcon.Settings, Placement: quick),
            new("Ir para o início", GoHome, Screen == Screen.Home ? "Você já está no início." : null, Icon: ActionIcon.Home, Placement: quick, ShortLabel: "Início"),
            new("Ir para pasta acima…", () => ShowPathMenu(pane), inBrowser && BuildBreadcrumbs(pane).Count > 1 ? null : "Não há pastas acima desta.",
                Detail: "Também pela barra de caminho (botão de ombro esquerdo).", Icon: ActionIcon.FolderUp, Section: "Navegar"),
            new(_tabs.Count > 1 ? $"Abas ({ActiveTab + 1} de {_tabs.Count})…" : "Abas…", ShowTabMenu, inBrowser || _closedTabs.Count > 0 ? null : "Abra uma pasta primeiro.",
                Detail: "Fechar e trocar de aba. Com 2+ abas, também pela faixa acima da barra superior (Cima).", Icon: ActionIcon.NewTab, Section: "Navegar"),
            new("Reabrir aba fechada", ReopenClosedTab, ReopenClosedTabUnavailable, Detail: "A última aba fechada volta com o local e o histórico dela.", Icon: ActionIcon.Undo, Section: "Navegar"),
            .. UndoMenuItems(),
            new("Esvaziar área de transferência", ClearClipboard, Clipboard is null ? "A área de transferência está vazia." : null, Icon: ActionIcon.Clear, Section: "ControlFS"),
            new("Sobre o ControlFS", ShowAbout, Detail: $"Versão {AppVersion} · licença AGPL-3.0-only", Icon: ActionIcon.About, Section: "ControlFS"),
            new("Sair", ShowExitDialog, Icon: ActionIcon.Exit, Section: "ControlFS"),
        ];
        PushModal(new MenuModal("Menu", items));
    }

    /// <summary>
    /// Configurações (Menu → Configurações): todos os ajustes, em grupos. Alternar um ajuste mantém o menu aberto com o
    /// texto novo e o foco nele; Voltar fecha. Opções com tela própria (controle ativo, teste, joysticks, atualizações) fecham
    /// Configurações e abrem essa tela, como os outros submenus.
    /// </summary>
    internal void ShowSettings() =>
        PushModal(new MenuModal("Configurações", SettingsItems()) { Icon = ActionIcon.Settings, Reload = SettingsItems });

    private List<MenuItem> SettingsItems()
    {
        var pane = Browser;
        var inBrowser = Screen == Screen.Browser;
        var sort = pane.List.Sort;
        const string view = "Exibição", privacy = "Busca e privacidade", controls = "Controles", app = "ControlFS";
        return
        [
            new($"Exibição: {ViewName(Settings.View)}", ToggleView, Detail: "Lista ou grade de ícones grandes (também R3 ou Ctrl+G).", Icon: ActionIcon.View, Section: view, KeepOpen: true),
            new($"Densidade da lista: {DensityName(Settings.Density)}", ToggleDensity,
                Detail: "Confortável: duas linhas, para TV. Compacta: uma linha com tipo, tamanho e data; na grade, blocos menores.", Icon: ActionIcon.Density, Section: view, KeepOpen: true),
            new(DetailsPanelMenuLabel, ToggleDetailsPanel,
                Detail: $"Ícone, tipo, tamanho e datas do item em foco ao lado da {ViewName(Settings.View)}. A escolha vale para a {ViewName(Settings.View)} e fica salva.", Icon: ActionIcon.DetailsPane, Section: view, KeepOpen: true),
            new($"Tema: {ThemeName(Settings.Theme)}", CycleTheme,
                Detail: "Automático segue o modo de apps do Windows (Configurações → Personalização → Cores). Muda na hora.", Icon: ActionIcon.Theme, Section: view, KeepOpen: true),
            new($"Cor de destaque: {AccentName(Settings.Accent)}", CycleAccent,
                Detail: "Cor do foco, do cursor e dos símbolos em destaque. Todas as opções mantêm o contraste nos dois temas.", Icon: ActionIcon.Accent, Section: view, KeepOpen: true),
            new($"Ordenar por: {SortLabel(sort.Field)}", () => pane.List.SetSort(sort with { Field = (SortField)(((int)sort.Field + 1) % 4) }),
                inBrowser ? null : "Abra uma pasta primeiro.", Icon: ActionIcon.Sort, Section: view, KeepOpen: true),
            new($"Ordem: {(sort.Descending ? "decrescente" : "crescente")}", () => pane.List.SetSort(sort with { Descending = !sort.Descending }),
                inBrowser ? null : "Abra uma pasta primeiro.", Icon: ActionIcon.SortOrder, Section: view, KeepOpen: true),
            new($"Itens ocultos: {(Settings.ShowHidden ? "mostrar" : "esconder")}", () =>
            {
                UpdateSettings(s => s with { ShowHidden = !s.ShowHidden });
                if (inBrowser) Refresh(pane);
            }, Icon: ActionIcon.Hidden, Section: view, KeepOpen: true),
            new($"Status do Git: {(Settings.ShowGitStatus ? "mostrar" : "não mostrar")}", ToggleGitStatus, Git is null ? "Indisponível nesta compilação." : null,
                Detail: "Em pastas de repositórios Git: o ramo no topo e \"Git: modificado/novo\" nos itens. Somente leitura; não precisa do Git instalado.",
                Icon: ActionIcon.Info, Section: view, KeepOpen: true),
            new($"Busca em subpastas: {(SearchIncludesSubfolders ? "incluir" : "não incluir")}", () => SearchIncludesSubfolders = !SearchIncludesSubfolders,
                Detail: "Vale para a próxima busca (Select/View).", Icon: ActionIcon.Subfolders, Section: privacy, KeepOpen: true),
            new($"Recentes: {(Settings.RememberRecents ? "lembrar" : "não lembrar")}", ToggleRememberRecents,
                Detail: "Pastas e arquivos abertos, só neste computador. Desligar apaga as listas.", Icon: ActionIcon.Recent, Section: privacy, KeepOpen: true),
            new($"Restaurar abas ao abrir: {(Settings.RestoreTabs ? "sim" : "não")}", ToggleRestoreTabs,
                Detail: "Com 2+ abas abertas, reabre as mesmas pastas na próxima vez; pastas que sumiram mostram o início. Desligar apaga a lista.", Icon: ActionIcon.NewTab, Section: privacy, KeepOpen: true),
            new($"Sugestões do teclado: {(Settings.KeyboardSuggestions ? "sim" : "não")}", ToggleKeyboardSuggestions,
                Detail: "Nomes digitados antes e desta pasta, só neste computador; nunca em senhas. Desligar apaga o histórico.", Icon: ActionIcon.Keyboard, Section: privacy, KeepOpen: true),
            new($"Confirmar com: {(Settings.Convention == ConfirmBackConvention.SouthConfirms ? "botão inferior" : "botão direito")}", () =>
                UpdateSettings(s => s with { Convention = s.Convention == ConfirmBackConvention.SouthConfirms ? ConfirmBackConvention.EastConfirms : ConfirmBackConvention.SouthConfirms }),
                Detail: "Troca comportamento e legendas de confirmar/voltar.", Icon: ActionIcon.Accept, Section: controls, KeepOpen: true),
            new($"Legendas: {LabelStyleName(Settings.LabelStyle)}", () =>
                UpdateSettings(s => s with { LabelStyle = (ButtonLabelStyle)(((int)s.LabelStyle + 1) % 5) }),
                Detail: Settings.LabelStyle != ButtonLabelStyle.Automatic ? null
                    : ActiveController is { } family ? $"Seguem o controle em uso (agora: {FamilyName(family)})." : "Seguem o controle em uso.", Icon: ActionIcon.Labels, Section: controls, KeepOpen: true),
            new($"Fluidez: {(Settings.SyncInputToDisplay ? "máxima (acompanha a tela)" : "economia de bateria")}",
                () => UpdateSettings(s => s with { SyncInputToDisplay = !s.SyncInputToDisplay }),
                Detail: "Máxima lê o controle a cada quadro da tela (120 vezes por segundo numa tela de 120 Hz). Economia gasta menos bateria em portáteis.",
                Icon: ActionIcon.Settings, Section: controls, KeepOpen: true),
            new($"Mira por giroscópio no teclado: {(Settings.GyroKeyboard ? "sim" : "não")} (experimental)", ToggleGyroKeyboard,
                Detail: "Controles com giroscópio (ex.: DualSense): no teclado virtual, gire ou incline o controle para apontar as teclas; R3 recentraliza. O direcional continua funcionando.",
                Icon: ActionIcon.Keyboard, Section: controls, KeepOpen: true),
            new(ActiveControllerMenuLabel, ShowActiveControllerMenu, _diagnostics is null ? "Controles indisponíveis nesta compilação." : null,
                Detail: "Escolha qual controle comanda o ControlFS (ex.: Steam Input ou DS4Windows duplicando o controle).", Icon: ActionIcon.Controller, Section: controls),
            new("Teste de controles…", ShowControllerTest, _diagnostics is null ? "Controles indisponíveis nesta compilação." : null,
                Detail: "Mostra cada botão e a ação que ele produz; copia um relatório para a issue #78.", Icon: ActionIcon.ControllerTest, Section: controls),
            new("Controles sem perfil…", ShowControllersMenu, Detail: "Configurar joysticks que não são reconhecidos como gamepad.", Icon: ActionIcon.ControllerSetup, Section: controls),
            new(UpdateMenuLabel, ShowUpdatesMenu, _updates is null ? "Atualizações indisponíveis nesta compilação." : null, Icon: ActionIcon.Update, Section: app),
        ];
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
            new("Escolher esta pasta", () => CompletePicker(here!.FullPath), here is null ? "Nenhuma pasta aberta." : null, here?.FullPath, Icon: ActionIcon.Choose),
            new("Criar pasta aqui", () => BeginCreateFolder(Picker), here is null ? "Nenhuma pasta aberta." : null, Icon: ActionIcon.NewFolder),
            new("Ir para pasta acima…", () => ShowPathMenu(Picker), BuildBreadcrumbs(Picker).Count > 1 ? null : "Não há pastas acima desta.", Icon: ActionIcon.FolderUp),
            new("Ir para outro local", ShowPickerPlaces, Icon: ActionIcon.Drive),
            new("Ir para caminho…", () => BeginGoToPath(Picker), Icon: ActionIcon.GoToPath),
            new("Cancelar escolha", CancelPicker, Icon: ActionIcon.Cancel),
        };
        PushModal(new MenuModal(PickerTitle, items) { Icon = ActionIcon.Folder });
    }

    /// <summary>Ícone do item no cabeçalho do menu de ações: o mesmo tipo que a lista mostra.</summary>
    private static ActionIcon EntryIcon(FileEntry entry, ArchiveFormat format = ArchiveFormat.Unknown) => entry switch
    {
        { IsSteamGame: true } => ActionIcon.Game,
        { Kind: EntryKind.Drive } => ActionIcon.Drive,
        { IsContainer: true } => ActionIcon.Folder,
        _ when ArchiveFormats.CanExtract(format) => ActionIcon.Archive,
        _ when IsPreviewableImage(entry) => ActionIcon.Image,
        _ when IsPdf(entry) => ActionIcon.Pdf,
        _ when IsPlayableAudio(entry) => ActionIcon.Audio,
        _ => ActionIcon.File,
    };

    /// <summary>Ícone de um local do início nos menus (unidade, lixeira, favorito indisponível, pasta).</summary>
    private static ActionIcon PlaceIcon(FileEntry place) => place.Kind switch
    {
        EntryKind.Drive => ActionIcon.Drive,
        _ when place.IsBlocked => ActionIcon.Warning,
        _ => ActionIcon.Folder,
    };

    private void ShowPickerPlaces()
    {
        var items = BuildPlaces().Where(p => p.FullPath is not null)
            .Select(p => new MenuItem(p.Name, () => Track(NavigateAsync(Picker, new PhysicalLocation(p.FullPath!), pushHistory: true)),
                p.IsBlocked ? "Favorito indisponível: a pasta não existe ou não está acessível." : null, p.IsBlocked ? p.FullPath : p.Detail, Icon: PlaceIcon(p)))
            .ToList();
        PushModal(new MenuModal("Locais", items) { Icon = ActionIcon.Drive });
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
        if (entry is { IsSteamGame: true, Shortcut.Url: { } steamTarget }) lines.Add(("Atalho", $"Jogo da Steam ({ShortText(steamTarget)})"));
        if (entry.Size is long size) lines.Add(("Tamanho", $"{FormatBytes(size)} ({size:N0} bytes)"));
        if (entry.Modified is { } modified) lines.Add(("Modificado", modified.LocalDateTime.ToString("g")));
        var attributes = new List<string>();
        if (entry.IsReadOnly) attributes.Add("somente leitura");
        if (entry.IsHidden) attributes.Add("oculto");
        if (entry.IsSystem) attributes.Add("sistema");
        if (entry.IsReparsePoint) attributes.Add("link/ponto de nova análise");
        if (attributes.Count > 0) lines.Add(("Atributos", string.Join(", ", attributes)));
        if (entry.Detail is { } detail) lines.Add(("Detalhes", detail));
        var dialog = ShowMessage("Propriedades", lines, icon: ActionIcon.Properties);
        if (entry is { IsContainer: true, Kind: not EntryKind.ArchiveDirectory, FullPath: { } folder })
            dialog.Options.Insert(0, new DialogOption("Calcular tamanho", DialogOptionKind.Primary, () => StartFolderSize(dialog, lines, folder), icon: ActionIcon.Density));
    }

    private void ShowArchiveEntryInfo(FileEntry entry)
    {
        var lines = new List<(string, string)> { ("Nome", entry.Name), ("Caminho no compactado", ArchiveTree.PathFromId(entry.Id)) };
        lines.Add(("Tamanho", entry.Size is long s ? FormatBytes(s) : "desconhecido"));
        if (entry.Detail is { } d) lines.Add(("Compressão", d));
        if (entry.Modified is { } m) lines.Add(("Modificado", m.LocalDateTime.ToString("g")));
        if (entry.IsEncrypted) lines.Add(("Proteção", "protegido por senha"));
        ShowMessage("Entrada do compactado", lines, "Somente leitura. Use o menu de ações para extrair.", icon: ActionIcon.Archive);
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
            ("Senha", tree.EncryptedCount > 0 ? $"{Plural.Of(tree.EncryptedCount, "entrada protegida", "entradas protegidas")}" : info.HasEncryptedEntries ? "há entradas protegidas" : "não"),
            ("Bloqueadas (nome recusado)", tree.BlockedCount.ToString()),
            ("Bloqueadas (links/especiais)", tree.LinkCount.ToString()),
            ("Verificação", info.Capabilities.CanVerifyIntegrity ? "CRC durante a extração" : "indisponível"),
        };
        foreach (var limitation in info.Limitations) lines.Add(("Limitação", limitation));
        ShowMessage("Informações do compactado", lines, icon: ActionIcon.Archive);
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
