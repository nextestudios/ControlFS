using ControlFS.Core.Models;

namespace ControlFS.Core.Contracts;

/// <summary>Decisão do usuário diante de um conflito de nome. A escolha inicial deve preservar o existente.</summary>
public interface IConflictInteraction
{
    /// <summary>Chamado para cada conflito sem decisão "aplicar aos demais".</summary>
    Task<ConflictDecision> ResolveConflictAsync(ConflictInfo conflict, CancellationToken cancellationToken);
}

public enum FileOperationKind
{
    Copy,
    Move,
    /// <summary>Para a Lixeira do Windows, ou permanente quando <see cref="FileOperationRequest.Permanent"/>.</summary>
    Delete,
}

/// <summary>Pedido de operação sobre itens físicos. Nunca sobrescreve sem decisão explícita do usuário.</summary>
public sealed class FileOperationRequest
{
    public required FileOperationKind Kind { get; init; }
    public required IReadOnlyList<string> Sources { get; init; }
    /// <summary>Pasta de destino (cópia e movimentação).</summary>
    public string? DestinationFolder { get; init; }
    /// <summary>Exclusão permanente (só com confirmação específica; nunca como fallback silencioso da Lixeira).</summary>
    public bool Permanent { get; init; }
    /// <summary>
    /// Mover: pastas de origem que ficaram para trás numa tentativa anterior. Ao final (sem cancelamento), são removidas
    /// se contiverem apenas pastas vazias; nunca se tiverem algum arquivo ou link.
    /// </summary>
    public IReadOnlyList<string>? LeftoverSourceFolders { get; init; }

    /// <summary>
    /// Pausa cooperativa: respeitada entre itens e, na cópia, entre blocos de um arquivo. Um arquivo parcial fica no
    /// temporário oculto até continuar ou cancelar; nada aparece no destino pela metade.
    /// </summary>
    public PauseGate? Pause { get; init; }

    public override string ToString() => $"{Kind} {Sources.Count} item(s)";
}

/// <summary>
/// Motor comum de operações de arquivo: planeja, informa progresso, pergunta sobre conflitos e devolve resultado por
/// item. Links e junctions dentro de pastas nunca são seguidos.
/// </summary>
public interface IFileOperationService
{
    Task<OperationResult> RunAsync(FileOperationRequest request, IConflictInteraction conflicts, IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>Verdadeiro se o Windows oferece Lixeira para o local (unidades locais; não para rede ou alguns removíveis).</summary>
    bool CanRecycle(string path);

    /// <summary>Renomeia no mesmo diretório. Nunca sobrescreve.</summary>
    /// <exception cref="FileOperationException"/>
    FileEntry Rename(string path, string newName);
}
