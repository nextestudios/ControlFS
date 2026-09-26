namespace ControlFS.Core.Policies;

public enum PathRejection
{
    None,
    Empty,
    NullByte,
    Absolute,
    DriveQualified,
    UncOrDeviceNamespace,
    Traversal,
    EmptyComponent,
    InvalidComponent,
    TooDeep,
    TooLong,
}

public sealed record SanitizedPath(IReadOnlyList<string> Components, PathRejection Rejection, string Message)
{
    public bool IsAccepted => Rejection == PathRejection.None;

    public string RelativePath => string.Join('/', Components);

    public static SanitizedPath Reject(PathRejection rejection, string message) => new([], rejection, message);
}

/// <summary>
/// Converte o nome (não confiável) de uma entrada de compactado em uma sequência de componentes
/// seguros, ou rejeita. Nunca "corrige" silenciosamente: qualquer forma ambígua é recusada.
/// A contenção é garantida por construção: o resultado é uma lista de componentes validados
/// individualmente, sem "..", sem raiz, sem unidade e sem namespace de dispositivo.
/// </summary>
public static class ArchivePathPolicy
{
    public static SanitizedPath Sanitize(string rawKey, int maxDepth, int maxRelativeLength)
    {
        if (string.IsNullOrEmpty(rawKey)) return SanitizedPath.Reject(PathRejection.Empty, "Entrada sem nome.");
        if (rawKey.Contains('\0', StringComparison.Ordinal)) return SanitizedPath.Reject(PathRejection.NullByte, "Nome contém byte nulo.");

        // Ambos os separadores são tratados como separadores (Windows aceita os dois).
        var key = rawKey.Replace('\\', '/');

        if (key.StartsWith("//", StringComparison.Ordinal))
            return SanitizedPath.Reject(PathRejection.UncOrDeviceNamespace, "Caminho UNC ou de dispositivo não é permitido.");
        if (key.StartsWith('/'))
            return SanitizedPath.Reject(PathRejection.Absolute, "Caminho absoluto não é permitido.");
        if (key.Length >= 2 && char.IsAsciiLetter(key[0]) && key[1] == ':')
            return SanitizedPath.Reject(PathRejection.DriveQualified, "Prefixo de unidade (ex.: C: ou C:arquivo) não é permitido.");

        // Barra final indica diretório; é a única forma de componente vazio aceita.
        if (key.EndsWith('/')) key = key[..^1];
        if (key.Length == 0) return SanitizedPath.Reject(PathRejection.Empty, "Entrada sem nome.");

        var parts = key.Split('/');
        var components = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            if (part.Length == 0) return SanitizedPath.Reject(PathRejection.EmptyComponent, "Componente vazio no caminho (\"//\").");
            if (part == ".") continue;
            if (part == "..") return SanitizedPath.Reject(PathRejection.Traversal, "Travessia \"..\" não é permitida.");
            var validation = WindowsNameRules.ValidateComponent(part);
            if (!validation.IsValid) return SanitizedPath.Reject(PathRejection.InvalidComponent, $"\"{part}\": {validation.Message}");
            components.Add(part);
        }

        if (components.Count == 0) return SanitizedPath.Reject(PathRejection.Empty, "Entrada sem nome efetivo.");
        if (components.Count > maxDepth) return SanitizedPath.Reject(PathRejection.TooDeep, $"Profundidade acima do limite ({maxDepth}).");
        var length = components.Sum(c => c.Length) + components.Count - 1;
        if (length > maxRelativeLength) return SanitizedPath.Reject(PathRejection.TooLong, $"Caminho acima do limite ({maxRelativeLength} caracteres).");

        return new SanitizedPath(components, PathRejection.None, string.Empty);
    }
}
