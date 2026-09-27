namespace ControlFS.Core.Actions;

/// <summary>Mapeamento padrão (seção 10 da especificação) sobre posições físicas.</summary>
public sealed class ActionMap
{
    private readonly Dictionary<PhysicalControl, InputAction> _map;

    public ActionMap(ConfirmBackConvention convention)
    {
        Convention = convention;
        var confirm = convention == ConfirmBackConvention.SouthConfirms ? PhysicalControl.South : PhysicalControl.East;
        var back = convention == ConfirmBackConvention.SouthConfirms ? PhysicalControl.East : PhysicalControl.South;
        _map = new()
        {
            [PhysicalControl.DPadUp] = InputAction.NavigateUp,
            [PhysicalControl.DPadDown] = InputAction.NavigateDown,
            [PhysicalControl.DPadLeft] = InputAction.NavigateLeft,
            [PhysicalControl.DPadRight] = InputAction.NavigateRight,
            [PhysicalControl.StickUp] = InputAction.NavigateUp,
            [PhysicalControl.StickDown] = InputAction.NavigateDown,
            [PhysicalControl.StickLeft] = InputAction.NavigateLeft,
            [PhysicalControl.StickRight] = InputAction.NavigateRight,
            [confirm] = InputAction.Confirm,
            [back] = InputAction.Back,
            [PhysicalControl.West] = InputAction.ToggleSelection,
            [PhysicalControl.North] = InputAction.OpenContextMenu,
            [PhysicalControl.LeftShoulder] = InputAction.PreviousRegion,
            [PhysicalControl.RightShoulder] = InputAction.NextRegion,
            [PhysicalControl.LeftTrigger] = InputAction.PageUp,
            [PhysicalControl.RightTrigger] = InputAction.PageDown,
            [PhysicalControl.Start] = InputAction.OpenAppMenu,
            [PhysicalControl.Select] = InputAction.Search,
            [PhysicalControl.RightStickClick] = InputAction.ChangeView,
        };
    }

    public ConfirmBackConvention Convention { get; }

    public InputAction? Resolve(PhysicalControl control) => _map.TryGetValue(control, out var a) ? a : null;

    /// <summary>Primeiro controle físico que produz a ação (para legendas).</summary>
    public PhysicalControl? ControlFor(InputAction action)
    {
        foreach (var (control, mapped) in _map)
            if (mapped == action) return control;
        return null;
    }
}
