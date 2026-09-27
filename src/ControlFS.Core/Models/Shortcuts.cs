using System.Text;

namespace ControlFS.Core.Models;

/// <summary>
/// Conteúdo de um atalho da Internet (.url): seção <c>[InternetShortcut]</c> com <c>URL=</c>, <c>IconFile=</c> e
/// <c>IconIndex=</c>. O arquivo não é confiável (qualquer um pode deixar um .url na Área de trabalho): só é lido, com
/// limite de tamanho, e nunca executado. Abrir continua sendo do Windows, pelo próprio arquivo.
/// </summary>
public sealed record InternetShortcut(string? Url, string? IconFile, int IconIndex)
{
    /// <summary>Atalhos maiores que isso não são lidos (um .url real tem poucas centenas de bytes).</summary>
    public const int MaxFileBytes = 64 * 1024;

    /// <summary>Tamanho máximo de um valor guardado (URL, caminho do ícone); o resto é descartado.</summary>
    public const int MaxValueLength = 2048;

    /// <summary>Jogo da Steam: decidido só pelo esquema <c>steam://</c> da URL (ex.: <c>steam://rungameid/892970</c>).</summary>
    public bool IsSteamGame => Url is not null && Url.StartsWith("steam://", StringComparison.OrdinalIgnoreCase);

    /// <summary>Número do jogo em <c>steam://rungameid/&lt;id&gt;</c>, ou <c>null</c>.</summary>
    public string? SteamAppId
    {
        get
        {
            const string prefix = "steam://rungameid/";
            if (Url is null || !Url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            var id = Url[prefix.Length..].TrimEnd('/');
            return id.Length is > 0 and <= 20 && id.All(char.IsAsciiDigit) ? id : null;
        }
    }

    /// <summary>
    /// Lê o conteúdo de um .url (UTF-8, ANSI ou UTF-16 com BOM). Tolerante: linhas estranhas são ignoradas e vale a
    /// primeira ocorrência de cada chave, como no Windows. <c>null</c> quando o arquivo passa do limite ou não tem a seção.
    /// </summary>
    public static InternetShortcut? Parse(ReadOnlySpan<byte> content)
    {
        if (content.Length > MaxFileBytes) return null;
        string text;
        if (content.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE])) text = Encoding.Unicode.GetString(content[2..]);
        else if (content.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF])) text = Encoding.BigEndianUnicode.GetString(content[2..]);
        else if (content.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) text = Encoding.UTF8.GetString(content[3..]);
        else text = Encoding.UTF8.GetString(content);

        var inSection = false;
        var found = false;
        string? url = null, iconFile = null, iconIndex = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.Length == 0 || line[0] is ';' or '#') continue;
            if (line[0] == '[')
            {
                inSection = line.Equals("[InternetShortcut]", StringComparison.OrdinalIgnoreCase);
                found |= inSection;
                continue;
            }
            if (!inSection) continue;
            var eq = line.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            var value = Clean(line[(eq + 1)..]);
            if (key.Equals("URL", StringComparison.OrdinalIgnoreCase)) url ??= value;
            else if (key.Equals("IconFile", StringComparison.OrdinalIgnoreCase)) iconFile ??= value;
            else if (key.Equals("IconIndex", StringComparison.OrdinalIgnoreCase)) iconIndex ??= value;
        }
        if (!found) return null;
        var index = int.TryParse(iconIndex, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        return new InternetShortcut(url, iconFile, index);
    }

    private static string? Clean(string value)
    {
        var trimmed = value.Trim().Trim('"').Trim();
        if (trimmed.Length == 0 || trimmed.Length > MaxValueLength) return null;
        return trimmed.Any(char.IsControl) ? null : trimmed;
    }
}
