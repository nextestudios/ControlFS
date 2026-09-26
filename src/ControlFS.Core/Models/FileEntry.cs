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

/// <summary>
/// Item apresentado em uma lista. <see cref="Id"/> identifica o item de forma estável dentro
/// da localização (usado para foco e seleção); <see cref="FullPath"/> só existe para itens físicos.
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
    string? BlockedReason = null)
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
