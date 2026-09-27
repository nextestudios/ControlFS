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
        Assert.Equal(["Favoritos", "Arquivos recentes", "Pasta de teste", "Meu computador"], app.QuickAccess.Select(q => q.Label));
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
        d.Press(InputAction.NavigateRight); // "a" (atual)
        d.Press(InputAction.NavigateRight); // primeiro atalho
        Assert.Equal(PaneRegion.QuickAccess, app.FocusRegion);
        d.Press(InputAction.NavigateLeft);
        Assert.Equal((PaneRegion.Breadcrumbs, "a"), (app.FocusRegion, app.Breadcrumbs[app.BreadcrumbFocus].Label));
        d.Press(InputAction.NavigateRight);
        d.Press(InputAction.NavigateRight);
        d.Press(InputAction.NavigateRight);
        d.Press(InputAction.Confirm); // "Pasta de teste" na mesma aba, com histórico
        await d.Idle();
        Assert.Equal(_tmp.Path, ((PhysicalLocation)app.Browser.Location!).FullPath);
        d.Press(InputAction.Back);
        await d.Idle();
        Assert.Equal(sub, ((PhysicalLocation)app.Browser.Location!).FullPath);

        // A raiz leva ao início com o foco de volta nos locais.
        d.Press(InputAction.PreviousRegion);
        d.Press(InputAction.PageUp);
        Assert.Equal(BreadcrumbKind.Root, app.Breadcrumbs[app.BreadcrumbFocus].Kind);
        d.Press(InputAction.Confirm);
        Assert.Equal(Screen.Home, app.Screen);
        Assert.Equal(PaneRegion.List, app.FocusRegion);
    });
}
