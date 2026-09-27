using System.Text.RegularExpressions;

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

public static partial class ArchiveFormats
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
    /// Indício rápido (só pelo nome) de um compactado que o extrator abre, incluindo volumes de um compactado dividido
    /// ("x.7z.001", "x.part2.rar", "x.z01", "x.r00"). Serve para legendas; a ação real sempre detecta o formato pelo conteúdo.
    /// </summary>
    public static bool HasExtractableExtension(string name)
    {
        foreach (var ext in new[] { ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz" })
            if (name.Length > ext.Length && name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return true;
        if (SpannedSuffix().IsMatch(name)) return true;
        var numbered = NumberedSuffix().Match(name);
        return numbered.Success && HasExtractableExtension(numbered.Groups[1].Value);
    }

    /// <summary>
    /// Nome sem as extensões de compactado nem o número do volume: "fotos.tar.gz" → "fotos", "dados.tgz" → "dados",
    /// "filme.7z.001" → "filme", "jogo.part01.rar" → "jogo", "backup.z01" → "backup".
    /// </summary>
    public static string StemOf(string path)
    {
        var name = Path.GetFileName(path);
        if (RarPartSuffix().Match(name) is { Success: true } part) return part.Groups[1].Value;
        if (SpannedSuffix().Match(name) is { Success: true } spanned) return spanned.Groups[1].Value;
        if (NumberedSuffix().Match(name) is { Success: true } numbered) return StemOf(numbered.Groups[1].Value);
        foreach (var ext in new[] { ".tar.gz", ".tar.bz2", ".tar.xz", ".tgz", ".zip", ".7z", ".rar", ".tar", ".gz" })
            if (name.Length > ext.Length && name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return name[..^ext.Length];
        return Path.GetFileNameWithoutExtension(name);
    }

    /// <summary>
    /// Chave que junta os volumes de um mesmo compactado dividido (pasta + nome base), para tratá-los como um só;
    /// null quando o nome não pode ser de um volume.
    /// </summary>
    public static string? VolumeSetKey(string path)
    {
        var name = Path.GetFileName(path);
        var dir = Path.GetDirectoryName(path) ?? string.Empty;
        string? key = RarPartSuffix().Match(name) is { Success: true } part ? part.Groups[1].Value + "|part"
            : SpannedSuffix().Match(name) is { Success: true } spanned ? spanned.Groups[1].Value + (spanned.Groups[2].Value.Equals("z", StringComparison.OrdinalIgnoreCase) ? "|zip" : "|rar")
            : NumberedSuffix().Match(name) is { Success: true } numbered ? numbered.Groups[1].Value + "|n"
            // O ".zip" é o último volume de um ZIP dividido e o ".rar" o primeiro de um RAR antigo (".r00"…).
            : name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? name[..^4] + "|zip"
            : name.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) ? name[..^4] + "|rar"
            : null;
        return key is null ? null : Path.Join(dir, key).ToUpperInvariant();
    }

    // "x.part1.rar"
    [GeneratedRegex(@"^(.+)\.part\d{1,4}\.rar$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RarPartSuffix();

    // "x.z01" (ZIP dividido) e "x.r00"…"x.s99" (RAR antigo)
    [GeneratedRegex(@"^(.+)\.([rsz])\d{2,3}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpannedSuffix();

    // "x.7z.001"
    [GeneratedRegex(@"^(.+)\.\d{3,4}$", RegexOptions.CultureInvariant)]
    private static partial Regex NumberedSuffix();
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
