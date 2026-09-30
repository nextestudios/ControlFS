using System.Runtime.Versioning;

namespace ControlFS.Infrastructure.Windows.Automation;

/// <summary>
/// Escuta os sinais emitidos por novas invocações do aplicativo (via protocolo ou linha de comando)
/// e despacha as ações configuradas.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SingleInstanceListener : IDisposable
{
    private readonly EventWaitHandle? _showSignal;
    private readonly EventWaitHandle? _closeSignal;
    private readonly RegisteredWaitHandle? _showWait;
    private readonly RegisteredWaitHandle? _closeWait;

    public SingleInstanceListener(Action onShow, Action onClose)
    {
        if (!OperatingSystem.IsWindows()) return;

        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, SingleInstanceCoordinator.ShowSignalName);
        _closeSignal = new EventWaitHandle(false, EventResetMode.AutoReset, SingleInstanceCoordinator.CloseSignalName);

        _showWait = ThreadPool.RegisterWaitForSingleObject(_showSignal, (_, _) => onShow(), null, Timeout.Infinite, executeOnlyOnce: false);
        _closeWait = ThreadPool.RegisterWaitForSingleObject(_closeSignal, (_, _) => onClose(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _showWait?.Unregister(null);
        _closeWait?.Unregister(null);
        _showSignal?.Dispose();
        _closeSignal?.Dispose();
    }
}
