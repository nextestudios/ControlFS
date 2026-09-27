namespace ControlFS.Core.Input.Mapping;

/// <summary>Tipo de entrada crua de um joystick sem perfil de gamepad (SDL <c>SDL_Joystick*</c>).</summary>
public enum RawInputKind
{
    Button,
    Hat,
    Axis,
}

/// <summary>Máscaras do direcional digital ("hat"), iguais às do SDL. Diagonais são a soma de duas.</summary>
public static class HatDirections
{
    public const int Centered = 0;
    public const int Up = 1;
    public const int Right = 2;
    public const int Down = 4;
    public const int Left = 8;

    public static bool IsSingle(int value) => value is Up or Right or Down or Left;
}

/// <summary>
/// Evento cru. <see cref="Value"/>: botão 1 (pressionado) ou 0; hat, a máscara (<see cref="HatDirections"/>);
/// eixo, -1..1 (convenção SDL: y negativo = para cima).
/// </summary>
public readonly record struct RawInputEvent(RawInputKind Kind, int Index, double Value)
{
    public static RawInputEvent Button(int index, bool down) => new(RawInputKind.Button, index, down ? 1 : 0);

    public static RawInputEvent Hat(int index, int mask) => new(RawInputKind.Hat, index, mask);

    public static RawInputEvent Axis(int index, double value) => new(RawInputKind.Axis, index, Math.Clamp(value, -1, 1));
}

/// <summary>
/// Uma entrada física crua ligada a um controle do produto. <see cref="Direction"/>: botão 0; hat, a máscara de UMA
/// direção; eixo, o sentido (-1 ou +1) em relação ao neutro calibrado. O sentido é a inversão do eixo: "Cima" em
/// +1 significa um eixo invertido em relação à convenção SDL.
/// </summary>
public readonly record struct RawBinding(RawInputKind Kind, int Index, int Direction)
{
    public string Describe() => Kind switch
    {
        RawInputKind.Button => $"botão {Index + 1}",
        RawInputKind.Hat => $"direcional {Index + 1} {Direction switch { HatDirections.Up => "↑", HatDirections.Down => "↓", HatDirections.Left => "←", _ => "→" }}",
        _ => $"eixo {Index + 1} {(Direction < 0 ? "−" : "+")}",
    };
}

/// <summary>Estado instantâneo de um joystick cru (para calibrar o neutro sem esperar eventos).</summary>
public sealed record RawJoystickState(IReadOnlyList<double> Axes, IReadOnlyList<bool> Buttons, IReadOnlyList<int> Hats)
{
    public static RawJoystickState Empty { get; } = new([], [], []);
}
