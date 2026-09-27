using System.Security.Cryptography;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Updates;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Updates;

public class UpdateServiceTests : IDisposable
{
    private readonly TempDir _tmp = new();
    private readonly FakeReleaseServer _server = new();

    public void Dispose()
    {
        _tmp.Dispose();
        _server.Dispose();
    }

    private GitHubReleaseUpdateService Service(string current = "0.1.0-alpha.1", bool installed = true) =>
        new(ReleaseVersion.Parse(current), installed, _tmp.Sub("updates"), _server.Trust, _server);

    [Fact]
    public async Task Finds_highest_newer_signed_release_and_downloads_verified_installer()
    {
        _server.Publish("0.1.0-alpha.2", FakeReleaseServer.Installer("a2"), prerelease: true);
        _server.Publish("0.1.0-alpha.3", FakeReleaseServer.Installer("a3"), prerelease: true);
        _server.Publish("0.1.0-alpha.1", FakeReleaseServer.Installer("a1"), prerelease: true);
        using var service = Service();

        var check = await service.CheckAsync(includePrereleases: true, CancellationToken.None);

        Assert.Equal(UpdateCheckOutcome.UpdateAvailable, check.Outcome);
        Assert.Equal("0.1.0-alpha.3", check.Manifest!.Version.ToString());
        var ready = await service.DownloadAsync(check.Manifest, null, CancellationToken.None);
        Assert.Equal(FakeReleaseServer.Installer("a3"), File.ReadAllBytes(ready.InstallerPath));
        Assert.DoesNotContain(Directory.EnumerateFiles(_tmp.Sub("updates"), "*", SearchOption.AllDirectories), f => f.EndsWith(".part", StringComparison.Ordinal));
        Assert.All(_server.Requests, r => Assert.StartsWith("https://", r, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Stable_channel_ignores_prereleases_and_never_downgrades()
    {
        _server.Publish("0.2.0-beta.1", FakeReleaseServer.Installer("b"), prerelease: true);
        _server.Publish("0.0.9", FakeReleaseServer.Installer("old"));
        using var service = Service(current: "0.1.0");

        var check = await service.CheckAsync(includePrereleases: false, CancellationToken.None);

        Assert.Equal(UpdateCheckOutcome.UpToDate, check.Outcome);
    }

    [Fact]
    public async Task Tampered_manifest_is_refused()
    {
        _server.Publish("0.1.0-alpha.2", FakeReleaseServer.Installer("x"), prerelease: true,
            tamperManifest: m => m.Replace("\"sha256\":\"", "\"sha256\":\"0", StringComparison.Ordinal)[..^0]);
        using var service = Service();
        var check = await service.CheckAsync(true, CancellationToken.None);
        Assert.Equal(UpdateCheckOutcome.Failed, check.Outcome);
        Assert.Contains("Assinatura", check.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manifest_signed_by_another_key_is_refused()
    {
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _server.Publish("0.1.0-alpha.2", FakeReleaseServer.Installer("x"), prerelease: true, signWith: attacker);
        using var service = Service();
        Assert.Equal(UpdateCheckOutcome.Failed, (await service.CheckAsync(true, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task Signed_manifest_for_an_older_version_cannot_be_replayed_under_a_newer_tag()
    {
        _server.Publish("0.9.0", FakeReleaseServer.Installer("x"), manifestVersion: "0.1.0-alpha.1");
        using var service = Service();
        var check = await service.CheckAsync(true, CancellationToken.None);
        Assert.Equal(UpdateCheckOutcome.Failed, check.Outcome);
    }

    [Fact]
    public async Task Release_without_signature_is_ignored()
    {
        _server.Publish("0.1.0-alpha.2", FakeReleaseServer.Installer("x"), prerelease: true, withSignature: false);
        using var service = Service();
        Assert.Equal(UpdateCheckOutcome.UpToDate, (await service.CheckAsync(true, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task Redirect_to_a_host_outside_the_allowlist_is_refused()
    {
        _server.Publish("0.1.0-alpha.2", FakeReleaseServer.Installer("x"), prerelease: true);
        _server.Route($"https://github.com/{FakeReleaseServer.Repo}/releases/download/v0.1.0-alpha.2/{UpdateTrust.ManifestAssetName}",
            () => FakeReleaseServer.Redirect("https://evil.example.com/manifest.json"));
        using var service = Service();
        var check = await service.CheckAsync(true, CancellationToken.None);
        Assert.Equal(UpdateCheckOutcome.Failed, check.Outcome);
        Assert.DoesNotContain(_server.Requests, r => r.Contains("evil.example.com", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Installer_with_wrong_content_is_deleted_and_refused()
    {
        _server.Publish("0.1.0-alpha.2", FakeReleaseServer.Installer("original"), prerelease: true);
        using var service = Service();
        var check = await service.CheckAsync(true, CancellationToken.None);
        var tampered = FakeReleaseServer.Installer("original");
        tampered[^1] ^= 0xFF;
        _server.Route("https://release-assets.githubusercontent.com/0.1.0-alpha.2/" + UpdateTrust.InstallerAssetName, () => FakeReleaseServer.Bytes(tampered));

        var ex = await Assert.ThrowsAsync<UpdateException>(() => service.DownloadAsync(check.Manifest!, null, CancellationToken.None));

        Assert.Contains("SHA-256", ex.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(_tmp.Sub("updates"), "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Oversized_download_is_cut_off()
    {
        _server.Publish("0.1.0-alpha.2", FakeReleaseServer.Installer("small"), prerelease: true);
        using var service = Service();
        var check = await service.CheckAsync(true, CancellationToken.None);
        _server.Route("https://release-assets.githubusercontent.com/0.1.0-alpha.2/" + UpdateTrust.InstallerAssetName,
            () => new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(new byte[10_000_000])) });
        await Assert.ThrowsAsync<UpdateException>(() => service.DownloadAsync(check.Manifest!, null, CancellationToken.None));
    }

    [Fact]
    public async Task Offline_is_reported_as_a_failed_check_not_an_exception()
    {
        using var service = new GitHubReleaseUpdateService(ReleaseVersion.Parse("0.1.0"), true, _tmp.Sub("u"), _server.Trust, new ThrowingHandler());
        var check = await service.CheckAsync(true, CancellationToken.None);
        Assert.Equal(UpdateCheckOutcome.Failed, check.Outcome);
    }

    [Fact]
    public void Launching_is_refused_in_portable_mode_or_outside_windows()
    {
        using var service = Service(installed: false);
        var manifest = new UpdateManifest(ReleaseVersion.Parse("9.0.0"), FakeReleaseServer.Repo, null, new ReleaseAsset(UpdateTrust.InstallerAssetName, 1, new string('0', 64)), null, "https://github.com");
        Assert.Throws<UpdateException>(() => service.LaunchInstaller(new ReadyUpdate(manifest, _tmp.Sub("x.exe")), true));
    }

    [Fact]
    public void Official_keys_are_distinct_p256_keys_and_still_include_the_one_every_installed_copy_trusts()
    {
        var fingerprints = UpdateTrust.Official.PublicKeysPem.Select(pem =>
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(pem);
            Assert.Equal("1.2.840.10045.3.1.7", key.ExportParameters(false).Curve.Oid.Value); // P-256
            return UpdateTrust.Fingerprint(pem);
        }).ToList();
        Assert.InRange(fingerprints.Count, 1, UpdateTrust.MaxTrustedKeys);
        Assert.Equal(fingerprints.Count, fingerprints.Distinct().Count());
        // Remover esta chave antes de uma rotação completa (docs/decisions/0006) quebra a atualização de quem a usa.
        Assert.Contains("8b841b19810a1a7e0fe417a34ce8fb1281125f52413576e5b4f4deba7c64f3e8", fingerprints);
    }

    [Fact]
    public void Manifest_signed_by_either_trusted_key_is_accepted_and_any_other_key_is_refused()
    {
        using var current = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var next = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var trust = UpdateTrust.Official with { PublicKeysPem = [current.ExportSubjectPublicKeyInfoPem(), next.ExportSubjectPublicKeyInfoPem()] };
        var manifest = System.Text.Encoding.UTF8.GetBytes(
            $$$"""{"schema":1,"product":"ControlFS","repository":"{{{FakeReleaseServer.Repo}}}","version":"0.9.0","installer":{"name":"{{{UpdateTrust.InstallerAssetName}}}","size":10,"sha256":"{{{new string('a', 64)}}}"}}""");
        byte[] Sign(ECDsa key) => System.Text.Encoding.ASCII.GetBytes(Convert.ToBase64String(key.SignData(manifest, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)));

        Assert.Equal("0.9.0", ManifestVerifier.Verify(manifest, Sign(current), trust).Version.ToString());
        Assert.Equal("0.9.0", ManifestVerifier.Verify(manifest, Sign(next), trust).Version.ToString());
        Assert.Throws<UpdateException>(() => ManifestVerifier.Verify(manifest, Sign(stranger), trust));
        // Uma chave fora da curva P-256 na lista nunca confere nada, nem a própria assinatura.
        var withP384 = trust with { PublicKeysPem = [p384.ExportSubjectPublicKeyInfoPem()] };
        Assert.Throws<UpdateException>(() => ManifestVerifier.Verify(manifest, Sign(p384), withP384));
        Assert.Throws<UpdateException>(() => ManifestVerifier.Verify(manifest, Sign(current), trust with { PublicKeysPem = [] }));
    }

    [Fact]
    public async Task After_a_key_rotation_an_old_copy_takes_the_newest_release_it_can_verify_and_never_an_older_one()
    {
        using var rotated = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _server.Publish("0.2.0", FakeReleaseServer.Installer("transicao")); // assinada com a chave antiga, já confia na nova
        _server.Publish("0.3.0", FakeReleaseServer.Installer("nova"), signWith: rotated);
        using var service = Service(current: "0.1.0");

        var check = await service.CheckAsync(includePrereleases: false, CancellationToken.None);

        Assert.Equal(UpdateCheckOutcome.UpdateAvailable, check.Outcome);
        Assert.Equal("0.2.0", check.Manifest!.Version.ToString());
        var ready = await service.DownloadAsync(check.Manifest, null, CancellationToken.None);
        Assert.Equal(FakeReleaseServer.Installer("transicao"), File.ReadAllBytes(ready.InstallerPath));

        using var upToDate = Service(current: "0.2.0"); // sem a chave nova e sem degrau mais novo que ela: recusa, nunca volta atrás
        Assert.Equal(UpdateCheckOutcome.Failed, (await upToDate.CheckAsync(false, CancellationToken.None)).Outcome);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("offline");
    }
}
