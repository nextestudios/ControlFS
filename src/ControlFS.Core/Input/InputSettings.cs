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

    /// <summary>Analógico direito (rolagem, #175): magnitude para começar a rolar (zona morta contra drift).</summary>
    public double ScrollDeadzone { get; init; } = 0.28;
    /// <summary>Magnitude abaixo da qual a rolagem para (histerese: menor que <see cref="ScrollDeadzone"/>).</summary>
    public double ScrollRelease { get; init; } = 0.2;
    /// <summary>Passos por segundo logo depois da zona morta e com o analógico no fim do curso.</summary>
    public double ScrollMinRate { get; init; } = 3;
    public double ScrollMaxRate { get; init; } = 14;
    /// <summary>Inclinação mantida acelera até este fator em <see cref="ScrollAccelerationTime"/> (proporcional à inclinação).</summary>
    public double ScrollMaxBoost { get; init; } = 2.2;
    public TimeSpan ScrollAccelerationTime { get; init; } = TimeSpan.FromMilliseconds(1500);

    public static InputSettings Default { get; } = new();
}
