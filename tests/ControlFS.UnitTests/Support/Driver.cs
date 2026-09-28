using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;

namespace ControlFS.UnitTests.Support;

/// <summary>Opera o AppController como um usuário de controle: somente ações semânticas.</summary>
public sealed class Driver(AppController app)
{
    public AppController App { get; } = app;

    public void Press(InputAction action) => App.Handle(action);

    public async Task Idle() => await App.WhenIdleAsync();

    public async Task FocusItem(string name)
    {
        await Idle();
        var list = App.ActivePane.List;
        var target = list.Items.ToList().FindIndex(i => i.Name == name);
        Assert.True(target >= 0, $"Item \"{name}\" não está na lista: {string.Join(", ", list.Items.Select(i => i.Name))}");
        while (list.FocusIndex != target) Press(list.FocusIndex < target ? InputAction.NavigateDown : InputAction.NavigateUp);
    }

    public async Task<MenuModal> WaitMenu()
    {
        await UiContext.WaitUntil(() => App.TopModal is MenuModal, "menu aberto");
        return (MenuModal)App.TopModal!;
    }

    public async Task<DialogModal> WaitDialog(string titleStart)
    {
        await UiContext.WaitUntil(() => App.TopModal is DialogModal d && d.Title.StartsWith(titleStart, StringComparison.Ordinal),
            $"diálogo \"{titleStart}\" (topo atual: {App.TopModal?.Title ?? "nenhum"})");
        return (DialogModal)App.TopModal!;
    }

    /// <summary>Aviso flutuante (StatusMessage) que começa com <paramref name="textStart"/>, sem nenhum modal por cima.</summary>
    public async Task WaitStatus(string textStart) =>
        await UiContext.WaitUntil(() => App.StatusMessage?.StartsWith(textStart, StringComparison.Ordinal) == true,
            $"aviso \"{textStart}\" (aviso atual: {App.StatusMessage ?? "nenhum"}; topo: {App.TopModal?.Title ?? "nenhum"})");

    public async Task<KeyboardModal> WaitKeyboard()
    {
        await UiContext.WaitUntil(() => App.TopModal is KeyboardModal { IsBusy: false }, "teclado virtual");
        return (KeyboardModal)App.TopModal!;
    }

    /// <summary>
    /// Escolhe a opção que começa com <paramref name="labelStart"/> só com setas e Confirmar. Ajustes que moram em
    /// Menu → Configurações (#193) são procurados lá quando o Menu não os tem; alternar um ajuste mantém Configurações
    /// aberto, então o driver fecha (Voltar), como quem ajusta e volta à pasta.
    /// </summary>
    public async Task ChooseMenu(string labelStart)
    {
        var menu = await WaitMenu();
        var index = IndexOf(menu, labelStart);
        if (index < 0 && menu.Title == "Menu" && IndexOf(menu, "Configurações") is var settings and >= 0)
        {
            FocusMenu(menu, settings);
            Press(InputAction.Confirm);
            var inner = await WaitMenu();
            await ChooseMenu(labelStart);
            if (ReferenceEquals(App.TopModal, inner)) Press(InputAction.Back);
            return;
        }
        Assert.True(index >= 0, $"Item de menu \"{labelStart}\" ausente: {string.Join(" | ", menu.Items.Select(i => i.Label))}");
        FocusMenu(menu, index);
        Press(InputAction.Confirm);
    }

    private static int IndexOf(MenuModal menu, string labelStart) => menu.Items.ToList().FindIndex(i => i.Label.StartsWith(labelStart, StringComparison.Ordinal));

    /// <summary>
    /// Só setas, como no controle. Bloco de uma grade: Baixo até entrar na grade, depois Cima/Baixo até a linha e
    /// Esquerda/Direita até a coluna. Item da lista: Baixo (dá a volta e atravessa as grades).
    /// </summary>
    public void FocusMenu(MenuModal menu, int index)
    {
        if (menu.GridOf(index) is { } grid)
        {
            for (var guard = 0; !grid.Contains(menu.FocusIndex); guard++)
            {
                Assert.True(guard < 500, "Grade inalcançável descendo.");
                Press(InputAction.NavigateDown);
            }
            while (grid.Row(menu.FocusIndex) > grid.Row(index)) Press(InputAction.NavigateUp);
            while (grid.Row(menu.FocusIndex) < grid.Row(index)) Press(InputAction.NavigateDown);
            while (menu.FocusIndex > index) Press(InputAction.NavigateLeft);
            while (menu.FocusIndex < index) Press(InputAction.NavigateRight);
            return;
        }
        for (var guard = 0; menu.FocusIndex != index; guard++)
        {
            Assert.True(guard < 500, "Item inalcançável descendo.");
            Press(InputAction.NavigateDown);
        }
    }

    public void ChooseOption(DialogModal dialog, string labelStart)
    {
        var index = dialog.Options.FindIndex(o => o.Label.StartsWith(labelStart, StringComparison.Ordinal));
        Assert.True(index >= 0, $"Opção \"{labelStart}\" ausente: {string.Join(" | ", dialog.Options.Select(o => o.Label))}");
        while (dialog.FocusIndex != index) Press(InputAction.NavigateRight);
        Press(InputAction.Confirm);
    }

    /// <summary>Digita no teclado virtual aberto usando apenas direções e confirmar.</summary>
    public void TypeOnKeyboard(KeyboardModal modal, string text) => KeyboardDriver.Type(Press, () => modal.Keyboard, text);

    public void PressKey(KeyboardModal modal, ControlFS.Core.Text.KeyKind kind) => KeyboardDriver.Press(Press, () => modal.Keyboard, k => k.Kind == kind);
}
