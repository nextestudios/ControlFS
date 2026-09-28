using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Models;
using ControlFS.Core.Text;

namespace ControlFS.Application;

public sealed partial class AppController
{
    /// <summary>Máximo de resultados por busca: protege memória e lista. Acima disso a busca para e pede um termo melhor.</summary>
    internal const int SearchResultLimit = 10_000;

    /// <summary>Intervalo mínimo entre lotes de resultados entregues à thread de UI (a UI nunca recebe um item por vez).</summary>
    private static readonly TimeSpan SearchBatchInterval = TimeSpan.FromMilliseconds(150);

    /// <summary>Escopo da próxima busca: alternado explicitamente no menu; vale para a sessão.</summary>
    public bool SearchIncludesSubfolders { get; private set; } = true;

    /// <summary>Nome do escopo da busca nas pastas principais (Início).</summary>
    internal const string HomeSearchScope = "pastas principais";

    /// <summary>
    /// Busca (Select/View, Ctrl+F): abre o teclado virtual; Concluir inicia a busca na pasta atual. Em Meu computador,
    /// busca na unidade em foco; nos resultados, uma nova busca usa o mesmo escopo.
    /// </summary>
    internal void BeginSearch(PaneState pane)
    {
        if (pane.Mode != PaneMode.Browse || pane.IsLoading) return;
        SearchLocation scope;
        string initial;
        switch (pane.Location)
        {
            case PhysicalLocation physical:
                scope = new SearchLocation(physical.FullPath, string.Empty, SearchIncludesSubfolders);
                initial = pane.Search?.Location.Query ?? string.Empty;
                break;
            case SearchLocation search:
                scope = search;
                initial = search.Query;
                break;
            case ThisPcLocation when pane.List.Focused is { Kind: EntryKind.Drive, FullPath: { } drive, IsBlocked: false }:
                scope = new SearchLocation(drive, string.Empty, SearchIncludesSubfolders);
                initial = string.Empty;
                break;
            case ThisPcLocation:
                StatusMessage = "Escolha uma unidade e aperte Buscar para procurar nela.";
                return;
            default:
                StatusMessage = "A busca funciona em pastas do disco, na tela inicial e numa unidade de Meu computador.";
                return;
        }
        OpenSearchKeyboard(scope, initial, location => StartSearch(pane, location));
    }

    /// <summary>
    /// Busca no início: nas pastas principais (Downloads, Documentos, Área de trabalho, Imagens, Vídeos, Músicas),
    /// com o mesmo motor da busca numa pasta. Os resultados abrem no navegador; Voltar retorna ao início.
    /// </summary>
    internal void BeginHomeSearch()
    {
        var roots = HomeSearchRoots();
        if (roots.Count == 0)
        {
            StatusMessage = "Nenhuma pasta principal disponível para buscar.";
            return;
        }
        var scope = new SearchLocation(roots[0], string.Empty, SearchIncludesSubfolders) { Roots = roots, ScopeName = HomeSearchScope };
        OpenSearchKeyboard(scope, string.Empty, location =>
        {
            var pane = Browser;
            pane.Back.Clear();
            pane.Forward.Clear();
            pane.Location = null; // Voltar nos resultados volta ao início
            Screen = Screen.Browser;
            StartSearch(pane, location);
        });
    }

    /// <summary>Pastas principais do início que existem, sem repetir nem uma dentro da outra (a busca não passa duas vezes).</summary>
    private List<string> HomeSearchRoots()
    {
        var roots = new List<string>();
        foreach (var place in Places)
        {
            if (place is not { Kind: EntryKind.KnownFolder, IsBlocked: false, FullPath: { } path } || !place.Id.StartsWith("place:", StringComparison.Ordinal)) continue;
            var full = Path.TrimEndingDirectorySeparator(path);
            if (roots.Any(r => IsSameOrInside(full, r) || IsSameOrInside(r, full))) continue;
            roots.Add(full);
        }
        return roots;
    }

    private static bool IsSameOrInside(string path, string root) =>
        string.Equals(path, root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private void OpenSearchKeyboard(SearchLocation scope, string initial, Action<SearchLocation> start)
    {
        var where = scope.ScopeName is { } name ? $"nas {name}" : $"em “{FolderName(scope.RootPath)}”";
        var title = $"Buscar {where}" + (SearchIncludesSubfolders ? " e subpastas" : " (sem subpastas)");
        var keyboard = new VirtualKeyboard(TextFieldKind.Generic, title, initial,
            validator: text => string.IsNullOrWhiteSpace(text) ? "Digite parte do nome." : null);
        KeyboardModal? modal = null;
        modal = new KeyboardModal(keyboard, k =>
        {
            CloseModal(modal!);
            start(scope with { Query = k.Text.Trim(), IncludeSubfolders = SearchIncludesSubfolders });
            return Task.CompletedTask;
        });
        PushModal(modal);
    }

    /// <summary>
    /// Inicia a busca como um local virtual do painel. A enumeração roda fora da thread de UI; os resultados chegam em
    /// lotes limitados. A pasta de origem vai para o histórico (Voltar retorna a ela com o foco de antes).
    /// </summary>
    internal void StartSearch(PaneState pane, SearchLocation location)
    {
        ++pane.Generation;
        pane.LoadCts?.Cancel();
        var cts = pane.LoadCts = new CancellationTokenSource();
        pane.IsLoading = false;
        // Nova busca a partir dos resultados substitui a anterior no mesmo lugar do histórico.
        if (pane.Location is { } current and not SearchLocation) PushHistory(pane, current);
        var search = new SearchState(location) { Cts = cts, Filter = SearchFilter };
        pane.Search = search;
        pane.Archive = null;
        pane.ArchivePassword = null;
        pane.Location = location;
        pane.InaccessibleCount = 0;
        pane.List.SetItems([]);
        RaiseChanged();
        var requests = location.AllRoots.Select(root => new SearchRequest(root, location.Query, location.IncludeSubfolders, Settings.ShowHidden)).ToList();
        Track(RunSearchAsync(pane, search, requests, cts.Token));
    }

    private async Task RunSearchAsync(PaneState pane, SearchState search, List<SearchRequest> requests, CancellationToken token)
    {
        var batcher = new SearchBatcher(this, pane, search);
        SearchStatus status;
        try
        {
            status = await Task.Run(() =>
            {
                foreach (var request in requests)
                {
                    try
                    {
                        foreach (var result in _fs.Search(request, token))
                        {
                            if (result.InaccessibleFolder is { } folder) batcher.AddSkipped(folder);
                            else if (result.Match is { } match && batcher.Add(match) >= SearchResultLimit) return SearchStatus.LimitReached;
                        }
                    }
                    catch (Exception ex) when (requests.Count > 1 && ex is IOException or UnauthorizedAccessException)
                    {
                        batcher.AddSkipped(request.RootPath); // várias pastas: uma ilegível não derruba as outras
                    }
                }
                return SearchStatus.Completed;
            }, token);
        }
        catch (OperationCanceledException)
        {
            status = SearchStatus.Cancelled;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            status = SearchStatus.Failed;
        }
        batcher.Flush();
        if (search.IsRunning) search.Status = status;
        RaiseChanged();
    }

    /// <summary>Voltar/East durante a busca: para a enumeração e mantém os resultados parciais.</summary>
    private static void CancelSearch(SearchState search)
    {
        search.Cts?.Cancel();
        if (search.IsRunning) search.Status = SearchStatus.Cancelled;
    }

    /// <summary>Abrir um resultado: vai à pasta real dele com o foco no item. Voltar retorna aos resultados.</summary>
    private void RevealResult(PaneState pane, FileEntry entry)
    {
        if (entry.FullPath is not { } path || Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path)) is not { } folder) return;
        Track(NavigateAsync(pane, new PhysicalLocation(folder), pushHistory: true, focusId: entry.Name));
    }

    private void ShowSearchMenu(PaneState pane, SearchState search)
    {
        var entry = pane.List.Focused;
        var location = search.Location;
        var items = new List<MenuItem>
        {
            new("Mostrar na pasta", entry is null ? null : () => RevealResult(pane, entry), entry is null ? "Nenhum resultado em foco." : null, Icon: ActionIcon.Reveal),
            new("Nova busca…", () => BeginSearch(pane), Icon: ActionIcon.Search),
            new($"Subpastas: {(location.IncludeSubfolders ? "incluídas" : "não incluídas")}", () =>
            {
                SearchIncludesSubfolders = !location.IncludeSubfolders;
                StartSearch(pane, location with { IncludeSubfolders = SearchIncludesSubfolders });
            }, Detail: "Alterna e busca de novo.", Icon: ActionIcon.Subfolders),
            new($"Pastas puladas ({search.SkippedFolders.Count})", () => ShowSkippedFolders(search),
                search.SkippedFolders.Count == 0 ? "Nenhuma pasta foi pulada." : null, Icon: ActionIcon.Warning),
        };
        if (search.IsRunning) items.Add(new MenuItem("Cancelar busca", () => CancelSearch(search), Icon: ActionIcon.Cancel));
        if (entry is not null) items.Add(new MenuItem("Propriedades", () => ShowProperties(entry), Icon: ActionIcon.Properties));
        PushModal(new MenuModal(entry?.Name ?? "Busca", items) { Icon = ActionIcon.Search });
    }

    private void ShowSkippedFolders(SearchState search)
    {
        const int shown = 20;
        var lines = search.SkippedFolders.Take(shown).Select(f => ("Pasta", f)).ToList();
        if (search.SkippedFolders.Count > shown) lines.Add(("…", $"mais {search.SkippedFolders.Count - shown}"));
        ShowMessage("Pastas puladas na busca", lines, "Sem permissão de leitura ou indisponíveis: o conteúdo delas não foi pesquisado.", icon: ActionIcon.Warning);
    }

    private static string FolderName(string path) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } name ? name : path;

    /// <summary>
    /// Junta resultados da enumeração (thread de fundo) e os entrega à UI em lotes: no máximo um lote pendente por vez e
    /// um lote a cada <see cref="SearchBatchInterval"/>, por maior que seja a árvore.
    /// </summary>
    private sealed class SearchBatcher(AppController app, PaneState pane, SearchState search)
    {
        private readonly Lock _gate = new();
        private List<FileEntry> _found = [];
        private List<string> _skipped = [];
        private int _total;
        private bool _scheduled;
        private long _lastFlush = Environment.TickCount64;

        public int Add(FileEntry entry)
        {
            lock (_gate)
            {
                _found.Add(entry);
                Schedule();
                return ++_total;
            }
        }

        public void AddSkipped(string folder)
        {
            lock (_gate)
            {
                _skipped.Add(folder);
                Schedule();
            }
        }

        private void Schedule()
        {
            if (_scheduled) return;
            _scheduled = true;
            var wait = SearchBatchInterval.TotalMilliseconds - (Environment.TickCount64 - _lastFlush);
            if (wait <= 0) app.Post(Flush);
            else _ = Task.Delay(TimeSpan.FromMilliseconds(wait)).ContinueWith(_ => app.Post(Flush), TaskScheduler.Default);
        }

        /// <summary>Thread de UI: aplica tudo o que chegou desde o último lote.</summary>
        public void Flush()
        {
            List<FileEntry> found;
            List<string> skipped;
            lock (_gate)
            {
                found = _found;
                skipped = _skipped;
                _found = [];
                _skipped = [];
                _scheduled = false;
                _lastFlush = Environment.TickCount64;
            }
            if (found.Count == 0 && skipped.Count == 0) return;
            search.Results.AddRange(found);
            search.Skipped.AddRange(skipped);
            if (pane.ActiveSearch == search)
            {
                var now = DateTimeOffset.Now;
                pane.List.AppendItems(search.Filter.IsActive ? found.Where(f => search.Filter.Matches(f, now)).ToList() : found);
            }
        }
    }
}
