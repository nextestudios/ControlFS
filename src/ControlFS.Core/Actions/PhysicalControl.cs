namespace ControlFS.Core.Actions;

/// <summary>
/// Controles por POSIÇÃO física (convenção SDL3: South/East/West/North), não por rótulo impresso.
/// Um "A" Nintendo fica em East; por isso nunca mapeamos por letra.
/// </summary>
public enum PhysicalControl
{
    DPadUp,
    DPadDown,
    DPadLeft,
    DPadRight,
    StickUp,
    StickDown,
    StickLeft,
    StickRight,
    South,
    East,
    West,
    North,
    LeftShoulder,
    RightShoulder,
    LeftTrigger,
    RightTrigger,
    Start,
    Select,
}

/// <summary>Qual botão de face confirma. O padrão é South; a alternativa troca comportamento E legendas.</summary>
public enum ConfirmBackConvention
{
    SouthConfirms,
    EastConfirms,
}

/// <summary>Preferência de legendas: <see cref="Automatic"/> segue a família do controle ativo; as demais a fixam.</summary>
public enum ButtonLabelStyle
{
    Automatic,
    Generic,
    Xbox,
    PlayStation,
    Nintendo,
}
