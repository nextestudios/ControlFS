namespace ControlFS.Core.Actions;

/// <summary>Ações semânticas. Telas trabalham apenas com estas ações, nunca com botões físicos.</summary>
public enum InputAction
{
    NavigateUp,
    NavigateDown,
    NavigateLeft,
    NavigateRight,
    Confirm,
    Back,
    ToggleSelection,
    OpenContextMenu,
    PreviousRegion,
    NextRegion,
    PageUp,
    PageDown,
    OpenAppMenu,
    Search,
    ChangeView,

    /// <summary>
    /// Rolagem contínua do analógico direito (#175): um passo por ação, na taxa que a inclinação pede. Não vem de botão
    /// nem repete pelo roteador; rola só a superfície ativa (lista, visualização, modal) e nunca abre, volta ou confirma.
    /// </summary>
    ScrollUp,
    ScrollDown,
    ScrollLeft,
    ScrollRight,
}

public static class InputActionExtensions
{
    /// <summary>Somente navegação repete automaticamente. Confirmar, excluir, colar etc. exigem nova transição de botão.</summary>
    public static bool IsRepeatable(this InputAction action) => action is
        InputAction.NavigateUp or InputAction.NavigateDown or InputAction.NavigateLeft or InputAction.NavigateRight or
        InputAction.PageUp or InputAction.PageDown;

    public static bool IsScroll(this InputAction action) => action is
        InputAction.ScrollUp or InputAction.ScrollDown or InputAction.ScrollLeft or InputAction.ScrollRight;
}
