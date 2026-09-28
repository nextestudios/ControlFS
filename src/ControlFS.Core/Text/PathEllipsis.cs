namespace ControlFS.Core.Text;

/// <summary>
/// Caminho longo encurtado no meio (auditoria de UX, P2-6): a unidade (ou o compartilhamento de rede) e as últimas pastas
/// ficam; o que some é o meio, trocado por "…". Cortar só o fim escondia justamente a pasta que interessa
/// ("C:\Users\…\Relatórios trimestrai…").
/// </summary>
public static class PathEllipsis
{
    public const string Mark = "…";

    /// <summary>
    /// <paramref name="path"/> com no máximo <paramref name="maxChars"/> caracteres quando possível: raiz + "…" + as pastas
    /// finais que cabem. A última parte fica sempre inteira (quem desenha ainda corta o fim se ela sozinha não couber).
    /// </summary>
    public static string Middle(string path, int maxChars)
    {
        if (string.IsNullOrEmpty(path) || path.Length <= maxChars) return path;
        var separator = path.Contains('\\', StringComparison.Ordinal) ? '\\' : '/';
        var root = Root(path, separator);
        var parts = path[root.Length..].Split(separator, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 1) return path;
        var tail = parts[^1];
        var prefix = root + Mark + separator;
        for (var i = parts.Length - 2; i >= 1; i--)
        {
            var longer = parts[i] + separator + tail;
            if (prefix.Length + longer.Length > maxChars) break;
            tail = longer;
        }
        return prefix + tail;
    }

    /// <summary>"C:\", "\\servidor\pasta\" ou "/": o que identifica onde o caminho está.</summary>
    private static string Root(string path, char separator)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var server = path.IndexOf('\\', 2);
            var share = server < 0 ? -1 : path.IndexOf('\\', server + 1);
            return share < 0 ? path : path[..(share + 1)];
        }
        if (path.Length >= 3 && path[1] == ':' && path[2] == separator) return path[..3];
        return path[0] == separator ? path[..1] : string.Empty;
    }
}
