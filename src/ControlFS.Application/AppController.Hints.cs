using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;

namespace ControlFS.Application;

public sealed partial class AppController
{
    /// <summary>
    /// Fonte única do rodapé: somente ações que funcionam no contexto atual, com o rótulo do que farão.
    /// Ações indisponíveis não aparecem (o motivo, quando existe, é mostrado no próprio item).
    /// </summary>
    private List<Hint> BuildHints() => WithTutorial(TopModal is null ? ScreenOrder(BuildScreenHints()) : BuildModalHints());

    /// <summary>Tutorial em andamento (#231): Marcar abre as opções dele (continuar, voltar passo, pular) em vez de marcar.</summary>
    private List<Hint> WithTutorial(List<Hint> hints)
    {
        if (Tutorial is null || TopModal is not (null or MenuModal)) return hints;
        var index = hints.FindIndex(h => h.Action == InputAction.ToggleSelection);
        if (index >= 0) hints[index] = new(InputAction.ToggleSelection, "Tutorial");
        else hints.Add(new(InputAction.ToggleSelection, "Tutorial"));
        return hints;
    }

    /// <summary>
    /// Ordem do rodapé nas telas (não nos modais, onde a ordem acompanha o conteúdo): Confirmar, Voltar, Marcar, Ações,
    /// Menu, Buscar, Lista/Grade e as regiões (LB/RB). Só a ordem muda; quais legendas aparecem continua por contexto.
    /// </summary>
    private List<Hint> ScreenOrder(List<Hint> hints)
    {
        if (FocusRegion != PaneRegion.List) return hints; // barra de caminho e abas: a ordem é a da tarefa
        return [.. hints.OrderBy(h => h.Action switch
        {
            InputAction.Confirm => 0,
            InputAction.Back => 1,
            InputAction.ToggleSelection => 2,
            InputAction.OpenContextMenu => 3,
            InputAction.OpenAppMenu => 4,
            InputAction.Search => 5,
            InputAction.ChangeView => 6,
            InputAction.PreviousRegion => 7,
            InputAction.NextRegion => 8,
            _ => 9,
        })];
    }

    /// <summary>Lista/grade (R3, Ctrl+G): a legenda é uma ação ("Ver em grade"), para não ser lida como o estado atual.</summary>
    private Hint ChangeViewHint => new(InputAction.ChangeView, IsGrid ? "Ver em lista" : "Ver em grade");

    private List<Hint> BuildModalHints()
    {
        var hints = new List<Hint>();
        switch (TopModal)
        {
            case MenuModal menu:
                // Item indisponível em foco: Confirmar só mostraria o motivo, que já aparece no item.
                if (menu.Items.Count > 0 && menu.Items[menu.FocusIndex].IsEnabled) hints.Add(new(InputAction.Confirm, "Escolher"));
                hints.Add(new(InputAction.Back, menu.IsPicker ? "Cancelar" : "Fechar"));
                return hints;
            case KeyboardModal { IsBusy: true }:
                return hints; // aguardando a validação do texto: nenhuma ação é aceita
            case KeyboardModal { Keyboard.FocusedSuggestion: not null }:
                hints.Add(new(InputAction.Confirm, "Usar sugestão"));
                hints.Add(new(InputAction.NavigateDown, "Teclas"));
                hints.Add(new(InputAction.OpenAppMenu, "Concluir"));
                hints.Add(new(InputAction.Back, "Cancelar"));
                return hints;
            case KeyboardModal:
                // Com o teclado físico, as teclas digitam direto (Enter conclui); "Selecionar" é coisa do controle.
                if (ActiveController is not null) hints.Add(new(InputAction.Confirm, "Selecionar"));
                hints.Add(new(InputAction.ToggleSelection, "Apagar"));
                hints.Add(new(InputAction.OpenContextMenu, "Maiúsculas"));
                hints.Add(new(InputAction.PreviousRegion, "Cursor ◀"));
                hints.Add(new(InputAction.NextRegion, "Cursor ▶"));
                hints.Add(new(InputAction.PageUp, "Início"));
                hints.Add(new(InputAction.PageDown, "Fim"));
                hints.Add(new(InputAction.Search, "Símbolos"));
                if (GyroAimAvailable) hints.Add(new(InputAction.ChangeView, "Recentralizar mira"));
                hints.Add(new(InputAction.OpenAppMenu, "Concluir"));
                hints.Add(new(InputAction.Back, "Cancelar"));
                return hints;
            case MappingWizardModal { Wizard.Phase: Core.Input.Mapping.MappingPhase.Review } wizard:
                hints.Add(new(InputAction.Confirm, MappingWizardModal.ReviewOptions[wizard.ReviewFocus]));
                hints.Add(new(InputAction.Back, "Descartar"));
                return hints;
            case MappingWizardModal wizard:
                if (wizard.Wizard.CanSkip) hints.Add(new(InputAction.Confirm, "Pular"));
                if (wizard.Wizard.CanRedoPrevious) hints.Add(new(InputAction.NavigateLeft, "Refazer anterior"));
                hints.Add(new(InputAction.Back, "Cancelar"));
                return hints;
            case ControllerTestModal:
                // No controle, Confirmar e Voltar também estão sendo testados: agem só quando mantidos.
                hints.Add(new(InputAction.Confirm, ActiveController is null ? "Copiar relatório" : "Segure: copiar relatório"));
                hints.Add(new(InputAction.Back, ActiveController is null ? "Sair" : "Segure: sair"));
                return hints;
            case AboutModal:
                hints.Add(new(InputAction.Back, "Fechar"));
                return hints;
            case PromoModal promo:
                hints.Add(new(InputAction.Confirm, promo.CloseFocused ? "Fechar" : "Abrir no navegador"));
                if (!promo.CloseFocused && promo.Cards.Count > 1)
                    hints.Add(promo.FocusIndex % PromoModal.Columns == 0 && promo.FocusIndex + 1 < promo.Cards.Count ? new(InputAction.NavigateRight, "Próximo") : new(InputAction.NavigateLeft, "Anterior"));
                hints.Add(new(InputAction.Back, "Fechar"));
                return hints;
            case OnboardingModal onboarding:
                if (onboarding.Options.Count > 0) hints.Add(new(InputAction.Confirm, onboarding.FocusedOption!.IsSetting ? "Trocar" : "Escolher"));
                if (onboarding.StepIndex > 0) hints.Add(new(InputAction.Back, "Passo anterior"));
                if (!onboarding.IsLastStep) hints.Add(new(InputAction.NextRegion, "Próximo passo"));
                hints.Add(new(InputAction.OpenAppMenu, "Pular"));
                return hints;
            case TextPreviewModal { Editor: { } editor }:
                hints.Add(new(InputAction.Confirm, "Editar linha"));
                hints.Add(new(InputAction.NavigateDown, "Linha"));
                hints.Add(new(InputAction.OpenContextMenu, "Inserir/apagar"));
                if (editor.IsModified) hints.Add(new(InputAction.OpenAppMenu, "Salvar"));
                hints.Add(new(InputAction.Back, editor.IsModified ? "Sair (descartar?)" : "Sair da edição"));
                return hints;
            case TextPreviewModal text:
                if (text.Document is { Lines.Count: > 0 })
                {
                    hints.Add(new(InputAction.NavigateDown, "Rolar"));
                    hints.Add(new(InputAction.PageUp, "Página ▲"));
                    hints.Add(new(InputAction.PageDown, "Página ▼"));
                    hints.Add(new(InputAction.PreviousRegion, "Início"));
                    hints.Add(new(InputAction.NextRegion, "Fim"));
                    hints.Add(new(InputAction.Confirm, text.Monospace ? "Fonte proporcional" : "Fonte fixa"));
                }
                if (CanOfferEdit(text)) hints.Add(new(InputAction.OpenContextMenu, "Editar"));
                hints.Add(new(InputAction.Back, "Fechar"));
                return hints;
            case ImagePreviewModal preview:
                if (preview.ZoomIndex > 0)
                {
                    hints.Add(new(InputAction.NavigateLeft, "Mover"));
                    hints.Add(new(InputAction.Confirm, "Ajustar à tela"));
                }
                // Só o que funciona agora (#171): na primeira imagem não há "Anterior", na última não há "Próxima".
                if (preview.Index > 0) hints.Add(new(InputAction.PreviousRegion, "Anterior"));
                if (preview.Index < preview.Images.Count - 1) hints.Add(new(InputAction.NextRegion, "Próxima"));
                if (preview.ZoomIndex > 0) hints.Add(new(InputAction.PageUp, "Menos zoom"));
                if (preview.Image is not null && preview.ZoomIndex < ZoomablePreviewModal.ZoomLevels.Count - 1) hints.Add(new(InputAction.PageDown, "Mais zoom"));
                hints.Add(new(InputAction.Back, "Fechar"));
                return hints;
            case PdfPreviewModal { NeedsPassword: true }:
                hints.Add(new(InputAction.Confirm, "Digitar senha"));
                hints.Add(new(InputAction.Back, "Fechar"));
                return hints;
            case PdfPreviewModal pdf:
                if (pdf.ZoomIndex > 0)
                {
                    hints.Add(new(InputAction.NavigateLeft, "Mover"));
                    hints.Add(new(InputAction.Confirm, "Ajustar à tela"));
                }
                if (pdf.Document is not null && pdf.PageIndex > 0) hints.Add(new(InputAction.PreviousRegion, "Página anterior"));
                if (pdf.Document is not null && pdf.PageIndex < pdf.PageCount - 1) hints.Add(new(InputAction.NextRegion, "Próxima página"));
                if (pdf.ZoomIndex > 0) hints.Add(new(InputAction.PageUp, "Menos zoom"));
                if (pdf.Page is not null && pdf.ZoomIndex < ZoomablePreviewModal.ZoomLevels.Count - 1) hints.Add(new(InputAction.PageDown, "Mais zoom"));
                hints.Add(new(InputAction.Back, "Fechar"));
                return hints;
            case AudioPreviewModal audio:
                if (audio.Session is not null && audio.DisplayError is null)
                {
                    var state = audio.Status.State;
                    hints.Add(new(InputAction.Confirm, state is MediaPlaybackState.Playing or MediaPlaybackState.Buffering ? "Pausar"
                        : state == MediaPlaybackState.Ended ? "Tocar de novo" : "Tocar"));
                    if (audio.Status.CanSeek)
                    {
                        hints.Add(new(InputAction.NavigateLeft, "−10 s"));
                        hints.Add(new(InputAction.NavigateRight, "+10 s"));
                        hints.Add(new(InputAction.PreviousRegion, "−1 min"));
                        hints.Add(new(InputAction.NextRegion, "+1 min"));
                    }
                    hints.Add(new(InputAction.NavigateUp, "Volume"));
                    hints.Add(new(InputAction.OpenContextMenu, audio.Status.IsMuted ? "Com som" : "Sem som"));
                }
                hints.Add(new(InputAction.Back, "Fechar"));
                return hints;
            case VideoPlayerModal video:
                if (video.Session is not null && video.DisplayError is null)
                {
                    if (video.SeekTarget is not null)
                    {
                        hints.Add(new(InputAction.Confirm, "Ir agora"));
                        hints.Add(new(InputAction.Back, "Cancelar salto"));
                        return hints;
                    }
                    var playing = video.Status.State is MediaPlaybackState.Playing or MediaPlaybackState.Buffering;
                    hints.Add(new(InputAction.Confirm, playing ? "Pausar" : video.Status.State == MediaPlaybackState.Ended ? "Ver de novo" : "Tocar"));
                    if (video.Status.CanSeek)
                    {
                        hints.Add(new(InputAction.NavigateLeft, "±10 s"));
                        hints.Add(new(InputAction.PreviousRegion, "−1 min"));
                        hints.Add(new(InputAction.NextRegion, "+1 min"));
                        hints.Add(new(InputAction.PageDown, "Linha do tempo"));
                    }
                    hints.Add(new(InputAction.NavigateUp, "Volume"));
                    hints.Add(new(InputAction.OpenContextMenu, "Legendas e áudio"));
                    hints.Add(new(InputAction.Back, video.OverlayVisible && playing ? "Esconder" : "Voltar à lista"));
                    return hints;
                }
                hints.Add(new(InputAction.Back, "Voltar à lista"));
                return hints;
            case DialogModal dialog:
            {
                // Confirmar executa a opção em foco; diálogo sem opções só fecha com Voltar. Com o foco na própria opção de
                // Voltar ("Cancelar", "Fechar"), os dois botões diriam o mesmo: no lugar de Confirmar, a próxima opção.
                var back = dialog.BackOption?.Label ?? "Fechar";
                if (dialog.Options.Count > 0)
                {
                    var focused = dialog.Options[dialog.FocusIndex];
                    if (focused != dialog.BackOption && focused.Label != back) hints.Add(new(InputAction.Confirm, focused.Label));
                    else if (dialog.Options.Count > 1) hints.Add(new(InputAction.NavigateRight, dialog.Options[(dialog.FocusIndex + 1) % dialog.Options.Count].Label));
                    if (dialog.StartOption is { } start && start != focused) hints.Add(new(InputAction.OpenAppMenu, start.Label));
                }
                hints.Add(new(InputAction.Back, back));
                return hints;
            }
        }
        return hints;
    }

    private List<Hint> BuildScreenHints()
    {
        var hints = new List<Hint>();
        if (Screen == Screen.Home && _homeRegion != PaneRegion.List)
        {
            AddTopBarHints(hints);
            return hints;
        }
        if (Screen == Screen.Home)
        {
            if (Places.Count > 0)
            {
                hints.Add(new(InputAction.Confirm, "Abrir"));
                hints.Add(new(InputAction.OpenContextMenu, "Ações"));
            }
            if (HomeSearchRoots().Count > 0) hints.Add(new(InputAction.Search, "Buscar"));
            AddTabTriggerHints(hints);
            hints.Add(new(InputAction.OpenAppMenu, "Menu"));
            if (Places.Count > 0) hints.Add(ChangeViewHint);
            hints.Add(new(InputAction.Back, "Sair"));
            return hints;
        }

        var pane = ActivePane;
        if (pane.IsLoading)
        {
            // Abrir, marcar e ações esperam a pasta carregar; Voltar cancela o carregamento.
            if (pane.Mode == PaneMode.Browse) hints.Add(new(InputAction.OpenAppMenu, "Menu"));
            hints.Add(new(InputAction.Back, "Cancelar"));
            return hints;
        }

        var crumbs = Breadcrumbs;
        if (pane.Region == PaneRegion.Tabs)
        {
            hints.Add(new(InputAction.PreviousRegion, "Aba anterior"));
            hints.Add(new(InputAction.NextRegion, "Próxima aba"));
            hints.Add(new(InputAction.OpenContextMenu, "Nova/fechar aba"));
            hints.Add(new(InputAction.Back, "Voltar à lista"));
            return hints;
        }

        if (pane.Region is PaneRegion.Breadcrumbs or PaneRegion.QuickAccess && crumbs.Count > 0)
        {
            AddTopBarHints(hints);
            return hints;
        }

        if (pane.ActiveSearch is { } search)
        {
            // Resultados de busca: abrir leva à pasta do item; Voltar primeiro cancela a busca em andamento.
            if (pane.List.Focused is not null) hints.Add(new(InputAction.Confirm, "Mostrar na pasta"));
            hints.Add(new(InputAction.OpenContextMenu, search.Filter.IsActive ? "Filtros (ativos)" : "Filtros"));
            hints.Add(new(InputAction.Search, "Nova busca"));
            hints.Add(ChangeViewHint);
            AddTopBarEntryHints(hints);
            hints.Add(new(InputAction.OpenAppMenu, "Menu"));
            hints.Add(new(InputAction.Back, search.IsRunning ? "Cancelar busca" : "Voltar"));
            return hints;
        }

        var focused = pane.List.Focused;
        var selection = pane.List.SelectionCount;
        if (pane.Location is RecycleBinLocation)
        {
            if (focused is not null)
            {
                hints.Add(new(InputAction.Confirm, "Restaurar/Excluir"));
                hints.Add(new(InputAction.ToggleSelection, pane.List.IsSelected(focused) ? "Desmarcar" : "Marcar"));
            }
            hints.Add(new(InputAction.OpenContextMenu, selection > 0 ? $"Operações ({selection})" : "Ações"));
            hints.Add(new(InputAction.OpenAppMenu, "Menu"));
            hints.Add(ChangeViewHint);
            hints.Add(new(InputAction.Back, selection > 0 ? "Cancelar seleção" : "Voltar"));
            return hints;
        }
        var archiveOnDisk = focused is { Kind: EntryKind.File, IsBlocked: false } && ArchiveFormats.HasExtractableExtension(focused.Name);
        if (focused is { IsBlocked: true }) hints.Add(new(InputAction.Confirm, "Motivo"));
        else if (focused is { Kind: EntryKind.ArchiveDirectory }) hints.Add(new(InputAction.Confirm, "Explorar"));
        else if (focused is { IsContainer: true }) hints.Add(new(InputAction.Confirm, "Abrir"));
        else if (archiveOnDisk && pane.Mode == PaneMode.Browse) hints.Add(new(InputAction.Confirm, "Explorar"));
        else if (focused is { Kind: EntryKind.File } && pane.Mode == PaneMode.Browse) hints.Add(new(InputAction.Confirm, "Abrir"));
        else if (focused is { Kind: EntryKind.ArchiveFile }) hints.Add(new(InputAction.Confirm, "Detalhes"));

        AddTopBarEntryHints(hints);
        if (pane.Mode == PaneMode.PickFolder)
        {
            hints.Add(new(InputAction.OpenAppMenu, "Escolher esta pasta…"));
            hints.Add(new(InputAction.Back, pane.CanGoBack ? "Voltar" : "Cancelar"));
            return hints;
        }
        if (focused is not null && !focused.IsBlocked && focused.Kind is not (EntryKind.Drive or EntryKind.KnownFolder))
            hints.Add(new(InputAction.ToggleSelection, pane.List.IsSelected(focused) ? "Desmarcar" : "Marcar"));
        hints.Add(new(InputAction.OpenContextMenu, ActionsLabel(pane, selection, archiveOnDisk)));
        if (pane.Location is PhysicalLocation || (pane.Location is ThisPcLocation && focused is { Kind: EntryKind.Drive, IsBlocked: false })) hints.Add(new(InputAction.Search, "Buscar"));
        AddTabTriggerHints(hints);
        hints.Add(new(InputAction.OpenAppMenu, "Menu"));
        hints.Add(ChangeViewHint);
        hints.Add(new(InputAction.Back, selection > 0 ? "Cancelar seleção" : "Voltar"));
        return hints;
    }

    /// <summary>
    /// O botão de ações diz o que abre: extração dentro de um compactado ou num compactado focado (o menu abre em
    /// "Extrair para…"), operações em lote com itens marcados. Marcar continua no botão de marcar, também para
    /// compactados, para que possam entrar em cópias/exclusões em lote.
    /// </summary>
    private static string ActionsLabel(PaneState pane, int selection, bool archiveOnDisk) => pane.Location switch
    {
        ArchiveLocation when selection > 0 => $"Extrair seleção ({selection})",
        ArchiveLocation => "Extrair…",
        PhysicalLocation when selection > 0 => $"Operações ({selection})",
        _ when archiveOnDisk => "Extrair…",
        _ => "Ações",
    };
}
