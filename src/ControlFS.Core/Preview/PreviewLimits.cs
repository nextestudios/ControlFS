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

    public static PreviewLimits Default { get; } = new();
}

/// <summary>Visualização recusada (limite, formato não reconhecido) ou que falhou ao ler/decodificar. A mensagem vai para a tela.</summary>
public sealed class PreviewException(string message, Exception? inner = null) : Exception(message, inner);
