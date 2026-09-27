namespace ControlFS.Core.Policies;

/// <summary>
/// Caminhos de ícone vindos de atalhos (.url <c>IconFile=</c>, .lnk IconLocation/destino) não são confiáveis: um atalho
/// deixado na Área de trabalho pode apontar para <c>\\servidor\x.ico</c> e, só por ler o ícone, o Windows enviaria as
/// credenciais do usuário (NTLM) para aquele servidor. Aqui fica a regra sintática: só caminho absoluto numa letra de
/// unidade (<c>C:\...</c>), sem UNC, sem prefixo de dispositivo (<c>\\?\</c>, <c>\\.\</c>, <c>\??\</c>), sem URL
/// (<c>http:</c>, <c>file:</c>), sem caminho relativo ou relativo à unidade (<c>C:x</c>), sem fluxo alternativo, sem
/// nome de dispositivo (NUL, COM1) e sem variável de ambiente por expandir. O tipo da unidade (só fixa) e a ausência de
/// links no caminho são conferidos na infraestrutura, antes de qualquer leitura.
/// </summary>
public static class IconLocationPolicy
{
    /// <summary>Caminho de ícone mais longo aceito.</summary>
    public const int MaxLength = 1024;

    private static readonly HashSet<string> IconExtensions = new(StringComparer.OrdinalIgnoreCase) { ".ico", ".exe", ".dll", ".icl", ".cpl" };

    /// <summary>Caminho local absoluto numa letra de unidade (barras normais viram invertidas), ou <c>null</c> se recusado.</summary>
    public static string? NormalizeLocal(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaxLength) return null;
        var normalized = path.Replace('/', '\\');
        if (normalized.Length < 4 || !char.IsAsciiLetter(normalized[0]) || normalized[1] != ':' || normalized[2] != '\\') return null;
        if (normalized.Contains('%', StringComparison.Ordinal)) return null;
        foreach (var component in normalized[3..].Split('\\'))
            if (!WindowsNameRules.ValidateComponent(component).IsValid) return null;
        return normalized;
    }

    /// <summary>Arquivo de onde se pode extrair um ícone (.ico, .exe, .dll…), num caminho local aceito por <see cref="NormalizeLocal"/>.</summary>
    public static string? NormalizeIconFile(string? path) =>
        NormalizeLocal(path) is { } local && IconExtensions.Contains(Path.GetExtension(local)) ? local : null;
}
