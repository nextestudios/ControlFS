namespace ControlFS.Core.Models;

public enum EntryKind
{
    Drive,
    KnownFolder,
    Directory,
    File,
    ArchiveDirectory,
    ArchiveFile,
}

/// <summary>Tipo de unidade (só em <see cref="EntryKind.Drive"/>): decide o símbolo e o texto que distinguem um pendrive do disco do sistema.</summary>
public enum DriveKind
{
    Fixed,
    Removable,
    Optical,
    Network,
}

/// <summary>
/// Espaço de uma unidade (só em <see cref="EntryKind.Drive"/>): capacidade e livre em bytes e o sistema de arquivos
/// (NTFS, exFAT…) quando o Windows informa. Alimenta a barra de uso dos cartões de unidade.
/// </summary>
public sealed record VolumeInfo(long TotalBytes, long FreeBytes, string? FileSystem)
{
    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);

    /// <summary>Fração usada (0 a 1); 0 quando a capacidade é desconhecida.</summary>
    public double UsedFraction => TotalBytes > 0 ? Math.Clamp(UsedBytes / (double)TotalBytes, 0, 1) : 0;
}

/// <summary>
/// Item apresentado em uma lista. <see cref="Id"/> identifica o item de forma estável dentro
/// da localização (usado para foco e seleção); <see cref="FullPath"/> só existe para itens físicos.
/// <see cref="FoundIn"/> só existe em resultados de busca: a pasta onde o item está, a partir da pasta buscada.
/// <see cref="Drive"/> e <see cref="Volume"/> só existem em unidades. <see cref="Shortcut"/> só existe em atalhos .url
/// locais que puderam ser lidos (conteúdo não confiável: só para exibir).
/// </summary>
public sealed record FileEntry(
    string Id,
    string Name,
    EntryKind Kind,
    long? Size = null,
    DateTimeOffset? Modified = null,
    string? FullPath = null,
    bool IsHidden = false,
    bool IsSystem = false,
    bool IsReadOnly = false,
    bool IsReparsePoint = false,
    bool IsEncrypted = false,
    string? Detail = null,
    string? BlockedReason = null,
    string? FoundIn = null,
    DriveKind? Drive = null,
    VolumeInfo? Volume = null,
    InternetShortcut? Shortcut = null)
{
    public bool IsBlocked => BlockedReason is not null;

    /// <summary>Atalho da Internet que abre um jogo da Steam (<c>steam://</c>).</summary>
    public bool IsSteamGame => Kind == EntryKind.File && Shortcut is { IsSteamGame: true };

    public bool IsContainer => Kind is EntryKind.Drive or EntryKind.KnownFolder or EntryKind.Directory or EntryKind.ArchiveDirectory;

    public string Extension
    {
        get
        {
            if (IsContainer) return string.Empty;
            var dot = Name.LastIndexOf('.');
            return dot <= 0 ? string.Empty : Name[dot..];
        }
    }
}
