using System.Security.Cryptography;
using System.Text;

namespace ControlFS.Infrastructure.Remote;

/// <summary>
/// A página do celular (#223): um único HTML com estilo e script embutidos (a biblioteca @noble vai dentro do script),
/// sem nada de fora (CDN, fontes, imagens). O hash do script entra na Content-Security-Policy, então só este script roda.
/// </summary>
internal static class CompanionPage
{
    private const string LibraryMarker = "/*@NOBLE@*/";

    private static readonly Lazy<(byte[] Html, string Hash)> Page = new(Build);

    public static byte[] Html => Page.Value.Html;

    /// <summary>'sha256-…' do conteúdo do único &lt;script&gt; da página.</summary>
    public static string ScriptHash => Page.Value.Hash;

    private static (byte[], string) Build() => Compose(Read("ControlFS.Companion.phone.html"), Read("ControlFS.Companion.noble.js"));

    /// <summary>
    /// Monta a página com a biblioteca dentro do script. Quebras de linha viram LF: o navegador normaliza CRLF para LF
    /// ao ler o HTML e calcula o hash do script sobre o texto normalizado, então um recurso embutido com CRLF (checkout
    /// do Git no Windows com autocrlf) teria hash diferente do declarado na CSP e o script seria bloqueado (#259).
    /// </summary>
    internal static (byte[] Html, string Hash) Compose(string page, string library)
    {
        var html = page.Replace(LibraryMarker, library, StringComparison.Ordinal).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var start = html.IndexOf("<script>", StringComparison.Ordinal) + "<script>".Length;
        var end = html.IndexOf("</script>", start, StringComparison.Ordinal);
        if (start < "<script>".Length || end < 0 || html.IndexOf("<script", end, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException("A página do celular precisa de exatamente um <script>.");
        var hash = "sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(html[start..end])));
        return (Encoding.UTF8.GetBytes(html), hash);
    }

    private static string Read(string name)
    {
        using var stream = typeof(CompanionPage).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"Recurso ausente: {name}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
