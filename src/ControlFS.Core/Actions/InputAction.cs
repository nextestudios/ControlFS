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
}

public static class InputActionExtensions
{
    /// <summary>Somente navegação repete automaticamente. Confirmar, excluir, colar etc. exigem nova transição de botão.</summary>
    public static bool IsRepeatable(this InputAction action) => action is
        InputAction.NavigateUp or InputAction.NavigateDown or InputAction.NavigateLeft or InputAction.NavigateRight or
        InputAction.PageUp or InputAction.PageDown;
}
