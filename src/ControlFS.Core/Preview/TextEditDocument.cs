using System.Globalization;
using System.Text;

namespace ControlFS.Core.Preview;

/// <summary>Uma linha como está no arquivo: o texto (tabulações preservadas) e a quebra que a termina ("" na última sem quebra).</summary>
public sealed record TextLine(string Text, string Ending);

/// <summary>
/// Arquivo de texto aberto para edição leve (#62). Carrega o arquivo inteiro dentro de limites e só aceita o que pode ser
/// regravado byte a byte igual (mesma codificação, BOM e quebras de linha): linhas que o usuário não tocou nunca mudam.
/// Binários, arquivos grandes demais e codificações que não fazem ida e volta são recusados com o motivo.
/// </summary>
public sealed class TextEditDocument
{
    /// <summary>Tamanho máximo de um arquivo editável.</summary>
    public const int MaxBytes = 1024 * 1024;

    /// <summary>Linhas máximas de um arquivo editável.</summary>
    public const int MaxLines = 10_000;

    /// <summary>Caracteres máximos de uma linha editada pelo teclado virtual.</summary>
    public const int MaxLineLength = 4096;

    private readonly Encoding _encoding;
    private readonly byte[] _preamble;

    private TextEditDocument(IReadOnlyList<TextLine> lines, Encoding encoding, byte[] preamble, string encodingName, string newLine)
    {
        Lines = lines;
        _encoding = encoding;
        _preamble = preamble;
        EncodingName = encodingName;
        NewLine = newLine;
    }

    /// <summary>Linhas do arquivo como foi lido.</summary>
    public IReadOnlyList<TextLine> Lines { get; }

    public string EncodingName { get; }

    /// <summary>Quebra de linha predominante (usada nas linhas novas).</summary>
    public string NewLine { get; }

    /// <exception cref="PreviewException">Arquivo que não pode ser editado aqui; a mensagem explica o motivo.</exception>
    public static TextEditDocument Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (stream.CanSeek && stream.Length > MaxBytes)
            throw new PreviewException(string.Create(CultureInfo.CurrentCulture, $"Arquivo grande demais para editar aqui (limite de {MaxBytes / 1024} KB). Use o aplicativo padrão."));
        var buffer = new byte[MaxBytes + 1];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = stream.Read(buffer, read, buffer.Length - read);
            if (n <= 0) break;
            read += n;
        }
        if (read > MaxBytes)
            throw new PreviewException(string.Create(CultureInfo.CurrentCulture, $"Arquivo grande demais para editar aqui (limite de {MaxBytes / 1024} KB). Use o aplicativo padrão."));
        var bytes = buffer.AsSpan(0, read);

        var (encoding, name, preambleLength) = TextPreview.Detect(bytes);
        if (encoding is null) throw new PreviewException("Este arquivo parece binário: só arquivos de texto podem ser editados.");
        var preamble = bytes[..preambleLength].ToArray();
        var body = bytes[preambleLength..];
        if (encoding is UnicodeEncoding && body.Length % 2 != 0)
            throw new PreviewException("O arquivo termina no meio de um caractere UTF-16: não dá para regravá-lo igual. Use o aplicativo padrão.");
        var text = encoding.GetString(body);
        if (TextPreview.LooksBinary(text)) throw new PreviewException("Este arquivo parece binário: só arquivos de texto podem ser editados.");

        var lines = Split(text);
        if (lines.Count > MaxLines)
            throw new PreviewException(string.Create(CultureInfo.CurrentCulture, $"Arquivo com linhas demais para editar aqui (limite de {MaxLines:N0}). Use o aplicativo padrão."));
        var newLine = lines.GroupBy(l => l.Ending).Where(g => g.Key.Length > 0).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault()
            ?? Environment.NewLine;
        var document = new TextEditDocument(lines, encoding, preamble, name, newLine);
        // Só edita o que volta igual: nenhum byte fora das linhas alteradas pode mudar ao salvar.
        if (!document.Encode(lines).AsSpan().SequenceEqual(bytes))
            throw new PreviewException("A codificação deste arquivo não pode ser regravada sem alterar outros caracteres. Use o aplicativo padrão.");
        return document;
    }

    /// <summary>Bytes do arquivo com estas linhas, na codificação, BOM e quebras originais.</summary>
    public byte[] Encode(IEnumerable<TextLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var text = new StringBuilder();
        foreach (var line in lines) text.Append(line.Text).Append(line.Ending);
        var body = _encoding.GetBytes(text.ToString());
        var result = new byte[_preamble.Length + body.Length];
        _preamble.CopyTo(result, 0);
        body.CopyTo(result, _preamble.Length);
        return result;
    }

    /// <summary>Quebra preservando cada terminador (\r\n, \n ou \r); um arquivo vazio tem uma linha vazia.</summary>
    private static List<TextLine> Split(string text)
    {
        var lines = new List<TextLine>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\r' or '\n')) continue;
            var ending = text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? "\r\n" : text[i].ToString();
            lines.Add(new TextLine(text[start..i], ending));
            i += ending.Length - 1;
            start = i + 1;
        }
        if (start < text.Length || lines.Count == 0) lines.Add(new TextLine(text[start..], string.Empty));
        return lines;
    }
}
