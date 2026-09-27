using ControlFS.Core.Input;

namespace ControlFS.Core.Actions;

/// <summary>Legendas por estilo. Somente rótulos: o comportamento segue a posição física e a convenção.</summary>
public static class ButtonGlyphs
{
    public static string For(PhysicalControl control, ControllerFamily family) => family switch
    {
        ControllerFamily.Xbox => control switch
        {
            PhysicalControl.South => "A", PhysicalControl.East => "B", PhysicalControl.West => "X", PhysicalControl.North => "Y",
            PhysicalControl.LeftShoulder => "LB", PhysicalControl.RightShoulder => "RB", PhysicalControl.LeftTrigger => "LT", PhysicalControl.RightTrigger => "RT",
            PhysicalControl.Start => "☰", PhysicalControl.Select => "⧉", PhysicalControl.RightStickClick => "RS", _ => Direction(control),
        },
        ControllerFamily.PlayStation => control switch
        {
            PhysicalControl.South => "✕", PhysicalControl.East => "○", PhysicalControl.West => "□", PhysicalControl.North => "△",
            PhysicalControl.LeftShoulder => "L1", PhysicalControl.RightShoulder => "R1", PhysicalControl.LeftTrigger => "L2", PhysicalControl.RightTrigger => "R2",
            PhysicalControl.Start => "Options", PhysicalControl.Select => "Create", PhysicalControl.RightStickClick => "R3", _ => Direction(control),
        },
        ControllerFamily.Nintendo => control switch
        {
            PhysicalControl.South => "B", PhysicalControl.East => "A", PhysicalControl.West => "Y", PhysicalControl.North => "X",
            PhysicalControl.LeftShoulder => "L", PhysicalControl.RightShoulder => "R", PhysicalControl.LeftTrigger => "ZL", PhysicalControl.RightTrigger => "ZR",
            PhysicalControl.Start => "+", PhysicalControl.Select => "−", PhysicalControl.RightStickClick => "RS", _ => Direction(control),
        },
        _ => control switch
        {
            PhysicalControl.South => "↓●", PhysicalControl.East => "→●", PhysicalControl.West => "←●", PhysicalControl.North => "↑●",
            PhysicalControl.LeftShoulder => "L1", PhysicalControl.RightShoulder => "R1", PhysicalControl.LeftTrigger => "L2", PhysicalControl.RightTrigger => "R2",
            PhysicalControl.Start => "Start", PhysicalControl.Select => "Select", PhysicalControl.RightStickClick => "R3", _ => Direction(control),
        },
    };

    /// <summary>
    /// Tecla física equivalente (teclado), usada nas legendas quando não há controle ativo. No teclado virtual
    /// (<paramref name="typing"/>) as teclas mudam: Enter conclui, ←/→ movem o cursor, Backspace apaga.
    /// </summary>
    public static string KeyboardFor(InputAction action, bool typing = false) => typing switch
    {
        true when action == InputAction.OpenAppMenu => "Enter",
        true when action == InputAction.PreviousRegion => "←",
        true when action == InputAction.NextRegion => "→",
        true when action == InputAction.ToggleSelection => "⌫",
        _ => KeyboardKey(action),
    };

    private static string KeyboardKey(InputAction action) => action switch
    {
        InputAction.Confirm => "Enter",
        InputAction.Back => "Esc",
        InputAction.ToggleSelection => "Espaço",
        InputAction.OpenContextMenu => "F2",
        InputAction.OpenAppMenu => "F10",
        InputAction.Search => "Ctrl+F",
        InputAction.ChangeView => "Ctrl+G",
        InputAction.PreviousRegion => "Ctrl+←",
        InputAction.NextRegion => "Ctrl+→",
        InputAction.PageUp => "PgUp",
        InputAction.PageDown => "PgDn",
        InputAction.NavigateUp or InputAction.NavigateDown => "↑↓",
        InputAction.NavigateLeft or InputAction.NavigateRight => "←→",
        _ => action.ToString(),
    };

    private static string Direction(PhysicalControl control) => control switch
    {
        PhysicalControl.DPadUp or PhysicalControl.StickUp => "▲",
        PhysicalControl.DPadDown or PhysicalControl.StickDown => "▼",
        PhysicalControl.DPadLeft or PhysicalControl.StickLeft => "◀",
        PhysicalControl.DPadRight or PhysicalControl.StickRight => "▶",
        _ => control.ToString(),
    };
}
