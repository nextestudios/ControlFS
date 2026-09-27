using System.Buffers.Binary;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;

namespace ControlFS.Infrastructure.Archives.Inspection;

/// <summary>
/// ZIP dividido (".z01"… + ".zip") visto como um ZIP comum, como o "zip -s 0" faria, sem gravar nada: os volumes são
/// lidos em sequência e só o diretório central (no último volume) é reescrito em memória, trocando "disco + deslocamento
/// no disco" pelo deslocamento na sequência. O leitor de volumes do SharpCompress 1.0.0 falha em entradas que atravessam
/// volumes; assim o ZIP é lido pelo caminho de arquivo único, já validado.
/// </summary>
internal static class SpannedZip
{
    private const uint CentralSignature = 0x02014B50;
    private const uint EndSignature = 0x06054B50;

    /// <param name="parts">Volumes na ordem do <see cref="VolumeSet"/>: ".zip" (o último disco) seguido de ".z01", ".z02"…</param>
    public static Stream Open(IReadOnlyList<string> parts)
    {
        var disks = parts.Skip(1).Append(parts[0]).ToList(); // ordem dos discos: .z01, .z02, …, .zip
        var starts = new long[disks.Count];
        long total = 0;
        for (var i = 0; i < disks.Count; i++)
        {
            starts[i] = total;
            total += new FileInfo(disks[i]).Length;
        }

        var last = File.ReadAllBytes(disks[^1]) is { Length: <= 64 * 1024 * 1024 } bytes
            ? bytes
            : throw Unsupported("o último volume é grande demais para conter só o diretório central.");
        var end = FindEnd(last) ?? throw Unsupported("registro final não encontrado.");
        var diskCount = BinaryPrimitives.ReadUInt16LittleEndian(last.AsSpan(end + 4)) + 1;
        var centralDisk = BinaryPrimitives.ReadUInt16LittleEndian(last.AsSpan(end + 6));
        var totalEntries = BinaryPrimitives.ReadUInt16LittleEndian(last.AsSpan(end + 10));
        var centralSize = BinaryPrimitives.ReadUInt32LittleEndian(last.AsSpan(end + 12));
        var centralOffset = BinaryPrimitives.ReadUInt32LittleEndian(last.AsSpan(end + 16));
        if (diskCount != disks.Count) throw Unsupported($"o registro final fala em {diskCount} volumes e há {disks.Count}.");
        if (centralDisk != disks.Count - 1 || centralOffset + (long)centralSize > end || totalEntries == ushort.MaxValue || centralOffset == uint.MaxValue)
            throw Unsupported("diretório central fora do último volume ou ZIP64.");

        // Diretório central reescrito: disco 0 e deslocamentos absolutos na sequência.
        var central = last.AsSpan((int)centralOffset, (int)centralSize).ToArray();
        for (var p = 0; p < central.Length;)
        {
            if (p + 46 > central.Length || BinaryPrimitives.ReadUInt32LittleEndian(central.AsSpan(p)) != CentralSignature)
                throw Unsupported("diretório central inválido.");
            var disk = BinaryPrimitives.ReadUInt16LittleEndian(central.AsSpan(p + 34));
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(central.AsSpan(p + 42));
            if (disk >= disks.Count || offset == uint.MaxValue) throw Unsupported("entrada em volume inexistente ou ZIP64.");
            var absolute = starts[disk] + offset;
            if (absolute > uint.MaxValue) throw Unsupported("conjunto maior que 4 GiB (exigiria ZIP64).");
            BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(p + 34), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(central.AsSpan(p + 42), (uint)absolute);
            p += 46 + BinaryPrimitives.ReadUInt16LittleEndian(central.AsSpan(p + 28)) + BinaryPrimitives.ReadUInt16LittleEndian(central.AsSpan(p + 30))
                + BinaryPrimitives.ReadUInt16LittleEndian(central.AsSpan(p + 32));
        }
        var newCentralOffset = starts[^1] + centralOffset;
        if (newCentralOffset > uint.MaxValue) throw Unsupported("conjunto maior que 4 GiB (exigiria ZIP64).");
        var tail = new byte[central.Length + 22];
        central.CopyTo(tail, 0);
        var e = tail.AsSpan(central.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(e, EndSignature);
        BinaryPrimitives.WriteUInt16LittleEndian(e[8..], totalEntries);
        BinaryPrimitives.WriteUInt16LittleEndian(e[10..], totalEntries);
        BinaryPrimitives.WriteUInt32LittleEndian(e[12..], centralSize);
        BinaryPrimitives.WriteUInt32LittleEndian(e[16..], (uint)newCentralOffset);

        var segments = new List<Segment>();
        for (var i = 0; i < disks.Count - 1; i++) segments.Add(new Segment(disks[i], null, new FileInfo(disks[i]).Length));
        segments.Add(new Segment(null, last.AsMemory(0, (int)centralOffset), centralOffset));
        segments.Add(new Segment(null, tail, tail.Length));
        return new JoinedStream(segments);
    }

    private static int? FindEnd(byte[] data)
    {
        for (var i = data.Length - 22; i >= Math.Max(0, data.Length - 22 - ushort.MaxValue); i--)
            if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i)) == EndSignature) return i;
        return null;
    }

    private static ArchiveAccessException Unsupported(string detail) =>
        new(OperationErrorKind.UnsupportedFormat, $"ZIP dividido em volumes não suportado: {detail}");

    private sealed record Segment(string? Path, ReadOnlyMemory<byte>? Memory, long Length);

    /// <summary>Fluxo somente leitura e posicionável sobre os segmentos em sequência (um arquivo aberto por vez).</summary>
    private sealed class JoinedStream(List<Segment> segments) : Stream
    {
        private readonly long _length = segments.Sum(s => s.Length);
        private long _position;
        private int _openIndex = -1;
        private FileStream? _open;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position { get => _position; set => _position = Math.Clamp(value, 0, _length); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var total = 0;
            while (buffer.Length > 0 && _position < _length)
            {
                long start = 0;
                var index = 0;
                while (start + segments[index].Length <= _position) start += segments[index++].Length;
                var segment = segments[index];
                var within = _position - start;
                var wanted = (int)Math.Min(buffer.Length, segment.Length - within);
                int read;
                if (segment.Memory is { } memory)
                {
                    memory.Span.Slice((int)within, wanted).CopyTo(buffer);
                    read = wanted;
                }
                else
                {
                    if (_openIndex != index)
                    {
                        _open?.Dispose();
                        _open = new FileStream(segment.Path!, FileMode.Open, FileAccess.Read, FileShare.Read, 81920);
                        _openIndex = index;
                    }
                    _open!.Position = within;
                    read = _open.Read(buffer[..wanted]);
                    if (read == 0) throw new EndOfStreamException("Um volume ficou menor durante a leitura.");
                }
                _position += read;
                total += read;
                buffer = buffer[read..];
            }
            return total;
        }

        public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => _length + offset,
        };

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _open?.Dispose();
            base.Dispose(disposing);
        }
    }
}
