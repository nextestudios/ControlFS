namespace ControlFS.Core.Contracts;

/// <summary>
/// Desenha páginas de PDF em pixels para a visualização (#59). Só desenha: nunca abre links, anexos, formulários ou
/// scripts, nem outros programas. Trabalha fora da thread de UI; quem chama já conferiu o arquivo com
/// <see cref="Preview.PdfPreviewPolicy"/>.
/// </summary>
public interface IPdfRenderer
{
    /// <summary>Abre o documento (sem desenhar nada ainda).</summary>
    /// <exception cref="PdfPasswordException">O PDF pede senha (nenhuma ou errada).</exception>
    /// <exception cref="Preview.PreviewException">Arquivo danificado ou que o Windows não abre.</exception>
    Task<IPdfDocument> OpenAsync(string path, string? password, CancellationToken cancellationToken);
}

/// <summary>Documento aberto. <see cref="IDisposable.Dispose"/> libera o arquivo (espera um desenho em andamento terminar).</summary>
public interface IPdfDocument : IDisposable
{
    int PageCount { get; }

    /// <summary>Desenha a página (0-based) sobre fundo branco, com o lado maior igual a <paramref name="maxSide"/>.</summary>
    /// <exception cref="Preview.PreviewException">A página não pôde ser desenhada.</exception>
    Task<PreviewImage> RenderPageAsync(int index, int maxSide, CancellationToken cancellationToken);
}

/// <summary>PDF protegido: <see cref="WrongPassword"/> diz se uma senha foi tentada e recusada.</summary>
public sealed class PdfPasswordException(bool wrongPassword, Exception? inner = null)
    : Exception(wrongPassword ? "Senha incorreta." : "Este PDF é protegido por senha.", inner)
{
    public bool WrongPassword { get; } = wrongPassword;
}
