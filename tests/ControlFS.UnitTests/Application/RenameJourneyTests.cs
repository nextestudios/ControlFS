using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Text;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Windows.FileSystem;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

public class RenameJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private Driver Boot()
    {
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), fileOperations: new FileOperationService());
        app.Start();
        return new Driver(app);
    }

    private static async Task<KeyboardModal> OpenRename(Driver d, string name)
    {
        await d.FocusItem(name);
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Renomear…");
        return await d.WaitKeyboard();
    }

    [Fact]
    public void Rename_starts_before_the_extension_and_keeps_focus_on_the_item() => UiContext.Run(async () =>
    {
        File.WriteAllText(_tmp.Sub("example-file.zip"), "z");
        File.WriteAllText(_tmp.Sub("zzz.txt"), "z");
        var d = Boot();
        d.Press(InputAction.Confirm);
        var kb = await OpenRename(d, "example-file.zip");
        Assert.Equal("example-file".Length, kb.Keyboard.Caret);

        d.TypeOnKeyboard(kb, "-2");
        d.PressKey(kb, KeyKind.Done);
        await UiContext.WaitUntil(() => d.App.TopModal is null, "teclado fechado");
        await d.Idle();

        Assert.True(File.Exists(_tmp.Sub("example-file-2.zip")));
        Assert.Equal("example-file-2.zip", d.App.Browser.List.Focused?.Name);
    });

    [Fact]
    public void Changing_the_extension_asks_first_and_collisions_are_refused() => UiContext.Run(async () =>
    {
        File.WriteAllText(_tmp.Sub("notas.txt"), "n");
        File.WriteAllText(_tmp.Sub("outro.md"), "o");
        var d = Boot();
        d.Press(InputAction.Confirm);
        var kb = await OpenRename(d, "notas.txt");
        d.PressKey(kb, KeyKind.Clear);
        d.TypeOnKeyboard(kb, "outro.md");
        d.PressKey(kb, KeyKind.Done);

        var confirm = await d.WaitDialog("Alterar a extensão");
        Assert.Equal("Cancelar", confirm.Options[confirm.FocusIndex].Label);
        d.ChooseOption(confirm, "Alterar extensão");
        await UiContext.WaitUntil(() => kb.Keyboard.ErrorMessage is not null, "erro de nome existente");
        Assert.Contains("Já existe", kb.Keyboard.ErrorMessage, StringComparison.Ordinal);
        Assert.True(File.Exists(_tmp.Sub("notas.txt")));
        Assert.Equal("o", File.ReadAllText(_tmp.Sub("outro.md")));
    });
}
