using System.Net;
using ControlFS.Core.Contracts;

namespace ControlFS.Infrastructure.Updates;

/// <summary>
/// HTTP somente por HTTPS, para hosts permitidos, seguindo redirecionamentos manualmente (cada destino é conferido)
/// e com limite de bytes lidos. Não envia cookies nem identificadores; apenas User-Agent com a versão.
/// </summary>
internal sealed class RestrictedHttp(HttpMessageHandler handler, UpdateTrust trust, string userAgent) : IDisposable
{
    private const int MaxRedirects = 5;
    private readonly HttpClient _client = new(handler, disposeHandler: true) { Timeout = TimeSpan.FromMinutes(10) };

    public async Task<HttpResponseMessage> GetAsync(Uri uri, string accept, CancellationToken ct)
    {
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            EnsureAllowed(uri);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd(userAgent);
            request.Headers.Accept.ParseAdd(accept);
            var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location is null) throw new UpdateException("Redirecionamento sem destino.");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                response.Dispose();
                throw new UpdateException($"O servidor de atualizações respondeu {status}.");
            }
            return response;
        }
        throw new UpdateException("Redirecionamentos demais.");
    }

    public async Task<byte[]> GetBytesAsync(Uri uri, string accept, int maxBytes, CancellationToken ct)
    {
        using var response = await GetAsync(uri, accept, ct).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength > maxBytes) throw new UpdateException("Resposta maior que o permitido.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxBytes) throw new UpdateException("Resposta maior que o permitido.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private void EnsureAllowed(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || !trust.AllowedHosts.Contains(uri.Host))
            throw new UpdateException($"Origem de atualização não permitida: {uri.Host}");
    }

    public void Dispose() => _client.Dispose();
}
