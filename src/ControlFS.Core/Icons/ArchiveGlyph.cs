using ControlFS.Core.Contracts;

namespace ControlFS.Core.Icons;

/// <summary>
/// Ícone de reserva para arquivos compactados quando o Windows não tem ícone para a extensão (#274): uma caixa roxa com
/// tampa e um zíper. Desenho original, feito aqui em código (nenhuma arte de terceiros), nítido em qualquer tamanho porque
/// é rasterizado direto no tamanho pedido, com suavização de bordas (3×3 amostras por pixel).
/// </summary>
public static class ArchiveGlyph
{
    private const int Samples = 3;

    // Cores em RGB (não pré-multiplicado); o resultado sai BGRA pré-multiplicado, como o resto dos ícones.
    private static readonly (byte R, byte G, byte B) Body = (0x7A, 0x5C, 0xE0);
    private static readonly (byte R, byte G, byte B) Lid = (0x5B, 0x3F, 0xC4);
    private static readonly (byte R, byte G, byte B) Zip = (0xF4, 0xF1, 0xFF);

    public static IconImage Render(int sizePx)
    {
        var size = Math.Max(16, sizePx);
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                double r = 0, g = 0, b = 0, a = 0;
                for (var sy = 0; sy < Samples; sy++)
                {
                    for (var sx = 0; sx < Samples; sx++)
                    {
                        var u = (x + ((sx + 0.5) / Samples)) / size;
                        var v = (y + ((sy + 0.5) / Samples)) / size;
                        if (Sample(u, v) is not { } color) continue;
                        r += color.R; g += color.G; b += color.B; a += 1;
                    }
                }
                if (a == 0) continue;
                var coverage = a / (Samples * Samples);
                var i = ((y * size) + x) * 4;
                // Média das cores das amostras cobertas, pré-multiplicada pela cobertura.
                pixels[i] = (byte)Math.Round(b / a * coverage);
                pixels[i + 1] = (byte)Math.Round(g / a * coverage);
                pixels[i + 2] = (byte)Math.Round(r / a * coverage);
                pixels[i + 3] = (byte)Math.Round(255 * coverage);
            }
        }
        return new IconImage(size, size, pixels);
    }

    /// <summary>Cor no ponto (u, v) de 0 a 1, ou <c>null</c> se fora do desenho.</summary>
    private static (byte R, byte G, byte B)? Sample(double u, double v)
    {
        // Tampa: faixa de cima, um pouco mais larga que o corpo.
        if (InRoundedRect(u, v, 0.14, 0.12, 0.86, 0.36, 0.06))
        {
            // Zíper: dentes alternados numa coluna central que atravessa a tampa e o corpo.
            return InZip(u, v) ? Zip : Lid;
        }
        if (InRoundedRect(u, v, 0.20, 0.32, 0.80, 0.90, 0.06))
            return InZip(u, v) ? Zip : Body;
        return null;
    }

    private static bool InZip(double u, double v)
    {
        if (v < 0.12 || v > 0.72 || u < 0.455 || u > 0.545) return false;
        // Dentes: quadrados alternados à esquerda e à direita do centro, de 0,06 de altura.
        var row = (int)Math.Floor((v - 0.12) / 0.06);
        var left = u < 0.5;
        return left == (row % 2 == 0);
    }

    private static bool InRoundedRect(double u, double v, double x0, double y0, double x1, double y1, double radius)
    {
        if (u < x0 || u > x1 || v < y0 || v > y1) return false;
        var cx = u < x0 + radius ? x0 + radius : u > x1 - radius ? x1 - radius : u;
        var cy = v < y0 + radius ? y0 + radius : v > y1 - radius ? y1 - radius : v;
        var dx = u - cx;
        var dy = v - cy;
        return (dx * dx) + (dy * dy) <= radius * radius;
    }
}
