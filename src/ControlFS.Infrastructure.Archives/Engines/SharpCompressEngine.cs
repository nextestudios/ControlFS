using System.Reflection;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Policies;
using ControlFS.Infrastructure.Archives.Inspection;
using ControlFS.Infrastructure.Archives.Security;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace ControlFS.Infrastructure.Archives.Engines;

/// <summary>
/// ZIP, 7z, RAR e GZ por acesso aleatório (API Archive do SharpCompress). TAR e TAR.GZ ficam com <see cref="BclTarEngine"/>:
/// o leitor de TAR do SharpCompress não interpreta cabeçalhos PAX. Cada formato é validado por fixtures.
/// Compactados divididos em volumes abrem a partir de qualquer volume (<see cref="VolumeSet"/>); faltando algum, nada é lido.
/// </summary>
public sealed class SharpCompressEngine : IArchiveEngine
{
    // Tipos de arquivo Unix (modo).
    private const int UnixTypeMask = 0xF000;
    private const int UnixRegular = 0x8000;
    private const int UnixDirectory = 0x4000;
    private const int WindowsReparsePoint = 0x400;
    private const int WindowsDevice = 0x40;

    public string Name => "SharpCompress";

    public string Version { get; } = typeof(ArchiveFactory).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(ArchiveFactory).Assembly.GetName().Version?.ToString() ?? "?";

    public bool Supports(ArchiveFormat format) => format is ArchiveFormat.Zip or ArchiveFormat.SevenZip or ArchiveFormat.Rar or ArchiveFormat.GZip;

    public IArchiveReadSession Open(string archivePath, ArchiveFormat format, string? password, ExtractionLimits limits, CancellationToken cancellationToken)
    {
        if (!Supports(format)) throw new ArchiveAccessException(OperationErrorKind.UnsupportedFormat, "Formato não suportado por este motor.");
        var volumes = VolumeSet.Find(archivePath);
        if (volumes is { Missing.Count: > 0 }) throw MissingVolumes(volumes.Missing);
        try
        {
            return RandomAccessSession.Open(archivePath, volumes, format, password, limits, cancellationToken);
        }
        catch (Exception ex) when (ex is not ArchiveAccessException and not OperationCanceledException)
        {
            // Divisão simples de ZIP/RAR não diz quantos volumes são: sem o último, o motor só vê um arquivo truncado.
            if (volumes is { IsMultiPart: true } && Translate(ex, password).Kind is OperationErrorKind.Corrupt or OperationErrorKind.MissingVolume or OperationErrorKind.Unknown)
                throw new ArchiveAccessException(OperationErrorKind.MissingVolume,
                    $"Não foi possível ler o conjunto de {volumes.Parts.Count} volumes: pode faltar o volume seguinte a {Path.GetFileName(volumes.Parts[^1])}, ou algum está corrompido.", ex);
            // 7z com a lista criptografada não tem verificador de senha: a senha errada só aparece como uma lista ilegível
            // (CRC do cabeçalho, dados LZMA inválidos). Se sem senha o motor pede uma, o problema é a senha informada.
            if (password is not null && ex is not CryptographicException && HeadersNeedPassword(archivePath, volumes, format))
                throw new ArchiveAccessException(OperationErrorKind.WrongPassword, "Senha incorreta (ou lista de arquivos corrompida).", ex);
            throw Translate(ex, password);
        }
    }

    internal static ArchiveAccessException MissingVolumes(IReadOnlyList<string> missing)
    {
        const int Shown = 10;
        var names = string.Join(", ", missing.Take(Shown)) + (missing.Count > Shown ? $" e mais {missing.Count - Shown}" : string.Empty);
        return new ArchiveAccessException(OperationErrorKind.MissingVolume,
            missing.Count == 1 ? $"Falta um volume do compactado dividido: {names}." : $"Faltam {missing.Count} volumes do compactado dividido: {names}.");
    }

    /// <summary>A lista de arquivos (cabeçalhos) só pode ser lida com senha?</summary>
    private static bool HeadersNeedPassword(string archivePath, VolumeSet? volumes, ArchiveFormat format)
    {
        try
        {
            using var archive = OpenArchive(archivePath, volumes, format, new ReaderOptions { LookForHeader = false });
            _ = archive.Entries.FirstOrDefault();
            return false;
        }
        catch (CryptographicException)
        {
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    internal static ArchiveAccessException Translate(Exception ex, string? password)
    {
        if (ex is CryptographicException)
            return password is null
                ? new ArchiveAccessException(OperationErrorKind.PasswordRequired, "O arquivo está protegido por senha.", ex)
                : new ArchiveAccessException(OperationErrorKind.WrongPassword, "Senha incorreta.", ex);
        var (kind, message) = ErrorMapper.Map(ex);
        return new ArchiveAccessException(kind, message, ex);
    }

    /// <summary>
    /// Links e tipos especiais são bloqueados. A interpretação de <paramref name="attrib"/> depende do formato:
    /// ZIP e 7z guardam o modo Unix nos 16 bits altos; RAR e TAR guardam o modo Unix (ou atributos Windows) nos baixos.
    /// </summary>
    internal static bool IsLinkOrSpecial(ArchiveFormat format, int? attrib, string? linkTarget)
    {
        if (!string.IsNullOrEmpty(linkTarget)) return true;
        if (attrib is not int a) return false;
        if (format is ArchiveFormat.Zip or ArchiveFormat.SevenZip)
        {
            var unixMode = (a >> 16) & 0xFFFF;
            var unixType = unixMode & UnixTypeMask;
            if (unixMode != 0 && unixType != 0 && unixType != UnixRegular && unixType != UnixDirectory) return true;
            return (a & (WindowsReparsePoint | WindowsDevice)) != 0;
        }
        var mode = a & 0xFFFF;
        var type = mode & UnixTypeMask;
        if (type != 0) return type != UnixRegular && type != UnixDirectory;
        return (a & WindowsReparsePoint) != 0; // atributos Windows (sem bits de tipo Unix)
    }

    /// <summary>Algumas propriedades não existem em certos formatos (ex.: Attrib em TAR/GZ lança NotImplementedException).</summary>
    private static T? Optional<T>(Func<T> read) where T : struct
    {
        try { return read(); }
        catch (NotImplementedException) { return null; }
        catch (NotSupportedException) { return null; }
    }

    private static ArchiveEntry Map(int index, IEntry e, ArchiveFormat format, string archivePath)
    {
        var key = e.Key;
        var attrib = Optional(() => e.Attrib ?? -1) is int a && a != -1 ? a : (int?)null;
        var crc = Optional(() => e.Crc);
        var compressed = Optional(() => e.CompressedSize);
        var modified = Optional(() => e.LastModifiedTime ?? DateTime.MinValue) is DateTime t && t != DateTime.MinValue ? t : (DateTime?)null;
        var linkTarget = e.LinkTarget;
        if (string.IsNullOrEmpty(key) && format == ArchiveFormat.GZip) key = ArchiveFormats.StemOf(archivePath); // GZ sem nome no cabeçalho
        return new ArchiveEntry(
            Index: index,
            RawKey: key ?? string.Empty,
            IsDirectory: e.IsDirectory,
            // GZ guarda o tamanho módulo 2^32 no rodapé: não serve para verificação, então fica desconhecido.
            Size: format == ArchiveFormat.GZip || e.Size < 0 ? null : e.Size,
            CompressedSize: compressed is > 0 ? compressed : null,
            Modified: modified is DateTime m ? new DateTimeOffset(DateTime.SpecifyKind(m, DateTimeKind.Local)) : null,
            IsEncrypted: e.IsEncrypted,
            IsLinkOrSpecial: IsLinkOrSpecial(format, attrib, linkTarget),
            // TAR/GZ não têm CRC por entrada; RAR5 criptografado guarda o CRC transformado pela chave (não comparável).
            Crc32: e.IsDirectory || crc is null || format is ArchiveFormat.Tar or ArchiveFormat.TarGZip or ArchiveFormat.GZip
                || (format == ArchiveFormat.Rar && e.IsEncrypted) ? null : (uint)crc.Value);
    }

    /// <summary>Volumes vão para o leitor do formato já detectado, na ordem de <see cref="VolumeSet.Parts"/>.</summary>
    private static IArchive OpenArchive(string path, VolumeSet? volumes, ArchiveFormat format, ReaderOptions options)
    {
        if (volumes is not { Parts.Count: > 1 }) return ArchiveFactory.Open(volumes?.Parts[0] ?? path, options);
        if (format == ArchiveFormat.Zip && volumes.IsSpannedZip)
        {
            options.LeaveStreamOpen = false; // o fluxo juntado é nosso: fecha junto com o compactado
            return ZipArchive.Open(SpannedZip.Open(volumes.Parts), options);
        }
        var files = volumes.Parts.Select(p => new FileInfo(p)).ToList();
        return format switch
        {
            ArchiveFormat.Zip => ZipArchive.Open(files, options),
            ArchiveFormat.SevenZip => SevenZipArchive.Open(files, options),
            ArchiveFormat.Rar => RarArchive.Open(files, options),
            _ => throw new ArchiveAccessException(OperationErrorKind.UnsupportedFormat, $"{ArchiveFormats.DisplayName(format)} dividido em volumes não é suportado."),
        };
    }

    internal static ArchiveInfo BuildInfo(string path, ArchiveFormat format, List<ArchiveEntry> entries, bool solid, int volumeCount = 1)
    {
        var encrypted = entries.Any(e => e.IsEncrypted);
        var limitations = new List<string>();
        if (solid) limitations.Add("Arquivo sólido: cada entrada depende das anteriores; extrair uma seleção pode ser lento.");
        if (format == ArchiveFormat.GZip) limitations.Add("GZ não guarda CRC acessível por entrada; a integridade não é verificada.");
        var caps = new ArchiveCapabilities(
            CanList: true,
            CanExtractAll: true,
            CanExtractSelection: true,
            CanReadEncryptedPayload: format is ArchiveFormat.Zip or ArchiveFormat.SevenZip or ArchiveFormat.Rar,
            CanReadEncryptedHeaders: format is ArchiveFormat.SevenZip or ArchiveFormat.Rar,
            CanReadMultiVolume: format is ArchiveFormat.Zip or ArchiveFormat.SevenZip or ArchiveFormat.Rar,
            CanVerifyIntegrity: format is ArchiveFormat.Zip or ArchiveFormat.SevenZip or ArchiveFormat.Rar,
            CanCancelCooperatively: true,
            CanPauseInSession: false,
            CanResumeAfterRestart: false,
            CanCreate: false);
        if (volumeCount > 1) limitations.Add($"Compactado dividido em {volumeCount} volumes: todos são lidos juntos.");
        return new ArchiveInfo(path, format, entries, caps, encrypted, IsMultiVolume: volumeCount > 1, limitations);
    }

    private static ArchiveType ExpectedType(ArchiveFormat format) => format switch
    {
        ArchiveFormat.Zip => ArchiveType.Zip,
        ArchiveFormat.SevenZip => ArchiveType.SevenZip,
        ArchiveFormat.Rar => ArchiveType.Rar,
        ArchiveFormat.GZip => ArchiveType.GZip,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    internal static void CheckCount(int count, ExtractionLimits limits)
    {
        if (count >= limits.MaxEntries)
            throw new ArchiveAccessException(OperationErrorKind.LimitExceeded, $"O arquivo tem mais de {limits.MaxEntries} entradas (limite configurado).");
    }

    private sealed class RandomAccessSession(IArchive archive, List<IArchiveEntry> sources, ArchiveInfo info) : IArchiveReadSession
    {
        public ArchiveInfo Info { get; } = info;

        public static RandomAccessSession Open(string path, VolumeSet? volumes, ArchiveFormat format, string? password, ExtractionLimits limits, CancellationToken ct)
        {
            var archive = OpenArchive(path, volumes, format, new ReaderOptions { Password = password, LookForHeader = false });
            try
            {
                if (archive.Type != ExpectedType(format))
                    throw new ArchiveAccessException(OperationErrorKind.UnsupportedFormat, $"O conteúdo não corresponde ao formato {ArchiveFormats.DisplayName(format)}.");
                var sources = new List<IArchiveEntry>();
                var entries = new List<ArchiveEntry>();
                foreach (var e in archive.Entries)
                {
                    ct.ThrowIfCancellationRequested();
                    CheckCount(entries.Count, limits);
                    entries.Add(Map(entries.Count, e, format, path));
                    sources.Add(e);
                }
                // RAR: uma entrada que continua num volume ausente (ou começa num anterior) nunca vira extração "completa".
                if (volumes is { IsMultiPart: true } && sources.Any(s => !s.IsComplete))
                    throw new ArchiveAccessException(OperationErrorKind.MissingVolume,
                        $"O conjunto de volumes está incompleto: falta o volume seguinte a {Path.GetFileName(volumes.Parts[^1])}.");
                return new RandomAccessSession(archive, sources, BuildInfo(path, format, entries, archive.IsSolid, volumes?.Parts.Count ?? 1));
            }
            catch
            {
                archive.Dispose();
                throw;
            }
        }

        public IEnumerable<(ArchiveEntry Entry, Func<Stream> Open)> ReadFiles(IReadOnlySet<int> wanted, CancellationToken cancellationToken)
        {
            for (var i = 0; i < sources.Count; i++)
            {
                if (!wanted.Contains(i)) continue;
                cancellationToken.ThrowIfCancellationRequested();
                var source = sources[i];
                yield return (Info.Entries[i], () => source.OpenEntryStream());
            }
        }

        public void Dispose() => archive.Dispose();
    }
}
