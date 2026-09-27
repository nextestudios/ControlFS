using System.Collections.Concurrent;

namespace ControlFS.UnitTests.Support;

/// <summary>
/// SynchronizationContext de thread única que simula a thread de UI: continuações e Post
/// executam em ordem nesta thread, como no DispatcherQueue do WinUI.
/// </summary>
public sealed class UiContext : SynchronizationContext
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];

    /// <summary>
    /// Continuações que chegam depois do fim do teste (ex.: progresso de uma operação em segundo plano) são ignoradas:
    /// a "UI" já não existe, como ao fechar a janela do app.
    /// </summary>
    public override void Post(SendOrPostCallback d, object? state)
    {
        if (_queue.IsAddingCompleted) return;
        try { _queue.Add((d, state)); }
        catch (InvalidOperationException) { /* fila fechada entre a checagem e o Add */ }
    }

    public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();

    public static void Run(Func<Task> body, int timeoutSeconds = 60)
    {
        var previous = Current;
        var context = new UiContext();
        SetSynchronizationContext(context);
        try
        {
            var task = body();
            task.ContinueWith(_ => context._queue.CompleteAdding(), TaskScheduler.Default);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            foreach (var (callback, state) in context._queue.GetConsumingEnumerable(timeout.Token)) callback(state);
            task.GetAwaiter().GetResult();
        }
        finally
        {
            SetSynchronizationContext(previous);
        }
    }

    /// <summary>Aguarda uma condição cedendo à fila da "UI".</summary>
    public static async Task WaitUntil(Func<bool> condition, string what, int timeoutMs = 20_000)
    {
        var start = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - start > timeoutMs) throw new TimeoutException($"Tempo esgotado aguardando: {what}");
            await Task.Delay(10);
        }
    }
}
