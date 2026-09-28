using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;

namespace ControlFS.Application;

/// <summary>Um passo do tutorial para a tela desenhar: número, texto, o que apertar e a região em destaque.</summary>
public sealed record TutorialCard(int Number, int Count, string Title, string Instruction, IReadOnlyList<Hint> Actions, TutorialTarget Target);

/// <summary>
/// Boas-vindas e tutorial guiado (#231). As boas-vindas aparecem só na primeira execução do app de verdade
/// (<see cref="OfferOnboarding"/>, ligado pela janela; nunca nos testes nem no gerador de capturas) e podem ser revistas pelo
/// Menu → Ajuda ou por Configurações. O tutorial observa o estado do AppController a cada mudança e avança quando o usuário
/// faz o que o passo pede; ele não é um modal (o usuário usa a tela de verdade) e nunca altera arquivos.
/// </summary>
public sealed partial class AppController
{
    private bool _deferredUpdateCheck;
    private InputAction? _lastAction;
    private bool _observingTutorial;
    private string? _announcedTutorial;

    /// <summary>
    /// Oferecer as boas-vindas na primeira execução (preferências novas). Só a janela real liga: testes, o gerador de
    /// capturas e quem abre com --no-onboarding nunca as veem sem pedir.
    /// </summary>
    public bool OfferOnboarding { get; set; }

    /// <summary>Tutorial guiado em andamento (null: nenhum).</summary>
    public GuidedTutorial? Tutorial { get; private set; }

    // Mostra uma vez a quem ainda não viu: instalação nova (false) e também preferências de antes das boas-vindas (null),
    // por exemplo quem reinstala ou atualiza — a pasta de dados sobrevive à reinstalação.
    private bool OnboardingPending => OfferOnboarding && Settings.OnboardingCompleted != true;

    // ---------- Boas-vindas ----------

    /// <summary>Abre as boas-vindas (primeira execução, Menu → Ajuda ou Configurações). Encerra um tutorial em andamento.</summary>
    public void ShowOnboarding() => ShowOnboarding(replay: true);

    private void ShowOnboarding(bool replay)
    {
        if (_modals.Any(m => m is OnboardingModal)) return;
        Tutorial = null;
        var modal = new OnboardingModal(replay);
        LoadOnboardingStep(modal, 0);
        PushModal(modal);
    }

    /// <summary>Pula as boas-vindas (Menu/Start, ou a tela quando não consegue desenhá-las): nunca prendem o app.</summary>
    public void SkipOnboarding() => FinishOnboarding(tutorial: false, skipped: true);

    private void FinishOnboarding(bool tutorial, bool skipped)
    {
        foreach (var modal in _modals.OfType<OnboardingModal>().ToList()) CloseModal(modal);
        if (Settings.OnboardingCompleted != true) UpdateSettings(s => s with { OnboardingCompleted = true }, notify: false);
        if (_deferredUpdateCheck)
        {
            // A verificação automática espera o passo de privacidade (onde ela pode ser desligada).
            _deferredUpdateCheck = false;
            StartAutomaticUpdateCheck();
        }
        ReturnHome();
        if (tutorial) StartTutorial();
        else StatusMessage = skipped ? "Boas-vindas puladas. Para rever: Menu → Ajuda." : "Tudo pronto. O tutorial guiado fica em Menu → Ajuda.";
        RaiseChanged();
        TryShowPromo(); // sem tutorial: "Mais da equipe" logo depois das boas-vindas (com tutorial, quando ele terminar)
    }

    /// <summary>Fim das boas-vindas e do tutorial: o início, com o primeiro cartão (ou local) em foco, sem nada aberto por cima.</summary>
    private void ReturnHome()
    {
        foreach (var modal in _modals.OfType<MenuModal>().ToList()) CloseModal(modal);
        if (Screen == Screen.FolderPicker) return;
        GoHome();
        PlacesFocus = IsGrid && HomeSections is [{ Places: [var first, ..] }, ..] ? first : 0;
    }

    private void HandleOnboarding(OnboardingModal modal, InputAction action)
    {
        var count = modal.Options.Count;
        switch (action)
        {
            case InputAction.NavigateUp:
            case InputAction.NavigateLeft:
                if (count > 0) modal.FocusIndex = (modal.FocusIndex - 1 + count) % count;
                break;
            case InputAction.NavigateDown:
            case InputAction.NavigateRight:
                if (count > 0) modal.FocusIndex = (modal.FocusIndex + 1) % count;
                break;
            case InputAction.Confirm:
                modal.FocusedOption?.Execute();
                break;
            case InputAction.Back:
            case InputAction.PreviousRegion:
                if (modal.StepIndex > 0) LoadOnboardingStep(modal, modal.StepIndex - 1);
                break;
            case InputAction.NextRegion:
                if (!modal.IsLastStep) LoadOnboardingStep(modal, modal.StepIndex + 1);
                break;
            case InputAction.OpenAppMenu:
                SkipOnboarding();
                break;
        }
    }

    private void NextOnboardingStep(OnboardingModal modal)
    {
        if (!modal.IsLastStep) LoadOnboardingStep(modal, modal.StepIndex + 1);
    }

    /// <summary>Monta o passo (textos e opções). <paramref name="keepFocus"/>: um ajuste mudou e o foco continua nele.</summary>
    private void LoadOnboardingStep(OnboardingModal modal, int index, bool keepFocus = false)
    {
        var focus = modal.FocusIndex;
        modal.StepIndex = Math.Clamp(index, 0, modal.StepCount - 1);
        modal.ControlLegend = [];
        void Reload() => LoadOnboardingStep(modal, modal.StepIndex, keepFocus: true);
        var next = new OnboardingOption("Continuar", () => NextOnboardingStep(modal), ActionIcon.Resume);
        switch (modal.Step)
        {
            case OnboardingStep.Welcome:
                modal.StepTitle = "Boas-vindas ao ControlFS";
                modal.StepBody = "Um gerenciador de arquivos feito para o controle: navegue, abra, copie e extraia do sofá, sem mouse nem teclado. "
                    + "São cinco passos curtos; Menu pula tudo quando quiser.";
                modal.Options = [new("Começar", () => NextOnboardingStep(modal), ActionIcon.Resume)];
                break;
            case OnboardingStep.Controls:
                modal.StepTitle = "Como o controle funciona";
                modal.StepBody = (ActiveController is null
                    ? "Estas são as teclas do teclado; pressione um botão do controle e as legendas mudam para os botões dele. "
                    : "Estes são os botões do controle em uso. ")
                    + "Ações muda com o contexto (Extrair num compactado, Filtros numa busca) e o rodapé sempre mostra o que cada botão faz ali.";
                modal.ControlLegend =
                [
                    new(InputAction.Confirm, "Abrir"),
                    new(InputAction.Back, "Voltar"),
                    new(InputAction.OpenContextMenu, "Ações"),
                    new(InputAction.OpenAppMenu, "Menu"),
                    new(InputAction.Search, "Buscar"),
                    new(InputAction.ToggleSelection, "Marcar"),
                    new(InputAction.PreviousRegion, "Barra superior"),
                    new(InputAction.NextRegion, "Barra superior"),
                    new(InputAction.PageUp, "Aba anterior"),
                    new(InputAction.PageDown, "Próxima aba"),
                    new(InputAction.ChangeView, "Lista ou grade"),
                ];
                modal.Options = [next];
                break;
            case OnboardingStep.Basics:
                var south = Settings.Convention == ConfirmBackConvention.SouthConfirms;
                modal.StepTitle = "O básico, do seu jeito";
                modal.StepBody = "Cada escolha vale na hora e pode ser mudada depois em Menu → Configurações.";
                modal.Options =
                [
                    new($"Confirmar com: {(south ? "botão inferior" : "botão direito")}", () =>
                    {
                        UpdateSettings(s => s with { Convention = s.Convention == ConfirmBackConvention.SouthConfirms ? ConfirmBackConvention.EastConfirms : ConfirmBackConvention.SouthConfirms });
                        Reload();
                    }, ActionIcon.Accept, south ? "botão inferior" : "botão direito", "Troca Confirmar e Voltar entre o botão inferior e o direito. A partir de agora, confirme com o botão escolhido."),
                    new($"Legendas: {LabelStyleName(Settings.LabelStyle)}", () =>
                    {
                        UpdateSettings(s => s with { LabelStyle = (ButtonLabelStyle)(((int)s.LabelStyle + 1) % 5) });
                        Reload();
                    }, ActionIcon.Labels, LabelStyleName(Settings.LabelStyle), "Automáticas seguem o controle em uso (Xbox, PlayStation, Nintendo)."),
                    new($"Tema: {ThemeName(Settings.Theme)}", () =>
                    {
                        CycleTheme();
                        Reload();
                    }, ActionIcon.Theme, ThemeName(Settings.Theme), "Automático segue o modo claro ou escuro do Windows."),
                    new($"Exibição: {ViewName(Settings.View)}", () =>
                    {
                        ToggleView();
                        Reload();
                    }, ActionIcon.View, ViewName(Settings.View), $"Lista de linhas ou grade de ícones grandes ({ButtonName(InputAction.ChangeView)} troca a qualquer momento)."),
                    new($"Fluidez: {(Settings.SyncInputToDisplay ? "máxima" : "economia de bateria")}", () =>
                    {
                        UpdateSettings(s => s with { SyncInputToDisplay = !s.SyncInputToDisplay });
                        Reload();
                    }, ActionIcon.Settings, Settings.SyncInputToDisplay ? "máxima" : "economia de bateria", "Máxima lê o controle ~125 vezes por segundo; economia gasta menos bateria em portáteis."),
                    next,
                ];
                break;
            case OnboardingStep.Privacy:
                modal.StepTitle = "Seus arquivos e dados ficam aqui";
                modal.StepBody = "Sem conta, sem anúncios e sem telemetria: preferências, recentes e históricos ficam só neste computador. "
                    + "O celular como controle fica desligado até você abri-lo no Menu. A internet só é usada para procurar atualizações, se você quiser.";
                modal.Options = _updates is null
                    ? [next]
                    :
                    [
                        new($"Procurar atualizações ao abrir: {(Settings.AutoCheckUpdates ? "sim" : "não")}", () =>
                        {
                            UpdateSettings(s => s with { AutoCheckUpdates = !s.AutoCheckUpdates });
                            Reload();
                        }, ActionIcon.Update, Settings.AutoCheckUpdates ? "sim" : "não", "No máximo uma vez por dia, só nas versões publicadas do ControlFS no GitHub."),
                        next,
                    ];
                break;
            case OnboardingStep.Tutorial:
                modal.StepTitle = "Quer fazer o tutorial guiado?";
                modal.StepBody = $"Em {GuidedTutorial.Steps.Count} passos curtos você usa os botões de verdade nas suas pastas, sem alterar nenhum arquivo. "
                    + "Dá para pular a qualquer momento.";
                modal.Options =
                [
                    new("Começar tutorial", () => FinishOnboarding(tutorial: true, skipped: false), ActionIcon.Tutorial),
                    new("Agora não", () => FinishOnboarding(tutorial: false, skipped: false), ActionIcon.Close, Detail: "Ele fica em Menu → Ajuda."),
                ];
                break;
        }
        modal.FocusIndex = keepFocus ? Math.Clamp(focus, 0, Math.Max(0, modal.Options.Count - 1)) : 0;
    }

    /// <summary>Ajuda (Menu → Ajuda): tutorial guiado, boas-vindas e Sobre.</summary>
    private void ShowHelpMenu() =>
        PushModal(new MenuModal("Ajuda",
        [
            new("Tutorial guiado", StartTutorial, Screen == Screen.FolderPicker ? "Conclua a escolha de pasta primeiro." : null,
                Detail: $"{GuidedTutorial.Steps.Count} passos com os botões de verdade, sem alterar arquivos. Dá para pular a qualquer momento.", Icon: ActionIcon.Tutorial),
            new("Rever boas-vindas", ShowOnboarding, Detail: "Controles, ajustes básicos e privacidade.", Icon: ActionIcon.Help),
            new("Mais da equipe", ShowPromo, Detail: "NextBoost PRO e Console Mode, os outros aplicativos da equipe.", Icon: ActionIcon.Game),
            new("Sobre o ControlFS", ShowAbout, Detail: $"Versão {AppVersion} · licença AGPL-3.0-only", Icon: ActionIcon.About),
        ]) { Icon = ActionIcon.Help });

    // ---------- Tutorial guiado ----------

    /// <summary>Começa o tutorial onde o usuário está (nunca no seletor de pasta). Nenhum passo altera arquivos.</summary>
    public void StartTutorial()
    {
        if (Screen == Screen.FolderPicker) return;
        _lastAction = null;
        var tutorial = new GuidedTutorial();
        tutorial.Start(ObserveForTutorial());
        Tutorial = tutorial;
        StatusMessage = null;
        RaiseChanged();
    }

    /// <summary>Encerra o tutorial (Pular tutorial).</summary>
    public void SkipTutorial()
    {
        if (Tutorial is null) return;
        Tutorial = null;
        SetStatus("Tutorial encerrado. Para refazer: Menu → Ajuda → Tutorial guiado.");
        TryShowPromo();
    }

    /// <summary>Volta um passo do tutorial (Voltar passo).</summary>
    public void PreviousTutorialStep()
    {
        if (Tutorial is not { } tutorial) return;
        tutorial.Previous(ObserveForTutorial());
        RaiseChanged();
    }

    /// <summary>
    /// Opções do tutorial (Marcar/West durante o tutorial, fora do teclado virtual): continuar, voltar um passo ou pular.
    /// Marcar não é usado por nenhum passo, e o tutorial nunca marca arquivos.
    /// </summary>
    private bool HandleTutorialOptions(InputAction action)
    {
        if (Tutorial is not { } tutorial || action != InputAction.ToggleSelection || TopModal is not (null or MenuModal)) return false;
        var card = BuildTutorialCard(tutorial);
        var dialog = new DialogModal("Tutorial guiado", [("Passo", $"{card.Number} de {card.Count}: {card.Title}")])
        {
            Icon = ActionIcon.Tutorial,
            Message = "Continue de onde parou, volte um passo ou encerre o tutorial.",
        };
        var keep = new DialogOption("Continuar", DialogOptionKind.Primary, () => CloseModal(dialog), ActionIcon.Resume);
        dialog.Options.Add(keep);
        if (tutorial.Index > 0 || tutorial.Phase > 0)
            dialog.Options.Add(new DialogOption("Voltar passo", DialogOptionKind.Safe, () =>
            {
                CloseModal(dialog);
                PreviousTutorialStep();
            }, ActionIcon.Back));
        dialog.Options.Add(new DialogOption("Pular tutorial", DialogOptionKind.Safe, () =>
        {
            CloseModal(dialog);
            SkipTutorial();
        }, ActionIcon.Skip));
        dialog.BackOption = keep;
        PushModal(dialog);
        return true;
    }

    /// <summary>O passo atual para a tela desenhar (null: sem tutorial).</summary>
    public TutorialCard? TutorialView => Tutorial is { IsComplete: false } tutorial ? BuildTutorialCard(tutorial) : null;

    private TutorialCard BuildTutorialCard(GuidedTutorial tutorial)
    {
        var first = tutorial.Phase == 0;
        var (title, text, actions, target) = tutorial.Step switch
        {
            TutorialStepKind.MoveFocus => ("Mova o foco", "Use o direcional ou o analógico esquerdo para andar pelos itens.",
                (Hint[])[new(InputAction.NavigateDown, "Mover")], TutorialTarget.Content),
            TutorialStepKind.OpenFolder => ("Abra uma pasta", "Escolha uma pasta (por exemplo, Documentos) e pressione Abrir.",
                [new(InputAction.Confirm, "Abrir")], TutorialTarget.Content),
            TutorialStepKind.GoBack => ("Volte", "Pressione Voltar para sair da pasta e voltar ao lugar de antes.",
                [new(InputAction.Back, "Voltar")], TutorialTarget.Content),
            TutorialStepKind.OpenActions when first => ("Abra as Ações", "Ações mostra o que dá para fazer com o item em foco. O botão muda com o contexto: Extrair num compactado, Filtros numa busca.",
                [new(InputAction.OpenContextMenu, "Ações")], TutorialTarget.Content),
            TutorialStepKind.OpenActions => ("Feche as Ações", "Nada será feito agora: pressione Voltar para fechar.",
                [new(InputAction.Back, "Fechar")], TutorialTarget.Modal),
            TutorialStepKind.TopBar when first => ("Barra superior", $"{ButtonName(InputAction.PreviousRegion)} entra no caminho (as pastas de cima) e {ButtonName(InputAction.NextRegion)} nos atalhos das suas pastas. Para o início: {ButtonName(InputAction.PreviousRegion)}, {ButtonName(InputAction.PageUp)} e Abrir.",
                [new(InputAction.PreviousRegion, "Caminho"), new(InputAction.NextRegion, "Atalhos")], TutorialTarget.TopBar),
            TutorialStepKind.TopBar => ("Abra um atalho", "Escolha um atalho (por exemplo, Downloads) com o direcional e pressione Abrir.",
                [new(InputAction.NavigateRight, "Escolher"), new(InputAction.Confirm, "Abrir")], TutorialTarget.TopBar),
            TutorialStepKind.ChangeView => ("Lista ou grade", "Troque a exibição. Pressione de novo quando quiser voltar ao que era.",
                [new(InputAction.ChangeView, "Lista/Grade")], TutorialTarget.Content),
            TutorialStepKind.Search when first => ("Buscar", "Pressione Buscar para procurar arquivos pelo nome: numa pasta, ou no início, nas pastas principais.",
                [new(InputAction.Search, "Buscar")], TutorialTarget.Footer),
            TutorialStepKind.Search => ("Conclua ou cancele", "Digite parte de um nome e conclua, ou pressione Voltar para cancelar.",
                [new(InputAction.OpenAppMenu, "Concluir"), new(InputAction.Back, "Cancelar")], TutorialTarget.Modal),
            TutorialStepKind.AppMenu when first => ("Menu", "Menu reúne Configurações, Início, colar, nova pasta, abas e operações. Abra o Menu.",
                [new(InputAction.OpenAppMenu, "Menu")], TutorialTarget.Footer),
            _ => ("Configurações", "Escolha Configurações: todos os ajustes ficam lá.",
                [new(InputAction.Confirm, "Escolher")], TutorialTarget.Modal),
        };
        return new(Math.Min(tutorial.Index, tutorial.Count - 1) + 1, tutorial.Count, title, text, actions, target);
    }

    /// <summary>
    /// Nome do botão no texto corrido, do controle em uso (LB no Xbox, L1 no PlayStation, L no Nintendo; a tecla sem
    /// controle). Símbolos que só fazem sentido como glifo (☰, ⧉) viram o nome falado ("Menu", "Exibir").
    /// </summary>
    private string ButtonName(InputAction action)
    {
        var prompt = PromptProvider.For(action, string.Empty);
        return prompt is { Button: { } button, Family: { } family } && prompt.Key is "☰" or "⧉"
            ? ControllerButtons.SpokenName(button, family)
            : prompt.Key;
    }

    private TutorialObservation ObserveForTutorial()
    {
        var home = Screen == Screen.Home;
        string? focus = home
            ? PlacesFocus >= 0 && PlacesFocus < Places.Count ? Places[PlacesFocus].Id : null
            : ActivePane.List.Focused?.Id;
        var modal = TopModal switch
        {
            null => TutorialModal.None,
            KeyboardModal keyboard when keyboard.Title.StartsWith("Buscar", StringComparison.Ordinal) => TutorialModal.SearchKeyboard,
            MenuModal { Title: "Menu" } => TutorialModal.AppMenu,
            MenuModal { Title: "Configurações" } => TutorialModal.Settings,
            MenuModal => TutorialModal.Actions,
            _ => TutorialModal.Other,
        };
        return new(_lastAction, home, home ? null : ActivePane.Location, !home && ActivePane.IsLoading, focus, FocusRegion, modal, Settings.View);
    }

    /// <summary>A cada mudança: o passo atual confere o estado; no fim, um resumo.</summary>
    private void ObserveTutorial()
    {
        if (Tutorial is not { } tutorial || _observingTutorial) return;
        _observingTutorial = true;
        try
        {
            if (!tutorial.Observe(ObserveForTutorial()) || !tutorial.IsComplete) return;
            Tutorial = null;
            ReturnHome();
            ShowMessage("Tutorial concluído",
            [
                ("Abrir e voltar", "Confirmar e Voltar"),
                ("O que fazer com um item", "Ações"),
                ("Barra superior", $"{ButtonName(InputAction.PreviousRegion)} e {ButtonName(InputAction.NextRegion)}"),
                ("Lista ou grade", ButtonName(InputAction.ChangeView)),
                ("Buscar e Menu", $"{ButtonName(InputAction.Search)} e {ButtonName(InputAction.OpenAppMenu)}"),
            ], "O rodapé sempre mostra os botões que funcionam na tela. Para refazer: Menu → Ajuda → Tutorial guiado.", ActionIcon.Success);
        }
        finally
        {
            _observingTutorial = false;
        }
    }

    /// <summary>Texto do passo para o Narrador quando o passo muda (null: nada novo).</summary>
    private string? TakeTutorialAnnouncement()
    {
        var card = TutorialView;
        var text = card is null ? null : $"Tutorial, passo {card.Number} de {card.Count}: {card.Title}. {card.Instruction}";
        if (text == _announcedTutorial) return null;
        _announcedTutorial = text;
        return text;
    }
}
