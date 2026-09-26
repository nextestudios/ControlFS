using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace ControlFS.App;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(_callbackParams =>
        {
            // Continuações assíncronas e o AppController rodam na thread de UI.
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
    }
}
