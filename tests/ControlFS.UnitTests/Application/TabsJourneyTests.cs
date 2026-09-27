using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Windows.Settings;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

public class TabsJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();
    private readonly TempDir _data = new();

    public void Dispose()
    {
        _tmp.Dispose();
        _data.Dispose();
    }

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

    [Fact]
    public void New_tab_from_the_main_menu_and_L2_R2_switch_tabs_while_L1_R1_stay_on_the_top_bar() => UiContext.Run(async () =>
    {
        for (var i = 0; i < 40; i++) File.WriteAllText(_tmp.Sub($"f{i:00}.txt"), "x");
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService());
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        await d.Idle();

        // Uma aba só: os gatilhos continuam paginando.
        d.Press(InputAction.PageDown);
        Assert.True(app.Browser.List.FocusIndex > 1);
        await d.FocusItem("f05.txt");

        // Menu → Nova aba abre a pasta atual numa aba nova, já ativa.
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Nova aba");
        await d.Idle();
        Assert.Equal((2, 1), (app.Tabs.Count, app.ActiveTab));
        await d.FocusItem("f30.txt");
        Assert.Contains(app.Hints, h => h.Action == InputAction.PageDown && h.Label == "Próxima aba");

        // L2 volta à aba 1 com o foco dela; R2 dá a volta e L2 de novo também.
        d.Press(InputAction.PageUp);
        Assert.Equal(0, app.ActiveTab);
        Assert.Equal("f05.txt", app.Browser.List.Focused?.Name);
        d.Press(InputAction.PageUp);
        Assert.Equal(1, app.ActiveTab);
        Assert.Equal("f30.txt", app.Browser.List.Focused?.Name);
        d.Press(InputAction.PageDown);
        Assert.Equal(0, app.ActiveTab);

        // L1/R1 levam à barra superior e nunca trocam de aba.
        d.Press(InputAction.NextRegion);
        Assert.Equal(0, app.ActiveTab);
        Assert.NotEqual(PaneRegion.List, app.FocusRegion);
        d.Press(InputAction.NavigateDown);

        // Com um menu aberto os gatilhos são dele: a aba não muda.
        d.Press(InputAction.OpenAppMenu);
        d.Press(InputAction.PageDown);
        Assert.Equal(0, app.ActiveTab);
    });

    [Fact]
    public void Open_tabs_are_restored_on_the_next_launch_a_missing_folder_shows_home_and_it_can_be_disabled() => UiContext.Run(async () =>
    {
        var music = _tmp.MakeDir("Músicas");
        var games = _tmp.MakeDir("Jogos");
        var store = new JsonSettingsStore(_data.Path);
        Driver Boot()
        {
            var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), store);
            app.Start();
            return new Driver(app);
        }

        // Sessão 1: três abas (pasta de teste, Jogos, Músicas), a ativa em Músicas.
        var d = Boot();
        d.Press(InputAction.Confirm);
        await d.FocusItem("Músicas");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Abrir em nova aba");
        await d.Idle();
        d.Press(InputAction.PageUp);
        await d.FocusItem("Jogos");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Abrir em nova aba");
        await d.Idle();
        d.Press(InputAction.PageDown);
        Assert.Equal(music, ((PhysicalLocation)d.App.Browser.Location!).FullPath);
        Directory.Delete(games);

        // Sessão 2: as abas voltam na mesma ordem e na aba ativa; a pasta que sumiu vira aba indisponível (sem travar).
        d = Boot();
        await d.Idle();
        Assert.Equal(3, d.App.Tabs.Count);
        Assert.Equal((2, Screen.Browser), (d.App.ActiveTab, d.App.Screen));
        Assert.Equal(music, ((PhysicalLocation)d.App.Browser.Location!).FullPath);
        Assert.Equal(_tmp.Path, ((PhysicalLocation)d.App.Tabs[0].Location!).FullPath);
        Assert.True(d.App.Tabs[1].IsUnavailable);
        Assert.Equal("Jogos (indisponível)", AppController.TabTitle(d.App.Tabs[1]));
        Assert.Contains("indisponível", d.App.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(d.App.Modals, m => m is DialogModal);
        d.Press(InputAction.PageUp);
        Assert.Equal((1, Screen.Home), (d.App.ActiveTab, d.App.Screen));

        // Desligado: a próxima abertura começa no início com uma aba só.
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Restaurar abas ao abrir: sim");
        Assert.Empty(store.Load().Settings.OpenTabs);
        d = Boot();
        await d.Idle();
        Assert.Single(d.App.Tabs);
        Assert.Equal(Screen.Home, d.App.Screen);
    });

    [Fact]
    public void A_closed_tab_reopens_at_its_place_with_its_location_and_history() => UiContext.Run(async () =>
    {
        var music = _tmp.MakeDir("Músicas");
        var rock = _tmp.MakeDir("Músicas", "Rock");
        File.WriteAllText(Path.Join(rock, "a.mp3"), "a");
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService());
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        await d.FocusItem("Músicas");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Abrir em nova aba");
        await d.FocusItem("Rock");
        d.Press(InputAction.Confirm);
        await d.Idle();
        d.Press(InputAction.ToggleSelection);

        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Abas");
        await d.ChooseMenu("Fechar aba");
        Assert.Single(app.Tabs);

        // Menu → Reabrir aba fechada: volta na posição 2, ativa, em Rock, e Voltar ainda leva a Músicas.
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Reabrir aba fechada");
        await d.Idle();
        Assert.Equal((2, 1), (app.Tabs.Count, app.ActiveTab));
        Assert.Equal(rock, ((PhysicalLocation)app.Browser.Location!).FullPath);
        Assert.Equal(0, app.Browser.List.SelectionCount);
        d.Press(InputAction.Back);
        await d.Idle();
        Assert.Equal(music, ((PhysicalLocation)app.Browser.Location!).FullPath);

        // A pilha esvaziou: a opção fica indisponível e diz o motivo.
        d.Press(InputAction.OpenAppMenu);
        var menu = await d.WaitMenu();
        Assert.False(menu.Items.Single(i => i.Label == "Reabrir aba fechada").IsEnabled);
    });

    [Fact]
    public void A_duplicated_tab_copies_location_history_and_focus_but_not_the_marks() => UiContext.Run(async () =>
    {
        var music = _tmp.MakeDir("Músicas");
        var rock = _tmp.MakeDir("Músicas", "Rock");
        File.WriteAllText(Path.Join(rock, "a.mp3"), "a");
        File.WriteAllText(Path.Join(rock, "b.mp3"), "b");
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService());
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        await d.FocusItem("Músicas");
        d.Press(InputAction.Confirm);
        await d.FocusItem("Rock");
        d.Press(InputAction.Confirm);
        await d.FocusItem("a.mp3");
        d.Press(InputAction.ToggleSelection);
        await d.FocusItem("b.mp3");

        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Abas");
        await d.ChooseMenu("Duplicar aba");
        await d.Idle();
        Assert.Equal((2, 1), (app.Tabs.Count, app.ActiveTab));
        Assert.Equal(rock, ((PhysicalLocation)app.Browser.Location!).FullPath);
        Assert.Equal("b.mp3", app.Browser.List.Focused?.Name);
        Assert.Equal(0, app.Browser.List.SelectionCount);

        // Histórico copiado mas independente: Voltar na cópia vai a Músicas; a aba original continua em Rock, marcada.
        d.Press(InputAction.Back);
        await d.Idle();
        Assert.Equal(music, ((PhysicalLocation)app.Browser.Location!).FullPath);
        Assert.Equal(rock, ((PhysicalLocation)app.Tabs[0].Location!).FullPath);
        Assert.Equal(["a.mp3"], app.Tabs[0].List.SelectedEntries.Select(e => e.Name));
        Assert.True(app.Tabs[0].CanGoBack);
    });
}
