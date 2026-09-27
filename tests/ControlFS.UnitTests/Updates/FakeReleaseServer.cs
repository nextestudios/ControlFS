using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ControlFS.Infrastructure.Updates;

namespace ControlFS.UnitTests.Updates;

/// <summary>Servidor falso de releases do GitHub com chave de assinatura própria do teste (nunca a real).</summary>
public sealed class FakeReleaseServer : HttpMessageHandler
{
    public const string Repo = "nextestudios/ControlFS";
    private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new(StringComparer.Ordinal);
    private readonly List<object> _releases = [];

    public FakeReleaseServer()
    {
        Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Trust = UpdateTrust.Official with { PublicKeysPem = [Key.ExportSubjectPublicKeyInfoPem()] };
        Route($"https://api.github.com/repos/{Repo}/releases?per_page=20", () => Json(JsonSerializer.Serialize(_releases)));
    }

    public ECDsa Key { get; }
    public UpdateTrust Trust { get; }
    public List<string> Requests { get; } = [];

    public static byte[] Installer(string text) => Encoding.UTF8.GetBytes("MZ-fake-installer-" + text);

    public void Route(string url, Func<HttpResponseMessage> response) => _routes[url] = response;

    /// <summary>Publica uma release com manifesto assinado. Os delegates permitem adulterar partes.</summary>
    public void Publish(string version, byte[] installer, bool prerelease = false, Func<string, string>? tamperManifest = null,
        string? manifestVersion = null, ECDsa? signWith = null, bool withSignature = true)
    {
        var baseUrl = $"https://github.com/{Repo}/releases/download/v{version}/";
        var manifest = JsonSerializer.Serialize(new
        {
            schema = 1,
            product = "ControlFS",
            repository = Repo,
            version = manifestVersion ?? version,
            releasedAt = "2026-09-26T20:00:00Z",
            installer = new { name = UpdateTrust.InstallerAssetName, size = installer.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(installer)) },
        });
        var signed = Encoding.UTF8.GetBytes(manifest);
        var signature = Convert.ToBase64String((signWith ?? Key).SignData(signed, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        var served = tamperManifest is null ? signed : Encoding.UTF8.GetBytes(tamperManifest(manifest));
        var assets = new List<object>
        {
            new { name = UpdateTrust.ManifestAssetName, browser_download_url = baseUrl + UpdateTrust.ManifestAssetName },
            new { name = UpdateTrust.InstallerAssetName, browser_download_url = baseUrl + UpdateTrust.InstallerAssetName },
        };
        if (withSignature) assets.Add(new { name = UpdateTrust.SignatureAssetName, browser_download_url = baseUrl + UpdateTrust.SignatureAssetName });
        _releases.Add(new { tag_name = "v" + version, draft = false, prerelease, assets });
        // Como no GitHub real: o link de download redireciona para o host de armazenamento.
        foreach (var (name, body) in new[] { (UpdateTrust.ManifestAssetName, served), (UpdateTrust.SignatureAssetName, Encoding.ASCII.GetBytes(signature)), (UpdateTrust.InstallerAssetName, installer) })
        {
            var storage = $"https://release-assets.githubusercontent.com/{version}/{name}";
            Route(baseUrl + name, () => Redirect(storage));
            Route(storage, () => Bytes(body));
        }
    }

    public static HttpResponseMessage Redirect(string to) => new(HttpStatusCode.Found) { Headers = { Location = new Uri(to) } };
    public static HttpResponseMessage Bytes(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        Requests.Add(url);
        return Task.FromResult(_routes.TryGetValue(url, out var r) ? r() : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Key.Dispose();
        base.Dispose(disposing);
    }
}
