namespace ControlFS.Core.Models;

/// <summary>
/// Um item no histórico: nome, desfecho e caminhos. Nunca guarda conteúdo de arquivos nem senhas.
/// </summary>
public sealed record OperationHistoryItem(string Name, ItemOutcome Outcome, OperationErrorKind Error = OperationErrorKind.None, string? Message = null)
{
    public string? SourcePath { get; init; }
    public string? FinalPath { get; init; }
}

/// <summary>
/// O que uma operação concluída fez (#20): tipo, horários, origem/destino e desfecho por item. Registra o que aconteceu;
/// por si só não promete desfazer nada.
/// </summary>
public sealed record OperationHistoryEntry
{
    /// <summary>Itens guardados por operação; o resto só entra nas contagens.</summary>
    public const int MaxItems = 100;

    /// <summary>Limite de texto das mensagens guardadas.</summary>
    public const int MaxMessageLength = 300;

    public required string Id { get; init; }
    public required OperationKind Kind { get; init; }
    public required string Title { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public required DateTimeOffset FinishedAt { get; init; }
    public required OperationState FinalState { get; init; }
    public OperationErrorKind Error { get; init; }
    public string? Message { get; init; }

    /// <summary>Pasta de origem, compactado extraído, ou item renomeado.</summary>
    public string? Source { get; init; }

    public string? Destination { get; init; }

    /// <summary>Total de itens (pode ser maior que <see cref="Items"/>, que é limitado).</summary>
    public int ItemCount { get; init; }

    public IReadOnlyDictionary<ItemOutcome, int> Counts { get; init; } = new Dictionary<ItemOutcome, int>();
    public IReadOnlyList<OperationHistoryItem> Items { get; init; } = [];

    public int Count(ItemOutcome outcome) => Counts.TryGetValue(outcome, out var n) ? n : 0;

    /// <summary>
    /// Monta a entrada a partir do resultado. Problemas vêm primeiro na lista limitada, para que nunca se percam.
    /// </summary>
    public static OperationHistoryEntry From(OperationKind kind, string title, DateTimeOffset? startedAt, DateTimeOffset finishedAt,
        OperationResult result, string? source, string? destination)
    {
        var counts = result.Items.GroupBy(i => i.Outcome).ToDictionary(g => g.Key, g => g.Count());
        var items = result.Items
            .OrderBy(i => i.Outcome is ItemOutcome.Failed or ItemOutcome.Blocked or ItemOutcome.NotProcessed ? 0 : 1)
            .Take(MaxItems)
            .Select(i => new OperationHistoryItem(i.Name, i.Outcome, i.Error, Truncate(i.Message)) { SourcePath = i.SourcePath, FinalPath = i.FinalPath })
            .ToList();
        return new OperationHistoryEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = kind,
            Title = title,
            StartedAt = startedAt,
            FinishedAt = finishedAt,
            FinalState = result.FinalState,
            Error = result.Error,
            Message = Truncate(result.Message),
            Source = source,
            Destination = result.Destination ?? destination,
            ItemCount = result.Items.Count,
            Counts = counts,
            Items = items,
        };
    }

    private static string? Truncate(string? text) =>
        text is null || text.Length <= MaxMessageLength ? text : text[..MaxMessageLength] + "…";
}
