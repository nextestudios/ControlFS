using System.Globalization;
using ControlFS.Core.Models;

namespace ControlFS.Core.Text;

/// <summary>Textos de andamento (pt-BR), iguais no Menu → Operações, nos detalhes e na barra de status.</summary>
public static class ProgressText
{
    public static string Percent(double fraction) => Math.Floor(fraction * 100).ToString(CultureInfo.InvariantCulture) + "%";

    /// <summary>"cerca de 2 min restantes (estimativa)"; sempre rotulado como estimativa.</summary>
    public static string Remaining(TimeSpan remaining)
    {
        var seconds = Math.Max(1, (int)Math.Round(remaining.TotalSeconds));
        if (seconds < 10) return "menos de 10 s restantes (estimativa)";
        var span = seconds < 60 ? $"{(seconds + 5) / 10 * 10} s"
            : seconds < 90 * 60 ? $"{(int)Math.Round(seconds / 60.0)} min"
            : $"{seconds / 3600} h {(seconds % 3600 + 30) / 60} min";
        return $"cerca de {span} restantes (estimativa)";
    }

    public static string Speed(double bytesPerSecond) => FormatBytes((long)bytesPerSecond) + "/s";

    public static string Items(OperationProgress p) =>
        p.ItemsTotal is > 0 and var total
            ? $"{p.ItemsProcessed.ToString("N0", CultureInfo.CurrentCulture)} de {Plural.Of(total, "item", "itens")}"
            : Plural.Of(p.ItemsProcessed, "item processado", "itens processados");

    public static string Bytes(OperationProgress p) =>
        p.BytesTotal is > 0 and var total && !p.Indeterminate
            ? $"{FormatBytes(p.BytesProcessed)} de {FormatBytes(total)}"
            : FormatBytes(p.BytesProcessed);

    /// <summary>Uma linha: "45% · 3 de 10 itens · 12 MB de 80 MB · 25 MB/s · cerca de 2 min restantes (estimativa)".</summary>
    public static string Summary(OperationProgress p, ProgressSnapshot? snapshot)
    {
        var parts = new List<string>();
        if (snapshot?.Fraction is { } fraction) parts.Add(Percent(fraction));
        else if (p.Indeterminate || p.Fraction is null) parts.Add("sem total conhecido");
        parts.Add(Items(p));
        if (p.BytesProcessed > 0 || p.BytesTotal > 0) parts.Add(Bytes(p));
        if (snapshot?.Fraction is not null && snapshot.BytesPerSecond is { } speed) parts.Add(Speed(speed));
        if (snapshot?.Remaining is { } remaining) parts.Add(Remaining(remaining));
        return string.Join(" · ", parts);
    }

    public static string FormatBytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.##} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.##} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0.##} KB",
        _ => $"{bytes} B",
    };
}
