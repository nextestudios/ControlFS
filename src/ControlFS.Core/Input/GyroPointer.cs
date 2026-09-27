namespace ControlFS.Core.Input;

/// <summary>Ajustes da mira por giroscópio (experimental, #77). Valores iniciais; a sensação real só se ajusta no hardware.</summary>
public sealed record GyroSettings
{
    /// <summary>Teclas percorridas por grau girado (1 tecla a cada 4°: a mão cruza o teclado de 11 colunas em ~45°).</summary>
    public double KeysPerDegree { get; init; } = 0.25;

    /// <summary>Velocidade (°/s) abaixo da qual o giro é tratado como tremor da mão e ignorado (zona morta suave).</summary>
    public double DeadzoneDegreesPerSecond { get; init; } = 2.5;

    /// <summary>Abaixo disto (°/s) o controle é considerado parado e a leitura entra na estimativa do desvio (bias) do sensor.</summary>
    public double StillDegreesPerSecond { get; init; } = 4;

    /// <summary>Peso de cada leitura parada na estimativa do desvio (calibração contínua).</summary>
    public double BiasLearningRate { get; init; } = 0.02;

    /// <summary>Suavização exponencial (0..1; 1 = sem suavizar).</summary>
    public double Smoothing { get; init; } = 0.5;

    /// <summary>Intervalo máximo aceito entre leituras; lacunas maiores (Bluetooth engasgou, janela pausada) não viram salto.</summary>
    public TimeSpan MaxStep { get; init; } = TimeSpan.FromMilliseconds(50);

    public static GyroSettings Default { get; } = new();
}

/// <summary>
/// Filtro da mira por giroscópio (#77): recebe a velocidade angular do gamepad (rad/s, convenção SDL: +X à direita do
/// controle, +Y para cima; rotação positiva no sentido anti-horário olhando do eixo) e devolve quanto o ponteiro anda,
/// em teclas. Calibração contínua (o desvio do sensor é aprendido enquanto o controle está parado), zona morta suave contra
/// tremor, suavização e limite de lacuna. Não conhece o teclado: quem aplica o deslocamento é o <c>AppController</c>.
/// Não é thread-safe.
/// </summary>
public sealed class GyroPointer(GyroSettings settings)
{
    private const double DegreesPerRadian = 180 / Math.PI;
    private double _biasPitch;
    private double _biasYaw;
    private double _pitch;
    private double _yaw;
    private TimeSpan? _last;

    /// <summary>
    /// Nova leitura. Devolve (dx, dy) em teclas: dx positivo = para a direita (girar o controle para a direita), dy
    /// positivo = para baixo (inclinar a frente do controle para baixo).
    /// </summary>
    public (double Dx, double Dy) Update(double pitchRadiansPerSecond, double yawRadiansPerSecond, TimeSpan timestamp)
    {
        var pitch = pitchRadiansPerSecond * DegreesPerRadian;
        var yaw = yawRadiansPerSecond * DegreesPerRadian;
        var elapsed = _last is { } last ? timestamp - last : TimeSpan.Zero;
        _last = timestamp;
        if (elapsed <= TimeSpan.Zero) return (0, 0);
        if (elapsed > settings.MaxStep) elapsed = settings.MaxStep;

        // Parado: a leitura é quase só o desvio do sensor, que é aprendido aos poucos (e nunca vira movimento).
        if (Math.Abs(pitch - _biasPitch) < settings.StillDegreesPerSecond && Math.Abs(yaw - _biasYaw) < settings.StillDegreesPerSecond)
        {
            _biasPitch += (pitch - _biasPitch) * settings.BiasLearningRate;
            _biasYaw += (yaw - _biasYaw) * settings.BiasLearningRate;
        }
        _pitch += (Deadzone(pitch - _biasPitch) - _pitch) * settings.Smoothing;
        _yaw += (Deadzone(yaw - _biasYaw) - _yaw) * settings.Smoothing;

        var seconds = elapsed.TotalSeconds;
        return (-_yaw * seconds * settings.KeysPerDegree, -_pitch * seconds * settings.KeysPerDegree);
    }

    /// <summary>Esquece a suavização e o intervalo (a próxima leitura só marca o tempo); a calibração aprendida continua.</summary>
    public void Reset()
    {
        _pitch = _yaw = 0;
        _last = null;
    }

    private double Deadzone(double value)
    {
        var magnitude = Math.Abs(value) - settings.DeadzoneDegreesPerSecond;
        return magnitude <= 0 ? 0 : Math.CopySign(magnitude, value);
    }
}
