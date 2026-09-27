using ControlFS.Application.State;
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

    /// <summary>Busca (Select/View, Ctrl+F): abre o teclado virtual; Concluir inicia a busca na pasta atual.</summary>
    internal void BeginSearch(PaneState pane)
    {
        if (pane.Mode != PaneMode.Browse || pane.IsLoading) return;
        string root;
        string initial;
        switch (pane.Location)
        {
            case PhysicalLocation physical:
                root = physical.FullPath;
                initial = pane.Search?.Location.Query ?? string.Empty;
                break;
            case SearchLocation search:
                root = search.RootPath;
                initial = search.Query;
                break;
            default:
                StatusMessage = "A busca funciona em pastas do disco.";
                return;
        }
        var title = $"Buscar em \"{FolderName(root)}\"" + (SearchIncludesSubfolders ? " e subpastas" : " (sem subpastas)");
        var keyboard = new VirtualKeyboard(TextFieldKind.Generic, title, initial,
            validator: text => string.IsNullOrWhiteSpace(text) ? "Digite parte do nome." : null);
        KeyboardModal? modal = null;
        modal = new KeyboardModal(keyboard, k =>
        {
            CloseModal(modal!);
            StartSearch(pane, new SearchLocation(root, k.Text.Trim(), SearchIncludesSubfolders));
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
        var search = new SearchState(location) { Cts = cts };
        pane.Search = search;
        pane.Archive = null;
        pane.ArchivePassword = null;
        pane.Location = location;
        pane.InaccessibleCount = 0;
        pane.List.SetItems([]);
        RaiseChanged();
        Track(RunSearchAsync(pane, search, new SearchRequest(location.RootPath, location.Query, location.IncludeSubfolders, Settings.ShowHidden), cts.Token));
    }

    private async Task RunSearchAsync(PaneState pane, SearchState search, SearchRequest request, CancellationToken token)
    {
        var batcher = new SearchBatcher(this, pane, search);
        SearchStatus status;
        try
        {
            status = await Task.Run(() =>
            {
                foreach (var result in _fs.Search(request, token))
                {
                    if (result.InaccessibleFolder is { } folder) batcher.AddSkipped(folder);
                    else if (result.Match is { } match && batcher.Add(match) >= SearchResultLimit) return SearchStatus.LimitReached;
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
            new("Mostrar na pasta", entry is null ? null : () => RevealResult(pane, entry), entry is null ? "Nenhum resultado em foco." : null),
            new("Nova busca…", () => BeginSearch(pane)),
            new($"Subpastas: {(location.IncludeSubfolders ? "incluídas" : "não incluídas")}", () =>
            {
                SearchIncludesSubfolders = !location.IncludeSubfolders;
                StartSearch(pane, location with { IncludeSubfolders = SearchIncludesSubfolders });
            }, Detail: "Alterna e busca de novo."),
            new($"Pastas puladas ({search.SkippedFolders.Count})", () => ShowSkippedFolders(search),
                search.SkippedFolders.Count == 0 ? "Nenhuma pasta foi pulada." : null),
        };
        if (search.IsRunning) items.Add(new MenuItem("Cancelar busca", () => CancelSearch(search)));
        if (entry is not null) items.Add(new MenuItem("Propriedades", () => ShowProperties(entry)));
        PushModal(new MenuModal(entry?.Name ?? "Busca", items));
    }

    private void ShowSkippedFolders(SearchState search)
    {
        const int shown = 20;
        var lines = search.SkippedFolders.Take(shown).Select(f => ("Pasta", f)).ToList();
        if (search.SkippedFolders.Count > shown) lines.Add(("…", $"mais {search.SkippedFolders.Count - shown}"));
        ShowMessage("Pastas puladas na busca", lines, "Sem permissão de leitura ou indisponíveis: o conteúdo delas não foi pesquisado.");
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
            if (pane.ActiveSearch == search) pane.List.AppendItems(found);
        }
    }
}
