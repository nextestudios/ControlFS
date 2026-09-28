using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;

namespace ControlFS.Application;

public enum UpdateState
{
    Idle,
    Checking,
    Downloading,
    /// <summary>Instalador baixado e verificado, aguardando o usuário (ou a saída do app).</summary>
    Ready,
    /// <summary>Nova versão encontrada no modo portátil: atualização manual.</summary>
    AvailableManual,
    UpToDate,
    Failed,
}

public sealed partial class AppController
{
    private static readonly TimeSpan AutomaticCheckInterval = TimeSpan.FromHours(24);
    private readonly IUpdateService? _updates;
    private bool _installerLaunched;

    public UpdateState UpdateState { get; private set; }
    public UpdateManifest? AvailableUpdate { get; private set; }
    public ReadyUpdate? ReadyUpdate { get; private set; }
    public string? UpdateMessage { get; private set; }
    public ReleaseVersion? CurrentVersion => _updates?.CurrentVersion;

    /// <summary>Relógio injetável para testes.</summary>
    internal Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.UtcNow;

    private bool IncludePrereleases => Settings.IncludePrereleases ?? _updates?.CurrentVersion.IsPrerelease ?? false;

    private string UpdateMenuLabel => UpdateState switch
    {
        UpdateState.Ready => $"Atualização {ReadyUpdate!.Manifest.Version} pronta",
        UpdateState.Downloading => "Atualizações (baixando…)",
        UpdateState.Checking => "Atualizações (verificando…)",
        _ => "Atualizações",
    };

    private void StartAutomaticUpdateCheck()
    {
        if (_updates is null || !Settings.AutoCheckUpdates) return;
        if (Settings.LastUpdateCheck is { } last && Now() - last < AutomaticCheckInterval) return;
        Track(CheckForUpdatesAsync(manual: false));
    }

    public Task CheckForUpdatesNowAsync() => CheckForUpdatesAsync(manual: true);

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_updates is null || UpdateState is UpdateState.Checking or UpdateState.Downloading or UpdateState.Ready) return;
        UpdateState = UpdateState.Checking;
        UpdateMessage = null;
        RaiseChanged();
        var result = await _updates.CheckAsync(IncludePrereleases, CancellationToken.None);
        UpdateSettings(s => s with { LastUpdateCheck = Now() });
        switch (result.Outcome)
        {
            case UpdateCheckOutcome.UpToDate:
                UpdateState = UpdateState.UpToDate;
                if (manual) ShowMessage("Atualizações", [("Versão atual", _updates.CurrentVersion.ToString())], "Você já está na versão mais recente.", icon: ActionIcon.Success);
                break;
            case UpdateCheckOutcome.Failed:
                UpdateState = UpdateState.Failed;
                UpdateMessage = result.Message;
                if (manual) ShowMessage("Não foi possível verificar atualizações", [("Motivo", result.Message ?? "desconhecido")], icon: ActionIcon.Error);
                break;
            case UpdateCheckOutcome.UpdateAvailable:
                AvailableUpdate = result.Manifest;
                if (_updates.IsInstalled) await DownloadUpdateAsync(result.Manifest!, manual);
                else
                {
                    UpdateState = UpdateState.AvailableManual;
                    if (manual || TopModal is null)
                        ShowMessage($"Nova versão {result.Manifest!.Version}", [("Versão atual", _updates.CurrentVersion.ToString()), ("Página", result.Manifest.ReleasePageUrl)],
                            "No modo portátil a atualização é manual: baixe o novo pacote na página da versão. Com o instalador, o ControlFS se atualiza sozinho.", icon: ActionIcon.Update);
                }
                break;
        }
        RaiseChanged();
    }

    private async Task DownloadUpdateAsync(UpdateManifest manifest, bool manual)
    {
        UpdateState = UpdateState.Downloading;
        RaiseChanged();
        try
        {
            ReadyUpdate = await _updates!.DownloadAsync(manifest, null, CancellationToken.None);
            UpdateState = UpdateState.Ready;
            // Não interrompe o usuário no meio de um diálogo ou de uma operação de disco.
            if (manual || (TopModal is null && Operations.ActiveCount == 0)) ShowUpdateReady();
            else StatusMessage = $"Atualização {manifest.Version} pronta. Abra o menu para instalar.";
        }
        catch (UpdateException ex)
        {
            UpdateState = UpdateState.Failed;
            UpdateMessage = ex.Message;
            if (manual) ShowMessage("Falha ao baixar a atualização", [("Motivo", ex.Message)], icon: ActionIcon.Error);
        }
        catch (IOException ex)
        {
            UpdateState = UpdateState.Failed;
            UpdateMessage = "Não foi possível gravar a atualização no disco.";
            if (manual) ShowError("Falha ao baixar a atualização", [], ex, "Baixar atualização");
        }
    }

    private void ShowUpdateReady()
    {
        if (ReadyUpdate is not { } ready) return;
        var dialog = new DialogModal($"Atualização {ready.Manifest.Version} pronta",
        [
            ("Versão atual", _updates!.CurrentVersion.ToString()),
            ("Nova versão", ready.Manifest.Version + (ready.Manifest.Version.IsPrerelease ? " (pré-lançamento)" : string.Empty)),
            ("Verificação", "assinatura do manifesto e SHA-256 conferidos"),
            ("Notas", ready.Manifest.ReleasePageUrl),
        ])
        { Message = Operations.ActiveCount > 0 ? "Há operações em andamento: a instalação fica para quando terminarem." : null, Icon = ActionIcon.Update };
        var later = new DialogOption(Settings.InstallUpdatesOnExit ? "Depois (instalar ao sair)" : "Depois", DialogOptionKind.Safe, () => CloseModal(dialog), icon: ActionIcon.Close);
        dialog.Options.Add(new DialogOption("Instalar e reiniciar", DialogOptionKind.Primary, () =>
        {
            CloseModal(dialog);
            InstallUpdateNow();
        }, icon: ActionIcon.Update));
        dialog.Options.Add(later);
        dialog.BackOption = later;
        PushModal(dialog);
    }

    private void InstallUpdateNow()
    {
        if (ReadyUpdate is not { } ready || _updates is null) return;
        if (Operations.ActiveCount > 0)
        {
            ShowMessage("Aguarde as operações", [("Em andamento", Operations.ActiveCount.ToString())], "A atualização será oferecida de novo quando as operações terminarem.", icon: ActionIcon.Warning);
            return;
        }
        try
        {
            _updates.LaunchInstaller(ready, relaunchAfterInstall: true);
            _installerLaunched = true;
            RequestExit();
        }
        catch (Exception ex) when (ex is UpdateException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            ShowError("Não foi possível instalar a atualização", [], ex, "Instalar atualização");
        }
    }

    /// <summary>
    /// Chamado pela janela ao fechar. Com "instalar ao sair" ligado, inicia o instalador verificado em modo silencioso
    /// (sem reabrir o app). Nunca durante operações de disco.
    /// </summary>
    public void PrepareShutdown()
    {
        if (_installerLaunched || _updates is null || ReadyUpdate is not { } ready || !Settings.InstallUpdatesOnExit || !_updates.IsInstalled) return;
        if (Operations.ActiveCount > 0) return;
        try
        {
            _updates.LaunchInstaller(ready, relaunchAfterInstall: false);
            _installerLaunched = true;
        }
        catch (Exception ex) when (ex is UpdateException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // Na saída não há como avisar; a atualização continua disponível na próxima abertura.
        }
    }

    private void RequestExit() => ExitRequested?.Invoke();

    private void ShowUpdatesMenu()
    {
        if (_updates is null) return;
        var busy = UpdateState is UpdateState.Checking or UpdateState.Downloading;
        var items = new List<MenuItem>();
        if (ReadyUpdate is { } ready)
            items.Add(new MenuItem($"Instalar {ready.Manifest.Version} e reiniciar", InstallUpdateNow,
                Operations.ActiveCount > 0 ? "Aguarde as operações em andamento terminarem." : null, Icon: ActionIcon.Update));
        items.Add(new MenuItem("Verificar agora", () => Track(CheckForUpdatesNowAsync()),
            busy ? "Já verificando ou baixando." : ReadyUpdate is not null ? "Uma atualização já está pronta." : null,
            Detail: $"Versão atual {_updates.CurrentVersion}" + (Settings.LastUpdateCheck is { } last ? $" · última verificação {last.LocalDateTime:g}" : string.Empty), Icon: ActionIcon.Refresh));
        items.Add(new MenuItem($"Verificar automaticamente: {(Settings.AutoCheckUpdates ? "sim" : "não")}",
            () => UpdateSettings(s => s with { AutoCheckUpdates = !s.AutoCheckUpdates }),
            Detail: "Uma consulta por dia às releases do GitHub; nenhum dado pessoal é enviado.", Icon: ActionIcon.Operations));
        items.Add(new MenuItem($"Instalar ao sair: {(Settings.InstallUpdatesOnExit ? "sim" : "não")}",
            () => UpdateSettings(s => s with { InstallUpdatesOnExit = !s.InstallUpdatesOnExit }),
            _updates.IsInstalled ? null : "Somente na versão instalada; a portátil é atualizada manualmente.", Icon: ActionIcon.Exit));
        items.Add(new MenuItem($"Versões de pré-lançamento: {(Settings.IncludePrereleases is null ? $"automático ({(IncludePrereleases ? "sim" : "não")})" : Settings.IncludePrereleases.Value ? "sim" : "não")}",
            () => UpdateSettings(s => s with { IncludePrereleases = s.IncludePrereleases switch { null => true, true => false, false => null } }), Icon: ActionIcon.Labels));
        if (UpdateMessage is { } message) items.Add(new MenuItem("Último erro: " + message, null, message, Icon: ActionIcon.Error));
        PushModal(new MenuModal("Atualizações", items) { Icon = ActionIcon.Update });
    }
}
