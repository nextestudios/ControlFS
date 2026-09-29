using System.Diagnostics;

namespace ControlFS.Application.Operations;

/// <summary>
/// <see cref="IProgress{T}"/> que entrega no máximo uma atualização por intervalo, sempre a mais recente (as
/// intermediárias são descartadas). Um motor que relata a cada bloco de 80 KB encheria a fila da thread de UI e cada
/// entrega redesenharia a tela: a interface travaria. O último valor nunca se perde: se chegou dentro do intervalo, é
/// entregue quando o intervalo acaba.
/// </summary>
internal sealed class CoalescingProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;
    private readonly TimeSpan _interval;
    private readonly SynchronizationContext? _context;
    private readonly object _gate = new();
    private T? _latest;
    private bool _scheduled;
    private long _lastDelivery = Stopwatch.GetTimestamp() - Stopwatch.Frequency * 60;

    public CoalescingProgress(Action<T> handler, TimeSpan interval)
    {
        _handler = handler;
        _interval = interval;
        _context = SynchronizationContext.Current;
    }

    public void Report(T value)
    {
        TimeSpan delay;
        lock (_gate)
        {
            _latest = value;
            if (_scheduled) return;
            _scheduled = true;
            delay = _interval - Stopwatch.GetElapsedTime(_lastDelivery);
            if (delay > TimeSpan.Zero)
            {
                Timer? timer = null;
                timer = new Timer(_ =>
                {
                    timer?.Dispose();
                    Post();
                }, null, delay, Timeout.InfiniteTimeSpan);
            }
        }
        if (delay <= TimeSpan.Zero) Post();
    }

    private void Post()
    {
        if (_context is null) Deliver();
        else _context.Post(_ => Deliver(), null);
    }

    private void Deliver()
    {
        T value;
        lock (_gate)
        {
            value = _latest!;
            _scheduled = false;
            _lastDelivery = Stopwatch.GetTimestamp();
        }
        _handler(value);
    }
}
