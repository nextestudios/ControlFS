using System.Globalization;
using System.Text;

namespace ControlFS.Core.Policies;

public enum NameProblem
{
    None,
    Empty,
    TooLong,
    InvalidCharacter,
    ControlCharacter,
    ReservedDeviceName,
    TrailingDotOrSpace,
    DotOrDotDot,
    StreamSyntax,
}

public sealed record NameValidation(NameProblem Problem, string Message)
{
    public bool IsValid => Problem == NameProblem.None;

    public static NameValidation Ok { get; } = new(NameProblem.None, string.Empty);
}

/// <summary>
/// Regras de nome de um componente de caminho no Windows (NTFS/FAT sob Win32), aplicadas
/// independentemente do sistema hospedeiro. Referência: "Naming Files, Paths, and Namespaces".
/// </summary>
public static class WindowsNameRules
{
    public const int MaxComponentLength = 255;

    private const string InvalidChars = "<>:\"/\\|?*";

    private static readonly HashSet<string> ReservedBaseNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    public static NameValidation ValidateComponent(string name)
    {
        if (string.IsNullOrEmpty(name)) return new(NameProblem.Empty, "O nome não pode ficar vazio.");
        if (name is "." or "..") return new(NameProblem.DotOrDotDot, "\".\" e \"..\" não são nomes válidos.");
        if (name.Length > MaxComponentLength) return new(NameProblem.TooLong, $"O nome excede {MaxComponentLength} caracteres.");
        foreach (var c in name)
        {
            if (c < 32 || c == 127) return new(NameProblem.ControlCharacter, "O nome contém caractere de controle.");
            if (c == ':') return new(NameProblem.StreamSyntax, "O caractere \":\" não é permitido (fluxos alternativos e unidades).");
            if (InvalidChars.Contains(c, StringComparison.Ordinal)) return new(NameProblem.InvalidCharacter, $"O caractere \"{c}\" não é permitido em nomes.");
        }
        if (name[^1] is '.' or ' ') return new(NameProblem.TrailingDotOrSpace, "O nome não pode terminar com ponto ou espaço.");
        if (IsReservedDeviceName(name)) return new(NameProblem.ReservedDeviceName, $"\"{name}\" é um nome reservado do Windows.");
        return NameValidation.Ok;
    }

    /// <summary>CON, NUL.txt, "COM1 .log" etc. O Windows ignora extensões e espaços finais ao comparar.</summary>
    public static bool IsReservedDeviceName(string name)
    {
        var dot = name.IndexOf('.', StringComparison.Ordinal);
        var baseName = (dot >= 0 ? name[..dot] : name).TrimEnd(' ');
        return ReservedBaseNames.Contains(baseName);
    }

    /// <summary>
    /// Chave para detectar colisões em um filesystem insensível a caixa: normalização Unicode NFC
    /// e maiúsculas invariantes. Nomes distintos com a mesma chave não podem ir para o mesmo destino.
    /// </summary>
    public static string CollisionKey(string relativePath) =>
        relativePath.Normalize(NormalizationForm.FormC).ToUpper(CultureInfo.InvariantCulture);
}
