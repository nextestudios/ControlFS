using ControlFS.Core.Models;
using ControlFS.Core.Policies;

namespace ControlFS.Core.Contracts;

public enum DestinationMode
{
    /// <summary>Cria uma pasta nova e exclusiva (padrão seguro). Se o nome existir, usa "nome (2)".</summary>
    CreateDedicatedFolder,
    /// <summary>Extrai dentro de uma pasta existente; conflitos são resolvidos pelo usuário.</summary>
    IntoExistingFolder,
}

/// <summary>
/// Pedido de extração. Classe (não record) para que a senha nunca apareça em ToString/logs.
/// </summary>
public sealed class ExtractionRequest
{
    public required string ArchivePath { get; init; }
    public required string DestinationDirectory { get; init; }
    public DestinationMode Mode { get; init; } = DestinationMode.CreateDedicatedFolder;
    /// <summary>Nome da pasta dedicada (somente <see cref="DestinationMode.CreateDedicatedFolder"/>).</summary>
    public string? DedicatedFolderName { get; init; }
    /// <summary>
    /// Caminhos lógicos sanitizados selecionados ("pasta" ou "pasta/arquivo.txt"). Null = todas as entradas.
    /// Uma pasta selecionada inclui seus descendentes.
    /// </summary>
    public IReadOnlyCollection<string>? SelectedPaths { get; init; }
    /// <summary>Prefixo lógico removido dos caminhos (extração de seleção a partir de uma subpasta do compactado).</summary>
    public string BaseInnerPath { get; init; } = string.Empty;
    public string? Password { get; init; }
    public ExtractionLimits Limits { get; init; } = ExtractionLimits.Default;

    public override string ToString() => $"Extract '{Path.GetFileName(ArchivePath)}' ({Mode})";
}

public interface IExtractionInteraction
{
    /// <summary>Chamado para cada conflito sem decisão "aplicar aos demais". A escolha inicial deve preservar o existente.</summary>
    Task<ConflictDecision> ResolveConflictAsync(ConflictInfo conflict, CancellationToken cancellationToken);
}

public interface IArchiveService
{
    /// <summary>Detecta o formato pelo conteúdo; a extensão é apenas pista.</summary>
    ArchiveFormat Detect(string path);

    /// <exception cref="ArchiveAccessException"/>
    Task<ArchiveInfo> InspectAsync(string archivePath, string? password, ExtractionLimits limits, CancellationToken cancellationToken);

    Task<OperationResult> ExtractAsync(ExtractionRequest request, IExtractionInteraction interaction, IProgress<OperationProgress>? progress, CancellationToken cancellationToken);
}

public sealed class ArchiveAccessException(OperationErrorKind kind, string message, Exception? inner = null) : Exception(message, inner)
{
    public OperationErrorKind Kind { get; } = kind;
}
