namespace ControlFS.Core.Contracts;

public enum TemporaryKind
{
    /// <summary>Pasta de staging da extração (<c>.controlfs-staging-*</c>), com manifesto que contém o token do registro.</summary>
    StagingFolder,
    /// <summary>Arquivo parcial de cópia ou compactação (<c>.controlfs-copy-*.part</c>, <c>.controlfs-new-*.part</c>).</summary>
    PartialFile,
    /// <summary>Pasta vazia criada só para ser renomeada (<c>.controlfs-new-*</c>).</summary>
    EmptyFolder,
}

/// <summary>Resultado da limpeza de sobras: o que foi removido e quantos registros ficaram para depois (ex.: em uso).</summary>
public sealed record LeftoverCleanup(IReadOnlyList<string> Removed, int Kept);

/// <summary>
/// Registro persistente dos temporários que o ControlFS cria no disco do usuário. Cada temporário é registrado
/// <b>antes</b> de ser criado e o registro só sai quando ele deixa de existir; assim, depois de uma queda ou falta de
/// energia, a próxima inicialização sabe exatamente o que é dela. Nada é apagado só pelo padrão do nome.
/// </summary>
public interface ITemporaryJournal
{
    /// <summary>
    /// Registra <paramref name="path"/> (ainda não criado). Descarte o retorno somente depois que o temporário não existir
    /// mais; se ele ficar para trás, o registro também fica.
    /// </summary>
    IDisposable Register(string path, TemporaryKind kind, string? token = null);

    /// <summary>
    /// Remove sobras de execuções que terminaram sem limpar (o processo dono não está mais rodando). Só apaga o que o
    /// registro prova ser do ControlFS: pasta de staging com manifesto e token corretos, arquivo parcial ou pasta vazia com
    /// o nome exato registrado. Links nunca são seguidos.
    /// </summary>
    LeftoverCleanup CleanUpLeftovers();
}

/// <summary>Sem registro (testes e componentes usados isoladamente).</summary>
public sealed class NoTemporaryJournal : ITemporaryJournal
{
    public static NoTemporaryJournal Instance { get; } = new();

    private NoTemporaryJournal()
    {
    }

    public IDisposable Register(string path, TemporaryKind kind, string? token = null) => Nothing.Instance;

    public LeftoverCleanup CleanUpLeftovers() => new([], 0);

    private sealed class Nothing : IDisposable
    {
        public static Nothing Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
