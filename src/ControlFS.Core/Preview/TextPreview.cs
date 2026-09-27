using System.Globalization;
using System.Text;

namespace ControlFS.Core.Preview;

/// <summary>Texto pronto para a visualização: linhas (tabulações já expandidas), codificação detectada e se é parcial.</summary>
public sealed record TextDocument(IReadOnlyList<string> Lines, string EncodingName, bool IsTruncated, long FileBytes, long BytesRead);

/// <summary>
/// Leitura limitada de arquivos de texto para a visualização (#58): nunca lê mais que <see cref="PreviewLimits.MaxTextBytes"/>
/// nem guarda mais que <see cref="PreviewLimits.MaxTextLines"/> linhas. Detecta a codificação (BOM, UTF-8 válido, UTF-16
/// sem BOM, ANSI) e recusa arquivos binários. Somente leitura; nada é interpretado ou executado.
/// </summary>
public static class TextPreview
{
    public const int TabSize = 4;

    /// <summary>Extensões que o Sul abre direto na visualização de texto (as demais só pelo menu, se não forem binárias).</summary>
    public static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".text", ".md", ".markdown", ".log", ".json", ".jsonc", ".xml", ".csv", ".tsv", ".ini", ".cfg", ".conf", ".config",
        ".yaml", ".yml", ".toml", ".nfo", ".srt", ".sub", ".vtt", ".cue", ".m3u", ".m3u8", ".diz", ".properties", ".env",
        ".cs", ".csproj", ".sln", ".slnx", ".props", ".targets", ".c", ".h", ".cpp", ".hpp", ".java", ".kt", ".go", ".rs", ".ts",
        ".css", ".scss", ".html", ".htm", ".sql", ".lua", ".gitignore", ".editorconfig",
    };

    public static bool IsTextExtension(string extension) => Extensions.Contains(extension);

    /// <summary>Lê o início do arquivo dentro dos limites.</summary>
    /// <exception cref="PreviewException">Arquivo binário (ou que não parece texto).</exception>
    public static TextDocument Read(Stream stream, PreviewLimits limits)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(limits);
        var buffer = new byte[limits.MaxTextBytes + 1]; // um byte a mais revela se o arquivo continua
        var read = 0;
        while (read < buffer.Length)
        {
            var n = stream.Read(buffer, read, buffer.Length - read);
            if (n <= 0) break;
            read += n;
        }
        var cutByBytes = read > limits.MaxTextBytes;
        var bytes = buffer.AsSpan(0, Math.Min(read, limits.MaxTextBytes));
        var fileBytes = stream.CanSeek ? stream.Length : read;

        var (encoding, name, preamble) = Detect(bytes);
        if (encoding is null) throw new PreviewException("Este arquivo parece binário (não é texto) e não pode ser visualizado como texto.");
        var body = bytes[preamble..];
        if (cutByBytes) body = body[..CompleteLength(body, encoding)];
        var text = encoding.GetString(body);
        if (LooksBinary(text)) throw new PreviewException("Este arquivo parece binário (não é texto) e não pode ser visualizado como texto.");

        var lines = new List<string>();
        var truncated = cutByBytes;
        using (var reader = new StringReader(text))
        {
            while (reader.ReadLine() is { } line)
            {
                if (lines.Count == limits.MaxTextLines)
                {
                    truncated = true;
                    break;
                }
                lines.Add(ExpandTabs(line));
            }
        }
        return new TextDocument(lines, name, truncated, fileBytes, bytes.Length);
    }

    /// <summary>Aviso de prévia parcial ("mostrando as primeiras N linhas"), ou null quando o arquivo inteiro está na tela.</summary>
    public static string? TruncationNotice(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!document.IsTruncated) return null;
        var size = document.FileBytes >= 1024 * 1024
            ? string.Create(CultureInfo.CurrentCulture, $"{document.FileBytes / (1024.0 * 1024):0.#} MB")
            : string.Create(CultureInfo.CurrentCulture, $"{document.FileBytes / 1024.0:0.#} KB");
        return string.Create(CultureInfo.CurrentCulture,
            $"Prévia parcial: mostrando as primeiras {document.Lines.Count:N0} linhas de um arquivo de {size}. Abra com o aplicativo padrão para ver tudo.");
    }

    internal static (Encoding? Encoding, string Name, int Preamble) Detect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) return (StrictUtf8, "UTF-8 com BOM", 3);
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE])) return (Encoding.Unicode, "UTF-16 LE", 2);
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF])) return (Encoding.BigEndianUnicode, "UTF-16 BE", 2);
        if (Utf16WithoutBom(bytes) is { } utf16) return utf16;
        if (bytes.Contains((byte)0)) return (null, string.Empty, 0); // NUL fora de UTF-16: binário
        if (IsValidUtf8(bytes)) return (StrictUtf8, "UTF-8", 0);
        var ansi = AnsiEncoding();
        return (ansi, $"ANSI ({ansi.WebName})", 0);
    }

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    /// <summary>UTF-16 sem BOM: texto latino tem um byte zero em quase toda posição par (BE) ou ímpar (LE).</summary>
    private static (Encoding, string, int)? Utf16WithoutBom(ReadOnlySpan<byte> bytes)
    {
        var sample = bytes[..(Math.Min(bytes.Length, 4096) & ~1)];
        if (sample.Length < 4) return null;
        int evenZeros = 0, oddZeros = 0;
        for (var i = 0; i < sample.Length; i += 2)
        {
            if (sample[i] == 0) evenZeros++;
            if (sample[i + 1] == 0) oddZeros++;
        }
        var pairs = sample.Length / 2;
        if (oddZeros > pairs * 0.4 && evenZeros < pairs * 0.05) return (Encoding.Unicode, "UTF-16 LE", 0);
        if (evenZeros > pairs * 0.4 && oddZeros < pairs * 0.05) return (Encoding.BigEndianUnicode, "UTF-16 BE", 0);
        return null;
    }

    /// <summary>UTF-8 válido, aceitando uma sequência incompleta no fim (o limite de bytes pode cortar um caractere).</summary>
    private static bool IsValidUtf8(ReadOnlySpan<byte> bytes) => System.Text.Unicode.Utf8.IsValid(bytes[..CompleteLength(bytes, StrictUtf8)]);

    /// <summary>Tamanho sem o caractere cortado no fim (UTF-8: sequência incompleta; UTF-16: byte ou par substituto soltos).</summary>
    private static int CompleteLength(ReadOnlySpan<byte> bytes, Encoding encoding)
    {
        if (encoding is UnicodeEncoding)
        {
            var even = bytes.Length & ~1;
            if (even < 2) return even;
            var bigEndian = encoding.CodePage == 1201;
            var last = bigEndian ? bytes[even - 2] : bytes[even - 1]; // byte alto da última unidade
            return last is >= 0xD8 and <= 0xDB ? even - 2 : even; // substituto alto sem o par
        }
        if (encoding is not UTF8Encoding) return bytes.Length;
        for (var back = 1; back <= Math.Min(3, bytes.Length); back++)
        {
            var b = bytes[^back];
            if ((b & 0xC0) == 0x80) continue; // byte de continuação
            var needed = b >= 0xF0 ? 4 : b >= 0xE0 ? 3 : b >= 0xC0 ? 2 : 1;
            return needed > back ? bytes.Length - back : bytes.Length;
        }
        return bytes.Length;
    }

    /// <summary>Muitos caracteres de controle (fora tabulação, quebras de linha, form feed e ESC): não é texto.</summary>
    internal static bool LooksBinary(string text)
    {
        if (text.Length == 0) return false;
        var sample = text.AsSpan(0, Math.Min(text.Length, 64 * 1024));
        var control = 0;
        foreach (var c in sample)
            if (c is '�' or < ' ' and not ('\t' or '\n' or '\r' or '\f' or '\v' or '\u001B'))
                control++;
        return control > Math.Max(8, sample.Length / 50);
    }

    private static Encoding AnsiEncoding()
    {
        var codePage = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
        try
        {
            return CodePagesEncodingProvider.Instance.GetEncoding(codePage) ?? CodePagesEncodingProvider.Instance.GetEncoding(1252) ?? Encoding.Latin1;
        }
        catch (ArgumentException)
        {
            return Encoding.Latin1;
        }
    }

    public static string ExpandTabs(string line)
    {
        if (!line.Contains('\t', StringComparison.Ordinal)) return line;
        var sb = new StringBuilder(line.Length + 16);
        foreach (var c in line)
        {
            if (c == '\t') sb.Append(' ', TabSize - sb.Length % TabSize);
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
