using System.Security.Cryptography;
using System.Text;

namespace ControlFS.Infrastructure.Remote;

internal enum HttpParseStatus
{
    /// <summary>Cabeçalho ainda não terminou (sem linha em branco).</summary>
    Incomplete,
    Ok,
    Invalid,
    TooLarge,
}

internal sealed record HttpRequestHead(string Method, string Target, IReadOnlyDictionary<string, string> Headers)
{
    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
}

/// <summary>
/// Leitor mínimo de HTTP/1.1 para as duas rotas do celular (#223): só GET, sem corpo, só ASCII, CRLF, cabeçalho de no
/// máximo 4 KB e 32 campos, nome de campo repetido recusado. Tudo o que foge disso é inválido e a conexão fecha — o
/// servidor não precisa entender o resto do HTTP.
/// </summary>
internal static class HttpRequestParser
{
    public const int MaxHeadBytes = 4096;
    public const int MaxHeaders = 32;
    public const int MaxTargetLength = 128;

    public static HttpParseStatus TryParse(ReadOnlySpan<byte> buffer, out HttpRequestHead? head)
    {
        head = null;
        var end = buffer.IndexOf("\r\n\r\n"u8);
        if (end < 0) return buffer.Length >= MaxHeadBytes ? HttpParseStatus.TooLarge : HttpParseStatus.Incomplete;
        if (end + 4 > MaxHeadBytes) return HttpParseStatus.TooLarge;
        if (end + 4 != buffer.Length) return HttpParseStatus.Invalid; // nada depois do cabeçalho (sem corpo, sem pedidos em fila)
        foreach (var b in buffer[..end])
            if (b is not ((>= 0x20 and <= 0x7E) or (byte)'\t' or (byte)'\r' or (byte)'\n')) return HttpParseStatus.Invalid;

        var lines = Encoding.ASCII.GetString(buffer[..end]).Split("\r\n");
        foreach (var line in lines)
            if (line.Contains('\r', StringComparison.Ordinal) || line.Contains('\n', StringComparison.Ordinal)) return HttpParseStatus.Invalid; // CR ou LF soltos

        var request = lines[0].Split(' ');
        if (request.Length != 3 || request[0] != "GET" || request[2] != "HTTP/1.1" || !IsSafeTarget(request[1])) return HttpParseStatus.Invalid;
        if (lines.Length - 1 > MaxHeaders) return HttpParseStatus.Invalid;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0) return HttpParseStatus.Invalid;
            var name = line[..colon];
            if (!name.All(IsTokenChar)) return HttpParseStatus.Invalid; // inclui espaço antes do ':' e linhas dobradas
            if (!headers.TryAdd(name, line[(colon + 1)..].Trim(' ', '\t'))) return HttpParseStatus.Invalid;
        }
        if (headers.ContainsKey("Transfer-Encoding")) return HttpParseStatus.Invalid;
        if (headers.TryGetValue("Content-Length", out var length) && length != "0") return HttpParseStatus.Invalid;
        head = new HttpRequestHead(request[0], request[1], headers);
        return HttpParseStatus.Ok;
    }

    /// <summary>Caminho sem consulta, sem "..", só letras, dígitos, '/', '-' e '_'.</summary>
    private static bool IsSafeTarget(string target) =>
        target.Length is > 0 and <= MaxTargetLength && target[0] == '/' && !target.Contains("//", StringComparison.Ordinal)
        && target.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_');

    private static bool IsTokenChar(char c) => char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-.^_`|~".Contains(c, StringComparison.Ordinal);

    /// <summary>
    /// Lê o cabeçalho de um pedido. Null: inválido, grande demais, conexão fechada ou prazo esgotado (o chamador fecha).
    /// </summary>
    public static async Task<HttpRequestHead?> ReadAsync(Stream stream, CancellationToken cancellation)
    {
        var buffer = new byte[MaxHeadBytes];
        var filled = 0;
        while (filled < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(filled), cancellation).ConfigureAwait(false);
            if (read == 0) return null;
            filled += read;
            var status = TryParse(buffer.AsSpan(0, filled), out var head);
            if (status == HttpParseStatus.Ok) return head;
            if (status != HttpParseStatus.Incomplete) return null;
        }
        return null;
    }

    /// <summary>Sec-WebSocket-Accept (RFC 6455 §4.2.2). Null: chave ausente ou que não é base64 de 16 bytes.</summary>
    public static string? WebSocketAccept(string? key)
    {
        if (key is null || key.Length != 24) return null;
        try
        {
            if (Convert.FromBase64String(key).Length != 16) return null;
        }
        catch (FormatException)
        {
            return null;
        }
#pragma warning disable CA5350 // SHA-1 é exigido pelo protocolo WebSocket; não protege nada aqui (a segurança vem dos quadros cifrados)
        return Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
#pragma warning restore CA5350
    }

    /// <summary>O cabeçalho pede o upgrade para WebSocket versão 13.</summary>
    public static bool IsWebSocketUpgrade(HttpRequestHead head) =>
        string.Equals(head.Header("Upgrade"), "websocket", StringComparison.OrdinalIgnoreCase)
        && (head.Header("Connection") ?? string.Empty).Split(',').Any(t => string.Equals(t.Trim(), "Upgrade", StringComparison.OrdinalIgnoreCase))
        && head.Header("Sec-WebSocket-Version") == "13";
}
