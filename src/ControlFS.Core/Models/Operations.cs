namespace ControlFS.Core.Models;

public enum OperationKind
{
    Extract,
    Compress,
    CreateFolder,
    Copy,
    Move,
    Delete,
    Rename,
    TestArchive,
}

public enum OperationState
{
    Queued,
    Planning,
    Running,
    WaitingForUser,
    Paused,
    CancelRequested,
    Cancelled,
    Completed,
    CompletedWithWarnings,
    Failed,
}

/// <summary>Classificação de falhas. Quando o motor não distingue causas, usa-se um valor que declara a incerteza.</summary>
public enum OperationErrorKind
{
    None,
    PasswordRequired,
    WrongPassword,
    WrongPasswordOrCorrupt,
    Corrupt,
    UnsupportedFormat,
    UnsupportedMethod,
    MissingVolume,
    AccessDenied,
    InsufficientSpace,
    DestinationUnavailable,
    PathRejected,
    LinkOrSpecialBlocked,
    NameCollision,
    DestinationTraversesLink,
    LimitExceeded,
    AlreadyExists,
    InvalidName,
    Cancelled,
    Unknown,
}

public enum ItemOutcome
{
    Succeeded,
    Skipped,
    Renamed,
    Replaced,
    Failed,
    Blocked,
    NotProcessed,
}

public sealed record ItemResult(string Name, ItemOutcome Outcome, OperationErrorKind Error = OperationErrorKind.None, string? Message = null, string? FinalPath = null)
{
    /// <summary>Item físico de origem (operações de arquivo). Base para "tentar de novo só as falhas".</summary>
    public string? SourcePath { get; init; }

    /// <summary>Pasta onde o item deveria chegar (operações de arquivo com destino; pode ser uma subpasta do destino).</summary>
    public string? TargetFolder { get; init; }

    /// <summary>
    /// Teste de integridade: a entrada foi lida por completo, mas o formato não guarda checksum para conferir
    /// (TAR, GZ, ZIP AES AE-2), então só o tamanho e a leitura sem erro foram verificados.
    /// </summary>
    public bool NoChecksum { get; init; }

    /// <summary>Não chegou ao fim: falhou, não foi processado ou foi interrompido no diálogo de conflito.</summary>
    public bool NeedsRetry => Outcome is ItemOutcome.Failed or ItemOutcome.NotProcessed ||
        (Outcome == ItemOutcome.Skipped && Error == OperationErrorKind.Cancelled);
}

public sealed record OperationProgress(
    string? CurrentItem,
    int ItemsProcessed,
    int? ItemsTotal,
    long BytesProcessed,
    long? BytesTotal);

public sealed record OperationResult(
    OperationState FinalState,
    IReadOnlyList<ItemResult> Items,
    OperationErrorKind Error = OperationErrorKind.None,
    string? Message = null,
    string? Destination = null)
{
    public int Count(ItemOutcome outcome) => Items.Count(i => i.Outcome == outcome);

    /// <summary>
    /// Cópia/movimentação: itens de nível superior que chegaram ao destino como itens novos (sem substituir nem mesclar),
    /// com o caminho final. Base para desfazer (#22).
    /// </summary>
    public IReadOnlyList<PlacedItem> Placed { get; init; } = [];
}

/// <summary>Item de nível superior colocado no destino: de <see cref="SourcePath"/> para <see cref="FinalPath"/>.</summary>
public sealed record PlacedItem(string SourcePath, string FinalPath, bool IsDirectory);

public enum ConflictChoice
{
    Skip,
    KeepBoth,
    Replace,
    Cancel,
}

/// <summary>Decisão sobre um conflito. <see cref="ApplyToRemaining"/> vale apenas para a operação atual.</summary>
public sealed record ConflictDecision(ConflictChoice Choice, bool ApplyToRemaining = false);

public sealed record ConflictInfo(
    string ExistingPath,
    long? ExistingSize,
    DateTimeOffset? ExistingModified,
    bool ExistingIsDirectory,
    string IncomingName,
    long? IncomingSize,
    DateTimeOffset? IncomingModified,
    bool IncomingIsDirectory = false)
{
    /// <summary>Pasta chegando onde já existe pasta: "substituir" significa mesclar o conteúdo.</summary>
    public bool IsFolderMerge => ExistingIsDirectory && IncomingIsDirectory;
}
