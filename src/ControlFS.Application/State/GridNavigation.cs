using ControlFS.Core.Actions;

namespace ControlFS.Application.State;

/// <summary>
/// Movimento do foco numa grade de <c>columns</c> colunas preenchida linha a linha. Esquerda/direita andam um item e
/// passam de uma linha para a outra nas pontas (nunca pulam itens); cima/baixo trocam de linha na mesma coluna, e
/// descer para uma última linha incompleta vai para o último item. Nas bordas da grade o foco fica onde está.
/// </summary>
public static class GridNavigation
{
    /// <summary>Novo índice, ou <c>null</c> quando a ação não é de navegação na grade.</summary>
    public static int? Move(int index, int count, int columns, int rowsPerPage, InputAction action)
    {
        if (count <= 0) return null;
        columns = Math.Max(1, columns);
        index = Math.Clamp(index, 0, count - 1);
        var page = columns * Math.Max(1, rowsPerPage);
        return action switch
        {
            InputAction.NavigateLeft => Math.Max(0, index - 1),
            InputAction.NavigateRight => Math.Min(count - 1, index + 1),
            InputAction.NavigateUp => index - columns >= 0 ? index - columns : index,
            InputAction.NavigateDown => Down(index, count, columns, 1),
            InputAction.PageUp => Math.Max(index % columns, index - page),
            InputAction.PageDown => Down(index, count, columns, Math.Max(1, rowsPerPage)),
            _ => null,
        };
    }

    private static int Down(int index, int count, int columns, int rows)
    {
        var lastRow = (count - 1) / columns;
        var row = index / columns;
        if (row == lastRow) return index;
        var target = index + (columns * Math.Min(rows, lastRow - row));
        return Math.Min(target, count - 1);
    }
}
