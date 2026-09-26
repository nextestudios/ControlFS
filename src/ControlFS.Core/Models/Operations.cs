namespace ControlFS.Core.Models;

public enum OperationKind
{
    Extract,
    Compress,
    CreateFolder,
    Copy,
    Move,
    Delete,
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

public sealed record ItemResult(string Name, ItemOutcome Outcome, OperationErrorKind Error = OperationErrorKind.None, string? Message = null, string? FinalPath = null);

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
}

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
    DateTimeOffset? IncomingModified);
