using System.Globalization;

namespace ControlFS.Core.Preview;

/// <summary>Decide, antes de qualquer decodificação, se uma imagem pode ser visualizada dentro dos limites.</summary>
public static class ImagePreviewPolicy
{
    /// <summary>Confere tamanho, formato real (pelo conteúdo) e resolução declarada.</summary>
    /// <exception cref="PreviewException">Quando a imagem não pode ser visualizada; a mensagem explica o motivo.</exception>
    public static ImageHeaderInfo Inspect(Stream stream, PreviewLimits limits)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(limits);
        if (stream.CanSeek && stream.Length > limits.MaxImageBytes)
            throw new PreviewException($"Imagem grande demais para visualizar aqui ({Megabytes(stream.Length)}; limite de {Megabytes(limits.MaxImageBytes)}).");
        var info = ImageHeader.TryRead(stream)
            ?? throw new PreviewException("O conteúdo não é uma imagem JPG, PNG, GIF, BMP ou WebP válida.");
        if (info.Pixels > limits.MaxImagePixels)
            throw new PreviewException(string.Create(CultureInfo.CurrentCulture,
                $"Resolução alta demais para visualizar aqui ({info.Width} × {info.Height}; limite de {limits.MaxImagePixels / 1_000_000} megapixels)."));
        return info;
    }

    /// <summary>Tamanho da decodificação: reduz proporcionalmente para caber em <see cref="PreviewLimits.MaxDecodedSide"/>, nunca amplia.</summary>
    public static (int Width, int Height) DecodedSize(int width, int height, int maxSide)
    {
        var longest = Math.Max(width, height);
        if (longest <= maxSide) return (width, height);
        var scale = (double)maxSide / longest;
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    private static string Megabytes(long bytes) => string.Create(CultureInfo.CurrentCulture, $"{bytes / (1024.0 * 1024):0.#} MB");
}
