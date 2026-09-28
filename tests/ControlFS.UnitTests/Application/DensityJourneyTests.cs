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
    public void First_run_on_a_handheld_starts_compact_but_never_overrides_a_saved_choice() => UiContext.Run(async () =>
    {
        // Auditoria de UX (P2-7): em 720p/800p a lista confortável mostrava ~5 linhas.
        var store = new JsonSettingsStore(_data.Path);
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), store);
        app.Start();
        app.ApplyFirstRunDensity(handheld: true);
        Assert.Equal(ListDensity.Compact, app.Settings.Density);

        // A pessoa volta para a confortável: nenhuma abertura seguinte no portátil muda isso.
        var d = new Driver(app);
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Densidade da lista: compacta");
        Assert.Equal(ListDensity.Comfortable, app.Settings.Density);
        var relaunched = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), store);
        relaunched.Start();
        relaunched.ApplyFirstRunDensity(handheld: true);
        Assert.Equal(ListDensity.Comfortable, relaunched.Settings.Density);

        // Primeira abertura fora dos portáteis: o padrão continua o de sempre.
        using var other = new TempDir();
        var desktop = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), new JsonSettingsStore(other.Path));
        desktop.Start();
        desktop.ApplyFirstRunDensity(handheld: false);
        desktop.ApplyFirstRunDensity(handheld: true); // só a primeira faixa conhecida vale
        Assert.Equal(ListDensity.Comfortable, desktop.Settings.Density);
    });

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

        // "Trocar visualização" (Ctrl+G / menu) alterna lista e grade; a densidade continua valendo para as duas
        relaunched.Handle(InputAction.ChangeView);
        Assert.Equal(ViewMode.Grid, store.Load().Settings.View);
        Assert.Equal(ListDensity.Compact, store.Load().Settings.Density);
    });
}
