namespace ControlFS.Core.Preview;

/// <summary>
/// Limites das visualizações internas. Tudo é conferido antes de decodificar: o tamanho do arquivo e a resolução
/// declarada no cabeçalho, para que uma imagem pequena no disco que se expande para gigapixels nunca chegue ao decodificador.
/// </summary>
public sealed record PreviewLimits
{
    /// <summary>Tamanho máximo do arquivo de imagem.</summary>
    public long MaxImageBytes { get; init; } = 100L * 1024 * 1024;

    /// <summary>Resolução máxima declarada (largura × altura).</summary>
    public long MaxImagePixels { get; init; } = 80_000_000;

    /// <summary>Maior lado da imagem decodificada (reduzida na decodificação): limita a memória a ~64 MB por imagem.</summary>
    public int MaxDecodedSide { get; init; } = 4096;

    /// <summary>Bytes lidos de um arquivo de texto; o resto não é lido (a prévia avisa que é parcial).</summary>
    public int MaxTextBytes { get; init; } = 2 * 1024 * 1024;

    /// <summary>Linhas mostradas de um arquivo de texto.</summary>
    public int MaxTextLines { get; init; } = 10_000;

    /// <summary>Tamanho máximo de um PDF.</summary>
    public long MaxPdfBytes { get; init; } = 200L * 1024 * 1024;

    /// <summary>Páginas navegáveis de um PDF (as demais não são desenhadas; a tela avisa).</summary>
    public int MaxPdfPages { get; init; } = 5_000;

    /// <summary>Lado maior de uma página desenhada: nítida com zoom numa TV 4K e ~36 MB por página no máximo.</summary>
    public int PdfRenderSide { get; init; } = 3072;

    /// <summary>Tempo máximo para abrir ou desenhar uma página: um PDF feito para travar o renderizador vira erro, não trava a tela.</summary>
    public TimeSpan PdfTimeout { get; init; } = TimeSpan.FromSeconds(20);

    public static PreviewLimits Default { get; } = new();
}

/// <summary>Visualização recusada (limite, formato não reconhecido) ou que falhou ao ler/decodificar. A mensagem vai para a tela.</summary>
public sealed class PreviewException(string message, Exception? inner = null) : Exception(message, inner);
