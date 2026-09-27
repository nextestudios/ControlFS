using ControlFS.Application;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Windows.Settings;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

public class DensityJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();
    private readonly TempDir _data = new();

    public void Dispose()
    {
        _tmp.Dispose();
        _data.Dispose();
    }

    [Fact]
    public void List_density_is_chosen_in_the_menu_and_persists_across_launches() => UiContext.Run(async () =>
    {
        var store = new JsonSettingsStore(_data.Path);
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), store);
        app.Start();
        var d = new Driver(app);
        Assert.Equal(ListDensity.Comfortable, app.Settings.Density);

        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Densidade da lista: confortável");
        Assert.Equal(ListDensity.Compact, app.Settings.Density);

        // Novo "lançamento" com o mesmo armazenamento
        var relaunched = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), store);
        relaunched.Start();
        Assert.Equal(ListDensity.Compact, relaunched.Settings.Density);

        // A ação reservada "trocar visualização" alterna a mesma preferência
        relaunched.Handle(InputAction.ChangeView);
        Assert.Equal(ListDensity.Comfortable, store.Load().Settings.Density);
    });
}
