using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Windows.FileSystem;

namespace ControlFS.UnitTests.Support;

/// <summary>
/// Provedor REAL de filesystem com locais iniciais restritos ao diretório temporário do teste
/// (nunca expõe pastas reais do usuário). Opcionalmente atrasa listagens para testar respostas tardias.
/// </summary>
public sealed class TestFileSystem(string root) : IFileSystemProvider
{
    private readonly LocalFileSystemProvider _real = new();

    public Dictionary<string, TimeSpan> Delays { get; } = new(StringComparer.Ordinal);

    /// <summary>Unidades simuladas depois da pasta de teste (pendrive "conectado" durante o teste).</summary>
    public List<FileEntry> Drives { get; } = [];

    public IReadOnlyList<FileEntry> GetPlaces() => [new FileEntry("place:" + root, "Pasta de teste", EntryKind.KnownFolder, FullPath: root), .. Drives];

    public async Task<DirectoryListing> ListAsync(string path, bool includeHidden, CancellationToken cancellationToken)
    {
        if (Delays.TryGetValue(Path.GetFullPath(path), out var delay)) await Task.Delay(delay, CancellationToken.None);
        return await _real.ListAsync(path, includeHidden, cancellationToken);
    }

    /// <summary>Pausa a busca depois de N resultados até <see cref="ResumeSearch"/>: torna observáveis o estado parcial e o cancelamento.</summary>
    public int? PauseSearchAfter { get; set; }

    /// <summary>A enumeração da busca terminou (concluída ou interrompida) e liberou a pasta.</summary>
    public bool SearchEnded { get; private set; }

    private readonly ManualResetEventSlim _resume = new(false);

    public void ResumeSearch() => _resume.Set();

    public IEnumerable<SearchResult> Search(SearchRequest request, CancellationToken cancellationToken)
    {
        SearchEnded = false;
        var found = 0;
        try
        {
            foreach (var result in _real.Search(request, cancellationToken))
            {
                yield return result;
                if (result.Match is not null && ++found == PauseSearchAfter) _resume.Wait(cancellationToken);
            }
        }
        finally
        {
            SearchEnded = true;
        }
    }

    /// <summary>Segura o cálculo de tamanho até ser cancelado: torna observável o cancelamento.</summary>
    public bool HoldMeasureUntilCancelled { get; set; }

    private int _measureCalls;

    /// <summary>Quantas somas de tamanho foram pedidas (o início não deve reler o disco a cada visita).</summary>
    public int MeasureCalls => Volatile.Read(ref _measureCalls);

    public FolderSize MeasureFolder(string path, IProgress<FolderSize>? progress, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _measureCalls);
        if (HoldMeasureUntilCancelled)
        {
            progress?.Report(new FolderSize(1, 1, 0, [], 0));
            cancellationToken.WaitHandle.WaitOne();
            cancellationToken.ThrowIfCancellationRequested();
        }
        return _real.MeasureFolder(path, progress, cancellationToken);
    }

    public string? GetParent(string path) => _real.GetParent(path);

    public bool DirectoryExists(string path) => _real.DirectoryExists(path);

    public FileEntry CreateDirectory(string parentPath, string name) => _real.CreateDirectory(parentPath, name);
}
