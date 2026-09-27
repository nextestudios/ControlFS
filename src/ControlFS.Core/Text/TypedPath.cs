namespace ControlFS.Core.Text;

/// <summary>
/// Caminho digitado ou colado em "Ir para caminho…": aceita aspas em volta (Copiar como caminho do Explorador),
/// espaços nas pontas e variáveis como %USERPROFILE%; exige caminho completo (unidade ou \\servidor\pasta).
/// </summary>
public static class TypedPath
{
    public static (string? Path, string? Error) Normalize(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"') trimmed = trimmed[1..^1].Trim();
        if (trimmed.Length == 0) return (null, "Digite um caminho, como C:\\Users.");
        if (trimmed.StartsWith(@"\\?\", StringComparison.Ordinal) || trimmed.StartsWith(@"\\.\", StringComparison.Ordinal))
            return (null, @"Caminhos de dispositivo (\\?\ ou \\.\) não são aceitos: digite o caminho normal da pasta.");
        var expanded = Environment.ExpandEnvironmentVariables(trimmed);
        // "C:" sozinho é relativo à pasta atual daquela unidade no Windows: vira a raiz, que é o que a pessoa quis.
        if (expanded.Length == 2 && expanded[1] == ':' && char.IsAsciiLetter(expanded[0])) expanded += System.IO.Path.DirectorySeparatorChar;
        if (!System.IO.Path.IsPathFullyQualified(expanded))
            return (null, "Use o caminho completo, começando pela unidade (C:\\…) ou por \\\\servidor\\pasta.");
        try
        {
            return (System.IO.Path.GetFullPath(expanded), null);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return (null, "Caminho inválido: verifique os caracteres digitados.");
        }
    }
}
