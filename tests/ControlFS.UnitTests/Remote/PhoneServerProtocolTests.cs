using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using ControlFS.Infrastructure.Remote;

namespace ControlFS.UnitTests.Remote;

/// <summary>
/// O servidor do celular (#259) falado como o Safari do iPhone fala, por um socket cru: a página, a sonda e o upgrade do
/// WebSocket com os cabeçalhos reais do navegador, e o que vai para o log quando algo é recusado.
/// </summary>
public class PhoneServerProtocolTests
{
    private const string SafariHeaders = "Accept-Language: pt-BR,pt;q=0.9\r\nAccept-Encoding: gzip, deflate\r\n"
        + "User-Agent: Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1\r\n";

    private static async Task<string> Exchange(int port, string request, bool untilHeadEnd = false)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request), timeout.Token);
        var received = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, timeout.Token);
            if (read == 0) break;
            received.Write(buffer, 0, read);
            if (untilHeadEnd && Encoding.UTF8.GetString(received.ToArray()).Contains("\r\n\r\n", StringComparison.Ordinal)) break;
        }
        return Encoding.UTF8.GetString(received.ToArray());
    }

    [Fact]
    public async Task Safari_style_requests_get_the_page_the_probe_and_the_websocket_upgrade()
    {
        var trace = new List<string>();
        using var server = new PhoneLinkServer(() => [IPAddress.Loopback], line => { lock (trace) trace.Add(line); });
        var pairing = server.Start();
        var uri = new Uri(pairing.Url[..pairing.Url.IndexOf('#', StringComparison.Ordinal)]);
        var host = uri.Authority;
        var key = pairing.Url[(pairing.Url.IndexOf("#k=", StringComparison.Ordinal) + 3)..];

        // Página: o hash da CSP tem de ser o do texto do <script> exatamente como o navegador o lê (LF; #259).
        var page = await Exchange(uri.Port, $"GET {uri.AbsolutePath} HTTP/1.1\r\nHost: {host}\r\nUpgrade-Insecure-Requests: 1\r\n"
            + $"Accept: text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8\r\n{SafariHeaders}Connection: keep-alive\r\n\r\n");
        Assert.StartsWith("HTTP/1.1 200", page, StringComparison.Ordinal);
        var body = page[(page.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4)..];
        Assert.False(body.Contains('\r', StringComparison.Ordinal));
        var script = body[(body.IndexOf("<script>", StringComparison.Ordinal) + 8)..body.IndexOf("</script>", StringComparison.Ordinal)];
        var hash = "sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(script)));
        Assert.Contains($"script-src '{hash}'", page, StringComparison.Ordinal);
        Assert.Contains($"connect-src ws://{host} http://{host}", page, StringComparison.Ordinal);

        Assert.StartsWith("HTTP/1.1 204", await Exchange(uri.Port, $"GET {uri.AbsolutePath}/ping HTTP/1.1\r\nHost: {host}\r\n{SafariHeaders}Connection: keep-alive\r\n\r\n"), StringComparison.Ordinal);

        // Upgrade como o Safari o envia: Connection só "Upgrade", extensão permessage-deflate oferecida (não ecoada na resposta).
        var upgrade = await Exchange(uri.Port, $"GET {uri.AbsolutePath}/ws HTTP/1.1\r\nHost: {host}\r\nConnection: Upgrade\r\nUpgrade: websocket\r\nOrigin: http://{host}\r\n"
            + "Sec-WebSocket-Version: 13\r\nSec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Extensions: permessage-deflate; client_max_window_bits\r\n"
            + $"{SafariHeaders}\r\n", untilHeadEnd: true);
        Assert.StartsWith("HTTP/1.1 101 Switching Protocols", upgrade, StringComparison.Ordinal);
        Assert.Contains("Sec-WebSocket-Accept: s3pPLMBiTxaQ9kYGzzhZRbK+xOo=", upgrade, StringComparison.Ordinal);
        Assert.DoesNotContain("Sec-WebSocket-Extensions", upgrade, StringComparison.OrdinalIgnoreCase);

        // Origin de outro site: recusado, e o motivo vai para o log sem chave nem id de sessão.
        Assert.StartsWith("HTTP/1.1 403", await Exchange(uri.Port, $"GET {uri.AbsolutePath}/ws HTTP/1.1\r\nHost: {host}\r\nConnection: Upgrade\r\nUpgrade: websocket\r\nOrigin: http://evil.example\r\n"
            + "Sec-WebSocket-Version: 13\r\nSec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n\r\n"), StringComparison.Ordinal);
        string[] lines;
        lock (trace) lines = [.. trace];
        Assert.Contains(lines, l => l.Contains("code=origin-mismatch", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains(key, StringComparison.Ordinal) || l.Contains(uri.AbsolutePath[1..], StringComparison.Ordinal));
    }
}
