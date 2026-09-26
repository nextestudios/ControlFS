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

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Resources.MergedDictionaries.Add(new XamlControlsResources());
        _window = new MainWindow();
        _window.Activate();
    }

    public IXamlType GetXamlType(Type type) => _provider.GetXamlType(type);

    public IXamlType GetXamlType(string fullName) => _provider.GetXamlType(fullName);

    public XmlnsDefinition[] GetXmlnsDefinitions() => _provider.GetXmlnsDefinitions();
}
