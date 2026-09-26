using System.Reflection;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Policies;
using ControlFS.Infrastructure.Archives.Security;
using SharpCompress.Archives.Zip;
using SharpCompress.Readers;

namespace ControlFS.Infrastructure.Archives.Engines;

/// <summary>
/// ZIP via SharpCompress (acesso aleatório pelo diretório central). Validado com fixtures:
/// ZIP armazenado/deflate, ZipCrypto correta/incorreta. AES e ZIP64 ainda não validados (ver docs/archive-support.md).
/// </summary>
public sealed class SharpCompressZipEngine : IArchiveEngine
{
    // Tipos de arquivo Unix (bits altos de "external attributes" quando o criador é Unix).
    private const int UnixTypeMask = 0xF000;
    private const int UnixRegular = 0x8000;
    private const int UnixDirectory = 0x4000;
    private const int WindowsReparsePoint = 0x400;
    private const int WindowsDevice = 0x40;

    public string Name => "SharpCompress";

    public string Version { get; } = typeof(ZipArchive).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(ZipArchive).Assembly.GetName().Version?.ToString() ?? "?";

    public bool Supports(ArchiveFormat format) => format == ArchiveFormat.Zip;

    public IArchiveReadSession Open(string archivePath, ArchiveFormat format, string? password, ExtractionLimits limits, CancellationToken cancellationToken)
    {
        if (format != ArchiveFormat.Zip) throw new ArchiveAccessException(OperationErrorKind.UnsupportedFormat, "Formato não suportado por este motor.");
        ZipArchive? archive = null;
        try
        {
            archive = ZipArchive.Open(archivePath, new ReaderOptions { Password = password, LookForHeader = false });
            var sourceEntries = new List<ZipArchiveEntry>();
            var entries = new List<ArchiveEntry>();
            var anyEncrypted = false;
            foreach (var e in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entries.Count >= limits.MaxEntries)
                    throw new ArchiveAccessException(OperationErrorKind.LimitExceeded, $"O arquivo tem mais de {limits.MaxEntries} entradas (limite configurado).");
                anyEncrypted |= e.IsEncrypted;
                entries.Add(new ArchiveEntry(
                    Index: entries.Count,
                    RawKey: e.Key ?? string.Empty,
                    IsDirectory: e.IsDirectory,
                    Size: e.Size >= 0 ? e.Size : null,
                    CompressedSize: e.CompressedSize >= 0 ? e.CompressedSize : null,
                    Modified: e.LastModifiedTime is DateTime m ? new DateTimeOffset(DateTime.SpecifyKind(m, DateTimeKind.Local)) : null,
                    IsEncrypted: e.IsEncrypted,
                    IsLinkOrSpecial: IsLinkOrSpecial(e.Attrib, e.LinkTarget),
                    Crc32: e.IsDirectory ? null : (uint)e.Crc));
                sourceEntries.Add(e);
            }

            var limitations = new List<string>
            {
                "ZIP multivolume não validado.",
            };
            if (anyEncrypted) limitations.Add("Criptografia: ZipCrypto validada; AES ainda não validada nesta versão.");

            var caps = new ArchiveCapabilities(
                CanList: true,
                CanExtractAll: true,
                CanExtractSelection: true,
                CanReadEncryptedPayload: true,
                CanReadEncryptedHeaders: false,
                CanReadMultiVolume: false,
                CanVerifyIntegrity: true,
                CanCancelCooperatively: true,
                CanPauseInSession: false,
                CanResumeAfterRestart: false,
                CanCreate: false);
            var info = new ArchiveInfo(archivePath, ArchiveFormat.Zip, entries, caps, anyEncrypted, IsMultiVolume: false, limitations);
            var session = new Session(archive, sourceEntries, info);
            archive = null;
            return session;
        }
        catch (Exception ex) when (ex is not ArchiveAccessException and not OperationCanceledException)
        {
            var (kind, message) = ErrorMapper.Map(ex);
            throw new ArchiveAccessException(kind, message, ex);
        }
        finally
        {
            archive?.Dispose();
        }
    }

    /// <summary>
    /// SharpCompress não expõe LinkTarget para ZIP; symlinks aparecem apenas no modo Unix
    /// (bits altos de Attrib). Qualquer tipo que não seja arquivo regular ou diretório é bloqueado.
    /// </summary>
    internal static bool IsLinkOrSpecial(int? attrib, string? linkTarget)
    {
        if (!string.IsNullOrEmpty(linkTarget)) return true;
        if (attrib is not int a) return false;
        var unixMode = (a >> 16) & 0xFFFF;
        var unixType = unixMode & UnixTypeMask;
        if (unixMode != 0 && unixType != 0 && unixType != UnixRegular && unixType != UnixDirectory) return true;
        return (a & (WindowsReparsePoint | WindowsDevice)) != 0;
    }

    private sealed class Session(ZipArchive archive, List<ZipArchiveEntry> sourceEntries, ArchiveInfo info) : IArchiveReadSession
    {
        public ArchiveInfo Info { get; } = info;

        public Stream OpenEntry(int index) => sourceEntries[index].OpenEntryStream();

        public void Dispose() => archive.Dispose();
    }
}
