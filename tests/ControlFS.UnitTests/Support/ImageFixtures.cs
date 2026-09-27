using System.Buffers.Binary;

namespace ControlFS.UnitTests.Support;

/// <summary>Cabeçalhos mínimos de imagem (sem dados de pixel): bastam para a política de visualização.</summary>
public static class ImageFixtures
{
    public static byte[] Png(uint width, uint height)
    {
        var bytes = new byte[33];
        ((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R']).CopyTo(bytes);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), height);
        bytes[24] = 8;
        bytes[25] = 6;
        return bytes;
    }

    /// <summary>JPEG com um segmento APP1 (EXIF) grande antes do SOF0, como fotos de celular.</summary>
    public static byte[] Jpeg(ushort width, ushort height, int exifBytes = 30_000)
    {
        using var s = new MemoryStream();
        s.Write([0xFF, 0xD8, 0xFF, 0xE1]);
        s.Write([(byte)((exifBytes + 2) >> 8), (byte)(exifBytes + 2)]);
        s.Write(new byte[exifBytes]);
        s.Write([0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 0x03]);
        s.Write(new byte[15]);
        s.Write([0xFF, 0xD9]);
        return s.ToArray();
    }
}
