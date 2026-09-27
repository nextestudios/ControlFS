using ControlFS.Core.Actions;

namespace ControlFS.Application.State;

public enum HomeSectionKind
{
    /// <summary>Pastas favoritas (só quando há alguma).</summary>
    Favorites,

    /// <summary>Pastas principais do Windows (Downloads, Documentos…), com contagem e tamanho reais.</summary>
    Folders,

    /// <summary>Unidades e dispositivos, com a barra de uso.</summary>
    Drives,

    /// <summary>Recentes e Lixeira.</summary>
    Other,
}

/// <summary>
/// Seção do início na grade: título e os índices em <see cref="AppController.Places"/> na ordem mostrada. A lista
/// continua mostrando <see cref="AppController.Places"/> na ordem original; só a grade agrupa.
/// </summary>
public sealed record HomeSection(HomeSectionKind Kind, string Title, IReadOnlyList<int> Places);

public enum FolderStatsState
{
    Calculating,
    Ready,

    /// <summary>O cálculo passou do tempo limite: o valor é real, mas mínimo (mostrado com "+").</summary>
    Partial,
    Unavailable,
}

/// <summary>Itens (arquivos e pastas, recursivo) e bytes de uma pasta, calculados fora da thread de UI.</summary>
public sealed record FolderStats(FolderStatsState State, long Items = 0, long Bytes = 0)
{
    public static FolderStats Calculating { get; } = new(FolderStatsState.Calculating);
}

/// <summary>
/// Foco 2D numa grade feita de seções empilhadas, cada uma com o próprio número de colunas (pastas em 3, unidades em
/// 2…). Esquerda/direita seguem a ordem visual e passam de uma seção para a outra; cima/baixo trocam de linha na mesma
/// coluna (limitada às colunas da seção de destino); LT/RT vão ao início da seção anterior/seguinte.
/// </summary>
public static class SectionGridNavigation
{
    /// <summary>
    /// Novo item focado (índice em <see cref="AppController.Places"/>), ou <c>null</c> quando a ação não é de navegação
    /// ou o foco não está em nenhuma seção.
    /// </summary>
    public static int? Move(IReadOnlyList<HomeSection> sections, Func<HomeSectionKind, int> columnsOf, int focused, InputAction action)
    {
        var s = -1;
        var i = -1;
        for (var k = 0; k < sections.Count && s < 0; k++)
        {
            var at = IndexOf(sections[k].Places, focused);
            if (at >= 0) (s, i) = (k, at);
        }
        if (s < 0) return null;
        var section = sections[s];
        var columns = Math.Max(1, columnsOf(section.Kind));
        var row = i / columns;
        var column = i % columns;
        var lastRow = (section.Places.Count - 1) / columns;
        switch (action)
        {
            case InputAction.NavigateLeft:
                if (i > 0) return section.Places[i - 1];
                return s > 0 ? sections[s - 1].Places[^1] : focused;
            case InputAction.NavigateRight:
                if (i < section.Places.Count - 1) return section.Places[i + 1];
                return s < sections.Count - 1 ? sections[s + 1].Places[0] : focused;
            case InputAction.NavigateUp:
                if (row > 0) return section.Places[i - columns];
                if (s == 0) return focused;
                var above = sections[s - 1];
                var aboveColumns = Math.Max(1, columnsOf(above.Kind));
                var aboveLastRow = (above.Places.Count - 1) / aboveColumns;
                return above.Places[Math.Min(above.Places.Count - 1, aboveLastRow * aboveColumns + Math.Min(column, aboveColumns - 1))];
            case InputAction.NavigateDown:
                if (row < lastRow) return section.Places[Math.Min(section.Places.Count - 1, i + columns)];
                if (s == sections.Count - 1) return focused;
                var below = sections[s + 1];
                return below.Places[Math.Min(below.Places.Count - 1, Math.Min(column, Math.Max(1, columnsOf(below.Kind)) - 1))];
            case InputAction.PageUp:
                if (i > 0) return section.Places[0];
                return s > 0 ? sections[s - 1].Places[0] : focused;
            case InputAction.PageDown:
                return s < sections.Count - 1 ? sections[s + 1].Places[0] : section.Places[^1];
            default:
                return null;
        }
    }

    private static int IndexOf(IReadOnlyList<int> list, int value)
    {
        for (var k = 0; k < list.Count; k++)
            if (list[k] == value) return k;
        return -1;
    }
}
