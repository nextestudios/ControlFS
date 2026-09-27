using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Text;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Windows.FileSystem;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

/// <summary>Renomear em lote (#71): a prévia é exatamente o resultado, conflitos bloqueiam antes do disco e tudo se desfaz.</summary>
public class BatchRenameJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private async Task<Driver> BootAndMark(params string[] names)
    {
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), fileOperations: new FileOperationService());
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        foreach (var name in names)
        {
            await d.FocusItem(name);
            d.Press(InputAction.ToggleSelection);
        }
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Renomear em lote…");
        return d;
    }

    private static async Task Type(Driver d, DialogModal dialog, string option, string text)
    {
        d.ChooseOption(dialog, option);
        var kb = await d.WaitKeyboard();
        d.PressKey(kb, KeyKind.Clear);
        d.TypeOnKeyboard(kb, text);
        d.PressKey(kb, KeyKind.Done);
        await UiContext.WaitUntil(() => d.App.TopModal == dialog, "de volta à prévia");
    }

    [Fact]
    public void Numbering_preview_is_exactly_what_gets_applied_and_undo_restores_every_name() => UiContext.Run(async () =>
    {
        File.WriteAllText(_tmp.Sub("c.jpg"), "c");
        File.WriteAllText(_tmp.Sub("a.jpg"), "a");
        File.WriteAllText(_tmp.Sub("b.png"), "b");
        var d = await BootAndMark("a.jpg", "b.png", "c.jpg");
        var dialog = await d.WaitDialog("Renomear 3 itens");
        await Type(d, dialog, "Nome base", "Foto");

        var preview = dialog.Lines.Where(l => l.Value.StartsWith("→ ", StringComparison.Ordinal)).Select(l => (l.Label, l.Value[2..])).ToList();
        Assert.Equal([("a.jpg", "Foto 001.jpg"), ("b.png", "Foto 002.png"), ("c.jpg", "Foto 003.jpg")], preview);

        d.Press(InputAction.OpenAppMenu); // Start aplica de qualquer opção em foco
        await UiContext.WaitUntil(() => d.App.UndoTitle is not null, "lote renomeado e registrado no Desfazer");
        await d.Idle();
        Assert.Equal(["Foto 001.jpg", "Foto 002.png", "Foto 003.jpg"], Directory.GetFiles(_tmp.Path).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal("a", File.ReadAllText(_tmp.Sub("Foto 001.jpg")));

        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Desfazer: ");
        d.ChooseOption(await d.WaitDialog("Desfazer \""), "Desfazer");
        await UiContext.WaitUntil(() => d.App.RedoTitle is not null, "nomes de volta");
        await d.Idle();
        Assert.Equal(["a.jpg", "b.png", "c.jpg"], Directory.GetFiles(_tmp.Path).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    });

    [Fact]
    public void A_name_already_used_in_the_folder_blocks_the_whole_batch_with_a_reason() => UiContext.Run(async () =>
    {
        File.WriteAllText(_tmp.Sub("cap 1.mkv"), "1");
        File.WriteAllText(_tmp.Sub("cap 2.mkv"), "2");
        File.WriteAllText(_tmp.Sub("parte 1.mkv"), "existente");
        var d = await BootAndMark("cap 1.mkv", "cap 2.mkv");
        var dialog = await d.WaitDialog("Renomear 2 itens");
        d.ChooseOption(dialog, "Modo:"); // Numeração → Localizar e substituir
        await Type(d, dialog, "Localizar", "CAP");
        await Type(d, dialog, "Substituir por", "parte");

        Assert.Contains(dialog.Lines, l => l.Label == "cap 1.mkv" && l.Value.Contains("Já existe", StringComparison.Ordinal));
        Assert.Contains(dialog.Lines, l => l.Label == "cap 2.mkv" && l.Value == "→ parte 2.mkv");
        Assert.NotNull(dialog.Message);

        d.Press(InputAction.OpenAppMenu);
        Assert.Same(dialog, d.App.TopModal);
        await d.Idle();
        Assert.Equal("existente", File.ReadAllText(_tmp.Sub("parte 1.mkv")));
        Assert.True(File.Exists(_tmp.Sub("cap 1.mkv")) && File.Exists(_tmp.Sub("cap 2.mkv")), "nada foi renomeado");
    });
}
