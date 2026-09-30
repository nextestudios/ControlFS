namespace ControlFS.Core.Automation;

/// <summary>
/// Ações suportadas pelos links do esquema <c>controlfs://</c> e argumentos de linha de comando.
/// Permite automação externa (Console Mode, Stream Deck, scripts, atalhos).
/// </summary>
public static class AppProtocol
{
    public const string Scheme = "controlfs";

    public const string StartAction = "start";
    public const string StopAction = "stop";
    public const string ShowAction = "show";

    /// <summary>
    /// Interpreta o argumento passado (URI <c>controlfs://...</c> ou flag de CLI como <c>--stop</c>, <c>--start</c>, <c>--close</c>).
    /// Retorna a ação normalizada ("start", "stop", "show") ou <c>null</c> se não for reconhecido.
    /// </summary>
    public static string? ParseAction(string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument)) return null;

        var trimmed = argument.Trim();

        // Linha de comando: --stop, --close, --quit
        if (trimmed.Equals("--stop", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("--close", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("--quit", StringComparison.OrdinalIgnoreCase))
        {
            return StopAction;
        }

        // Linha de comando: --start, --open
        if (trimmed.Equals("--start", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("--open", StringComparison.OrdinalIgnoreCase))
        {
            return StartAction;
        }

        // Linha de comando: --show
        if (trimmed.Equals("--show", StringComparison.OrdinalIgnoreCase))
        {
            return ShowAction;
        }

        // Esquema controlfs://... ou controlfs:...
        if (!trimmed.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Exemplos aceitos:
        // controlfs://start, controlfs://start/, controlfs:start, controlfs://start?foo=1#bar
        // controlfs://stop, controlfs://close, controlfs://quit
        // controlfs://show
        // controlfs://open
        var rest = trimmed[(Scheme.Length + 1)..].TrimStart('/');
        var end = rest.IndexOfAny(['/', '?', '#']);
        var action = (end < 0 ? rest : rest[..end]).Trim().ToLowerInvariant();

        return action switch
        {
            StartAction or "open" => StartAction,
            StopAction or "close" or "quit" => StopAction,
            ShowAction => ShowAction,
            _ => null
        };
    }

    /// <summary>Argumento que o instalador passa ao reabrir o app depois de uma atualização: espera a instância antiga sair.</summary>
    public const string RelaunchFlag = "--relaunch";

    public static bool IsRelaunch(IEnumerable<string>? args) =>
        args?.Any(a => a.Equals(RelaunchFlag, StringComparison.OrdinalIgnoreCase)) == true;

    /// <summary>
    /// Localiza a primeira ação de protocolo ou linha de comando válida dentro dos argumentos passados.
    /// </summary>
    public static string? ParseActionFromArgs(IEnumerable<string>? args)
    {
        if (args is null) return null;
        foreach (var arg in args)
        {
            if (ParseAction(arg) is { } action) return action;
        }
        return null;
    }
}
