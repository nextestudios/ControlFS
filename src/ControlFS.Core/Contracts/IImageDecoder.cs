namespace ControlFS.Core.Contracts;

/// <summary>Imagem decodificada para a visualização: pixels BGRA de 32 bits com alfa pré-multiplicado, de cima para baixo.</summary>
public sealed record PreviewImage(int Width, int Height, ReadOnlyMemory<byte> Pixels);

/// <summary>
/// Decodificador de imagens da visualização. Só decodifica pixels (nunca abre programas nem executa conteúdo) e trabalha
/// fora da thread de UI. Quem chama já conferiu tamanho e resolução com <see cref="Preview.ImagePreviewPolicy"/>.
/// </summary>
public interface IImageDecoder
{
    /// <summary>Decodifica o primeiro quadro reduzido para caber em <paramref name="maxSide"/>, já na orientação EXIF.</summary>
    /// <exception cref="Preview.PreviewException">Arquivo ilegível ou formato que o sistema não decodifica.</exception>
    Task<PreviewImage> DecodeAsync(string path, int maxSide, CancellationToken cancellationToken);
}
