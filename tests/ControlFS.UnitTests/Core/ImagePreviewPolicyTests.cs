using System.Buffers.Binary;
using ControlFS.Core.Preview;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Core;

public class ImagePreviewPolicyTests
{
    private static ImageHeaderInfo Inspect(byte[] bytes, PreviewLimits? limits = null) =>
        ImagePreviewPolicy.Inspect(new MemoryStream(bytes), limits ?? PreviewLimits.Default);

    private static byte[] Gif(ushort w, ushort h) => [.. "GIF89a"u8, (byte)w, (byte)(w >> 8), (byte)h, (byte)(h >> 8), 0, 0, 0];

    private static byte[] Bmp(int w, int h)
    {
        var b = new byte[54];
        b[0] = (byte)'B';
        b[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(18), w);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(22), h);
        return b;
    }

    private static byte[] WebPExtended(int w, int h)
    {
        var b = new byte[40];
        "RIFF"u8.CopyTo(b);
        "WEBP"u8.CopyTo(b.AsSpan(8));
        "VP8X"u8.CopyTo(b.AsSpan(12));
        b[24] = (byte)(w - 1); b[25] = (byte)((w - 1) >> 8); b[26] = (byte)((w - 1) >> 16);
        b[27] = (byte)(h - 1); b[28] = (byte)((h - 1) >> 8); b[29] = (byte)((h - 1) >> 16);
        return b;
    }

    public static TheoryData<string, ImageFormat, int, int> Headers() => new()
    {
        { "png", ImageFormat.Png, 640, 480 },
        { "jpeg", ImageFormat.Jpeg, 4032, 3024 },
        { "gif", ImageFormat.Gif, 320, 200 },
        { "bmp", ImageFormat.Bmp, 800, 600 },
        { "webp", ImageFormat.WebP, 1920, 1080 },
    };

    [Theory]
    [MemberData(nameof(Headers))]
    public void Reads_format_and_resolution_from_the_content(string kind, ImageFormat format, int width, int height)
    {
        var bytes = kind switch
        {
            "png" => ImageFixtures.Png((uint)width, (uint)height),
            "jpeg" => ImageFixtures.Jpeg((ushort)width, (ushort)height),
            "gif" => Gif((ushort)width, (ushort)height),
            "bmp" => Bmp(width, -height), // de cima para baixo
            _ => WebPExtended(width, height),
        };
        Assert.Equal(new ImageHeaderInfo(format, width, height), Inspect(bytes));
    }

    [Fact]
    public void Refuses_decompression_bombs_oversized_files_and_non_images_before_decoding()
    {
        // 100 000 × 100 000 declarados num arquivo de 33 bytes: nunca chega ao decodificador.
        var bomb = Assert.Throws<PreviewException>(() => Inspect(ImageFixtures.Png(100_000, 100_000)));
        Assert.Contains("Resolução alta demais", bomb.Message, StringComparison.Ordinal);

        var big = Assert.Throws<PreviewException>(() => Inspect(ImageFixtures.Png(10, 10), new PreviewLimits { MaxImageBytes = 16 }));
        Assert.Contains("grande demais", big.Message, StringComparison.Ordinal);

        // Um executável renomeado para .png é recusado pelo conteúdo.
        Assert.Throws<PreviewException>(() => Inspect([(byte)'M', (byte)'Z', 0x90, 0, 3, 0, 0, 0, 4, 0, 0, 0, 0xFF, 0xFF, 0, 0]));
        // JPEG truncado antes do SOF.
        Assert.Throws<PreviewException>(() => Inspect(ImageFixtures.Jpeg(10, 10)[..100]));
    }

    [Fact]
    public void Decoded_size_shrinks_to_the_max_side_and_never_enlarges()
    {
        Assert.Equal((4096, 3072), ImagePreviewPolicy.DecodedSize(8000, 6000, 4096));
        Assert.Equal((100, 50), ImagePreviewPolicy.DecodedSize(100, 50, 4096));
    }

    [Fact]
    public void Exif_orientation_6_rotates_clockwise()
    {
        // 2×1: pixel A à esquerda, B à direita. Girado 90° no horário vira 1×2 com A em cima.
        byte[] pixels = [1, 1, 1, 1, 2, 2, 2, 2];
        var (rotated, w, h) = PixelOrientation.Apply(pixels, 2, 1, 6);
        Assert.Equal((1, 2), (w, h));
        Assert.Equal([1, 1, 1, 1, 2, 2, 2, 2], rotated);
        var (flipped, _, _) = PixelOrientation.Apply(pixels, 2, 1, 3);
        Assert.Equal([2, 2, 2, 2, 1, 1, 1, 1], flipped);
    }
}
