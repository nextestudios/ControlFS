using ControlFS.Application;
using ControlFS.Application.Prompts;
using ControlFS.Core.Actions;
using ControlFS.Core.Input;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

/// <summary>Legendas do rodapé por família, troca a quente, teclado e convenção confirmar/voltar.</summary>
public class PromptJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private static ControllerPrompt Prompt(AppController app, InputAction action) => Assert.Single(app.Prompts, p => p.Action == action);

    [Fact]
    public void Prompts_follow_the_active_family_the_keyboard_and_the_confirm_convention() => UiContext.Run(async () =>
    {
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService());
        app.Start();
        var d = new Driver(app);
        var changes = 0;
        app.Changed += () => changes++;

        // Sem controle ativo: teclas do teclado.
        Assert.Equal(new ControllerPrompt(InputAction.Confirm, "Abrir", null, null, "Enter", "Enter: Abrir"), Prompt(app, InputAction.Confirm));

        app.SetActiveController(ControllerFamily.Xbox);
        Assert.Equal(1, changes); // o rodapé é redesenhado na hora
        Assert.Equal((ControllerButton.FaceSouth, ControllerFamily.Xbox), (Prompt(app, InputAction.Confirm).Button, Prompt(app, InputAction.Confirm).Family));
        Assert.Equal("Botão Menu: Menu", Prompt(app, InputAction.OpenAppMenu).AccessibilityText);

        // R3 troca lista ↔ grade; a legenda diz para onde vai (na lista, "Grade"; na grade, "Lista"). Teclado: Ctrl+G.
        Assert.Equal((ControllerButton.RightStickClick, "Grade"), (Prompt(app, InputAction.ChangeView).Button, Prompt(app, InputAction.ChangeView).Label));
        d.Press(InputAction.ChangeView);
        Assert.True(app.IsGrid);
        Assert.Equal("Pressionar analógico direito: Lista", Prompt(app, InputAction.ChangeView).AccessibilityText);
        d.Press(InputAction.ChangeView);
        Assert.Equal(new[] { InputAction.Confirm, InputAction.Back, InputAction.OpenContextMenu, InputAction.OpenAppMenu, InputAction.ChangeView },
            app.Prompts.Select(p => p.Action)); // ordem do rodapé: Abrir, Voltar, Ações, Menu, Lista/Grade

        app.SetActiveController(ControllerFamily.PlayStation); // troca a quente
        Assert.Equal("Botão cruz: Abrir", Prompt(app, InputAction.Confirm).AccessibilityText);
        Assert.Equal("Botão círculo: Sair", Prompt(app, InputAction.Back).AccessibilityText);

        // Convenção "confirmar com o botão direito": troca comportamento E legenda.
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Confirmar com");
        Assert.Equal(ControllerButton.FaceEast, Prompt(app, InputAction.Confirm).Button);
        Assert.Equal(ControllerButton.FaceSouth, Prompt(app, InputAction.Back).Button);

        // Estilo fixado no menu vence a família detectada.
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Legendas"); // automáticas -> genéricas
        Assert.Equal(ControllerFamily.Generic, Prompt(app, InputAction.Confirm).Family);

        app.SetActiveController(null); // voltou ao teclado
        Assert.True(Prompt(app, InputAction.Confirm).IsKeyboard);
        Assert.Equal("Ctrl+G: Grade", Prompt(app, InputAction.ChangeView).AccessibilityText);
    });
}
