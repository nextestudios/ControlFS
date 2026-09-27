namespace ControlFS.Core.Preview;

/// <summary>Aplica a orientação EXIF (1–8) a pixels de 32 bits, para fotos de celular aparecerem em pé.</summary>
public static class PixelOrientation
{
    /// <summary>Devolve os pixels já orientados e o novo tamanho. Orientação 1 ou desconhecida: sem cópia.</summary>
    public static (byte[] Pixels, int Width, int Height) Apply(byte[] pixels, int width, int height, int orientation)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (orientation is < 2 or > 8 || pixels.Length != width * height * 4) return (pixels, width, height);
        var swap = orientation >= 5;
        var (w, h) = swap ? (height, width) : (width, height);
        var output = new byte[pixels.Length];
        var source = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, int>(pixels.AsSpan());
        var target = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, int>(output.AsSpan());
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var (tx, ty) = orientation switch
            {
                2 => (width - 1 - x, y),
                3 => (width - 1 - x, height - 1 - y),
                4 => (x, height - 1 - y),
                5 => (y, x),
                6 => (height - 1 - y, x),
                7 => (height - 1 - y, width - 1 - x),
                _ => (y, width - 1 - x), // 8
            };
            target[ty * w + tx] = source[y * width + x];
        }
        return (output, w, h);
    }
}
