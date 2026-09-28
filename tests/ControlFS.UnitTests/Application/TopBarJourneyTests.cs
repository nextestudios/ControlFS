using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

/// <summary>Barra superior do redesenho: caminho com raiz real e acesso rápido, só com ações semânticas.</summary>
public class TopBarJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public void Lb_reaches_quick_access_from_home_and_from_a_folder_where_it_keeps_history_and_the_root_goes_home() => UiContext.Run(async () =>
    {
        var sub = _tmp.MakeDir("a");
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService());
        app.Start();
        var d = new Driver(app);

        // Início: "Locais › Início"; LB vai para o primeiro atalho e as setas andam pela barra.
        Assert.Equal(["Locais", "Início"], app.Breadcrumbs.Select(c => c.Label));
        Assert.Equal(["Favoritos", "Recentes", "Pasta de teste", "Meu computador"], app.QuickAccess.Select(q => q.Label));
        d.Press(InputAction.PreviousRegion);
        Assert.Equal(PaneRegion.QuickAccess, app.FocusRegion);
        Assert.Equal("Acesso rápido", app.DescribeFocus().Context);

        // Favoritos sem nenhum: o menu diz como adicionar, sem ação falsa.
        d.Press(InputAction.Confirm);
        var favorites = await d.WaitMenu();
        Assert.False(Assert.Single(favorites.Items).IsEnabled);
        d.Press(InputAction.Back);
        Assert.Equal(PaneRegion.QuickAccess, app.FocusRegion); // fechar o menu devolve à barra

        d.Press(InputAction.NavigateRight);
        d.Press(InputAction.NavigateRight);
        Assert.Equal("Pasta de teste", app.QuickAccess[app.QuickAccessFocus].Label);
        d.Press(InputAction.Confirm);
        await d.Idle();
        Assert.Equal(Screen.Browser, app.Screen);
        Assert.Equal(_tmp.Path, ((PhysicalLocation)app.Browser.Location!).FullPath);
        Assert.Equal(PaneRegion.List, app.FocusRegion);
        Assert.True(app.IsQuickAccessActive(app.QuickAccess[2]));

        // Numa subpasta: raiz "Meu computador" + caminho real; LB foca a pasta de cima e a direita passa ao acesso rápido.
        await d.FocusItem("a");
        d.Press(InputAction.Confirm);
        await d.Idle();
        Assert.Equal("Meu computador", app.Breadcrumbs[0].Label);
        Assert.Equal("a", app.Breadcrumbs[^1].Label);
        d.Press(InputAction.PreviousRegion);
        Assert.Equal(Path.GetFileName(_tmp.Path), app.Breadcrumbs[app.BreadcrumbFocus].Label);
        d.Press(InputAction.NavigateRight); // #176: "a" (atual) é só o rótulo; direto ao primeiro atalho
        Assert.Equal(PaneRegion.QuickAccess, app.FocusRegion);
        d.Press(InputAction.NavigateLeft);
        Assert.Equal((PaneRegion.Breadcrumbs, Path.GetFileName(_tmp.Path)), (app.FocusRegion, app.Breadcrumbs[app.BreadcrumbFocus].Label));
        d.Press(InputAction.NavigateRight);
        d.Press(InputAction.NavigateRight);
        d.Press(InputAction.NavigateRight);
        d.Press(InputAction.Confirm); // "Pasta de teste" na mesma aba, com histórico
        await d.Idle();
        Assert.Equal(_tmp.Path, ((PhysicalLocation)app.Browser.Location!).FullPath);
        d.Press(InputAction.Back);
        await d.Idle();
        Assert.Equal(sub, ((PhysicalLocation)app.Browser.Location!).FullPath);

        // A raiz "Meu computador" abre as unidades na mesma aba (fase B; antes levava ao início); a raiz "Locais" de lá
        // leva ao início com o foco de volta nos locais.
        d.Press(InputAction.PreviousRegion);
        d.Press(InputAction.PageUp);
        Assert.Equal(BreadcrumbKind.Root, app.Breadcrumbs[app.BreadcrumbFocus].Kind);
        d.Press(InputAction.Confirm);
        await d.Idle();
        Assert.IsType<ThisPcLocation>(app.Browser.Location);
        Assert.Equal(["Locais", "Meu computador"], app.Breadcrumbs.Select(c => c.Label));
        d.Press(InputAction.PreviousRegion);
        d.Press(InputAction.PageUp);
        d.Press(InputAction.Confirm);
        Assert.Equal(Screen.Home, app.Screen);
        Assert.Equal(PaneRegion.List, app.FocusRegion);
    });

    [Fact]
    public void L1_and_R1_walk_the_top_bar_skipping_the_current_folder_and_restore_focus_after_navigating_in_grid_and_list() => UiContext.Run(async () =>
    {
        var sub = _tmp.MakeDir("a", "b");
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService());
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm); // pasta de teste
        await d.FocusItem("a");
        d.Press(InputAction.Confirm);
        await d.FocusItem("b");
        d.Press(InputAction.Confirm);
        await d.Idle();
        Assert.Equal(sub, ((PhysicalLocation)app.Browser.Location!).FullPath);
        string Focused() => app.FocusRegion == PaneRegion.QuickAccess ? app.QuickAccess[app.QuickAccessFocus].Label : app.Breadcrumbs[app.BreadcrumbFocus].Label;

        // Do conteúdo: L1 foca a pasta de cima e R1 o primeiro atalho; a pasta atual ("b") fica entre eles e é pulada.
        d.Press(InputAction.PreviousRegion);
        Assert.Equal("a", Focused());
        Assert.Equal("Ir para", app.Hints.Single(h => h.Action == InputAction.Confirm).Label);
        d.Press(InputAction.NextRegion);
        Assert.Equal((PaneRegion.QuickAccess, "Favoritos"), (app.FocusRegion, Focused()));
        d.Press(InputAction.PreviousRegion);
        Assert.Equal("a", Focused());
        d.Press(InputAction.Back);
        d.Press(InputAction.NextRegion);
        Assert.Equal("Favoritos", Focused());
        d.Press(InputAction.Back);

        // Nas pontas, L1/R1 param (não saem da barra nem caem na pasta atual).
        d.Press(InputAction.PreviousRegion);
        d.Press(InputAction.PageUp);
        Assert.Equal(BreadcrumbKind.Root, app.Breadcrumbs[app.BreadcrumbFocus].Kind);
        d.Press(InputAction.PreviousRegion);
        Assert.Equal((PaneRegion.Breadcrumbs, 0), (app.FocusRegion, app.BreadcrumbFocus));
        var walked = new List<string>();
        for (var i = 0; i < 20; i++)
        {
            d.Press(InputAction.NextRegion);
            walked.Add(Focused());
        }
        Assert.Equal((PaneRegion.QuickAccess, app.QuickAccess.Count - 1), (app.FocusRegion, app.QuickAccessFocus));
        Assert.DoesNotContain("b", walked);

        // A pasta atual não recarrega nem por toque: nada muda.
        d.Press(InputAction.Back);
        var location = app.Browser.Location;
        app.PointerActivateBreadcrumb(app.Breadcrumbs.Count - 1);
        await d.Idle();
        Assert.Same(location, app.Browser.Location);
        Assert.Equal(PaneRegion.List, app.FocusRegion);

        // Na grade: um segmento de cima navega e o foco volta à lista no filho de onde viemos.
        d.Press(InputAction.ChangeView);
        Assert.True(app.IsGrid);
        d.Press(InputAction.PreviousRegion);
        d.Press(InputAction.NavigateLeft); // "a" -> pasta de teste
        d.Press(InputAction.Confirm);
        await d.Idle();
        Assert.Equal((_tmp.Path, "a", PaneRegion.List), (((PhysicalLocation)app.Browser.Location!).FullPath, app.Browser.List.Focused?.Name, app.FocusRegion));

        // Na lista: o atalho da pasta atual ("Pasta de teste") não é alvo; outro atalho abre e o foco volta à lista.
        d.Press(InputAction.ChangeView);
        Assert.False(app.IsGrid);
        var here = app.QuickAccess.ToList().FindIndex(q => q.Label == "Pasta de teste");
        Assert.True(app.IsQuickAccessActive(app.QuickAccess[here]));
        d.Press(InputAction.NextRegion);
        while (app.QuickAccessFocus < app.QuickAccess.Count - 1)
        {
            Assert.NotEqual(here, app.QuickAccessFocus);
            d.Press(InputAction.NextRegion);
        }
        var before = app.Browser.Location;
        app.PointerActivateQuickAccess(here);
        await d.Idle();
        Assert.Same(before, app.Browser.Location);
        Assert.Equal("Meu computador", Focused());
        d.Press(InputAction.Confirm);
        await d.Idle();
        Assert.IsType<ThisPcLocation>(app.Browser.Location);
        Assert.Equal(PaneRegion.List, app.FocusRegion);
        d.Press(InputAction.Back); // histórico: volta à pasta de teste
        await d.Idle();
        Assert.Equal(_tmp.Path, ((PhysicalLocation)app.Browser.Location!).FullPath);
    });
}
