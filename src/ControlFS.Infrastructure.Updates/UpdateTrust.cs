namespace ControlFS.Infrastructure.Updates;

/// <summary>
/// Âncoras de confiança das atualizações. A chave privada correspondente existe apenas como secret
/// do GitHub Actions (UPDATE_SIGNING_KEY) e assina o manifesto de cada release na CI.
/// Impressão digital (SHA-256 do SubjectPublicKeyInfo DER): 8b841b19810a1a7e0fe417a34ce8fb1281125f52413576e5b4f4deba7c64f3e8
/// </summary>
public sealed record UpdateTrust(string PublicKeyPem, string Repository, IReadOnlySet<string> AllowedHosts)
{
    public const string InstallerAssetName = "ControlFS-Setup-x64.exe";
    public const string ManifestAssetName = "release-manifest.json";
    public const string SignatureAssetName = "release-manifest.json.sig";
    public const long MaxInstallerBytes = 1L << 30;

    public static UpdateTrust Official { get; } = new(
        """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEje4E9APUKixppzz1Cz+8YUhB1bSu
        Sh95vaUBQMgihvy1rzFyFplxHc++8eOmRfdvV9TqrsR6Gq+EB7e+ZHovPQ==
        -----END PUBLIC KEY-----
        """,
        "nextestudios/ControlFS",
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "api.github.com",
            "github.com",
            "objects.githubusercontent.com",
            "release-assets.githubusercontent.com",
        });
}
