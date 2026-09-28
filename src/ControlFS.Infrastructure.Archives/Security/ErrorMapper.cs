using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Policies;
using SharpCompress.Common;

namespace ControlFS.Infrastructure.Archives.Security;

/// <summary>
/// Traduz exceções do motor e do sistema em categorias do produto. Onde o motor não distingue
/// causas (ex.: ZipCrypto com senha errada que passa na verificação do cabeçalho), a categoria
/// declara a incerteza (<see cref="OperationErrorKind.WrongPasswordOrCorrupt"/>).
/// </summary>
internal static class ErrorMapper
{
    private const int ErrorHandleDiskFull = unchecked((int)0x80070027);
    private const int ErrorDiskFull = unchecked((int)0x80070070);
    private const int UnixEnospc = 28;

    public static (OperationErrorKind Kind, string Message) Map(Exception ex, bool entryEncrypted = false) => ex switch
    {
        FileOperationException f => (f.Kind, f.Message),
        ArchiveAccessException a => (a.Kind, a.Message),
        OperationCanceledException => (OperationErrorKind.Cancelled, "Operação cancelada."),
        CryptographicException c when c.Message.Contains("No password", StringComparison.OrdinalIgnoreCase) =>
            (OperationErrorKind.PasswordRequired, "O arquivo está protegido por senha."),
        CryptographicException c when c.Message.Contains("did not match", StringComparison.OrdinalIgnoreCase) =>
            (OperationErrorKind.WrongPassword, "Senha incorreta."),
        // WinZip AES: o verificador de 2 bytes derivado da senha não confere (SharpCompress lança "bad password").
        InvalidFormatException f when f.Message.Contains("bad password", StringComparison.OrdinalIgnoreCase) =>
            (OperationErrorKind.WrongPassword, "Senha incorreta."),
        CryptographicException => (OperationErrorKind.WrongPasswordOrCorrupt, "Senha incorreta ou dados corrompidos (o motor não distingue)."),
        MultiVolumeExtractionException or MultipartStreamRequiredException =>
            (OperationErrorKind.MissingVolume, "O arquivo faz parte de um conjunto de volumes; volume ausente ou não suportado."),
        IncompleteArchiveException => (OperationErrorKind.Corrupt, "Arquivo incompleto ou truncado."),
        NotSupportedException or ArgumentException when ex.Message.Contains("support", StringComparison.OrdinalIgnoreCase) =>
            (OperationErrorKind.UnsupportedMethod, "Método de compressão ou recurso não suportado."),
        SharpCompressException or InvalidDataException or EndOfStreamException =>
            entryEncrypted
                ? (OperationErrorKind.WrongPasswordOrCorrupt, "Senha incorreta ou dados corrompidos (o motor não distingue).")
                : (OperationErrorKind.Corrupt, "Arquivo corrompido ou formato inválido."),
        UnauthorizedAccessException => (OperationErrorKind.AccessDenied, "Permissão negada."),
        DirectoryNotFoundException or DriveNotFoundException => (OperationErrorKind.DestinationUnavailable, "Destino indisponível (pasta ou unidade removida?)."),
        IOException io when io.HResult is ErrorDiskFull or ErrorHandleDiskFull or UnixEnospc =>
            (OperationErrorKind.InsufficientSpace, "Espaço insuficiente no destino."),
        _ => Friendly(ex),
    };

    /// <summary>Demais erros do sistema: a categoria legível do tradutor único (o texto cru só no log).</summary>
    private static (OperationErrorKind Kind, string Message) Friendly(Exception ex)
    {
        var error = UserErrors.Describe(ex, "Compactado");
        return (UserErrors.OperationKind(error.Kind), error.Text);
    }
}
