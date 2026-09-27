using ControlFS.Application;
using ControlFS.Core.Actions;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Windows.FileSystem;
using ControlFS.UnitTests.Support;
using static ControlFS.UnitTests.Support.ZipFixtures;

namespace ControlFS.UnitTests.Application;

public class SelectAllJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private static string? Label(AppController app, InputAction action) => app.Hints.SingleOrDefault(h => h.Action == action)?.Label;

    [Fact]
    public void Select_all_and_clear_selection_from_the_actions_menu_show_counts_and_update_the_action_bar() => UiContext.Run(async () =>
    {
        File.WriteAllText(_tmp.Sub("a.txt"), "a");
        File.WriteAllText(_tmp.Sub("b.txt"), "b");
        _tmp.MakeDir("pasta");
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), fileOperations: new FileOperationService());
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        await d.Idle();

        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Marcar todos (3)");
        Assert.Equal(3, app.Browser.List.SelectionCount);
        Assert.Equal("Operações (3)", Label(app, InputAction.OpenContextMenu));

        // Tudo marcado: o menu em lote só oferece limpar, com a contagem
        d.Press(InputAction.OpenContextMenu);
        var menu = await d.WaitMenu();
        Assert.DoesNotContain(menu.Items, i => i.Label.StartsWith("Marcar todos", StringComparison.Ordinal));
        await d.ChooseMenu("Limpar marcação (3)");
        Assert.Equal(0, app.Browser.List.SelectionCount);
        Assert.Equal("Ações", Label(app, InputAction.OpenContextMenu));
    });

    [Fact]
    public void Select_all_inside_an_archive_skips_blocked_entries() => UiContext.Run(async () =>
    {
        Create(_tmp.Sub("arq.zip"), Text("a.txt", "a"), Text("docs/b.txt", "b"), Text("../fora.txt", "x"));
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService());
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        await d.FocusItem("arq.zip");
        d.Press(InputAction.Confirm);
        await d.Idle();

        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Marcar todos (2)");
        Assert.Equal(2, app.Browser.List.SelectionCount);
        Assert.DoesNotContain(app.Browser.List.SelectedEntries, e => e.IsBlocked);
    });
}
