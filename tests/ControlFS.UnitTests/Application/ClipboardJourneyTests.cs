using ControlFS.Application;
using ControlFS.Core.Actions;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Windows.FileSystem;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

public class ClipboardJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private Driver Boot()
    {
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), fileOperations: new FileOperationService());
        app.Start();
        return new Driver(app);
    }

    private static async Task GoInto(Driver d, string folder)
    {
        await d.FocusItem(folder);
        d.Press(InputAction.Confirm);
        await d.Idle();
    }

    [Fact]
    public void Copy_then_paste_in_another_folder_keeps_the_original_and_the_clipboard() => UiContext.Run(async () =>
    {
        File.WriteAllText(_tmp.Sub("foto.jpg"), "img");
        _tmp.MakeDir("Álbum");
        var d = Boot();
        d.Press(InputAction.Confirm);
        await d.FocusItem("foto.jpg");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Copiar");
        Assert.False(d.App.Clipboard!.IsCut);

        await GoInto(d, "Álbum");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Colar 1 item");
        d.ChooseOption(await d.WaitDialog("Copiar 1 item?"), "Copiar");
        await d.WaitDialog("Copiar: concluído");

        Assert.Equal("img", File.ReadAllText(_tmp.Sub("Álbum", "foto.jpg")));
        Assert.True(File.Exists(_tmp.Sub("foto.jpg")));
        Assert.NotNull(d.App.Clipboard); // copiar permite colar de novo
    });

    [Fact]
    public void Cut_marks_items_and_paste_moves_them_then_empties_the_clipboard() => UiContext.Run(async () =>
    {
        File.WriteAllText(_tmp.Sub("a.txt"), "a");
        _tmp.MakeDir("Destino");
        var d = Boot();
        d.Press(InputAction.Confirm);
        await d.FocusItem("a.txt");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Recortar");
        Assert.True(d.App.IsCut(d.App.Browser.List.Focused!));
        Assert.True(File.Exists(_tmp.Sub("a.txt")), "recortar não altera nada até colar");

        await GoInto(d, "Destino");
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Colar 1 item (mover)");
        d.ChooseOption(await d.WaitDialog("Mover 1 item?"), "Mover");
        await UiContext.WaitUntil(() => d.App.Operations.Items.Count == 1 && !d.App.Operations.Items[0].IsActive, "movimentação concluída");
        await d.Idle();

        Assert.False(File.Exists(_tmp.Sub("a.txt")));
        Assert.Equal("a", File.ReadAllText(_tmp.Sub("Destino", "a.txt")));
        Assert.Null(d.App.Clipboard);
    });
}
