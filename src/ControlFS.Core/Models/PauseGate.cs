namespace ControlFS.Core.Models;

/// <summary>
/// Pausa cooperativa (#21): o motor chama <see cref="WaitIfPausedAsync"/> entre itens e entre blocos de dados; enquanto
/// pausado, fica parado ali sem tocar no disco. Cancelar continua funcionando durante a pausa. Só existe para motores
/// que sabem parar num ponto seguro; os outros não recebem um e a opção de pausar nem aparece.
/// </summary>
public sealed class PauseGate
{
    private readonly object _lock = new();
    private TaskCompletionSource? _resumed;

    public bool IsPaused
    {
        get
        {
            lock (_lock) return _resumed is not null;
        }
    }

    public void Pause()
    {
        lock (_lock) _resumed ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void Resume()
    {
        TaskCompletionSource? resumed;
        lock (_lock)
        {
            resumed = _resumed;
            _resumed = null;
        }
        resumed?.TrySetResult();
    }

    public Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        Task? wait;
        lock (_lock) wait = _resumed?.Task;
        return wait is null ? Task.CompletedTask : wait.WaitAsync(cancellationToken);
    }
}
