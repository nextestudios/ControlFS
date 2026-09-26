using ControlFS.Core.Actions;

namespace ControlFS.Core.Input;

public enum StickDirection
{
    None,
    Up,
    Down,
    Left,
    Right,
}

/// <summary>
/// Converte um analógico (x, y em -1..1; y negativo = para cima, convenção SDL) em no máximo
/// uma direção digital, com zona morta, limiar de ativação, histerese e prioridade de eixo.
/// </summary>
public sealed class StickNormalizer(InputSettings settings)
{
    public StickDirection Current { get; private set; }

    public StickDirection Update(double x, double y)
    {
        x = Math.Clamp(x, -1, 1);
        y = Math.Clamp(y, -1, 1);
        var magnitude = Math.Sqrt(x * x + y * y);
        if (magnitude < settings.Deadzone)
        {
            Current = StickDirection.None;
            return Current;
        }

        // Mantém a direção atual enquanto sua componente não cair abaixo do limiar de liberação.
        if (Current != StickDirection.None && Component(Current, x, y) >= settings.ReleaseThreshold)
        {
            var candidate = Dominant(x, y);
            var candidateValue = candidate == StickDirection.None ? 0 : Component(candidate, x, y);
            // Troca apenas se a nova direção for claramente dominante e ativa.
            if (candidate != Current && candidate != StickDirection.None && candidateValue >= settings.ActivationThreshold &&
                candidateValue >= Component(Current, x, y) * settings.DominanceRatio)
            {
                Current = candidate;
            }
            return Current;
        }

        var dominant = Dominant(x, y);
        Current = dominant != StickDirection.None && Component(dominant, x, y) >= settings.ActivationThreshold ? dominant : StickDirection.None;
        return Current;
    }

    public void Reset() => Current = StickDirection.None;

    public static PhysicalControl ToControl(StickDirection d) => d switch
    {
        StickDirection.Up => PhysicalControl.StickUp,
        StickDirection.Down => PhysicalControl.StickDown,
        StickDirection.Left => PhysicalControl.StickLeft,
        StickDirection.Right => PhysicalControl.StickRight,
        _ => throw new ArgumentOutOfRangeException(nameof(d)),
    };

    private StickDirection Dominant(double x, double y)
    {
        var ax = Math.Abs(x);
        var ay = Math.Abs(y);
        if (ax >= ay * settings.DominanceRatio) return x < 0 ? StickDirection.Left : StickDirection.Right;
        if (ay >= ax * settings.DominanceRatio) return y < 0 ? StickDirection.Up : StickDirection.Down;
        return StickDirection.None; // diagonal ambígua: nenhuma ação
    }

    private static double Component(StickDirection d, double x, double y) => d switch
    {
        StickDirection.Up => -y,
        StickDirection.Down => y,
        StickDirection.Left => -x,
        StickDirection.Right => x,
        _ => 0,
    };
}
