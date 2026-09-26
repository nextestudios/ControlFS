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

    public async Task<KeyboardModal> WaitKeyboard()
    {
        await UiContext.WaitUntil(() => App.TopModal is KeyboardModal { IsBusy: false }, "teclado virtual");
        return (KeyboardModal)App.TopModal!;
    }

    public async Task ChooseMenu(string labelStart)
    {
        var menu = await WaitMenu();
        var index = menu.Items.ToList().FindIndex(i => i.Label.StartsWith(labelStart, StringComparison.Ordinal));
        Assert.True(index >= 0, $"Item de menu \"{labelStart}\" ausente: {string.Join(" | ", menu.Items.Select(i => i.Label))}");
        while (menu.FocusIndex != index) Press(InputAction.NavigateDown);
        Press(InputAction.Confirm);
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
