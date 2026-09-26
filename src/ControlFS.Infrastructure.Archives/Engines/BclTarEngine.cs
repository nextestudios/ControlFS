using System.Formats.Tar;
using System.IO.Compression;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Policies;
using ControlFS.Infrastructure.Archives.Security;

namespace ControlFS.Infrastructure.Archives.Engines;

/// <summary>
/// TAR e TAR.GZ com o leitor da biblioteca do .NET (System.Formats.Tar): entende PAX, GNU (nomes longos) e ustar,
/// e informa o tipo real de cada entrada. Leitura sempre sequencial: listar percorre o arquivo inteiro.
/// Links, dispositivos e FIFOs são bloqueados pelo tipo da entrada.
/// </summary>
public sealed class BclTarEngine : IArchiveEngine
{
    public string Name => "System.Formats.Tar";

    public string Version => Environment.Version.ToString();

    public bool Supports(ArchiveFormat format) => format is ArchiveFormat.Tar or ArchiveFormat.TarGZip;

    public IArchiveReadSession Open(string archivePath, ArchiveFormat format, string? password, ExtractionLimits limits, CancellationToken cancellationToken)
    {
        if (!Supports(format)) throw new ArchiveAccessException(OperationErrorKind.UnsupportedFormat, "Formato não suportado por este motor.");
        try
        {
            var entries = new List<ArchiveEntry>();
            foreach (var (entry, _) in Read(archivePath, format, cancellationToken))
            {
                SharpCompressEngine.CheckCount(entries.Count, limits);
                entries.Add(entry with { Index = entries.Count });
            }
            var info = SharpCompressEngine.BuildInfo(archivePath, format, entries, solid: format == ArchiveFormat.TarGZip);
            info = info with
            {
                Limitations = [.. info.Limitations, "TAR não guarda CRC por entrada; a integridade não é verificada.",
                    .. format == ArchiveFormat.TarGZip ? new[] { "TAR.GZ é lido em sequência: listar exige descomprimir o arquivo inteiro." } : []],
            };
            return new Session(archivePath, format, info);
        }
        catch (Exception ex) when (ex is not ArchiveAccessException and not OperationCanceledException)
        {
            var (kind, message) = ErrorMapper.Map(ex);
            throw new ArchiveAccessException(kind is OperationErrorKind.Unknown ? OperationErrorKind.Corrupt : kind,
                kind is OperationErrorKind.Unknown ? "Arquivo TAR corrompido ou em formato não reconhecido." : message, ex);
        }
    }

    /// <summary>Percorre o TAR entregando cada entrada (índice relativo à ordem) e o leitor posicionado nela.</summary>
    private static IEnumerable<(ArchiveEntry Entry, TarEntry Source)> Read(string path, ArchiveFormat format, CancellationToken ct)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920);
        using Stream stream = format == ArchiveFormat.TarGZip ? new GZipStream(file, CompressionMode.Decompress) : file;
        using var reader = new TarReader(stream, leaveOpen: true);
        var index = 0;
        TarEntry? entry;
        while ((entry = reader.GetNextEntry(copyData: false)) is not null)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.EntryType is TarEntryType.GlobalExtendedAttributes) continue; // metadados PAX globais, não é arquivo
            var isDirectory = entry.EntryType == TarEntryType.Directory;
            var isRegular = entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.ContiguousFile;
            var mapped = new ArchiveEntry(
                Index: index++,
                RawKey: entry.Name,
                IsDirectory: isDirectory,
                Size: isRegular ? entry.Length : null,
                CompressedSize: null,
                Modified: entry.ModificationTime,
                IsEncrypted: false,
                IsLinkOrSpecial: !isDirectory && !isRegular,
                Crc32: null);
            yield return (mapped, entry);
        }
    }

    private sealed class Session(string path, ArchiveFormat format, ArchiveInfo info) : IArchiveReadSession
    {
        public ArchiveInfo Info { get; } = info;

        public IEnumerable<(ArchiveEntry Entry, Func<Stream> Open)> ReadFiles(IReadOnlySet<int> wanted, CancellationToken cancellationToken)
        {
            foreach (var (entry, source) in Read(path, format, cancellationToken))
            {
                if (entry.Index >= Info.Entries.Count || !string.Equals(entry.RawKey, Info.Entries[entry.Index].RawKey, StringComparison.Ordinal))
                    throw new ArchiveAccessException(OperationErrorKind.Corrupt, "O conteúdo do arquivo mudou desde a listagem.");
                if (!wanted.Contains(entry.Index)) continue;
                yield return (Info.Entries[entry.Index], () => source.DataStream ?? Stream.Null);
            }
        }

        public void Dispose()
        {
        }
    }
}
