using ControlFS.Core.Models;

namespace ControlFS.Core.Contracts;

public sealed record OperationHistoryLoadResult(IReadOnlyList<OperationHistoryEntry> Entries, string? Notice);

/// <summary>
/// Histórico de operações entre sessões, em JSON versionado com gravação atômica. Nunca contém senhas nem conteúdo de
/// arquivos: só o que <see cref="OperationHistoryEntry"/> descreve.
/// </summary>
public interface IOperationHistoryStore
{
    OperationHistoryLoadResult Load();

    void Save(IReadOnlyList<OperationHistoryEntry> entries);
}
