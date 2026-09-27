using ControlFS.Core.Models;

namespace ControlFS.Application;

/// <summary>Textos de um item usados na tela e lidos pelo Narrador (uma fonte só, para os dois dizerem o mesmo).</summary>
public static class EntryText
{
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
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.#} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.#} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes} B",
    };
}
