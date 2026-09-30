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
    public void A_relaunch_after_an_update_waits_for_the_old_instance_to_leave_instead_of_signalling_it()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Requer Windows.");
        if (Mutex.TryOpenExisting(SingleInstanceCoordinator.InstanceMutexName, out var existing))
        {
            existing.Dispose();
            Assert.Skip("Outra instância real do ControlFS está rodando no momento.");
        }

        var held = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        var old = new Thread(() =>
        {
            using var mutex = new Mutex(true, SingleInstanceCoordinator.InstanceMutexName);
            held.Set();
            release.Wait();
            mutex.ReleaseMutex();
        });
        old.Start();
        Assert.True(held.Wait(TimeSpan.FromSeconds(3)));
        using var closeSignal = new EventWaitHandle(false, EventResetMode.AutoReset, SingleInstanceCoordinator.CloseSignalName);
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, SingleInstanceCoordinator.ShowSignalName);
        var previous = SingleInstanceCoordinator.RelaunchWait;
        SingleInstanceCoordinator.RelaunchWait = TimeSpan.FromSeconds(10);
        try
        {
            _ = Task.Run(async () => { await Task.Delay(400); release.Set(); });
            var exit = SingleInstanceCoordinator.HandleLaunch(["ControlFS.exe", "--relaunch"], out var mutex);
            try
            {
                Assert.False(exit); // virou a primeira instância e segue abrindo
                Assert.NotNull(mutex);
                Assert.False(showSignal.WaitOne(0)); // a instância antiga não foi sinalizada
            }
            finally { mutex?.Dispose(); }
        }
        finally
        {
            SingleInstanceCoordinator.RelaunchWait = previous;
            release.Set();
            old.Join();
        }
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
