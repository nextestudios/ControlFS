using ControlFS.App.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.XamlTypeInfo;

namespace ControlFS.App;

/// <summary>
/// Aplicação sem XAML compilado: implementa IXamlMetadataProvider delegando ao provedor dos
/// controles WinUI, necessário para carregar os estilos padrão (XamlControlsResources).
/// </summary>
public sealed class App : Microsoft.UI.Xaml.Application, IXamlMetadataProvider
{
    private readonly XamlControlsXamlMetaDataProvider _provider = new();
    private MainWindow? _window;

    public App()
    {
        UnhandledException += (_, e) => AppLog.Crash(e.Exception, "Application.UnhandledException: " + e.Message);
        AppLog.Info("App criado");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            AppLog.Info("OnLaunched: carregando XamlControlsResources");
            Resources.MergedDictionaries.Add(new XamlControlsResources());
            AppLog.Info("OnLaunched: criando janela");
            _window = new MainWindow();
            AppLog.Info("OnLaunched: ativando janela");
            _window.Activate();
            AppLog.Info("Janela ativada");
        }
        catch (Exception ex)
        {
            AppLog.Crash(ex, "App.OnLaunched");
            throw;
        }
    }

    public IXamlType GetXamlType(Type type) => _provider.GetXamlType(type);

    public IXamlType GetXamlType(string fullName) => _provider.GetXamlType(fullName);

    public XmlnsDefinition[] GetXmlnsDefinitions() => _provider.GetXmlnsDefinitions();
}
