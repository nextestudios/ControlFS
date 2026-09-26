using ControlFS.Core.Models;

namespace ControlFS.Core.Contracts;

/// <summary>
/// Atualizações com autenticidade (manifesto assinado), integridade (SHA-256 e tamanho) e origem (repositório e hosts
/// fixos). Nenhuma chamada acontece sem que o usuário tenha deixado a verificação ligada ou pedido explicitamente.
/// </summary>
public interface IUpdateService
{
    ReleaseVersion CurrentVersion { get; }

    /// <summary>Instalado pelo instalador (atualização automática possível). No modo portátil só avisamos.</summary>
    bool IsInstalled { get; }

    Task<UpdateCheckResult> CheckAsync(bool includePrereleases, CancellationToken cancellationToken);

    /// <exception cref="UpdateException"/>
    Task<ReadyUpdate> DownloadAsync(UpdateManifest manifest, IProgress<long>? progress, CancellationToken cancellationToken);

    /// <summary>Reconfere o arquivo e inicia o instalador em modo silencioso. O chamador deve encerrar o app em seguida.</summary>
    /// <exception cref="UpdateException"/>
    void LaunchInstaller(ReadyUpdate update, bool relaunchAfterInstall);
}

public sealed class UpdateException(string message, Exception? inner = null) : Exception(message, inner);
