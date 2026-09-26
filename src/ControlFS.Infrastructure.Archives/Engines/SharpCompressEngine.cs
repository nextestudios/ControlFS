using System.Reflection;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Policies;
using ControlFS.Infrastructure.Archives.Security;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace ControlFS.Infrastructure.Archives.Engines;

/// <summary>
/// ZIP, 7z, RAR, TAR e GZ por acesso aleatório (API Archive do SharpCompress) e TAR.GZ por leitura sequencial (API
/// Reader) — a API Archive trata .tar.gz como um GZ com um único .tar dentro. Cada formato é validado por fixtures.
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

    public bool Supports(ArchiveFormat format) => ArchiveFormats.CanExtract(format);

    public IArchiveReadSession Open(string archivePath, ArchiveFormat format, string? password, ExtractionLimits limits, CancellationToken cancellationToken)
    {
        if (!Supports(format)) throw new ArchiveAccessException(OperationErrorKind.UnsupportedFormat, "Formato não suportado por este motor.");
        try
        {
            return format == ArchiveFormat.TarGZip
                ? SequentialSession.Open(archivePath, format, password, limits, cancellationToken)
                : RandomAccessSession.Open(archivePath, format, password, limits, cancellationToken);
        }
        catch (Exception ex) when (ex is not ArchiveAccessException and not OperationCanceledException)
        {
            throw Translate(ex, password);
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

    private static ArchiveEntry Map(int index, IEntry e, ArchiveFormat format, string archivePath)
    {
        var key = e.Key;
        if (string.IsNullOrEmpty(key) && format == ArchiveFormat.GZip) key = ArchiveFormats.StemOf(archivePath); // GZ sem nome no cabeçalho
        return new ArchiveEntry(
            Index: index,
            RawKey: key ?? string.Empty,
            IsDirectory: e.IsDirectory,
            // GZ guarda o tamanho módulo 2^32 no rodapé: não serve para verificação, então fica desconhecido.
            Size: format == ArchiveFormat.GZip || e.Size < 0 ? null : e.Size,
            CompressedSize: e.CompressedSize > 0 ? e.CompressedSize : null,
            Modified: e.LastModifiedTime is DateTime m ? new DateTimeOffset(DateTime.SpecifyKind(m, DateTimeKind.Local)) : null,
            IsEncrypted: e.IsEncrypted,
            IsLinkOrSpecial: IsLinkOrSpecial(format, e.Attrib, e.LinkTarget),
            Crc32: e.IsDirectory || format is ArchiveFormat.Tar or ArchiveFormat.TarGZip or ArchiveFormat.GZip ? null : (uint)e.Crc);
    }

    private static ArchiveInfo BuildInfo(string path, ArchiveFormat format, List<ArchiveEntry> entries, bool solid)
    {
        var encrypted = entries.Any(e => e.IsEncrypted);
        var limitations = new List<string> { "Arquivos divididos em volumes ainda não são suportados." };
        if (solid) limitations.Add("Arquivo sólido: cada entrada depende das anteriores; extrair uma seleção pode ser lento.");
        if (format == ArchiveFormat.TarGZip) limitations.Add("TAR.GZ é lido em sequência: listar exige descomprimir o arquivo inteiro.");
        if (format is ArchiveFormat.Tar or ArchiveFormat.TarGZip or ArchiveFormat.GZip) limitations.Add("Este formato não guarda CRC por entrada; a integridade não é verificada.");
        var caps = new ArchiveCapabilities(
            CanList: true,
            CanExtractAll: true,
            CanExtractSelection: true,
            CanReadEncryptedPayload: format is ArchiveFormat.Zip or ArchiveFormat.SevenZip or ArchiveFormat.Rar,
            CanReadEncryptedHeaders: false,
            CanReadMultiVolume: false,
            CanVerifyIntegrity: format is ArchiveFormat.Zip or ArchiveFormat.SevenZip or ArchiveFormat.Rar,
            CanCancelCooperatively: true,
            CanPauseInSession: false,
            CanResumeAfterRestart: false,
            CanCreate: false);
        return new ArchiveInfo(path, format, entries, caps, encrypted, IsMultiVolume: false, limitations);
    }

    private static ArchiveType ExpectedType(ArchiveFormat format) => format switch
    {
        ArchiveFormat.Zip => ArchiveType.Zip,
        ArchiveFormat.SevenZip => ArchiveType.SevenZip,
        ArchiveFormat.Rar => ArchiveType.Rar,
        ArchiveFormat.GZip => ArchiveType.GZip,
        _ => ArchiveType.Tar,
    };

    private static void CheckCount(int count, ExtractionLimits limits)
    {
        if (count >= limits.MaxEntries)
            throw new ArchiveAccessException(OperationErrorKind.LimitExceeded, $"O arquivo tem mais de {limits.MaxEntries} entradas (limite configurado).");
    }

    private sealed class RandomAccessSession(IArchive archive, List<IArchiveEntry> sources, ArchiveInfo info) : IArchiveReadSession
    {
        public ArchiveInfo Info { get; } = info;

        public static RandomAccessSession Open(string path, ArchiveFormat format, string? password, ExtractionLimits limits, CancellationToken ct)
        {
            var archive = ArchiveFactory.Open(path, new ReaderOptions { Password = password, LookForHeader = false });
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
                return new RandomAccessSession(archive, sources, BuildInfo(path, format, entries, archive.IsSolid));
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

    private sealed class SequentialSession(string path, string? password, ArchiveInfo info) : IArchiveReadSession
    {
        public ArchiveInfo Info { get; } = info;

        public static SequentialSession Open(string path, ArchiveFormat format, string? password, ExtractionLimits limits, CancellationToken ct)
        {
            var entries = new List<ArchiveEntry>();
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = ReaderFactory.Open(stream, new ReaderOptions { Password = password }))
            {
                if (reader.ArchiveType != ArchiveType.Tar)
                    throw new ArchiveAccessException(OperationErrorKind.UnsupportedFormat, "O conteúdo comprimido não é um TAR.");
                while (reader.MoveToNextEntry())
                {
                    ct.ThrowIfCancellationRequested();
                    CheckCount(entries.Count, limits);
                    entries.Add(Map(entries.Count, reader.Entry, format, path));
                }
            }
            return new SequentialSession(path, password, BuildInfo(path, format, entries, solid: true));
        }

        public IEnumerable<(ArchiveEntry Entry, Func<Stream> Open)> ReadFiles(IReadOnlySet<int> wanted, CancellationToken cancellationToken)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = ReaderFactory.Open(stream, new ReaderOptions { Password = password });
            var index = 0;
            while (reader.MoveToNextEntry())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (index >= Info.Entries.Count || !string.Equals(reader.Entry.Key ?? string.Empty, Info.Entries[index].RawKey, StringComparison.Ordinal))
                    throw new ArchiveAccessException(OperationErrorKind.Corrupt, "O conteúdo do arquivo mudou desde a listagem.");
                if (wanted.Contains(index)) yield return (Info.Entries[index], () => reader.OpenEntryStream());
                index++;
            }
        }

        public void Dispose()
        {
        }
    }
}
