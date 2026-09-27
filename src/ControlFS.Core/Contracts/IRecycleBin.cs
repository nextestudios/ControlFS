namespace ControlFS.Core.Contracts;

/// <summary>
/// Item na Lixeira. <see cref="Id"/> identifica o conteúdo guardado pelo Windows; <see cref="OriginalPath"/> vem de
/// metadados em disco e só é usado depois de validado. <see cref="Problem"/> diz por que o item não pode ser restaurado
/// (ex.: local original inválido); nesse caso só a exclusão permanente é oferecida.
/// </summary>
public sealed record RecycledItem(string Id, string Name, string OriginalPath, bool IsDirectory, long? Size, DateTimeOffset? DeletedAt, string? Problem = null);

/// <summary>Lixeira do Windows do usuário atual, em todas as unidades.</summary>
public interface IRecycleBin
{
    /// <summary>Itens da Lixeira. Síncrono: quem chama roda fora da thread de UI.</summary>
    IReadOnlyList<RecycledItem> List(CancellationToken cancellationToken);

    /// <summary>Devolve o item ao local original, recriando pastas que faltarem. Nunca sobrescreve um item que já esteja lá.</summary>
    /// <returns>O caminho restaurado.</returns>
    /// <exception cref="FileOperationException"/>
    string Restore(string id);

    /// <summary>Exclui de vez (sem seguir links dentro de pastas).</summary>
    /// <exception cref="FileOperationException"/>
    void DeletePermanently(string id);
}
