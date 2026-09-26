using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;

namespace ControlFS.Infrastructure.Updates;

/// <summary>
/// Verifica a assinatura ECDSA P-256/SHA-256 (formato IEEE P1363, em base64) sobre os bytes EXATOS do manifesto e só
/// então interpreta o JSON. Qualquer campo inesperado ou fora do formato é recusado.
/// </summary>
public static class ManifestVerifier
{
    public const int MaxManifestBytes = 64 * 1024;

    public static UpdateManifest Verify(ReadOnlySpan<byte> manifest, ReadOnlySpan<byte> signatureFile, UpdateTrust trust)
    {
        if (manifest.Length == 0 || manifest.Length > MaxManifestBytes || signatureFile.Length > 1024)
            throw new UpdateException("Manifesto de atualização com tamanho inválido.");

        byte[] signature;
        try { signature = Convert.FromBase64String(Encoding.ASCII.GetString(signatureFile).Trim()); }
        catch (FormatException ex) { throw new UpdateException("Assinatura do manifesto em formato inválido.", ex); }

        using (var ecdsa = ECDsa.Create())
        {
            ecdsa.ImportFromPem(trust.PublicKeyPem);
            if (!ecdsa.VerifyData(manifest, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                throw new UpdateException("Assinatura do manifesto inválida: a atualização foi recusada.");
        }

        try
        {
            using var doc = JsonDocument.Parse(manifest.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
            var root = doc.RootElement;
            if (root.GetProperty("schema").GetInt32() != 1) throw new UpdateException("Versão de manifesto não suportada.");
            if (root.GetProperty("product").GetString() != "ControlFS") throw new UpdateException("Manifesto de outro produto.");
            var repository = root.GetProperty("repository").GetString();
            if (!string.Equals(repository, trust.Repository, StringComparison.Ordinal)) throw new UpdateException("Manifesto de outro repositório.");
            if (!ReleaseVersion.TryParse(root.GetProperty("version").GetString(), out var version)) throw new UpdateException("Versão inválida no manifesto.");
            DateTimeOffset? releasedAt = root.TryGetProperty("releasedAt", out var r) && r.TryGetDateTimeOffset(out var at) ? at : null;
            var installer = Asset(root.GetProperty("installer"));
            if (installer.Name != UpdateTrust.InstallerAssetName) throw new UpdateException("Nome de instalador inesperado no manifesto.");
            var portable = root.TryGetProperty("portable", out var p) ? Asset(p) : null;
            return new UpdateManifest(version, repository!, releasedAt, installer, portable,
                $"https://github.com/{trust.Repository}/releases/tag/v{version}");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new UpdateException("Manifesto de atualização malformado.", ex);
        }
    }

    private static ReleaseAsset Asset(JsonElement e)
    {
        var name = e.GetProperty("name").GetString() ?? string.Empty;
        var size = e.GetProperty("size").GetInt64();
        var sha = e.GetProperty("sha256").GetString() ?? string.Empty;
        if (name.Length is 0 or > 128 || name.IndexOfAny(['/', '\\', ':']) >= 0 || name.StartsWith('.'))
            throw new UpdateException("Nome de arquivo inválido no manifesto.");
        if (size <= 0 || size > UpdateTrust.MaxInstallerBytes) throw new UpdateException("Tamanho inválido no manifesto.");
        if (sha.Length != 64 || !sha.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f')) throw new UpdateException("SHA-256 inválido no manifesto.");
        return new ReleaseAsset(name, size, sha);
    }
}
