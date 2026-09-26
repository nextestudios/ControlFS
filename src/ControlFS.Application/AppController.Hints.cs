using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Models;

namespace ControlFS.Application;

public sealed partial class AppController
{
    /// <summary>Somente ações que funcionam no contexto atual aparecem no rodapé.</summary>
    private List<Hint> BuildHints()
    {
        var hints = new List<Hint>();
        switch (TopModal)
        {
            case MenuModal:
                hints.Add(new(InputAction.Confirm, "Escolher"));
                hints.Add(new(InputAction.Back, "Fechar"));
                return hints;
            case KeyboardModal:
                hints.Add(new(InputAction.Confirm, "Tecla"));
                hints.Add(new(InputAction.ToggleSelection, "Apagar"));
                hints.Add(new(InputAction.OpenContextMenu, "Maiúsculas"));
                hints.Add(new(InputAction.PreviousRegion, "Cursor ◀"));
                hints.Add(new(InputAction.NextRegion, "Cursor ▶"));
                hints.Add(new(InputAction.Search, "Símbolos"));
                hints.Add(new(InputAction.OpenAppMenu, "OK"));
                hints.Add(new(InputAction.Back, "Cancelar"));
                return hints;
            case DialogModal dialog:
                hints.Add(new(InputAction.Confirm, "Escolher"));
                hints.Add(new(InputAction.Back, dialog.BackOption?.Label ?? "Fechar"));
                return hints;
        }

        if (Screen == Screen.Home)
        {
            hints.Add(new(InputAction.Confirm, "Abrir"));
            hints.Add(new(InputAction.OpenAppMenu, "Menu"));
            hints.Add(new(InputAction.Back, "Sair"));
            return hints;
        }

        var pane = ActivePane;
        var focused = pane.List.Focused;
        if (focused is { IsContainer: true }) hints.Add(new(InputAction.Confirm, "Abrir"));
        else if (focused is { Kind: EntryKind.File } && pane.Mode == PaneMode.Browse) hints.Add(new(InputAction.Confirm, "Abrir/detalhes"));
        else if (focused is { Kind: EntryKind.ArchiveFile }) hints.Add(new(InputAction.Confirm, "Detalhes"));

        if (pane.Mode == PaneMode.PickFolder)
        {
            hints.Add(new(InputAction.OpenAppMenu, "Escolher esta pasta…"));
            hints.Add(new(InputAction.Back, pane.CanGoBack ? "Voltar" : "Cancelar"));
            return hints;
        }
        if (focused is not null && !focused.IsBlocked && focused.Kind is not (EntryKind.Drive or EntryKind.KnownFolder))
            hints.Add(new(InputAction.ToggleSelection, pane.List.IsSelected(focused) ? "Desmarcar" : "Marcar"));
        hints.Add(new(InputAction.OpenContextMenu, "Ações"));
        hints.Add(new(InputAction.OpenAppMenu, "Menu"));
        hints.Add(new(InputAction.Back, pane.List.SelectionCount > 0 ? "Limpar seleção" : "Voltar"));
        return hints;
    }
}
