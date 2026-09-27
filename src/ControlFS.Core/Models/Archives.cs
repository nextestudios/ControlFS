namespace ControlFS.Core.Models;

public enum ArchiveFormat
{
    Unknown,
    Zip,
    SevenZip,
    Rar,
    Tar,
    /// <summary>TAR comprimido com GZip (.tar.gz/.tgz): lido em sequência.</summary>
    TarGZip,
    /// <summary>Um único arquivo comprimido com GZip (não é pasta).</summary>
    GZip,
}

public static class ArchiveFormats
{
    /// <summary>Formatos que o extrator abre e extrai nesta versão (cada um coberto por testes com fixtures).</summary>
    public static bool CanExtract(ArchiveFormat format) => format is ArchiveFormat.Zip or ArchiveFormat.SevenZip or
        ArchiveFormat.Rar or ArchiveFormat.Tar or ArchiveFormat.TarGZip or ArchiveFormat.GZip;

    public static string DisplayName(ArchiveFormat format) => format switch
    {
        ArchiveFormat.Zip => "ZIP",
        ArchiveFormat.SevenZip => "7z",
        ArchiveFormat.Rar => "RAR",
        ArchiveFormat.Tar => "TAR",
        ArchiveFormat.TarGZip => "TAR.GZ",
        ArchiveFormat.GZip => "GZ",
        _ => "desconhecido",
    };

    /// <summary>
    /// Indício rápido (só pelo nome) de um compactado que o extrator abre. Serve para legendas; a ação real sempre
    /// detecta o formato pelo conteúdo.
    /// </summary>
    public static bool HasExtractableExtension(string name)
    {
        foreach (var ext in new[] { ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz" })
            if (name.Length > ext.Length && name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>Nome sem as extensões de compactado: "fotos.tar.gz" → "fotos", "dados.tgz" → "dados".</summary>
    public static string StemOf(string path)
    {
        var name = Path.GetFileName(path);
        foreach (var ext in new[] { ".tar.gz", ".tar.bz2", ".tar.xz", ".tgz", ".zip", ".7z", ".rar", ".tar", ".gz" })
            if (name.Length > ext.Length && name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return name[..^ext.Length];
        return Path.GetFileNameWithoutExtension(name);
    }
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
