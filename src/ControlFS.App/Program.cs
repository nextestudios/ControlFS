using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace ControlFS.App;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        AppLog.Session();
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) AppLog.Crash(ex, "AppDomain.UnhandledException");
        };
        try
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
        catch (Exception ex)
        {
            AppLog.Crash(ex, "Program.Main");
            throw;
        }
    }
}
