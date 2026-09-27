using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives.Security;
using SharpCompress.Compressors.LZMA;

namespace ControlFS.Infrastructure.Archives.Creation;

/// <summary>
/// Grava um 7z sólido com um único bloco LZMA (codificador do SDK LZMA, domínio público, distribuído no SharpCompress).
/// O contêiner segue o 7zFormat.txt do SDK: cabeçalho de assinatura, fluxo compactado e cabeçalho final sem compressão
/// com tamanhos, CRC-32 de cada arquivo, nomes (UTF-16), datas e atributos. Sem criptografia nem filtros.
/// Decisão e alternativas em docs/decisions/0008-criacao-de-7z.md.
/// </summary>
internal static class SevenZipWriter
{
    private static readonly byte[] Signature = [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];
    private const int SignatureHeaderSize = 32;
    private const int BufferSize = 81920;

    // Identificadores de propriedades do 7zFormat.txt.
    private const byte KEnd = 0x00, KHeader = 0x01, KMainStreamsInfo = 0x04, KFilesInfo = 0x05, KPackInfo = 0x06, KUnpackInfo = 0x07,
        KSubStreamsInfo = 0x08, KSize = 0x09, KCrc = 0x0A, KFolder = 0x0B, KCodersUnpackSize = 0x0C, KNumUnpackStream = 0x0D,
        KEmptyStream = 0x0E, KEmptyFile = 0x0F, KName = 0x11, KMTime = 0x14, KWinAttributes = 0x15;

    private const uint AttributeDirectory = 0x10;
    private const uint KeptFileAttributes = 0x01 | 0x02 | 0x20; // somente leitura, oculto, arquivo

    private sealed record Item(string Name, bool IsDirectory, long Size, uint Crc, DateTime Modified, uint Attributes);

    public static void Write(Stream output, CompressionStrength strength, List<(string FullPath, string EntryName, bool IsDirectory, long Size)> plan,
        List<ItemResult> results, Action<string, long, bool> report, CancellationToken ct)
    {
        output.Write(new byte[SignatureHeaderSize]); // preenchido no fim
        var items = new List<Item>();
        long packed = 0;
        long unpacked = 0;
        byte[]? coderProperties = null;
        var buffer = new byte[BufferSize];
        var counter = new CountingStream(output);
        LzmaStream? lzma = null;
        try
        {
            foreach (var entry in plan)
            {
                ct.ThrowIfCancellationRequested();
                var name = entry.EntryName.TrimEnd('/');
                if (entry.IsDirectory)
                {
                    items.Add(new Item(name, true, 0, 0, SafeTime(() => Directory.GetLastWriteTimeUtc(entry.FullPath)), AttributeDirectory));
                    continue;
                }
                FileStream source;
                try { source = new FileStream(entry.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    results.Add(new ItemResult(entry.EntryName, ItemOutcome.Failed, ex is UnauthorizedAccessException ? OperationErrorKind.AccessDenied : OperationErrorKind.Unknown,
                        "Não foi possível ler (arquivo em uso ou sem permissão)."));
                    continue;
                }
                long size = 0;
                var crc = new Crc32();
                using (source)
                {
                    int read;
                    while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (lzma is null)
                        {
                            lzma = new LzmaStream(Properties(strength, plan), false, counter);
                            coderProperties = lzma.Properties;
                        }
                        lzma.Write(buffer, 0, read);
                        crc.Append(buffer.AsSpan(0, read));
                        size += read;
                        report(entry.EntryName, read, false);
                    }
                }
                var attributes = (uint)SafeAttributes(entry.FullPath) & KeptFileAttributes;
                items.Add(new Item(name, false, size, crc.Value, SafeTime(() => File.GetLastWriteTimeUtc(entry.FullPath)), attributes));
                unpacked += size;
                results.Add(new ItemResult(entry.EntryName, ItemOutcome.Succeeded));
                report(entry.EntryName, 0, true);
            }
            lzma?.Dispose(); // grava o fim do fluxo LZMA
            lzma = null;
            packed = counter.Written;
        }
        finally
        {
            lzma?.Dispose();
        }

        var header = BuildHeader(items, packed, unpacked, coderProperties);
        output.Write(header);

        Span<byte> start = stackalloc byte[SignatureHeaderSize];
        Signature.CopyTo(start);
        start[6] = 0; // versão 0.4
        start[7] = 4;
        BinaryPrimitives.WriteUInt64LittleEndian(start[12..], (ulong)packed); // o cabeçalho vem logo depois do fluxo
        BinaryPrimitives.WriteUInt64LittleEndian(start[20..], (ulong)header.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(start[28..], Crc32.Compute(header));
        BinaryPrimitives.WriteUInt32LittleEndian(start[8..], Crc32.Compute(start[12..]));
        output.Position = 0;
        output.Write(start);
        output.Seek(0, SeekOrigin.End);
    }

    private static LzmaEncoderProperties Properties(CompressionStrength strength, List<(string FullPath, string EntryName, bool IsDirectory, long Size)> plan)
    {
        // Dicionário limitado ao tamanho da entrada (memória do codificador ≈ 11× o dicionário).
        var (dictionary, fastBytes) = strength switch
        {
            CompressionStrength.Fast => (1 << 20, 32),
            CompressionStrength.Maximum => (1 << 24, 64),
            _ => (1 << 23, 32),
        };
        var total = plan.Where(p => !p.IsDirectory).Sum(p => p.Size);
        while (dictionary > 1 << 16 && dictionary / 2 >= total) dictionary /= 2;
        return new LzmaEncoderProperties(eos: false, dictionary, fastBytes);
    }

    private static byte[] BuildHeader(List<Item> items, long packed, long unpacked, byte[]? coderProperties)
    {
        var h = new HeaderWriter();
        h.Byte(KHeader);
        var streams = items.Where(i => !i.IsDirectory && i.Size > 0).ToList();
        if (streams.Count > 0)
        {
            h.Byte(KMainStreamsInfo);

            h.Byte(KPackInfo);
            h.Number(0); // posição do fluxo após o cabeçalho de assinatura
            h.Number(1);
            h.Byte(KSize);
            h.Number((ulong)packed);
            h.Byte(KEnd);

            h.Byte(KUnpackInfo);
            h.Byte(KFolder);
            h.Number(1);
            h.Byte(0); // não é externo
            h.Number(1); // um codificador
            h.Byte(0x23); // id de 3 bytes, com propriedades
            h.Bytes([0x03, 0x01, 0x01]); // LZMA
            h.Number((ulong)coderProperties!.Length);
            h.Bytes(coderProperties);
            h.Byte(KCodersUnpackSize);
            h.Number((ulong)unpacked);
            h.Byte(KEnd);

            h.Byte(KSubStreamsInfo);
            h.Byte(KNumUnpackStream);
            h.Number((ulong)streams.Count);
            if (streams.Count > 1)
            {
                h.Byte(KSize);
                foreach (var s in streams.Take(streams.Count - 1)) h.Number((ulong)s.Size);
            }
            h.Byte(KCrc);
            h.Byte(1); // todos definidos
            foreach (var s in streams) h.UInt32(s.Crc);
            h.Byte(KEnd);

            h.Byte(KEnd);
        }

        h.Byte(KFilesInfo);
        h.Number((ulong)items.Count);
        var empty = items.Select(i => i.IsDirectory || i.Size == 0).ToList();
        if (empty.Contains(true))
        {
            h.Property(KEmptyStream, BitField(empty));
            var emptyFiles = items.Where(i => i.IsDirectory || i.Size == 0).Select(i => !i.IsDirectory).ToList();
            if (emptyFiles.Contains(true)) h.Property(KEmptyFile, BitField(emptyFiles));
        }

        var names = new List<byte> { 0 }; // não é externo
        foreach (var item in items)
        {
            names.AddRange(Encoding.Unicode.GetBytes(item.Name));
            names.AddRange([0, 0]);
        }
        h.Property(KName, [.. names]);

        var times = new byte[2 + 8 * items.Count];
        times[0] = 1; // todos definidos
        times[1] = 0; // não é externo
        for (var i = 0; i < items.Count; i++) BinaryPrimitives.WriteInt64LittleEndian(times.AsSpan(2 + 8 * i), items[i].Modified.ToFileTimeUtc());
        h.Property(KMTime, times);

        var attributes = new byte[2 + 4 * items.Count];
        attributes[0] = 1;
        attributes[1] = 0;
        for (var i = 0; i < items.Count; i++) BinaryPrimitives.WriteUInt32LittleEndian(attributes.AsSpan(2 + 4 * i), items[i].Attributes);
        h.Property(KWinAttributes, attributes);

        h.Byte(KEnd);
        h.Byte(KEnd);
        return h.ToArray();
    }

    /// <summary>Vetor de bits do 7z: o primeiro item é o bit mais alto do primeiro byte.</summary>
    private static byte[] BitField(List<bool> bits)
    {
        var bytes = new byte[(bits.Count + 7) / 8];
        for (var i = 0; i < bits.Count; i++)
            if (bits[i]) bytes[i / 8] |= (byte)(0x80 >> (i % 8));
        return bytes;
    }

    private static DateTime SafeTime(Func<DateTime> read)
    {
        try
        {
            var time = read();
            return time.Year is < 1601 or > 9999 ? new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc) : time;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        }
    }

    private static FileAttributes SafeAttributes(string path)
    {
        try { return File.GetAttributes(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return FileAttributes.Archive; }
    }

    private sealed class HeaderWriter
    {
        private readonly ArrayBufferWriter<byte> _buffer = new();

        public void Byte(byte value) => _buffer.Write([value]);

        public void Bytes(ReadOnlySpan<byte> value) => _buffer.Write(value);

        public void UInt32(uint value)
        {
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(b, value);
            _buffer.Write(b);
        }

        /// <summary>Número de tamanho variável do 7z: os bits altos do primeiro byte dizem quantos bytes seguem.</summary>
        public void Number(ulong value)
        {
            byte first = 0;
            byte mask = 0x80;
            int i;
            for (i = 0; i < 8; i++)
            {
                if (value < 1UL << (7 * (i + 1)))
                {
                    first |= (byte)(value >> (8 * i));
                    break;
                }
                first |= mask;
                mask >>= 1;
            }
            Byte(first);
            for (; i > 0; i--)
            {
                Byte((byte)value);
                value >>= 8;
            }
        }

        public void Property(byte id, byte[] data)
        {
            Byte(id);
            Number((ulong)data.Length);
            Bytes(data);
        }

        public byte[] ToArray() => _buffer.WrittenSpan.ToArray();
    }

    /// <summary>Conta os bytes compactados sem fechar o destino (o codificador não fecha o fluxo de saída).</summary>
    private sealed class CountingStream(Stream inner) : Stream
    {
        public long Written { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => Written; set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count)
        {
            inner.Write(buffer, offset, count);
            Written += count;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            inner.Write(buffer);
            Written += buffer.Length;
        }

        public override void WriteByte(byte value)
        {
            inner.WriteByte(value);
            Written++;
        }

        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
