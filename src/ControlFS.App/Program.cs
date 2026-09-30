using ControlFS.App.Diagnostics;
using ControlFS.Infrastructure.Windows.Automation;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace ControlFS.App;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        AppLog.Session();
        // Erros mostrados ao usuário em linguagem simples: a exceção original fica no log local.
        Core.Policies.UserErrors.Log = AppLog.Error;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) AppLog.Crash(ex, "AppDomain.UnhandledException");
        };

        var args = Environment.GetCommandLineArgs();
        if (SingleInstanceCoordinator.HandleLaunch(args, out var instanceMutex, bypass: ScreenRenderer.OutputDirectory(args) is not null, log: AppLog.Info))
        {
            return;
        }

        try
        {
            using (instanceMutex)
            {
                WinRT.ComWrappersSupport.InitializeComWrappers();
                Microsoft.UI.Xaml.Application.Start(_callbackParams =>
                {
                    // Continuações assíncronas e o AppController rodam na thread de UI.
                    SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                    _ = new App();
                });
                AppLog.Info("Encerrado normalmente");
            }
        }
        catch (Exception ex)
        {
            AppLog.Crash(ex, "Program.Main");
            throw;
        }
    }
}
