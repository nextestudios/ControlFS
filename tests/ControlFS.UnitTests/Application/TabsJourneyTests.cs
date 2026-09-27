using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

public class TabsJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public void Each_tab_keeps_its_own_location_focus_and_selection_and_the_strip_is_reached_above_the_top_bar() => UiContext.Run(async () =>
    {
        var music = _tmp.MakeDir("Músicas");
        _tmp.MakeDir("Jogos");
        File.WriteAllText(Path.Join(music, "a.mp3"), "a");
        File.WriteAllText(Path.Join(music, "b.mp3"), "b");
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService());
        app.Start();
        var d = new Driver(app);

        // Aba 1: pasta de teste com o foco em "Jogos"; "Músicas" abre numa aba nova
        d.Press(InputAction.Confirm);
        await d.FocusItem("Músicas");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Abrir em nova aba");
        await d.Idle();
        Assert.Equal(2, app.Tabs.Count);
        Assert.Equal(1, app.ActiveTab);
        Assert.Equal(music, ((PhysicalLocation)app.Browser.Location!).FullPath);

        // Aba 2: marcar "b.mp3"
        await d.FocusItem("b.mp3");
        d.Press(InputAction.ToggleSelection);

        // #176: R1 leva à barra superior e Cima nela entra na faixa de abas (antes, RB ia direto para a faixa);
        // L1 na faixa volta para a aba 1, que continua na pasta de teste com o foco em "Músicas"
        d.Press(InputAction.NextRegion);
        Assert.Equal(PaneRegion.QuickAccess, app.Browser.Region);
        d.Press(InputAction.NavigateUp);
        Assert.Equal(PaneRegion.Tabs, app.Browser.Region);
        d.Press(InputAction.PreviousRegion);
        Assert.Equal(0, app.ActiveTab);
        Assert.Equal(_tmp.Path, ((PhysicalLocation)app.Browser.Location!).FullPath);
        Assert.Equal("Músicas", app.Browser.List.Focused?.Name);
        Assert.Equal(0, app.Browser.List.SelectionCount);

        // RB volta à aba 2: a marcação e o foco continuam lá
        d.Press(InputAction.NextRegion);
        Assert.Equal(1, app.ActiveTab);
        Assert.Equal(["b.mp3"], app.Browser.List.SelectedEntries.Select(e => e.Name));
        Assert.Equal("b.mp3", app.Browser.List.Focused?.Name);
        d.Press(InputAction.NavigateDown);
        Assert.Equal(PaneRegion.List, app.Browser.Region);

        // Menu → Abas: trocar de aba sem a faixa
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Abas (2 de 2)");
        await d.ChooseMenu("Aba 1:");
        Assert.Equal((0, PaneRegion.List), (app.ActiveTab, app.Browser.Region));
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Abas");
        await d.ChooseMenu("Aba 2:");
        Assert.Equal(1, app.ActiveTab);

        // Norte na faixa: fechar a aba 2 volta para a aba 1
        d.Press(InputAction.PreviousRegion);
        d.Press(InputAction.NavigateUp);
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Fechar aba");
        Assert.Single(app.Tabs);
        Assert.Equal(0, app.ActiveTab);
        Assert.Equal(_tmp.Path, ((PhysicalLocation)app.Browser.Location!).FullPath);
        Assert.Equal(PaneRegion.List, app.Browser.Region);

        // Com uma aba só a faixa some (repetiria o caminho): Cima na barra não leva a ela
        d.Press(InputAction.NextRegion);
        d.Press(InputAction.NavigateUp);
        Assert.Equal(PaneRegion.QuickAccess, app.Browser.Region);
    });
}
