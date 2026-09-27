using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Models;

namespace ControlFS.Application;

public sealed partial class AppController
{
    /// <summary>
    /// Fonte única do rodapé: somente ações que funcionam no contexto atual, com o rótulo do que farão.
    /// Ações indisponíveis não aparecem (o motivo, quando existe, é mostrado no próprio item).
    /// </summary>
    private List<Hint> BuildHints()
    {
        var hints = new List<Hint>();
        switch (TopModal)
        {
            case MenuModal menu:
                // Item indisponível em foco: Confirmar só mostraria o motivo, que já aparece no item.
                if (menu.Items.Count > 0 && menu.Items[menu.FocusIndex].IsEnabled) hints.Add(new(InputAction.Confirm, "Escolher"));
                hints.Add(new(InputAction.Back, "Fechar"));
                return hints;
            case KeyboardModal { IsBusy: true }:
                return hints; // aguardando a validação do texto: nenhuma ação é aceita
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
                hints.Add(new(InputAction.Back, "Fechar"));
                return hints;
            case ImagePreviewModal preview:
                if (preview.ZoomIndex > 0)
                {
                    hints.Add(new(InputAction.NavigateLeft, "Mover"));
                    hints.Add(new(InputAction.Confirm, "Ajustar à tela"));
                }
                if (preview.Images.Count > 1)
                {
                    hints.Add(new(InputAction.PreviousRegion, "Anterior"));
                    hints.Add(new(InputAction.NextRegion, "Próxima"));
                }
                if (preview.ZoomIndex > 0) hints.Add(new(InputAction.PageUp, "Menos zoom"));
                if (preview.Image is not null && preview.ZoomIndex < ImagePreviewModal.ZoomLevels.Count - 1) hints.Add(new(InputAction.PageDown, "Mais zoom"));
                hints.Add(new(InputAction.Back, "Fechar"));
                return hints;
            case DialogModal dialog:
                // Confirmar executa a opção em foco; diálogo sem opções só fecha com Voltar.
                if (dialog.Options.Count > 0) hints.Add(new(InputAction.Confirm, dialog.Options[dialog.FocusIndex].Label));
                hints.Add(new(InputAction.Back, dialog.BackOption?.Label ?? "Fechar"));
                return hints;
        }

        if (Screen == Screen.Home)
        {
            if (Places.Count > 0)
            {
                hints.Add(new(InputAction.Confirm, "Abrir"));
                hints.Add(new(InputAction.OpenContextMenu, "Ações"));
            }
            hints.Add(new(InputAction.OpenAppMenu, "Menu"));
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

        if (pane.Region == PaneRegion.Breadcrumbs && crumbs.Count > 0)
        {
            var crumb = crumbs[Math.Clamp(pane.BreadcrumbFocus, 0, crumbs.Count - 1)];
            hints.Add(new(InputAction.Confirm, crumb.Kind == BreadcrumbKind.Collapsed ? "Mostrar pastas" : crumb.IsCurrent ? "Voltar à lista" : "Ir para"));
            hints.Add(new(InputAction.OpenContextMenu, "Caminho completo"));
            hints.Add(new(InputAction.Back, "Voltar à lista"));
            return hints;
        }

        if (pane.ActiveSearch is { } search)
        {
            // Resultados de busca: abrir leva à pasta do item; Voltar primeiro cancela a busca em andamento.
            if (pane.List.Focused is not null) hints.Add(new(InputAction.Confirm, "Mostrar na pasta"));
            hints.Add(new(InputAction.OpenContextMenu, search.Filter.IsActive ? "Filtros (ativos)" : "Filtros"));
            hints.Add(new(InputAction.Search, "Nova busca"));
            hints.Add(new(InputAction.NextRegion, TabsHint));
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

        if (crumbs.Count > 1) hints.Add(new(InputAction.PreviousRegion, "Caminho"));
        if (pane.Mode == PaneMode.PickFolder)
        {
            hints.Add(new(InputAction.OpenAppMenu, "Escolher esta pasta…"));
            hints.Add(new(InputAction.Back, pane.CanGoBack ? "Voltar" : "Cancelar"));
            return hints;
        }
        hints.Add(new(InputAction.NextRegion, TabsHint));
        if (focused is not null && !focused.IsBlocked && focused.Kind is not (EntryKind.Drive or EntryKind.KnownFolder))
            hints.Add(new(InputAction.ToggleSelection, pane.List.IsSelected(focused) ? "Desmarcar" : "Marcar"));
        hints.Add(new(InputAction.OpenContextMenu, ActionsLabel(pane, selection, archiveOnDisk)));
        if (pane.Location is PhysicalLocation) hints.Add(new(InputAction.Search, "Buscar"));
        hints.Add(new(InputAction.OpenAppMenu, "Menu"));
        hints.Add(new(InputAction.Back, selection > 0 ? "Cancelar seleção" : "Voltar"));
        return hints;
    }

    private string TabsHint => _tabs.Count > 1 ? $"Abas ({ActiveTab + 1}/{_tabs.Count})" : "Abas";

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
