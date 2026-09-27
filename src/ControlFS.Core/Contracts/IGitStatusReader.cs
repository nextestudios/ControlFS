namespace ControlFS.Core.Contracts;

/// <summary>Mudança de um item (ou de algo dentro de uma pasta) num repositório Git, da mais para a menos importante.</summary>
public enum GitChange
{
    Conflicted,
    Modified,
    Renamed,
    Added,
    Deleted,
    Untracked,
}

/// <summary>
/// Status do Git da pasta mostrada (#75): o ramo e, para cada item da pasta, a mudança mais importante dele (numa
/// subpasta, a de qualquer coisa dentro dela). Itens sem mudança ficam de fora.
/// </summary>
public sealed record GitFolderStatus(string RepositoryRoot, string Branch, IReadOnlyDictionary<string, GitChange> Children)
{
    public int ChangedCount => Children.Count;
}

/// <summary>Leitura somente leitura do status do Git; nunca muda o repositório nem executa comandos dele.</summary>
public interface IGitStatusReader
{
    /// <summary>Status da pasta, ou null se ela não está num repositório (ou o repositório não pôde ser lido). Síncrono: rode fora da UI.</summary>
    GitFolderStatus? Read(string folder);
}
