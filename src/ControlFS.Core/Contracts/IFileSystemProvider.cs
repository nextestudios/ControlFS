using ControlFS.Core.Models;

namespace ControlFS.Core.Contracts;

public sealed record DirectoryListing(string Path, IReadOnlyList<FileEntry> Entries, int InaccessibleCount);

public interface IFileSystemProvider
{
    /// <summary>Locais iniciais: pastas conhecidas obtidas do sistema e unidades prontas.</summary>
    IReadOnlyList<FileEntry> GetPlaces();

    Task<DirectoryListing> ListAsync(string path, bool includeHidden, CancellationToken cancellationToken);

    /// <summary>
    /// Busca por nome sob <see cref="SearchRequest.RootPath"/>, sem índice. Síncrono e preguiçoso: quem chama enumera fora
    /// da thread de UI. Nunca entra em junções, links simbólicos ou outros pontos de nova análise, exceto pastas de arquivos na nuvem
    /// (OneDrive sob demanda), que são pastas comuns e só têm os nomes listados (o próprio link pode ser
    /// um resultado). Pastas que não puderam ser lidas viram <see cref="SearchResult.Skipped"/>, nunca somem em silêncio.
    /// </summary>
    IEnumerable<SearchResult> Search(SearchRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Tamanho recursivo de <paramref name="path"/>. Síncrono (quem chama roda fora da thread de UI), cancelável e com
    /// parciais em <paramref name="progress"/>. Mesmas regras de descida da busca: nunca segue junções nem links.
    /// </summary>
    FolderSize MeasureFolder(string path, IProgress<FolderSize>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Análise de uso do disco (#72): lê a árvore de <paramref name="path"/> uma vez, com o mesmo percurso de
    /// <see cref="MeasureFolder"/> (nunca segue junções nem links), e devolve os totais por pasta. Síncrono, cancelável,
    /// com parciais em <paramref name="progress"/>.
    /// </summary>
    DiskUsage AnalyzeDiskUsage(string path, IProgress<FolderSize>? progress, CancellationToken cancellationToken);

    string? GetParent(string path);

    bool DirectoryExists(string path);

    /// <summary>
    /// O caminho está na rede (UNC ou unidade mapeada). Nesses locais o app não confere existência na thread de UI: um
    /// servidor desligado pode levar dezenas de segundos para responder.
    /// </summary>
    bool IsNetworkPath(string path) => Policies.NetworkPathPolicy.NormalizeShare(path) is not null;

    /// <summary>Cria uma pasta nova. Nunca reutiliza nem sobrescreve uma existente.</summary>
    /// <exception cref="FileOperationException">Com <see cref="OperationErrorKind"/> específico.</exception>
    FileEntry CreateDirectory(string parentPath, string name);
}

public sealed class FileOperationException(OperationErrorKind kind, string message, Exception? inner = null) : Exception(message, inner)
{
    public OperationErrorKind Kind { get; } = kind;
}
