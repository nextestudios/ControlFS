using ControlFS.Application.State;
using ControlFS.Core.Actions;

namespace ControlFS.Application;

public sealed partial class AppController
{
    /// <summary>
    /// Rolagem de conteúdo que só a tela conhece (corpo de um diálogo, "Sobre"): passos verticais para a tela aplicar no
    /// modal do topo. Nada por baixo do modal rola.
    /// </summary>
    public event Action<int>? ModalBodyScrollRequested;

    /// <summary>
    /// Um passo do analógico direito (#175) na superfície ativa, e só nela. Listas e grades andam uma linha (o foco
    /// acompanha e continua à vista, sem dar a volta nas pontas); texto rola uma linha ou algumas colunas; imagem com zoom
    /// desliza; diálogos rolam o corpo. Nunca abre, volta, troca de pasta ou de imagem. Falso: nada mudou (sem redesenho).
    /// </summary>
    private bool HandleScroll(InputAction action)
    {
        var (dx, dy) = action switch
        {
            InputAction.ScrollUp => (0, -1),
            InputAction.ScrollDown => (0, 1),
            InputAction.ScrollLeft => (-1, 0),
            _ => (1, 0),
        };
        switch (TopModal)
        {
            case null:
                return dy != 0 && ScrollScreen(dy);
            case MenuModal menu when dy != 0 && menu.Items.Count > 0:
                // Como cima/baixo (grade de ações rápidas e depois a lista), mas sem dar a volta nas pontas.
                var before = menu.FocusIndex;
                HandleMenu(menu, dy < 0 ? InputAction.NavigateUp : InputAction.NavigateDown);
                if (dy > 0 ? menu.FocusIndex < before : menu.FocusIndex > before) menu.FocusIndex = before;
                return menu.FocusIndex != before;
            case TextPreviewModal text:
                var (top, column) = (text.Top, text.Column);
                if (dy != 0) text.ScrollBy(dy);
                else text.ShiftColumns(dx * TextPreviewModal.ScrollColumns);
                return text.Top != top || text.Column != column;
            case ZoomablePreviewModal image when image.ZoomIndex > 0: // imagem ou PDF com zoom
                var (x, y) = (image.CenterX, image.CenterY);
                image.Pan(dx * ZoomablePreviewModal.ScrollPan, dy * ZoomablePreviewModal.ScrollPan);
                return image.CenterX != x || image.CenterY != y;
            case DialogModal or AboutModal when dy != 0:
                ModalBodyScrollRequested?.Invoke(dy); // a tela rola o corpo no lugar, sem refazer o modal
                return false;
            default:
                return false; // teclado virtual, assistente, teste do controle: o analógico direito não faz nada
        }
    }

    /// <summary>Lista/grade da tela (início ou navegador), só quando a lista tem o foco (não a barra de caminho).</summary>
    private bool ScrollScreen(int dy)
    {
        var step = dy < 0 ? InputAction.NavigateUp : InputAction.NavigateDown;
        if (Screen == Screen.Home)
        {
            if (_homeRegion != PaneRegion.List || Places.Count == 0) return false;
            var cell = IsGrid ? SectionGridNavigation.Move(HomeSections, HomeColumns, PlacesFocus, step) : null;
            var target = cell ?? Math.Clamp(PlacesFocus + dy, 0, Places.Count - 1);
            if (target == PlacesFocus) return false;
            PlacesFocus = target;
            return true;
        }
        var pane = ActivePane;
        if (pane.Region != PaneRegion.List) return false;
        var list = pane.List;
        return IsGrid && GridNavigation.Move(list.FocusIndex, list.Items.Count, GridColumns, GridRowsPerPage, step) is { } index
            ? list.FocusAt(index)
            : list.Move(dy);
    }
}
