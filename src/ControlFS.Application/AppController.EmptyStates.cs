using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Models;

namespace ControlFS.Application;

/// <summary>
/// Texto da lista vazia: diz onde você está e, quando há, o próximo passo com o botão do controle em uso
/// (ex.: "Pasta vazia — Y Ações: Colar / Nova pasta"). Nunca oferece o que não funciona ali.
/// </summary>
public sealed partial class AppController
{
    public string EmptyMessage(PaneState pane)
    {
        if (pane.IsLoading) return "Carregando…";
        if (pane.List.Items.Count > 0) return string.Empty;
        string Key(InputAction action) => PromptProvider.For(action, string.Empty).Key;
        switch (pane.Location)
        {
            case null:
                return "Sem pasta: ative este painel para escolher um local";
            case SearchLocation when pane.ActiveSearch is { } search:
                if (search.IsRunning) return "Buscando…";
                return search.Filter.IsActive && search.Results.Count > 0
                    ? $"Nenhum resultado com estes filtros — {Key(InputAction.OpenContextMenu)} Filtros para mudar"
                    : $"Nenhum resultado — {Key(InputAction.OpenContextMenu)} Filtros · {Key(InputAction.Search)} Nova busca";
            case RecycleBinLocation:
                return "Lixeira vazia";
            case ArchiveLocation { InnerPath.Length: 0 }:
                return "Compactado vazio";
            case PhysicalLocation when pane.Mode == PaneMode.Browse:
                var next = new List<string>();
                if (Clipboard is not null && PasteUnavailable(pane) is null) next.Add("Colar");
                next.Add("Nova pasta");
                return $"Pasta vazia — {Key(InputAction.OpenContextMenu)} Ações: {string.Join(" / ", next)}";
            default:
                return "Pasta vazia";
        }
    }
}
