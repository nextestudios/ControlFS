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
                hints.Add(new(InputAction.Search, "Símbolos"));
                hints.Add(new(InputAction.OpenAppMenu, "Concluir"));
                hints.Add(new(InputAction.Back, "Cancelar"));
                return hints;
            case AboutModal:
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
            if (Places.Count > 0) hints.Add(new(InputAction.Confirm, "Abrir"));
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

        var focused = pane.List.Focused;
        var selection = pane.List.SelectionCount;
        var archiveOnDisk = focused is { Kind: EntryKind.File, IsBlocked: false } && ArchiveFormats.HasExtractableExtension(focused.Name);
        if (focused is { IsBlocked: true }) hints.Add(new(InputAction.Confirm, "Motivo"));
        else if (focused is { IsContainer: true }) hints.Add(new(InputAction.Confirm, "Abrir"));
        else if (archiveOnDisk && pane.Mode == PaneMode.Browse) hints.Add(new(InputAction.Confirm, "Explorar"));
        else if (focused is { Kind: EntryKind.File } && pane.Mode == PaneMode.Browse) hints.Add(new(InputAction.Confirm, "Abrir"));
        else if (focused is { Kind: EntryKind.ArchiveFile }) hints.Add(new(InputAction.Confirm, "Detalhes"));

        if (pane.Mode == PaneMode.PickFolder)
        {
            hints.Add(new(InputAction.OpenAppMenu, "Escolher esta pasta…"));
            hints.Add(new(InputAction.Back, pane.CanGoBack ? "Voltar" : "Cancelar"));
            return hints;
        }
        if (focused is not null && !focused.IsBlocked && focused.Kind is not (EntryKind.Drive or EntryKind.KnownFolder))
            hints.Add(new(InputAction.ToggleSelection, pane.List.IsSelected(focused) ? "Desmarcar" : "Marcar"));
        hints.Add(new(InputAction.OpenContextMenu, ActionsLabel(pane, selection, archiveOnDisk)));
        hints.Add(new(InputAction.OpenAppMenu, "Menu"));
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
        ArchiveLocation => "Extrair…",
        PhysicalLocation when selection > 0 => $"Operações ({selection})",
        _ when archiveOnDisk => "Extrair…",
        _ => "Ações",
    };
}
