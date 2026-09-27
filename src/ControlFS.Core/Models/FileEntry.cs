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
/// Item apresentado em uma lista. <see cref="Id"/> identifica o item de forma estável dentro
/// da localização (usado para foco e seleção); <see cref="FullPath"/> só existe para itens físicos.
/// <see cref="FoundIn"/> só existe em resultados de busca: a pasta onde o item está, a partir da pasta buscada.
/// <see cref="Drive"/> só existe em unidades.
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
    DriveKind? Drive = null)
{
    public bool IsBlocked => BlockedReason is not null;

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
