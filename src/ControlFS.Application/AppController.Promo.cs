using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;

namespace ControlFS.Application;

/// <summary>
/// "Mais da equipe": uma tela, uma única vez, com os outros aplicativos da equipe (NextBoost PRO e Console Mode). Aparece
/// quando o app está livre (depois das boas-vindas e do tutorial), é marcada como vista assim que abre e nunca volta
/// sozinha; para rever, Menu → Ajuda e tutorial → Mais da equipe. O ControlFS não acessa a internet por causa dela: os
/// logos vêm no pacote e os endereços só abrem no navegador quando o usuário escolhe.
/// </summary>
public sealed partial class AppController
{
    public static readonly IReadOnlyList<PromoCard> TeamApps =
    [
        new("NextBoost PRO", "Otimizador de Windows 10 e 11 para jogos",
            "Limpa o que pesa, reduz a latência e ajusta o Windows para uma experiência mais estável nos jogos, em 1 clique e reversível.",
            "Conhecer o NextBoost PRO", "https://nextboost.pro/", "promo-nextboost.png"),
        new("Console Mode", "Seu PC vira um console de videogame",
            "Foca a TV, desliga as outras telas, troca o áudio e abre o Steam Big Picture, o Playnite ou o Xbox. Ao sair do jogo, tudo volta. Código aberto.",
            "Baixar o Console Mode", "https://github.com/lippdev/consolemode", "promo-consolemode.png"),
    ];

    /// <summary>Ligado pela janela real (como <see cref="OfferOnboarding"/>): testes, capturas e --no-onboarding nunca a veem.</summary>
    public bool OfferPromo { get; set; }

    private bool _promoDue;

    private void ArmPromo() => _promoDue = OfferPromo && Settings.PromoSeen != true;

    /// <summary>
    /// Mostra a tela se ainda é a vez dela e o app está livre: nada aberto, sem tutorial, sem boas-vindas e fora do seletor de
    /// pasta. Chamada ao iniciar e sempre que um modal fecha; se não é a hora, não faz nada.
    /// </summary>
    private void TryShowPromo()
    {
        if (!_promoDue || _modals.Count > 0 || Tutorial is not null || OnboardingPending || Screen == Screen.FolderPicker) return;
        _promoDue = false;
        // Vista já ao abrir: fechar o app com ela na tela, ou uma queda, não a traz de volta.
        UpdateSettings(s => s with { PromoSeen = true }, notify: false);
        PushModal(new PromoModal(TeamApps) { Subtitle = "Outros aplicativos da equipe. Esta tela aparece só uma vez; para rever: Menu → Ajuda e tutorial." });
    }

    /// <summary>Abre a tela a pedido (Menu → Ajuda e tutorial → Mais da equipe), mesmo depois de vista.</summary>
    public void ShowPromo()
    {
        if (_modals.Any(m => m is PromoModal)) return;
        _promoDue = false;
        PushModal(new PromoModal(TeamApps) { Subtitle = "Outros aplicativos da equipe." });
    }

    private void HandlePromo(PromoModal modal, InputAction action)
    {
        var count = modal.Cards.Count;
        switch (action)
        {
            case InputAction.NavigateLeft when !modal.CloseFocused:
                modal.FocusIndex = Math.Max(0, modal.FocusIndex - 1);
                break;
            case InputAction.NavigateRight when !modal.CloseFocused:
                modal.FocusIndex = Math.Min(count - 1, modal.FocusIndex + 1);
                break;
            case InputAction.NavigateDown:
                modal.FocusIndex = count;
                break;
            case InputAction.NavigateUp when modal.CloseFocused:
                modal.FocusIndex = 0;
                break;
            case InputAction.Confirm when modal.CloseFocused:
            case InputAction.Back:
            case InputAction.OpenAppMenu:
                CloseModal(modal);
                break;
            case InputAction.Confirm:
                OpenPromoLink(modal.Cards[modal.FocusIndex]);
                break;
        }
    }

    /// <summary>Clique/toque num cartão ou em Fechar.</summary>
    public void PointerChoosePromo(int index)
    {
        if (TopModal is not PromoModal modal) return;
        modal.FocusIndex = Math.Clamp(index, 0, modal.Cards.Count);
        Handle(InputAction.Confirm);
    }

    private void OpenPromoLink(PromoCard card)
    {
        // Só os endereços fixos da lista acima chegam aqui; o Windows os abre no navegador padrão.
        RunShell(s => s.OpenLink(new Uri(card.Url)), external: true);
    }
}
