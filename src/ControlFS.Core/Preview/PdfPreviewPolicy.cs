using System.Globalization;

namespace ControlFS.Core.Preview;

/// <summary>
/// Decide, antes de entregar o arquivo ao Windows, se um PDF pode ser visualizado (#59): tamanho dentro do limite e
/// conteúdo que realmente começa como PDF (um executável renomeado para .pdf nunca chega ao renderizador).
/// </summary>
public static class PdfPreviewPolicy
{
    /// <summary>Onde a assinatura <c>%PDF-</c> pode aparecer (a especificação tolera lixo antes dela; leitores aceitam até 1 KB).</summary>
    public const int SignatureWindow = 1024;

    private static readonly byte[] Signature = "%PDF-"u8.ToArray();

    public static bool IsPdfExtension(string extension) => string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase);

    /// <exception cref="PreviewException">Quando o PDF não pode ser visualizado; a mensagem explica o motivo.</exception>
    public static void Inspect(Stream stream, PreviewLimits limits)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(limits);
        if (stream.CanSeek && stream.Length > limits.MaxPdfBytes)
            throw new PreviewException(string.Create(CultureInfo.CurrentCulture,
                $"PDF grande demais para visualizar aqui ({stream.Length / (1024.0 * 1024):0.#} MB; limite de {limits.MaxPdfBytes / (1024 * 1024)} MB)."));
        var head = new byte[SignatureWindow];
        var read = 0;
        while (read < head.Length)
        {
            var n = stream.Read(head, read, head.Length - read);
            if (n <= 0) break;
            read += n;
        }
        if (head.AsSpan(0, read).IndexOf(Signature) < 0)
            throw new PreviewException("O conteúdo não é um PDF válido.");
    }

    /// <summary>Tamanho do desenho: a página ampliada ou reduzida até o lado maior valer <paramref name="maxSide"/>.</summary>
    public static (int Width, int Height) RenderSize(double pageWidth, double pageHeight, int maxSide)
    {
        if (!(pageWidth > 0) || !(pageHeight > 0) || double.IsInfinity(pageWidth) || double.IsInfinity(pageHeight))
            throw new PreviewException("A página do PDF tem um tamanho inválido.");
        var scale = maxSide / Math.Max(pageWidth, pageHeight);
        return (Math.Clamp((int)Math.Round(pageWidth * scale), 1, maxSide), Math.Clamp((int)Math.Round(pageHeight * scale), 1, maxSide));
    }
}
