using ControlFS.Core.Models;
using ControlFS.Core.Policies;

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

    /// <summary>Andamento (0–1) pelos bytes ou, sem o total de bytes, pelos itens; null enquanto o total não é conhecido.</summary>
    public double? Fraction => Progress switch
    {
        { BytesTotal: > 0 and var bytes } p => Math.Clamp((double)p.BytesProcessed / bytes, 0, 1),
        { ItemsTotal: > 0 and var items } p => Math.Clamp((double)p.ItemsProcessed / items, 0, 1),
        _ => null,
    };

    /// <summary>Quando saiu da fila e começou (null: cancelada antes de iniciar).</summary>
    public DateTimeOffset? StartedAt { get; internal set; }

    /// <summary>Origem para o histórico (pasta de origem ou compactado). Definido por quem enfileirou.</summary>
    public string? Source { get; internal set; }

    /// <summary>Destino para o histórico. Definido por quem enfileirou; o resultado pode trazer o destino real.</summary>
    public string? Destination { get; internal set; }

    /// <summary>Pausa cooperativa, só para motores que sabem parar num ponto seguro (null: não pausa; a opção fica oculta).</summary>
    internal PauseGate? PauseGate { get; set; }

    public bool CanPause => PauseGate is not null && State == OperationState.Running;
    public bool CanResume => PauseGate is not null && State == OperationState.Paused;

    /// <summary>Entrada do histórico gravada ao concluir.</summary>
    public string? HistoryEntryId { get; internal set; }

    /// <summary>
    /// Refaz o pedido original (o motor planeja de novo; nada do estado anterior é presumido). Definido por quem enfileirou.
    /// </summary>
    internal Action? RetryAction { get; set; }

    /// <summary>Tentar de novo só vale para operações encerradas que não terminaram limpas.</summary>
    public bool CanRetry => RetryAction is not null && State is OperationState.Failed or OperationState.CompletedWithWarnings or OperationState.Cancelled;

    /// <summary>Refaz só os itens que falharam ou não foram processados. Definido ao concluir, a partir do resultado por item.</summary>
    internal Action? RetryFailedAction { get; set; }

    /// <summary>Quantos itens a opção "tentar de novo só as falhas" refaria.</summary>
    public int RetryableItemCount { get; internal set; }

    public bool CanRetryFailed => RetryFailedAction is not null && RetryableItemCount > 0 &&
        State is OperationState.Failed or OperationState.CompletedWithWarnings or OperationState.Cancelled;

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
    public OperationItem? Current => _items.FirstOrDefault(i => i.State is OperationState.Planning or OperationState.Running or OperationState.WaitingForUser or OperationState.Paused or OperationState.CancelRequested);
    public int ActiveCount => _items.Count(i => i.IsActive);

    /// <summary>
    /// Andamento somado das operações ativas (0–1; a média, cada uma sem total conhecido contando como zero), para a barra
    /// fina sob o título; null sem operação ativa.
    /// </summary>
    public double? Progress
    {
        get
        {
            var active = _items.Where(i => i.IsActive).ToList();
            return active.Count == 0 ? null : active.Average(i => i.Fraction ?? 0);
        }
    }

    public event Action? Changed;
    public event Action<OperationItem>? Completed;

    /// <param name="pausable">O motor respeita <see cref="OperationItem.PauseGate"/> (pausa cooperativa num ponto seguro).</param>
    public OperationItem Enqueue(string title, OperationKind kind, Func<OperationItem, CancellationToken, Task<OperationResult>> work, bool pausable = false)
    {
        var item = new OperationItem(_nextId++, title, kind, work) { PauseGate = pausable ? new PauseGate() : null };
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
        item.Cts.Cancel(); // uma operação pausada sai da espera pelo cancelamento; não precisa continuar antes
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

    /// <summary>Refaz só os itens com falha ou não processados, como uma nova operação.</summary>
    public bool RetryFailed(OperationItem item)
    {
        if (!item.CanRetryFailed) return false;
        item.RetryFailedAction!();
        return true;
    }

    /// <summary>
    /// Pausa no próximo ponto seguro do motor (entre itens ou blocos). A fila é serial: as seguintes esperam a
    /// operação pausada continuar ou ser cancelada.
    /// </summary>
    public bool Pause(OperationItem item)
    {
        if (!item.CanPause || !item.TryTransition(OperationState.Paused)) return false;
        item.PauseGate!.Pause();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Continua do item atual (sem refazer o que já terminou).</summary>
    public bool Resume(OperationItem item)
    {
        if (!item.CanResume || !item.TryTransition(OperationState.Running)) return false;
        item.PauseGate!.Resume();
        Changed?.Invoke();
        return true;
    }

    internal void SetWaiting(OperationItem item, bool waiting)
    {
        // Um conflito perguntado pouco antes da pausa não tira a operação do estado "pausada".
        if (!waiting && item.State != OperationState.WaitingForUser) return;
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
                next.StartedAt = DateTimeOffset.Now;
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
                    var error = UserErrors.Describe(ex, next.Title);
                    result = new OperationResult(OperationState.Failed, [], UserErrors.OperationKind(error.Kind), error.Text);
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
