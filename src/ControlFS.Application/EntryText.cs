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

    /// <summary>
    /// Nome mostrado na lista, na grade e dito pelo Narrador: o nome do arquivo, exceto nos atalhos de jogos da Steam, que
    /// aparecem pelo título ("Valheim" em vez de "Valheim.url"). Detalhes e Propriedades mostram o nome real.
    /// </summary>
    public static string DisplayName(FileEntry entry) =>
        entry.IsSteamGame && Path.GetFileNameWithoutExtension(entry.Name) is { Length: > 0 } title ? title : entry.Name;

    public static string TypeName(FileEntry entry) => entry.Kind switch
    {
        EntryKind.File when entry.IsSteamGame => "Jogo da Steam",
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

    /// <summary>
    /// Coluna "Tamanho" de uma pasta com soma recursiva (pastas principais do início): "5,2 GB", "5,2 GB+" (parcial),
    /// "Calculando…" ou vazio quando o conteúdo não pôde ser lido.
    /// </summary>
    public static string FolderSize(FolderStats stats) => stats.State switch
    {
        FolderStatsState.Ready => Size(stats.Bytes),
        FolderStatsState.Partial => Size(stats.Bytes) + "+",
        FolderStatsState.Unavailable => string.Empty,
        _ => "Calculando…",
    };

    /// <summary>
    /// Data amigável da lista e do painel de detalhes, na hora local: "Hoje, 14:32", "Ontem, 18:05"; senão
    /// "25/09/2026, 20:11" (também para datas depois de hoje, como as de um relógio adiantado).
    /// </summary>
    public static string FriendlyDate(DateTimeOffset value, DateTime now)
    {
        var local = value.ToLocalTime().DateTime;
        var time = local.ToString("HH:mm", Culture);
        if (local.Date == now.Date) return "Hoje, " + time;
        if (local.Date == now.Date.AddDays(-1)) return "Ontem, " + time;
        return local.ToString("dd/MM/yyyy", Culture) + ", " + time;
    }

    /// <summary>Data amigável em relação ao relógio do computador.</summary>
    public static string FriendlyDate(DateTimeOffset value) => FriendlyDate(value, DateTime.Now);

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
