using ControlFS.Core.Models;

namespace ControlFS.Infrastructure.Archives.Inspection;

/// <summary>Detecta o formato por assinatura de conteúdo. A extensão nunca decide sozinha.</summary>
public static class FormatDetector
{
    private const int HeaderLength = 512;

    public static ArchiveFormat Detect(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.None);
        return Detect(stream);
    }

    public static ArchiveFormat Detect(Stream stream)
    {
        Span<byte> header = stackalloc byte[HeaderLength];
        var read = stream.ReadAtLeast(header, HeaderLength, throwOnEndOfStream: false);
        return Detect(header[..read]);
    }

    public static ArchiveFormat Detect(ReadOnlySpan<byte> h)
    {
        if (h.Length >= 4 && h[0] == 'P' && h[1] == 'K' && ((h[2] == 3 && h[3] == 4) || (h[2] == 5 && h[3] == 6) || (h[2] == 7 && h[3] == 8)))
            return ArchiveFormat.Zip;
        if (h.Length >= 6 && h[..6].SequenceEqual((ReadOnlySpan<byte>)[0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C]))
            return ArchiveFormat.SevenZip;
        // RAR 4: "Rar!\x1A\x07\x00"; RAR 5: "Rar!\x1A\x07\x01\x00".
        if (h.Length >= 7 && h[..6].SequenceEqual((ReadOnlySpan<byte>)[0x52, 0x61, 0x72, 0x21, 0x1A, 0x07]) && h[6] is 0 or 1)
            return ArchiveFormat.Rar;
        if (h.Length >= 2 && h[0] == 0x1F && h[1] == 0x8B)
            return ArchiveFormat.GZip;
        if (h.Length >= 262 && h.Slice(257, 5).SequenceEqual("ustar"u8))
            return ArchiveFormat.Tar;
        return ArchiveFormat.Unknown;
    }
}
