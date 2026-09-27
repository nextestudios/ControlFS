using System.Globalization;
using ControlFS.Application.State;
using ControlFS.Core.Models;

namespace ControlFS.Application;

/// <summary>
/// Textos de um item usados na tela e lidos pelo Narrador (uma fonte só, para os dois dizerem o mesmo). Números no
/// formato da interface (pt-BR: "698,5 GB", "1.234 itens"), independente do idioma do Windows.
/// </summary>
public static class EntryText
{
    public static CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("pt-BR");

    public static string TypeName(FileEntry entry) => entry.Kind switch
    {
        EntryKind.Drive => entry.Drive switch
        {
            DriveKind.Removable => "Unidade removível (USB)",
            DriveKind.Optical => "Unidade óptica",
            DriveKind.Network => "Unidade de rede",
            _ => "Unidade local",
        },
        EntryKind.KnownFolder => "Pasta especial",
        EntryKind.Directory or EntryKind.ArchiveDirectory => "Pasta",
        _ => entry.Extension.Length > 1 ? "Arquivo " + entry.Extension[1..].ToUpperInvariant() : "Arquivo",
    };

    public static string Size(long bytes) => bytes switch
    {
        >= 1L << 40 => string.Create(Culture, $"{bytes / (double)(1L << 40):0.#} TB"),
        >= 1L << 30 => string.Create(Culture, $"{bytes / (double)(1L << 30):0.#} GB"),
        >= 1L << 20 => string.Create(Culture, $"{bytes / (double)(1L << 20):0.#} MB"),
        >= 1L << 10 => string.Create(Culture, $"{bytes / 1024.0:0.#} KB"),
        _ => $"{bytes} B",
    };

    /// <summary>"1 item", "1.234 itens".</summary>
    public static string Items(long count) => count == 1 ? "1 item" : string.Create(Culture, $"{count:N0} itens");

    /// <summary>
    /// Linha de conteúdo de uma pasta nos cartões: "124 itens • 5,2 GB"; "Calculando…" enquanto a soma roda; com "+"
    /// quando o cálculo parou no limite de tempo (valor real, mas mínimo).
    /// </summary>
    public static string FolderStats(FolderStats stats) => stats.State switch
    {
        FolderStatsState.Ready => $"{Items(stats.Items)} • {Size(stats.Bytes)}",
        FolderStatsState.Partial => $"{Items(stats.Items)}+ • {Size(stats.Bytes)}+",
        FolderStatsState.Unavailable => "Conteúdo indisponível",
        _ => "Calculando…",
    };

    /// <summary>Uso de uma unidade: "698,5 GB livres de 1,8 TB · NTFS" (sistema de arquivos quando o Windows informa).</summary>
    public static string DriveUsage(FileEntry drive)
    {
        if (drive.Volume is not { TotalBytes: > 0 } volume) return drive.Detail ?? TypeName(drive);
        var text = $"{Size(volume.FreeBytes)} livres de {Size(volume.TotalBytes)}";
        var kind = drive.Drive switch
        {
            DriveKind.Removable => "USB",
            DriveKind.Network => "Rede",
            DriveKind.Optical => "Óptica",
            _ => null,
        };
        return string.Join(" · ", new[] { kind, text, volume.FileSystem }.Where(p => p is { Length: > 0 }));
    }
}
