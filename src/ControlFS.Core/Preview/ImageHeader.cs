using System.Buffers.Binary;

namespace ControlFS.Core.Preview;

public enum ImageFormat
{
    Png,
    Jpeg,
    Gif,
    Bmp,
    WebP,
}

public sealed record ImageHeaderInfo(ImageFormat Format, int Width, int Height)
{
    public long Pixels => (long)Width * Height;
}

/// <summary>
/// Lê só o cabeçalho de PNG, JPEG, GIF, BMP e WebP pelo conteúdo (nunca pela extensão) para saber formato e resolução
/// antes de decodificar. Não interpreta dados de imagem; lê no máximo <see cref="MaxScanBytes"/>.
/// </summary>
public static class ImageHeader
{
    public const int MaxScanBytes = 4 * 1024 * 1024;

    public static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".gif", ".bmp", ".dib", ".webp",
    };

    public static bool IsImageExtension(string extension) => Extensions.Contains(extension);

    /// <summary>Formato e resolução, ou null quando o conteúdo não é uma imagem suportada (ou o cabeçalho é inválido).</summary>
    public static ImageHeaderInfo? TryRead(Stream stream)
    {
        Span<byte> head = stackalloc byte[32];
        var read = ReadAtMost(stream, head);
        head = head[..read];
        ImageHeaderInfo? info = null;
        if (read >= 24 && head[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]) && head[12..16].SequenceEqual("IHDR"u8))
            info = new(ImageFormat.Png, (int)Math.Min(int.MaxValue, BinaryPrimitives.ReadUInt32BigEndian(head[16..])), (int)Math.Min(int.MaxValue, BinaryPrimitives.ReadUInt32BigEndian(head[20..])));
        else if (read >= 10 && (head[..6].SequenceEqual("GIF87a"u8) || head[..6].SequenceEqual("GIF89a"u8)))
            info = new(ImageFormat.Gif, BinaryPrimitives.ReadUInt16LittleEndian(head[6..]), BinaryPrimitives.ReadUInt16LittleEndian(head[8..]));
        else if (read >= 26 && head[0] == 'B' && head[1] == 'M')
            info = ReadBmp(head);
        else if (read >= 30 && head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8))
            info = ReadWebP(head);
        else if (read >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
            info = ReadJpeg(stream, head);
        return info is { Width: > 0, Height: > 0 } ? info : null;
    }

    private static ImageHeaderInfo? ReadBmp(ReadOnlySpan<byte> head)
    {
        var dibSize = BinaryPrimitives.ReadUInt32LittleEndian(head[14..]);
        if (dibSize == 12)
            return new(ImageFormat.Bmp, BinaryPrimitives.ReadUInt16LittleEndian(head[18..]), BinaryPrimitives.ReadUInt16LittleEndian(head[20..]));
        if (dibSize < 40) return null;
        var width = BinaryPrimitives.ReadInt32LittleEndian(head[18..]);
        var height = BinaryPrimitives.ReadInt32LittleEndian(head[22..]);
        // Altura negativa: linhas de cima para baixo. int.MinValue não tem módulo.
        return width <= 0 || height == int.MinValue ? null : new(ImageFormat.Bmp, width, Math.Abs(height));
    }

    private static ImageHeaderInfo? ReadWebP(ReadOnlySpan<byte> head)
    {
        var chunk = head[12..16];
        if (chunk.SequenceEqual("VP8 "u8) && head[23] == 0x9D && head[24] == 0x01 && head[25] == 0x2A)
            return new(ImageFormat.WebP, BinaryPrimitives.ReadUInt16LittleEndian(head[26..]) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(head[28..]) & 0x3FFF);
        if (chunk.SequenceEqual("VP8L"u8) && head[20] == 0x2F)
        {
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(head[21..]);
            return new(ImageFormat.WebP, (int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
        }
        if (chunk.SequenceEqual("VP8X"u8))
            return new(ImageFormat.WebP, (head[24] | head[25] << 8 | head[26] << 16) + 1, (head[27] | head[28] << 8 | head[29] << 16) + 1);
        return null;
    }

    /// <summary>Percorre os segmentos até o primeiro SOF (início do quadro), que traz altura e largura.</summary>
    private static ImageHeaderInfo? ReadJpeg(Stream stream, ReadOnlySpan<byte> head)
    {
        using var buffer = new MemoryStream();
        buffer.Write(head);
        var reader = new JpegReader(stream, buffer);
        var position = 2;
        while (position < MaxScanBytes)
        {
            if (reader.At(position) is not 0xFF) return null;
            var marker = reader.At(position + 1);
            if (marker is null) return null;
            if (marker == 0xFF) { position++; continue; } // preenchimento
            position += 2;
            if (marker is 0x01 or (>= 0xD0 and <= 0xD7)) continue; // sem tamanho
            if (marker is 0xD9 or 0xDA) return null; // fim ou dados comprimidos antes do SOF
            var length = reader.UInt16(position);
            if (length is null or < 2) return null;
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                var height = reader.UInt16(position + 3);
                var width = reader.UInt16(position + 5);
                return height is null || width is null ? null : new(ImageFormat.Jpeg, width.Value, height.Value);
            }
            position += length.Value;
        }
        return null;
    }

    /// <summary>Leitura sob demanda (streams não posicionáveis também), nunca além de <see cref="MaxScanBytes"/>.</summary>
    private sealed class JpegReader(Stream stream, MemoryStream buffer)
    {
        public int? At(int index)
        {
            if (index >= MaxScanBytes) return null;
            while (buffer.Length <= index)
            {
                var chunk = new byte[Math.Min(64 * 1024, MaxScanBytes - (int)buffer.Length)];
                var n = stream.Read(chunk);
                if (n <= 0) return null;
                buffer.Write(chunk, 0, n);
            }
            return buffer.GetBuffer()[index];
        }

        public int? UInt16(int index) => At(index) is { } hi && At(index + 1) is { } lo ? hi << 8 | lo : null;
    }

    private static int ReadAtMost(Stream stream, Span<byte> destination)
    {
        var total = 0;
        while (total < destination.Length)
        {
            var n = stream.Read(destination[total..]);
            if (n <= 0) break;
            total += n;
        }
        return total;
    }
}
