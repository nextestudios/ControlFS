using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Input.Mapping;
using ControlFS.Core.Policies;
using ControlFS.Core.Preview;

namespace ControlFS.Application;

/// <summary>
/// Erros legíveis: exceções do ControlFS já trazem a mensagem em pt-BR; as do sistema (E/S, permissões, dispositivos)
/// passam pelo tradutor único (<see cref="UserErrors"/>), que dá a categoria e uma ação sugerida, registra a exceção no
/// log e deixa o texto cru só como "Detalhes técnicos".
/// </summary>
public sealed partial class AppController
{
    private static bool IsOwnError(Exception ex) => ex is FileOperationException or ArchiveAccessException or PreviewException or ShellException
        or ControllerProfileException or PhoneLinkException or UpdateException;

    /// <summary>Uma frase para o rodapé, o teclado ou a visualização: o motivo e o que fazer.</summary>
    internal static string ErrorText(Exception ex, string context) => IsOwnError(ex) ? ex.Message : UserErrors.Describe(ex, context).Text;

    /// <summary>Diálogo de erro: as linhas de contexto, o motivo, o que fazer e, por último, os detalhes técnicos.</summary>
    internal DialogModal ShowError(string title, IReadOnlyList<(string, string)> lines, Exception ex, string context)
    {
        if (IsOwnError(ex)) return ShowMessage(title, [.. lines, ("Motivo", ex.Message)], icon: ActionIcon.Error);
        var error = UserErrors.Describe(ex, context);
        return ShowMessage(title, [.. lines, ("Motivo", error.Message), (UserError.TechnicalLabel, error.Technical)], error.Suggestion, ActionIcon.Error);
    }
}
