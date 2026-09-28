using ControlFS.Core.Models;

namespace ControlFS.Core.Policies;

/// <summary>Categoria de um erro do sistema, do jeito que o usuário entende.</summary>
public enum UserErrorKind
{
    NotFound,
    AccessDenied,
    InUse,
    DiskFull,
    PathTooLong,
    DeviceNotReady,
    Generic,
}

/// <summary>
/// Erro pronto para a tela: o que houve (<see cref="Message"/>), o que fazer (<see cref="Suggestion"/>) e o texto do
/// sistema (<see cref="Technical"/>), que só aparece como linha secundária "Detalhes técnicos".
/// </summary>
public sealed record UserError(UserErrorKind Kind, string Message, string Suggestion, string Technical)
{
    /// <summary>Uma frase só, para o rodapé, o teclado ou a visualização: o que houve e o que fazer.</summary>
    public string Text => $"{Message} {Suggestion}";

    public const string TechnicalLabel = "Detalhes técnicos";
}

/// <summary>
/// Tradutor único de exceções do sistema (E/S, permissões, dispositivos) para mensagens em pt-BR com uma ação sugerida.
/// O texto cru da exceção (em inglês ou com códigos) nunca é a mensagem principal: fica em <see cref="UserError.Technical"/>
/// e vai para o log (<see cref="Log"/>). Usado pela aplicação e pelo mapeador de erros do extrator.
/// </summary>
public static class UserErrors
{
    // HRESULTs do Windows (0x8007xxxx = erro Win32 xxxx).
    private const int FileNotFound = unchecked((int)0x80070002);
    private const int PathNotFound = unchecked((int)0x80070003);
    private const int AccessDeniedCode = unchecked((int)0x80070005);
    private const int InvalidDrive = unchecked((int)0x8007000F);
    private const int NotReady = unchecked((int)0x80070015);
    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);
    private const int HandleDiskFull = unchecked((int)0x80070027);
    private const int BadNetPath = unchecked((int)0x80070035);
    private const int DiskFullCode = unchecked((int)0x80070070);
    private const int FilenameTooLong = unchecked((int)0x800700CE);
    private const int NoMediaInDrive = unchecked((int)0x80070459);
    private const int DeviceNotConnected = unchecked((int)0x8007048F);
    private const int UnixEnoent = 2;
    private const int UnixEacces = 13;
    private const int UnixEnospc = 28;
    private const int UnixEnametoolong = 36;

    /// <summary>
    /// Recebe toda exceção descrita por <see cref="Describe"/> (com o contexto de onde veio). O app liga ao log local;
    /// nos testes fica vazio.
    /// </summary>
    public static Action<Exception, string>? Log { get; set; }

    public static UserErrorKind KindOf(Exception ex) => ex switch
    {
        PathTooLongException => UserErrorKind.PathTooLong,
        FileNotFoundException or DirectoryNotFoundException => UserErrorKind.NotFound,
        DriveNotFoundException => UserErrorKind.DeviceNotReady,
        UnauthorizedAccessException or System.Security.SecurityException => UserErrorKind.AccessDenied,
        // Processos e shell: o código Win32 está em NativeErrorCode (HResult é o genérico E_FAIL).
        System.ComponentModel.Win32Exception { NativeErrorCode: > 0 and < 0x10000 } w => KindOfCode(unchecked((int)0x80070000) | w.NativeErrorCode, ex),
        _ => KindOfCode(ex.HResult, ex),
    };

    private static UserErrorKind KindOfCode(int code, Exception ex) => code switch
    {
        FileNotFound or PathNotFound or BadNetPath or UnixEnoent => UserErrorKind.NotFound,
        AccessDeniedCode or UnixEacces => UserErrorKind.AccessDenied,
        SharingViolation or LockViolation => UserErrorKind.InUse,
        HandleDiskFull or DiskFullCode or UnixEnospc => UserErrorKind.DiskFull,
        FilenameTooLong or UnixEnametoolong => UserErrorKind.PathTooLong,
        NotReady or InvalidDrive or NoMediaInDrive or DeviceNotConnected => UserErrorKind.DeviceNotReady,
        _ when ex.InnerException is { } inner => KindOf(inner),
        _ => UserErrorKind.Generic,
    };

    /// <summary>Descreve a exceção e a registra no log com <paramref name="context"/> (ex.: "Abrir pasta").</summary>
    public static UserError Describe(Exception ex, string context = "")
    {
        try
        {
            Log?.Invoke(ex, context);
        }
        catch (Exception)
        {
            // O log nunca derruba a mensagem ao usuário.
        }
        var kind = KindOf(ex);
        var (message, suggestion) = Texts(kind);
        return new UserError(kind, message, suggestion, $"{ex.GetType().Name}: {ex.Message}");
    }

    /// <summary>Mensagem e ação sugerida de cada categoria.</summary>
    public static (string Message, string Suggestion) Texts(UserErrorKind kind) => kind switch
    {
        UserErrorKind.NotFound => ("O item não foi encontrado.", "Ele pode ter sido movido, renomeado ou apagado; atualize a pasta."),
        UserErrorKind.AccessDenied => ("Acesso negado.", "O Windows não deixa o ControlFS mexer aqui; escolha outra pasta ou confira as permissões."),
        UserErrorKind.InUse => ("O arquivo está em uso por outro programa.", "Feche o programa que o está usando e tente de novo."),
        UserErrorKind.DiskFull => ("O disco está cheio.", "Libere espaço ou escolha outro destino."),
        UserErrorKind.PathTooLong => ("O caminho é longo demais.", "Use nomes mais curtos ou uma pasta mais perto da raiz da unidade."),
        UserErrorKind.DeviceNotReady => ("O dispositivo não está pronto.", "Confira se o pendrive, o disco ou a unidade de rede está conectado e tente de novo."),
        _ => ("Algo deu errado.", "Tente de novo em instantes."),
    };

    /// <summary>Categoria de operação equivalente (resultados de cópia, extração e fila).</summary>
    public static OperationErrorKind OperationKind(UserErrorKind kind) => kind switch
    {
        UserErrorKind.AccessDenied => OperationErrorKind.AccessDenied,
        UserErrorKind.DiskFull => OperationErrorKind.InsufficientSpace,
        UserErrorKind.NotFound or UserErrorKind.DeviceNotReady => OperationErrorKind.DestinationUnavailable,
        UserErrorKind.PathTooLong => OperationErrorKind.InvalidName,
        _ => OperationErrorKind.Unknown,
    };
}
