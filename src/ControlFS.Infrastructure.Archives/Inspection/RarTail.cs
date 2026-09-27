using System.Buffers.Binary;

namespace ControlFS.Infrastructure.Archives.Inspection;

/// <summary>
/// Lê só os cabeçalhos de um volume RAR (pulando os dados) até o bloco de fim de arquivo, que diz se há mais volumes
/// depois dele. É o único jeito de saber que o último volume sumiu quando nenhum arquivo ficou pela metade.
/// null = não dá para saber (cabeçalhos criptografados, RAR antigo sem bloco final, arquivo estranho).
/// </summary>
internal static class RarTail
{
    private const int MaxHeaders = 1_000_000;
    private const int Rar5MaxHeaderSize = 2 * 1024 * 1024;

    public static bool? MoreVolumesFollow(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096);
            Span<byte> signature = stackalloc byte[8];
            if (stream.ReadAtLeast(signature, 8, throwOnEndOfStream: false) < 7) return null;
            if (!signature[..6].SequenceEqual((ReadOnlySpan<byte>)[0x52, 0x61, 0x72, 0x21, 0x1A, 0x07])) return null;
            if (signature[6] == 1 && signature[7] == 0) return Rar5(stream);
            if (signature[6] == 0) return Rar4(stream);
            return null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    // RAR5: CRC32, tamanho do cabeçalho (vint), tipo, flags, [área extra], [tamanho dos dados]. Tipo 4 = cabeçalhos
    // criptografados; tipo 5 = fim, cuja flag 0x0001 diz "é volume e não é o último".
    private static bool? Rar5(FileStream stream)
    {
        var position = 8L;
        var buffer = new byte[64];
        for (var i = 0; i < MaxHeaders && position + 6 <= stream.Length; i++)
        {
            stream.Position = position + 4; // pula o CRC32
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            var p = 0;
            if (!ReadVInt(buffer, read, ref p, out var headerSize) || headerSize is 0 or > Rar5MaxHeaderSize) return null;
            var headerStart = position + 4 + p;
            if (!ReadVInt(buffer, read, ref p, out var type) || !ReadVInt(buffer, read, ref p, out var flags)) return null;
            ulong dataSize = 0;
            if ((flags & 0x0001) != 0 && !ReadVInt(buffer, read, ref p, out _)) return null;
            if ((flags & 0x0002) != 0 && !ReadVInt(buffer, read, ref p, out dataSize)) return null;
            if (type == 4) return null;
            if (type == 5) return ReadVInt(buffer, read, ref p, out var endFlags) ? (endFlags & 0x0001) != 0 : null;
            if (dataSize > (ulong)stream.Length) return null;
            var next = headerStart + (long)headerSize + (long)dataSize;
            if (next <= position) return null;
            position = next;
        }
        return null;
    }

    // RAR 2.9–4: HEAD_CRC(2) HEAD_TYPE(1) HEAD_FLAGS(2) HEAD_SIZE(2) [ADD_SIZE(4) com 0x8000]; arquivos com 0x0100 guardam
    // os 32 bits altos do tamanho no deslocamento 32. Principal (0x73) com 0x0080 = cabeçalhos criptografados;
    // fim (0x7B) com 0x0001 = há um próximo volume.
    private static bool? Rar4(FileStream stream)
    {
        var position = 7L;
        Span<byte> header = stackalloc byte[36];
        for (var i = 0; i < MaxHeaders && position + 7 <= stream.Length; i++)
        {
            stream.Position = position;
            var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
            if (read < 7) return null;
            var type = header[2];
            var flags = BinaryPrimitives.ReadUInt16LittleEndian(header[3..]);
            var size = BinaryPrimitives.ReadUInt16LittleEndian(header[5..]);
            if (size < 7) return null;
            if (type == 0x73 && (flags & 0x0080) != 0) return null;
            if (type == 0x7B) return (flags & 0x0001) != 0;
            long add = 0;
            if ((flags & 0x8000) != 0)
            {
                if (read < 11) return null;
                add = BinaryPrimitives.ReadUInt32LittleEndian(header[7..]);
                if (type is 0x74 or 0x7A && (flags & 0x0100) != 0)
                {
                    if (read < 36) return null;
                    add += (long)BinaryPrimitives.ReadUInt32LittleEndian(header[32..]) << 32;
                }
            }
            if (add > stream.Length) return null;
            position += size + add;
        }
        return null;
    }

    private static bool ReadVInt(byte[] buffer, int length, ref int position, out ulong value)
    {
        value = 0;
        for (var shift = 0; shift < 64 && position < length; shift += 7)
        {
            var b = buffer[position++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return true;
        }
        return false;
    }
}
