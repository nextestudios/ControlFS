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
    public void Each_tab_keeps_its_own_location_focus_and_selection_when_switching_with_the_tab_strip() => UiContext.Run(async () =>
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

        // RB entra na faixa de abas; LB volta para a aba 1, que continua na pasta de teste com o foco em "Músicas"
        d.Press(InputAction.NextRegion);
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

        // Norte na faixa: fechar a aba 2 volta para a aba 1
        d.Press(InputAction.NextRegion);
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Fechar aba");
        Assert.Single(app.Tabs);
        Assert.Equal(0, app.ActiveTab);
        Assert.Equal(_tmp.Path, ((PhysicalLocation)app.Browser.Location!).FullPath);
    });
}
