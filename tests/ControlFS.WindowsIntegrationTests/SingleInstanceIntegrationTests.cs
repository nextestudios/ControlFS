using ControlFS.Infrastructure.Windows.Automation;
using Xunit;

namespace ControlFS.WindowsIntegrationTests;

public class SingleInstanceIntegrationTests
{
    [Fact]
    public void HandleLaunch_with_bypass_returns_false_without_mutex()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Requer Windows.");

        var shouldExit = SingleInstanceCoordinator.HandleLaunch(["ControlFS.exe"], out var mutex, bypass: true);
        Assert.False(shouldExit);
        Assert.Null(mutex);
    }

    [Fact]
    public void HandleLaunch_first_instance_with_stop_returns_true_without_starting()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Requer Windows.");

        // Se nenhuma instância estiver aberta e alguém passar --stop ou controlfs://stop, não abre o app.
        // Simulamos usando bypass falso apenas se o mutex não estiver já ocupado por outro processo de verdade.
        if (Mutex.TryOpenExisting(SingleInstanceCoordinator.InstanceMutexName, out var existing))
        {
            existing.Dispose();
            Assert.Skip("Outra instância real do ControlFS está rodando no momento.");
        }

        var shouldExit = SingleInstanceCoordinator.HandleLaunch(["ControlFS.exe", "controlfs://stop"], out var mutex);
        Assert.True(shouldExit);
        Assert.Null(mutex);
    }

    [Fact]
    public void Second_instance_signals_show_and_close_to_listener()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Requer Windows.");

        if (Mutex.TryOpenExisting(SingleInstanceCoordinator.InstanceMutexName, out var existing))
        {
            existing.Dispose();
            Assert.Skip("Outra instância real do ControlFS está rodando no momento.");
        }

        // Criamos o mutex da primeira instância para simular a instância ativa
        using var firstInstanceMutex = new Mutex(true, SingleInstanceCoordinator.InstanceMutexName);

        var showTriggered = new ManualResetEventSlim(false);
        var closeTriggered = new ManualResetEventSlim(false);

        using var listener = new SingleInstanceListener(
            onShow: () => showTriggered.Set(),
            onClose: () => closeTriggered.Set());

        // Segunda instância envia sinal Show
        var exitForShow = SingleInstanceCoordinator.HandleLaunch(["ControlFS.exe", "controlfs://start"], out var showMutex);
        Assert.True(exitForShow);
        Assert.Null(showMutex);
        Assert.True(showTriggered.Wait(TimeSpan.FromSeconds(3)), "O sinal Show deveria ter sido recebido pelo listener.");

        // Segunda instância envia sinal Close
        var exitForClose = SingleInstanceCoordinator.HandleLaunch(["ControlFS.exe", "--stop"], out var closeMutex);
        Assert.True(exitForClose);
        Assert.Null(closeMutex);
        Assert.True(closeTriggered.Wait(TimeSpan.FromSeconds(3)), "O sinal Close deveria ter sido recebido pelo listener.");
    }
}
