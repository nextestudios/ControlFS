namespace ControlFS.Core.Models;

public enum ArchiveFormat
{
    Unknown,
    Zip,
    SevenZip,
    Rar,
    Tar,
    GZip,
}

/// <summary>
/// Entrada de um compactado como relatada pelo motor. Todos os campos vêm de metadados
/// NÃO confiáveis; <see cref="RawKey"/> nunca deve ser usado como caminho de disco.
/// </summary>
public sealed record ArchiveEntry(
    int Index,
    string RawKey,
    bool IsDirectory,
    long? Size,
    long? CompressedSize,
    DateTimeOffset? Modified,
    bool IsEncrypted,
    bool IsLinkOrSpecial,
    uint? Crc32);

/// <summary>
/// Capacidades de um arquivo compactado específico, após inspeção. Não são constantes globais
/// do formato: a inspeção pode restringi-las.
/// </summary>
public sealed record ArchiveCapabilities(
    bool CanList,
    bool CanExtractAll,
    bool CanExtractSelection,
    bool CanReadEncryptedPayload,
    bool CanReadEncryptedHeaders,
    bool CanReadMultiVolume,
    bool CanVerifyIntegrity,
    bool CanCancelCooperatively,
    bool CanPauseInSession,
    bool CanResumeAfterRestart,
    bool CanCreate)
{
    public static ArchiveCapabilities None { get; } = new(false, false, false, false, false, false, false, false, false, false, false);
}

public sealed record ArchiveInfo(
    string ArchivePath,
    ArchiveFormat Format,
    IReadOnlyList<ArchiveEntry> Entries,
    ArchiveCapabilities Capabilities,
    bool HasEncryptedEntries,
    bool IsMultiVolume,
    IReadOnlyList<string> Limitations)
{
    /// <summary>Soma dos tamanhos declarados, ou null se algum for desconhecido.</summary>
    public long? DeclaredTotalSize
    {
        get
        {
            long total = 0;
            foreach (var e in Entries)
            {
                if (e.IsDirectory) continue;
                if (e.Size is not long s) return null;
                total += s;
            }
            return total;
        }
    }
}
