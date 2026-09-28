using ControlFS.Application.State;
using ControlFS.Core.Actions;

namespace ControlFS.Application;

/// <summary>
/// Tela cheia (#230): a escolha do usuário fica em <see cref="Core.Contracts.AppSettings.FullScreen"/> (F11, Menu → Tela
/// cheia, Configurações ou o botão ao lado de minimizar) e vale na próxima abertura. O vídeo (#170) põe a janela em tela
/// cheia enquanto está aberto sem mudar essa escolha; ao fechar, a janela volta ao que o usuário tinha. A janela só lê
/// <see cref="WindowFullScreen"/> e troca o modo de exibição (o presenter do Windows não roda nos testes).
/// </summary>
public sealed partial class AppController
{
    /// <summary>Vídeo em que o usuário saiu da tela cheia (F11 durante o vídeo): só vale até ele fechar.</summary>
    private VideoPlayerModal? _videoWindowed;

    private VideoPlayerModal? ActiveVideo => _modals.OfType<VideoPlayerModal>().LastOrDefault();

    /// <summary>A janela deve estar em tela cheia agora: com vídeo aberto, sim (salvo F11 durante ele); senão, a escolha salva.</summary>
    public bool WindowFullScreen => ActiveVideo is { } video ? !ReferenceEquals(video, _videoWindowed) : Settings.FullScreen;

    /// <summary>
    /// Liga/desliga a tela cheia. Com um vídeo aberto, só a janela desse vídeo muda (a escolha salva continua a mesma e
    /// volta ao fechar o vídeo).
    /// </summary>
    public void ToggleFullScreen()
    {
        if (ActiveVideo is { } video)
            _videoWindowed = ReferenceEquals(_videoWindowed, video) ? null : video;
        else
        {
            UpdateSettings(s => s with { FullScreen = !s.FullScreen });
            StatusMessage = Settings.FullScreen ? "Tela cheia. F11 ou Menu → Sair da tela cheia volta à janela." : "Tela cheia desligada.";
        }
        RaiseChanged();
    }

    private MenuItem FullScreenTile() => Settings.FullScreen
        ? new("Sair da tela cheia", ToggleFullScreen, Detail: "Volta à janela com minimizar, maximizar e fechar. Também F11.", Icon: ActionIcon.ExitFullScreen,
            Placement: MenuPlacement.Quick, ShortLabel: "Janela")
        : new("Tela cheia", ToggleFullScreen, Detail: "Ocupa a tela inteira, sem a barra do Windows. Também F11.", Icon: ActionIcon.FullScreen,
            Placement: MenuPlacement.Quick);
}
