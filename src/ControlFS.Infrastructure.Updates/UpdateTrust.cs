namespace ControlFS.Infrastructure.Updates;

/// <summary>
/// Âncoras de confiança das atualizações: um manifesto é aceito se QUALQUER uma das chaves públicas de
/// <see cref="PublicKeysPem"/> confere a assinatura (rotação de chave, #86 — procedimento em docs/decisions/0006).
/// A chave privada em uso existe apenas como secret do GitHub Actions (UPDATE_SIGNING_KEY) e assina o manifesto de cada
/// release na CI. Impressões digitais (SHA-256 do SubjectPublicKeyInfo DER), na ordem da lista:
/// 1. 8b841b19810a1a7e0fe417a34ce8fb1281125f52413576e5b4f4deba7c64f3e8 (chave original, em uso)
/// </summary>
public sealed record UpdateTrust(IReadOnlyList<string> PublicKeysPem, string Repository, IReadOnlySet<string> AllowedHosts)
{
    /// <summary>Uma chave em uso e, durante uma rotação, a próxima (ou a anterior). Mais que isso é engano de configuração.</summary>
    public const int MaxTrustedKeys = 4;

    public const string InstallerAssetName = "ControlFS-Setup-x64.exe";
    public const string ManifestAssetName = "release-manifest.json";
    public const string SignatureAssetName = "release-manifest.json.sig";
    public const long MaxInstallerBytes = 1L << 30;

    public static UpdateTrust Official { get; } = new(
        [
            // 1. Chave original (em uso). Numa rotação, a próxima chave entra aqui ANTES de trocar o secret.
            """
            -----BEGIN PUBLIC KEY-----
            MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEje4E9APUKixppzz1Cz+8YUhB1bSu
            Sh95vaUBQMgihvy1rzFyFplxHc++8eOmRfdvV9TqrsR6Gq+EB7e+ZHovPQ==
            -----END PUBLIC KEY-----
            """,
        ],
        "nextestudios/ControlFS",
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "api.github.com",
            "github.com",
            "objects.githubusercontent.com",
            "release-assets.githubusercontent.com",
        });

    /// <summary>Impressão digital de uma chave pública PEM: SHA-256 do SubjectPublicKeyInfo DER, em hexadecimal minúsculo.</summary>
    public static string Fingerprint(string publicKeyPem)
    {
        using var key = System.Security.Cryptography.ECDsa.Create();
        key.ImportFromPem(publicKeyPem);
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(key.ExportSubjectPublicKeyInfo()));
    }
}
