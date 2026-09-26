using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;

namespace ControlFS.Infrastructure.Updates;

/// <summary>
/// Atualizações a partir das releases do GitHub. A lista de releases serve apenas para localizar arquivos; a decisão
/// de confiança vem exclusivamente do manifesto assinado. Nunca aceita versão igual ou anterior à atual.
/// </summary>
public sealed class GitHubReleaseUpdateService : IUpdateService, IDisposable
{
    public const string InstalledMarkerName = "ControlFS.installed";
    private const int MaxReleaseListBytes = 4 * 1024 * 1024;
    private readonly UpdateTrust _trust;
    private readonly RestrictedHttp _http;
    private readonly string _downloadDirectory;
    private readonly Dictionary<string, Uri> _installerUrls = new(StringComparer.Ordinal);

    public GitHubReleaseUpdateService(ReleaseVersion currentVersion, bool isInstalled, string downloadDirectory,
        UpdateTrust? trust = null, HttpMessageHandler? handler = null)
    {
        CurrentVersion = currentVersion;
        IsInstalled = isInstalled;
        _downloadDirectory = downloadDirectory;
        _trust = trust ?? UpdateTrust.Official;
        _http = new RestrictedHttp(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false }, _trust, $"ControlFS/{currentVersion}");
    }

    public ReleaseVersion CurrentVersion { get; }
    public bool IsInstalled { get; }

    /// <summary>Configuração padrão do app: versão do assembly, marcador do instalador e %LOCALAPPDATA%\ControlFS\updates.</summary>
    public static GitHubReleaseUpdateService CreateDefault()
    {
        var informational = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = ReleaseVersion.TryParse(informational, out var v) ? v : ReleaseVersion.Parse("0.0.0");
        var installed = File.Exists(Path.Join(AppContext.BaseDirectory, InstalledMarkerName));
        var dir = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ControlFS", "updates");
        return new GitHubReleaseUpdateService(version, installed, dir);
    }

    public async Task<UpdateCheckResult> CheckAsync(bool includePrereleases, CancellationToken cancellationToken)
    {
        try
        {
            var list = await _http.GetBytesAsync(new Uri($"https://api.github.com/repos/{_trust.Repository}/releases?per_page=20"),
                "application/vnd.github+json", MaxReleaseListBytes, cancellationToken).ConfigureAwait(false);
            var candidates = new List<(ReleaseVersion Version, Uri Manifest, Uri Signature, Uri Installer)>();
            using (var doc = JsonDocument.Parse(list, new JsonDocumentOptions { MaxDepth = 16 }))
            {
                foreach (var release in doc.RootElement.EnumerateArray())
                {
                    if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
                    if (!ReleaseVersion.TryParse(release.GetProperty("tag_name").GetString(), out var version)) continue;
                    if (version <= CurrentVersion || (version.IsPrerelease && !includePrereleases)) continue;
                    Uri? manifest = null, signature = null, installer = null;
                    foreach (var asset in release.GetProperty("assets").EnumerateArray())
                    {
                        var name = asset.GetProperty("name").GetString();
                        var url = asset.GetProperty("browser_download_url").GetString();
                        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) continue;
                        if (name == UpdateTrust.ManifestAssetName) manifest = uri;
                        else if (name == UpdateTrust.SignatureAssetName) signature = uri;
                        else if (name == UpdateTrust.InstallerAssetName) installer = uri;
                    }
                    if (manifest is not null && signature is not null && installer is not null)
                        candidates.Add((version, manifest, signature, installer));
                }
            }
            if (candidates.Count == 0) return new UpdateCheckResult(UpdateCheckOutcome.UpToDate);

            var best = candidates.MaxBy(c => c.Version)!;
            var manifestBytes = await _http.GetBytesAsync(best.Manifest, "application/octet-stream", ManifestVerifier.MaxManifestBytes, cancellationToken).ConfigureAwait(false);
            var signatureBytes = await _http.GetBytesAsync(best.Signature, "application/octet-stream", 1024, cancellationToken).ConfigureAwait(false);
            var verified = ManifestVerifier.Verify(manifestBytes, signatureBytes, _trust);
            if (verified.Version != best.Version) throw new UpdateException("O manifesto assinado não corresponde à versão da release.");
            if (verified.Version <= CurrentVersion) throw new UpdateException("Versão anterior ou igual recusada.");
            _installerUrls[verified.Version.ToString()] = best.Installer;
            return new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, verified);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is UpdateException or HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException or IOException)
        {
            return new UpdateCheckResult(UpdateCheckOutcome.Failed, Message: ex is UpdateException ? ex.Message : "Não foi possível verificar atualizações (rede indisponível?).");
        }
    }

    public async Task<ReadyUpdate> DownloadAsync(UpdateManifest manifest, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        if (!_installerUrls.TryGetValue(manifest.Version.ToString(), out var url))
            throw new UpdateException("Verifique as atualizações antes de baixar.");
        var folder = Path.Join(_downloadDirectory, manifest.Version.ToString());
        var final = Path.Join(folder, manifest.Installer.Name);
        CleanupOtherVersions(manifest.Version.ToString());
        Directory.CreateDirectory(folder);
        if (File.Exists(final) && Matches(final, manifest.Installer)) return new ReadyUpdate(manifest, final);
        if (File.Exists(final)) File.Delete(final);

        var partial = final + ".part";
        if (File.Exists(partial)) File.Delete(partial);
        try
        {
            using var response = await _http.GetAsync(url, "application/octet-stream", cancellationToken).ConfigureAwait(false);
            if (response.Content.Headers.ContentLength is long length && length != manifest.Installer.Size)
                throw new UpdateException("Tamanho do instalador difere do manifesto assinado.");
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > manifest.Installer.Size) throw new UpdateException("O download excedeu o tamanho do manifesto assinado.");
                    sha.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    progress?.Report(total);
                }
                await target.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            if (total != manifest.Installer.Size) throw new UpdateException("Download incompleto.");
            if (!string.Equals(Convert.ToHexStringLower(sha.GetHashAndReset()), manifest.Installer.Sha256, StringComparison.Ordinal))
                throw new UpdateException("O SHA-256 do instalador não confere com o manifesto assinado.");
            File.Move(partial, final, overwrite: false);
            return new ReadyUpdate(manifest, final);
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateException("Falha de rede ao baixar a atualização.", ex);
        }
        finally
        {
            if (File.Exists(partial)) File.Delete(partial);
        }
    }

    public void LaunchInstaller(ReadyUpdate update, bool relaunchAfterInstall)
    {
        if (!OperatingSystem.IsWindows()) throw new UpdateException("A instalação automática só existe no Windows.");
        if (!IsInstalled) throw new UpdateException("No modo portátil a atualização é manual.");
        // Mantém o arquivo aberto sem permitir escrita entre a conferência e o início do processo.
        using var hold = new FileStream(update.InstallerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (hold.Length != update.Manifest.Installer.Size ||
            !string.Equals(Convert.ToHexStringLower(SHA256.HashData(hold)), update.Manifest.Installer.Sha256, StringComparison.Ordinal))
            throw new UpdateException("O instalador baixado foi alterado; a atualização foi cancelada.");
        var start = new ProcessStartInfo(update.InstallerPath) { UseShellExecute = false };
        foreach (var arg in new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS" }) start.ArgumentList.Add(arg);
        if (relaunchAfterInstall) start.ArgumentList.Add("/RELAUNCH=1");
        using var process = Process.Start(start) ?? throw new UpdateException("Não foi possível iniciar o instalador.");
    }

    private static bool Matches(string path, ReleaseAsset asset)
    {
        using var stream = File.OpenRead(path);
        return stream.Length == asset.Size && string.Equals(Convert.ToHexStringLower(SHA256.HashData(stream)), asset.Sha256, StringComparison.Ordinal);
    }

    /// <summary>Remove downloads de outras versões — apenas subpastas com nome de versão dentro da pasta própria de updates.</summary>
    private void CleanupOtherVersions(string keep)
    {
        if (!Directory.Exists(_downloadDirectory)) return;
        foreach (var dir in Directory.EnumerateDirectories(_downloadDirectory))
        {
            var name = Path.GetFileName(dir);
            if (name == keep || !ReleaseVersion.TryParse(name, out _)) continue;
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public void Dispose() => _http.Dispose();
}
