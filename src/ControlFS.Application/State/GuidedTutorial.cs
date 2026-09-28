using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;

namespace ControlFS.Application.State;

/// <summary>Passos do tutorial guiado (#231), nesta ordem. Nenhum passo altera arquivos.</summary>
public enum TutorialStepKind
{
    MoveFocus,
    OpenFolder,
    GoBack,
    OpenActions,
    TopBar,
    ChangeView,
    Search,
    AppMenu,
}

/// <summary>Região da tela que o passo destaca (a tela escurece o resto e ancora a explicação nela).</summary>
public enum TutorialTarget
{
    Content,
    TopBar,
    Footer,
    Modal,
}

/// <summary>Modal no topo, do jeito que o tutorial precisa saber (nunca o botão físico).</summary>
public enum TutorialModal
{
    None,
    Actions,
    AppMenu,
    Settings,
    SearchKeyboard,
    Other,
}

/// <summary>
/// Retrato semântico da tela que o tutorial observa a cada mudança: a última ação (nunca o botão), se está no início, o
/// local e o item focado, a região, o modal no topo e a exibição.
/// </summary>
public sealed record TutorialObservation(InputAction? LastAction, bool Home, object? Location, bool Loading, string? FocusId, PaneRegion Region,
    TutorialModal Modal, ViewMode View);

/// <summary>
/// Máquina de estados do tutorial guiado (#231): cada passo espera o usuário fazer de verdade o que ele pede, detectado
/// pelo estado do AppController (ações semânticas, local, foco, modal), e só então avança. Passos com duas partes (abrir
/// e fechar Ações, entrar na barra e abrir um atalho…) usam <see cref="Phase"/>. Sem dependência de tela: testável.
/// </summary>
public sealed class GuidedTutorial
{
    public static IReadOnlyList<TutorialStepKind> Steps { get; } = Enum.GetValues<TutorialStepKind>();

    public int Index { get; private set; }
    public TutorialStepKind Step => Steps[Math.Min(Index, Steps.Count - 1)];
    public int Count => Steps.Count;

    /// <summary>Parte do passo: 0 = primeira ação pedida, 1 = a segunda (quando o passo tem duas).</summary>
    public int Phase { get; private set; }

    /// <summary>Todos os passos concluídos.</summary>
    public bool IsComplete => Index >= Steps.Count;

    /// <summary>Estado quando o passo (ou a parte dele) começou: o que precisa mudar é comparado com ele.</summary>
    internal TutorialObservation? Baseline { get; private set; }

    public void Start(TutorialObservation now)
    {
        Index = 0;
        Enter(now);
    }

    /// <summary>Volta um passo (no primeiro, recomeça o primeiro). Refaz o passo desde o começo.</summary>
    public void Previous(TutorialObservation now)
    {
        Index = Math.Max(0, Math.Min(Index, Steps.Count) - 1);
        Enter(now);
    }

    /// <summary>Confere o retrato atual; devolve true quando o passo avançou (ou mudou de parte).</summary>
    public bool Observe(TutorialObservation now)
    {
        if (IsComplete || Baseline is not { } start) return false;
        switch (Step)
        {
            case TutorialStepKind.MoveFocus:
                if (!Same(now, start))
                {
                    Baseline = now; // outro local (aberto pelo mouse, por exemplo): o foco conta a partir dele
                    return false;
                }
                if (now is { Modal: TutorialModal.None, Region: PaneRegion.List, FocusId: { } focus } && focus != start.FocusId) return Advance(now);
                return false;
            case TutorialStepKind.OpenFolder:
                if (now is { Home: false, Loading: false, Location: not null, LastAction: InputAction.Confirm or InputAction.NavigateRight } && !Same(now, start)) return Advance(now);
                Follow(now, InputAction.Confirm, InputAction.NavigateRight);
                return false;
            case TutorialStepKind.GoBack:
                if (now is { Loading: false, LastAction: InputAction.Back or InputAction.NavigateLeft } && !Same(now, start)) return Advance(now);
                Follow(now, InputAction.Back, InputAction.NavigateLeft);
                return false;
            case TutorialStepKind.OpenActions when Phase == 0:
                if (now is { Modal: TutorialModal.Actions, LastAction: InputAction.OpenContextMenu }) return NextPhase(now);
                return false;
            case TutorialStepKind.OpenActions:
                if (now.Modal == TutorialModal.None) return Advance(now);
                return false;
            case TutorialStepKind.TopBar when Phase == 0:
                if (now.Modal == TutorialModal.None && now.Region is PaneRegion.QuickAccess or PaneRegion.Breadcrumbs or PaneRegion.Tabs) return NextPhase(now);
                return false;
            case TutorialStepKind.TopBar:
                // Um atalho aberto: outro local, ou o que ele mostra num modal (Favoritos, Recentes).
                if (now is { LastAction: InputAction.Confirm, Loading: false } && (!Same(now, start) || now.Modal != TutorialModal.None)) return Advance(now);
                if (now is { Modal: TutorialModal.None, Region: PaneRegion.List } && now.LastAction != InputAction.Confirm && Same(now, start))
                {
                    Phase = 0; // saiu da barra sem abrir nada: pede de novo L1/R1
                    Baseline = now;
                    return true;
                }
                return false;
            case TutorialStepKind.ChangeView:
                if (now.View != start.View) return Advance(now);
                return false;
            case TutorialStepKind.Search when Phase == 0:
                if (now.Modal == TutorialModal.SearchKeyboard) return NextPhase(now);
                return false;
            case TutorialStepKind.Search:
                if (now.Modal == TutorialModal.None) return Advance(now);
                return false;
            case TutorialStepKind.AppMenu when now.Modal == TutorialModal.Settings:
                return Advance(now);
            case TutorialStepKind.AppMenu when Phase == 0:
                if (now.Modal == TutorialModal.AppMenu) return NextPhase(now);
                return false;
            case TutorialStepKind.AppMenu:
                // Escolher uma opção fecha o Menu antes de abrir a tela dela: só Voltar/Menu fecham de verdade.
                if (now is { Modal: TutorialModal.None, LastAction: InputAction.Back or InputAction.OpenAppMenu or InputAction.NavigateLeft })
                {
                    Phase = 0; // fechou o Menu sem escolher Configurações
                    Baseline = now;
                    return true;
                }
                return false;
            default:
                return false;
        }
    }

    private static bool Same(TutorialObservation a, TutorialObservation b) => a.Home == b.Home && Equals(a.Location, b.Location);

    /// <summary>
    /// Passos de ir e voltar: o usuário andou por outro caminho (Voltar quando o passo pede Abrir, ou o contrário), então o
    /// ponto de partida passa a ser onde ele está; só a ação pedida, a partir daí, conta.
    /// </summary>
    private void Follow(TutorialObservation now, InputAction wanted, InputAction alternative)
    {
        if (!now.Loading && now.LastAction != wanted && now.LastAction != alternative) Baseline = now;
    }

    private bool Advance(TutorialObservation now)
    {
        Index++;
        if (!IsComplete) Enter(now);
        return true;
    }

    private bool NextPhase(TutorialObservation now)
    {
        Phase = 1;
        Baseline = now;
        return true;
    }

    private void Enter(TutorialObservation now)
    {
        // "Volte" precisa de uma pasta aberta: no início (ex.: voltou um passo depois de sair da pasta), pede para abrir uma.
        if (Step == TutorialStepKind.GoBack && now.Home) Index = Steps.ToList().IndexOf(TutorialStepKind.OpenFolder);
        Phase = 0;
        Baseline = now;
    }
}
