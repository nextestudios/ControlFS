namespace ControlFS.Core.Input;

public sealed record InputSettings
{
    /// <summary>Magnitude abaixo da qual o analógico é considerado neutro (drift).</summary>
    public double Deadzone { get; init; } = 0.25;
    /// <summary>Componente do eixo necessário para ativar uma direção.</summary>
    public double ActivationThreshold { get; init; } = 0.55;
    /// <summary>Componente abaixo do qual uma direção ativa é liberada (histerese: menor que ativação).</summary>
    public double ReleaseThreshold { get; init; } = 0.40;
    /// <summary>O eixo dominante precisa superar o outro por este fator para evitar diagonais acidentais.</summary>
    public double DominanceRatio { get; init; } = 1.25;
    /// <summary>Gatilho analógico considerado pressionado acima deste valor (0..1).</summary>
    public double TriggerThreshold { get; init; } = 0.5;

    public TimeSpan RepeatInitialDelay { get; init; } = TimeSpan.FromMilliseconds(380);
    public TimeSpan RepeatStartInterval { get; init; } = TimeSpan.FromMilliseconds(130);
    public TimeSpan RepeatMinInterval { get; init; } = TimeSpan.FromMilliseconds(45);
    /// <summary>Fator aplicado ao intervalo a cada repetição (repetição progressiva).</summary>
    public double RepeatAcceleration { get; init; } = 0.9;

    public static InputSettings Default { get; } = new();
}
