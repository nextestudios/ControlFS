using ControlFS.Application;
using ControlFS.Core.Actions;
using ControlFS.Core.Text;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Windows.FileSystem;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

public class KeyboardSuggestionJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public void Suggestions_come_from_the_folder_and_typed_history_never_for_passwords_and_can_be_turned_off() => UiContext.Run(async () =>
    {
        Directory.CreateDirectory(_tmp.Sub("Relatório 2026"));
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), fileOperations: new FileOperationService());
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        await d.Idle();

        // "rel" (sem acento) acha o nome da pasta; cima na primeira linha foca a faixa e Sul usa a sugestão.
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Nova pasta");
        var kb = await d.WaitKeyboard();
        d.PressKey(kb, KeyKind.Clear);
        d.TypeOnKeyboard(kb, "rel");
        Assert.Contains("Relatório 2026", kb.Keyboard.Suggestions);
        kb.Keyboard.FocusKey(0, 0);
        d.Press(InputAction.NavigateUp);
        Assert.Equal("Relatório 2026", kb.Keyboard.FocusedSuggestion);
        Assert.Contains(app.Hints, h => h.Action == InputAction.Confirm && h.Label == "Usar sugestão");
        d.Press(InputAction.Confirm);
        Assert.Equal("Relatório 2026", kb.Keyboard.Text);
        Assert.Null(kb.Keyboard.SuggestionIndex);

        // Um nome novo concluído passa a ser sugerido nas próximas vezes.
        d.PressKey(kb, KeyKind.Clear);
        d.TypeOnKeyboard(kb, "Projeto final");
        d.PressKey(kb, KeyKind.Done);
        await UiContext.WaitUntil(() => app.TopModal is null, "teclado fechado");
        Assert.Equal("Projeto final", app.Settings.TypedTexts[0]);

        // Senhas nunca têm sugestões, mesmo com uma fonte atribuída.
        var password = new VirtualKeyboard(TextFieldKind.Password, "Senha") { SuggestionSource = _ => ["Projeto final"] };
        Assert.Empty(password.Suggestions);

        // Desligar apaga o histórico e o próximo teclado não sugere nada.
        app.ToggleKeyboardSuggestions();
        Assert.Empty(app.Settings.TypedTexts);
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Nova pasta");
        var again = await d.WaitKeyboard();
        d.PressKey(again, KeyKind.Clear);
        d.TypeOnKeyboard(again, "rel");
        Assert.Empty(again.Keyboard.Suggestions);
    });
}
