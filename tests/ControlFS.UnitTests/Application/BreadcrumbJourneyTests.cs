using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;
using static ControlFS.UnitTests.Support.ZipFixtures;

namespace ControlFS.UnitTests.Application;

public class BreadcrumbJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private Driver Boot()
    {
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService());
        app.Start();
        return new Driver(app);
    }

    private static async Task Open(Driver d, string name)
    {
        await d.FocusItem(name);
        d.Press(InputAction.Confirm);
        await d.Idle();
    }

    [Fact]
    public void Shoulder_opens_the_path_bar_and_a_segment_navigates_up_restoring_focus_on_the_child() => UiContext.Run(async () =>
    {
        _tmp.MakeDir("a", "b", "c");
        _tmp.MakeDir("a", "outra");
        var d = Boot();
        var app = d.App;
        d.Press(InputAction.Confirm); // pasta de teste
        await d.Idle();
        await Open(d, "a");
        await Open(d, "b");
        await Open(d, "c");

        // LB: foco vai para a pasta de cima ("b"); a lista deixa de receber as setas
        d.Press(InputAction.PreviousRegion);
        Assert.Equal(PaneRegion.Breadcrumbs, app.Browser.Region);
        Assert.Equal("b", app.Breadcrumbs[app.Browser.BreadcrumbFocus].Label);
        Assert.Equal("c", app.Breadcrumbs[^1].Label);
        Assert.True(app.Breadcrumbs[^1].IsCurrent);

        d.Press(InputAction.NavigateLeft);
        Assert.Equal("a", app.Breadcrumbs[app.Browser.BreadcrumbFocus].Label);
        d.Press(InputAction.Confirm);
        await d.Idle();

        Assert.Equal(_tmp.Sub("a"), ((PhysicalLocation)app.Browser.Location!).FullPath);
        Assert.Equal("b", app.Browser.List.Focused?.Name); // a pasta de onde viemos
        Assert.Equal(PaneRegion.List, app.Browser.Region);

        // Voltar no histórico retorna para "c"
        d.Press(InputAction.Back);
        await d.Idle();
        Assert.Equal(_tmp.Sub("a", "b", "c"), ((PhysicalLocation)app.Browser.Location!).FullPath);
    });

    [Fact]
    public void Archive_boundary_is_a_distinct_segment_and_the_disk_folder_focuses_the_archive() => UiContext.Run(async () =>
    {
        Create(_tmp.Sub("arq.zip"), Text("docs/sub/b.txt", "b"));
        var d = Boot();
        var app = d.App;
        d.Press(InputAction.Confirm);
        await d.Idle();
        await Open(d, "arq.zip");
        await Open(d, "docs");
        await Open(d, "sub");

        var crumbs = app.Breadcrumbs;
        Assert.Equal(["arq.zip", "docs", "sub"], crumbs.TakeLast(3).Select(c => c.Label));
        Assert.Equal([BreadcrumbKind.Archive, BreadcrumbKind.ArchiveFolder, BreadcrumbKind.ArchiveFolder], crumbs.TakeLast(3).Select(c => c.Kind));
        Assert.Equal(Path.GetFileName(_tmp.Path), crumbs[^4].Label); // pasta do disco logo antes da fronteira

        // Do meio do compactado direto para a raiz dele: foco em "docs"
        d.Press(InputAction.PreviousRegion);
        d.Press(InputAction.NavigateLeft); // "docs" -> "arq.zip"
        d.Press(InputAction.Confirm);
        await d.Idle();
        Assert.Equal("", ((ArchiveLocation)app.Browser.Location!).InnerPath);
        Assert.Equal("docs", app.Browser.List.Focused?.Name);

        // Da raiz do compactado para a pasta do disco: foco no próprio arquivo
        d.Press(InputAction.PreviousRegion);
        d.Press(InputAction.Confirm);
        await d.Idle();
        Assert.Equal(_tmp.Path, ((PhysicalLocation)app.Browser.Location!).FullPath);
        Assert.Equal("arq.zip", app.Browser.List.Focused?.Name);
    });

    [Fact]
    public void Long_paths_collapse_the_middle_but_keep_the_root_the_archive_and_the_last_three()
    {
        static Breadcrumb F(string n, BreadcrumbKind k = BreadcrumbKind.Folder) => new(n, k, null, null);
        var all = new[] { F("C:\\"), F("u"), F("ana"), F("jogos"), F("roms.zip", BreadcrumbKind.Archive), F("snes", BreadcrumbKind.ArchiveFolder),
            F("rpg", BreadcrumbKind.ArchiveFolder), F("x", BreadcrumbKind.ArchiveFolder), F("y", BreadcrumbKind.ArchiveFolder) };
        var visible = BreadcrumbTrail.Collapse(all);
        Assert.Equal(["C:\\", "…", "roms.zip", "…", "rpg", "x", "y"], visible.Select(c => c.Label));
        Assert.Equal(["u", "ana", "jogos"], visible[1].Hidden.Select(c => c.Label));
        Assert.Equal(["snes"], visible[3].Hidden.Select(c => c.Label));
        Assert.Equal(6, BreadcrumbTrail.Collapse(all[..6]).Count); // curto: nada recolhido
    }
}
