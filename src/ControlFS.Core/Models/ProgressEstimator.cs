namespace ControlFS.Core.Models;

/// <summary>Andamento suavizado: fração que nunca recua, velocidade e, quando confiável, o tempo restante.</summary>
/// <param name="Fraction">0–1, monotônica; null quando o total é desconhecido (indeterminado).</param>
/// <param name="BytesPerSecond">Velocidade suavizada em bytes; null sem dados suficientes.</param>
/// <param name="Remaining">Estimativa do tempo restante; null enquanto não for confiável ou quando o trabalho é pequeno demais.</param>
public sealed record ProgressSnapshot(double? Fraction, double? BytesPerSecond, TimeSpan? Remaining);

/// <summary>
/// Estimador puro de andamento (sem relógio próprio: quem chama informa o tempo). A velocidade é uma média móvel
/// exponencial; a estimativa só aparece com pelo menos 3 s de trabalho, 5% concluído e total conhecido, e a fração
/// mostrada nunca recua (um total que cresce durante o planejamento não faz a barra voltar).
/// </summary>
public sealed class ProgressEstimator
{
    private readonly TimeSpan _start;

    public ProgressEstimator(TimeSpan start)
    {
        _start = start;
        _sampleAt = start;
    }

    public static readonly TimeSpan MinElapsed = TimeSpan.FromSeconds(3);
    public const double MinFraction = 0.05;
    private const double Smoothing = 0.3;
    private static readonly TimeSpan MinSample = TimeSpan.FromMilliseconds(250);

    private double _fraction;
    private double? _fractionRate;
    private double? _byteRate;
    private double _sampleFraction;
    private long _sampleBytes;
    private TimeSpan _sampleAt;
    private TimeSpan _paused;
    private TimeSpan? _pausedAt;

    public ProgressSnapshot Update(TimeSpan now, OperationProgress progress)
    {
        var raw = progress.Fraction;
        if (raw is { } value) _fraction = Math.Max(_fraction, value);
        var elapsed = (_pausedAt ?? now) - _start - _paused;

        if (_pausedAt is null && now - _sampleAt >= MinSample)
        {
            var seconds = (now - _sampleAt).TotalSeconds;
            if (raw is not null)
            {
                var rate = Math.Max(0, _fraction - _sampleFraction) / seconds;
                _fractionRate = _fractionRate is { } previous ? Smoothing * rate + (1 - Smoothing) * previous : rate;
            }
            var bytesRate = Math.Max(0, progress.BytesProcessed - _sampleBytes) / seconds;
            _byteRate = _byteRate is { } prior ? Smoothing * bytesRate + (1 - Smoothing) * prior : bytesRate;
            _sampleAt = now;
            _sampleFraction = _fraction;
            _sampleBytes = progress.BytesProcessed;
        }

        TimeSpan? remaining = null;
        if (raw is not null && elapsed >= MinElapsed && _fraction >= MinFraction && _fraction < 1 && _fractionRate is > 0)
            remaining = TimeSpan.FromSeconds((1 - _fraction) / _fractionRate.Value);
        return new ProgressSnapshot(raw is null ? null : _fraction, _byteRate is > 0 ? _byteRate : null, remaining);
    }

    /// <summary>O tempo parado não conta como trabalho (nem deprime a velocidade).</summary>
    public void Pause(TimeSpan now) => _pausedAt ??= now;

    public void Resume(TimeSpan now)
    {
        if (_pausedAt is not { } since) return;
        _paused += now - since;
        _pausedAt = null;
        _sampleAt = now;
    }
}
