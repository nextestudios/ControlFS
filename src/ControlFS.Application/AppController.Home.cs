using ControlFS.Application.State;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;

namespace ControlFS.Application;

/// <summary>
/// Início na grade: seções (Favoritos, Pastas principais, Unidades e dispositivos, Outros locais) com navegação 2D por
/// seção, e a contagem/tamanho reais das pastas principais. A soma usa o mesmo percurso do "Calcular tamanho" (#55:
/// nunca segue junções, cancelável), fora da thread de UI, uma pasta por vez, com tempo limite por pasta e resultado
/// guardado por alguns minutos: voltar ao início não relê o disco. Sair do início cancela a soma em andamento.
/// </summary>
public sealed partial class AppController
{
    private IReadOnlyList<FileEntry>? _sectionsSource;
    private IReadOnlyList<HomeSection> _sections = [];
    private readonly Dictionary<HomeSectionKind, int> _homeColumns = [];
    private readonly Dictionary<string, (FolderStats Stats, TimeSpan At)> _folderStats = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Soma em andamento (descartada pela própria tarefa ao terminar).</summary>
    private CancellationTokenSource? StatsRun { get; set; }

    /// <summary>Por quanto tempo uma soma pronta vale (voltar ao início dentro desse tempo não relê o disco).</summary>
    internal TimeSpan FolderStatsLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Tempo máximo de soma por pasta; passou disso, fica o parcial com "+".</summary>
    internal TimeSpan FolderStatsBudget { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Seções do início na grade (sempre na ordem: favoritos, pastas principais, unidades, outros locais).</summary>
    public IReadOnlyList<HomeSection> HomeSections
    {
        get
        {
            if (!ReferenceEquals(_sectionsSource, Places))
            {
                _sectionsSource = Places;
                _sections = BuildHomeSections();
            }
            return _sections;
        }
    }

    private List<HomeSection> BuildHomeSections()
    {
        var favorites = new List<int>();
        var folders = new List<int>();
        var drives = new List<int>();
        var other = new List<int>();
        for (var i = 0; i < Places.Count; i++)
        {
            var place = Places[i];
            if (IsFavoriteEntry(place)) favorites.Add(i);
            else if (place.Kind == EntryKind.Drive) drives.Add(i);
            else if (IsRecentPlace(place) || place.Id == RecycleBinLocation.PlaceId) other.Add(i);
            else folders.Add(i);
        }
        var sections = new List<HomeSection>();
        if (favorites.Count > 0) sections.Add(new HomeSection(HomeSectionKind.Favorites, "Favoritos", favorites));
        if (folders.Count > 0) sections.Add(new HomeSection(HomeSectionKind.Folders, "Pastas principais", folders));
        if (drives.Count > 0) sections.Add(new HomeSection(HomeSectionKind.Drives, "Unidades e dispositivos", drives));
        if (other.Count > 0) sections.Add(new HomeSection(HomeSectionKind.Other, "Outros locais", other));
        return sections;
    }

    /// <summary>Colunas de cada seção como a view mostra (a navegação 2D usa as mesmas).</summary>
    public void SetHomeGridLayout(IReadOnlyDictionary<HomeSectionKind, int> columns)
    {
        foreach (var (kind, count) in columns) _homeColumns[kind] = Math.Max(1, count);
    }

    public int HomeColumns(HomeSectionKind kind) => _homeColumns.TryGetValue(kind, out var columns) ? columns : 1;

    /// <summary>Muda a cada soma concluída (a lista refaz só as linhas visíveis quando muda).</summary>
    public int FolderStatsVersion { get; private set; }

    /// <summary>Contagem e tamanho de uma pasta principal: pronto, parcial, indisponível ou "Calculando…".</summary>
    public FolderStats FolderStatsFor(string path) => _folderStats.TryGetValue(path, out var cached) ? cached.Stats : FolderStats.Calculating;

    /// <summary>Pasta principal do início (tem contagem e tamanho reais, na grade e na lista).</summary>
    public bool IsHomeStatFolder(string path) => HomeStatFolders().Contains(path, StringComparer.OrdinalIgnoreCase);

    /// <summary>Pastas principais do início (as que têm contagem e tamanho).</summary>
    private IEnumerable<string> HomeStatFolders()
    {
        foreach (var section in HomeSections)
        {
            if (section.Kind != HomeSectionKind.Folders) continue;
            foreach (var index in section.Places)
                if (Places[index] is { FullPath: { } path, IsBlocked: false }) yield return path;
        }
    }

    /// <summary>
    /// Chamado a cada mudança de estado: no início (grade ou lista), soma as pastas que ainda não têm valor recente; fora do
    /// início, cancela a soma em andamento (o que já terminou fica guardado).
    /// </summary>
    private void UpdateHomeStats()
    {
        if (Screen != Screen.Home)
        {
            StatsRun?.Cancel();
            return;
        }
        if (StatsRun is not null) return; // grade (cartões) e lista (coluna "Tamanho") mostram as mesmas somas
        var now = Clock();
        var pending = HomeStatFolders().Where(path => !_folderStats.TryGetValue(path, out var cached) || now - cached.At > FolderStatsLifetime).ToList();
        if (pending.Count == 0) return;
        foreach (var path in pending) _folderStats.Remove(path); // vencidos voltam a "Calculando…"
        var cts = StatsRun = new CancellationTokenSource();
        Track(MeasureHomeFoldersAsync(pending, cts));
    }

    private async Task MeasureHomeFoldersAsync(List<string> folders, CancellationTokenSource cts)
    {
        try
        {
            foreach (var folder in folders)
            {
                if (cts.IsCancellationRequested) return;
                if (await MeasureStatsAsync(folder, cts.Token) is not { } result) return; // saiu do início: recomeça na próxima visita
                _folderStats[folder] = (result, Clock());
                FolderStatsVersion++;
                RaiseChanged();
            }
        }
        finally
        {
            if (ReferenceEquals(StatsRun, cts)) StatsRun = null;
            cts.Dispose();
            if (Screen == Screen.Home) UpdateHomeStats(); // voltou ao início enquanto a soma cancelada terminava
        }
    }

    /// <summary>
    /// Soma recursiva de uma pasta (mesmo percurso do "Calcular tamanho", #55) fora da thread de UI, com tempo limite:
    /// passado o limite, o parcial real (com "+"). Null quando <paramref name="cancellationToken"/> cancelou.
    /// </summary>
    private async Task<FolderStats?> MeasureStatsAsync(string folder, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(FolderStatsBudget);
        var partial = new LatestProgress();
        try
        {
            var size = await Task.Run(() => _fs.MeasureFolder(folder, partial, budget.Token), CancellationToken.None);
            return new FolderStats(FolderStatsState.Ready, size.Files + size.Folders, size.Bytes);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var last = partial.Last ?? FolderSize.Empty;
            return new FolderStats(FolderStatsState.Partial, last.Files + last.Folders, last.Bytes);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex) when (ex is FileOperationException or IOException or UnauthorizedAccessException)
        {
            return new FolderStats(FolderStatsState.Unavailable);
        }
    }

    /// <summary>Guarda o último parcial do percurso (chamado na thread do percurso).</summary>
    private sealed class LatestProgress : IProgress<FolderSize>
    {
        private FolderSize? _last;

        public FolderSize? Last => Volatile.Read(ref _last);

        public void Report(FolderSize value) => Volatile.Write(ref _last, value);
    }

    /// <summary>
    /// Linhas de um local nos cartões do início (as mesmas lidas pelo Narrador): pasta = caminho e, nas pastas
    /// principais em grade, "N itens • tamanho"; unidade = "X livres de Y"; favorito = caminho e "Favorito".
    /// </summary>
    public (string Primary, string? Secondary) DescribePlace(FileEntry place)
    {
        if (place.IsBlocked) return ("Indisponível: " + place.BlockedReason, null);
        if (place.Kind == EntryKind.Drive) return (EntryText.DriveUsage(place), null);
        if (IsFavoriteEntry(place)) return (place.FullPath ?? string.Empty, "Favorito");
        if (place.FullPath is { } path)
            return (path, IsGrid && HomeStatFolders().Contains(path, StringComparer.OrdinalIgnoreCase) ? EntryText.FolderStats(FolderStatsFor(path)) : null);
        return (place.Detail ?? EntryText.TypeName(place), null);
    }

    // ---------- Meu computador ----------

    /// <summary>Unidades prontas, na ordem do sistema (a mesma lista do início).</summary>
    private List<FileEntry> ThisPcEntries() => [.. _fs.GetPlaces().Where(p => p.Kind == EntryKind.Drive)];

    /// <summary>
    /// Meu computador (raiz do caminho ou acesso rápido): as unidades como cartões na grade e linhas na lista. No
    /// navegador abre na aba atual com histórico; no início, abre o navegador.
    /// </summary>
    private void ShowThisPc() => OpenFromTopBar(ThisPcLocation.Instance);
}
