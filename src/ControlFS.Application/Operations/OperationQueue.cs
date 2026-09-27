using ControlFS.Core.Models;

namespace ControlFS.Application.Operations;

public sealed class OperationItem
{
    internal OperationItem(int id, string title, OperationKind kind, Func<OperationItem, CancellationToken, Task<OperationResult>> work)
    {
        Id = id;
        Title = title;
        Kind = kind;
        Work = work;
    }

    public int Id { get; }
    public string Title { get; }
    public OperationKind Kind { get; }
    public OperationState State { get; private set; } = OperationState.Queued;
    public OperationProgress? Progress { get; internal set; }
    public OperationResult? Result { get; private set; }
    internal Func<OperationItem, CancellationToken, Task<OperationResult>> Work { get; }
    internal CancellationTokenSource Cts { get; } = new();

    public bool IsActive => !OperationStateMachine.IsTerminal(State);

    /// <summary>
    /// Refaz o pedido original (o motor planeja de novo; nada do estado anterior é presumido). Definido por quem enfileirou.
    /// </summary>
    internal Action? RetryAction { get; set; }

    /// <summary>Tentar de novo só vale para operações encerradas que não terminaram limpas.</summary>
    public bool CanRetry => RetryAction is not null && State is OperationState.Failed or OperationState.CompletedWithWarnings or OperationState.Cancelled;

    internal bool TryTransition(OperationState to)
    {
        if (!OperationStateMachine.CanTransition(State, to)) return false;
        State = to;
        return true;
    }

    internal void Finish(OperationResult result)
    {
        Result = result;
        if (!TryTransition(result.FinalState))
        {
            // Resultado sempre prevalece como estado terminal verdadeiro.
            State = result.FinalState;
        }
    }
}

/// <summary>
/// Fila serial: uma operação de disco por vez (política simples e explicável).
/// Deve ser usada na thread de UI; o trabalho em si roda fora dela.
/// </summary>
public sealed class OperationQueue
{
    private readonly List<OperationItem> _items = [];
    private int _nextId = 1;
    private bool _running;

    public IReadOnlyList<OperationItem> Items => _items;
    public OperationItem? Current => _items.FirstOrDefault(i => i.State is OperationState.Planning or OperationState.Running or OperationState.WaitingForUser or OperationState.CancelRequested);
    public int ActiveCount => _items.Count(i => i.IsActive);

    public event Action? Changed;
    public event Action<OperationItem>? Completed;

    public OperationItem Enqueue(string title, OperationKind kind, Func<OperationItem, CancellationToken, Task<OperationResult>> work)
    {
        var item = new OperationItem(_nextId++, title, kind, work);
        _items.Add(item);
        Changed?.Invoke();
        _ = PumpAsync();
        return item;
    }

    public bool Cancel(OperationItem item)
    {
        if (item.State == OperationState.Queued)
        {
            item.Finish(new OperationResult(OperationState.Cancelled, [], OperationErrorKind.Cancelled, "Cancelada antes de iniciar."));
            Changed?.Invoke();
            Completed?.Invoke(item);
            return true;
        }
        if (!item.TryTransition(OperationState.CancelRequested)) return false;
        item.Cts.Cancel();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Refaz o pedido original como uma nova operação na fila. A operação antiga permanece no histórico.</summary>
    public bool Retry(OperationItem item)
    {
        if (!item.CanRetry) return false;
        item.RetryAction!();
        return true;
    }

    internal void SetWaiting(OperationItem item, bool waiting)
    {
        if (item.TryTransition(waiting ? OperationState.WaitingForUser : OperationState.Running)) Changed?.Invoke();
    }

    internal void ReportProgress(OperationItem item, OperationProgress progress)
    {
        item.Progress = progress;
        Changed?.Invoke();
    }

    private async Task PumpAsync()
    {
        if (_running) return;
        _running = true;
        try
        {
            while (_items.FirstOrDefault(i => i.State == OperationState.Queued) is { } next)
            {
                next.TryTransition(OperationState.Planning);
                next.TryTransition(OperationState.Running);
                Changed?.Invoke();
                OperationResult result;
                try
                {
                    result = await next.Work(next, next.Cts.Token);
                }
                catch (OperationCanceledException)
                {
                    result = new OperationResult(OperationState.Cancelled, [], OperationErrorKind.Cancelled, "Operação cancelada.");
                }
                catch (Exception ex)
                {
                    result = new OperationResult(OperationState.Failed, [], OperationErrorKind.Unknown, $"Erro inesperado: {ex.GetType().Name}.");
                }
                next.Finish(result);
                Changed?.Invoke();
                Completed?.Invoke(next);
            }
        }
        finally
        {
            _running = false;
        }
    }
}
