namespace ControlFS.Core.Policies;

/// <summary>
/// Limites finitos de extração. Contagens de bytes são aplicadas sobre dados efetivamente
/// escritos, não sobre o tamanho declarado no cabeçalho.
/// </summary>
public sealed record ExtractionLimits
{
    public int MaxEntries { get; init; } = 200_000;
    public int MaxDepth { get; init; } = 64;
    public int MaxRelativePathLength { get; init; } = 1024;
    public long MaxTotalBytes { get; init; } = 32L * 1024 * 1024 * 1024;
    public long MaxEntryBytes { get; init; } = 16L * 1024 * 1024 * 1024;

    /// <summary>Relação de expansão por entrada. Sinal adicional; só avaliado acima de <see cref="RatioCheckThresholdBytes"/>.</summary>
    public double MaxCompressionRatio { get; init; } = 1000;
    public long RatioCheckThresholdBytes { get; init; } = 64L * 1024 * 1024;

    public TimeSpan MaxDuration { get; init; } = TimeSpan.FromHours(12);

    public static ExtractionLimits Default { get; } = new();

    public void Validate()
    {
        if (MaxEntries <= 0 || MaxDepth <= 0 || MaxRelativePathLength <= 0 || MaxTotalBytes <= 0 || MaxEntryBytes <= 0 || MaxCompressionRatio <= 0 || MaxDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ExtractionLimits), "Todos os limites precisam ser finitos e positivos.");
    }
}
