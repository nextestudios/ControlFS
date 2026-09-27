using ControlFS.Application.State;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;

namespace ControlFS.Application;

/// <summary>
/// Lixeira do Windows como local do navegador: lista os itens com o local original e a data da exclusão; Sul/A (ou
/// Norte) num item oferece Restaurar e Excluir permanentemente. Marcar funciona para agir em vários. Excluir de vez
/// sempre pergunta antes, com o foco em "Cancelar". Restaurar nunca sobrescreve o que estiver no local original.
/// </summary>
public sealed partial class AppController
{
    private readonly IRecycleBin? _recycleBin;

    private IEnumerable<FileEntry> RecycleBinPlace()
    {
        if (_recycleBin is null) yield break;
        yield return new FileEntry(RecycleBinLocation.PlaceId, "Lixeira", EntryKind.KnownFolder, Detail: "Itens excluídos: restaurar ou excluir de vez");
    }

    private void OpenRecycleBin() => OpenVirtual(RecycleBinLocation.Instance);

    /// <summary>Abre um local virtual (Lixeira, Meu computador) no navegador a partir do início, sem histórico.</summary>
    private void OpenVirtual(Location location)
    {
        Browser.Back.Clear();
        Browser.Forward.Clear();
        Browser.Location = null;
        Screen = Screen.Browser;
        Track(NavigateAsync(Browser, location, pushHistory: false));
    }

    /// <summary>
    /// Itens da Lixeira na lista: sem caminho físico (nada de abrir, copiar ou renomear), com a pasta de origem em
    /// <see cref="FileEntry.FoundIn"/> e a data da exclusão como data do item. Mais recentes primeiro na ordem por data.
    /// </summary>
    private static List<FileEntry> RecycledEntries(IReadOnlyList<RecycledItem> items) =>
        [.. items.Select(item => new FileEntry(item.Id, item.Name, item.IsDirectory ? EntryKind.Directory : EntryKind.File,
            Size: item.Size, Modified: item.DeletedAt, FoundIn: Path.GetDirectoryName(item.OriginalPath) ?? item.OriginalPath,
            BlockedReason: item.Problem))];

    private void ShowRecycleBinMenu(PaneState pane)
    {
        if (pane.IsLoading || _recycleBin is null) return;
        var marked = pane.List.SelectedEntries;
        var targets = marked.Count > 0 ? marked : pane.List.Focused is { } focused ? [focused] : [];
        var items = new List<MenuItem>();
        if (targets.Count > 0)
        {
            var restorable = targets.Where(t => !t.IsBlocked).ToList();
            var single = targets.Count == 1;
            items.Add(new MenuItem(single ? "Restaurar" : $"Restaurar {restorable.Count} item(ns)", () => Restore(pane, restorable),
                restorable.Count == 0 ? targets[0].BlockedReason : null, single ? "Para " + OriginalPath(targets[0]) : "Cada item volta para a pasta de onde saiu."));
            items.Add(new MenuItem(single ? "Excluir permanentemente…" : $"Excluir {targets.Count} item(ns) permanentemente…", () => ConfirmPurge(pane, targets)));
            if (targets.Count == 1) items.Add(new MenuItem("Propriedades", () => ShowRecycledProperties(targets[0])));
        }
        items.AddRange(SelectionItems(pane));
        items.Add(new MenuItem("Atualizar", () => Refresh(pane)));
        PushModal(new MenuModal(marked.Count > 0 ? $"{marked.Count} item(ns) marcado(s)" : targets.Count == 1 ? targets[0].Name : "Lixeira", items));
    }

    private static string OriginalPath(FileEntry entry) => Path.Join(entry.FoundIn, entry.Name);

    private void ShowRecycledProperties(FileEntry entry)
    {
        var lines = new List<(string, string)>
        {
            ("Nome", entry.Name),
            ("Local original", entry.FoundIn ?? "—"),
            ("Tipo", entry.IsContainer ? "Pasta" : entry.Extension.Length > 0 ? $"Arquivo {entry.Extension}" : "Arquivo"),
        };
        if (entry.Size is long size) lines.Add(("Tamanho", $"{FormatBytes(size)} ({size:N0} bytes)"));
        if (entry.Modified is { } deleted) lines.Add(("Excluído em", deleted.LocalDateTime.ToString("g")));
        if (entry.BlockedReason is { } problem) lines.Add(("Aviso", problem));
        ShowMessage("Propriedades", lines);
    }

    private void Restore(PaneState pane, IReadOnlyList<FileEntry> entries)
    {
        var ids = entries.Select(e => (e.Id, e.Name)).ToList();
        Track(RunOnBinAsync(pane, ids, id => _recycleBin!.Restore(id), done => done == 1 ? "1 item restaurado para o local original." : $"{done} itens restaurados para os locais originais.",
            "Alguns itens não foram restaurados"));
    }

    private void ConfirmPurge(PaneState pane, IReadOnlyList<FileEntry> entries)
    {
        var names = entries.Select(e => e.Name).Take(3).ToList();
        var itemsText = string.Join(", ", names) + (entries.Count > 3 ? $" e mais {entries.Count - 3}" : string.Empty);
        var dialog = new DialogModal($"Excluir {entries.Count} item(ns) permanentemente?", [("Itens", itemsText), ("Local", "Lixeira")], sensitive: true)
        {
            Message = "Os itens saem da Lixeira e não há como desfazer.",
        };
        var cancel = new DialogOption("Cancelar", DialogOptionKind.Safe, () => CloseModal(dialog));
        dialog.Options.Add(cancel);
        dialog.Options.Add(new DialogOption("Excluir permanentemente", DialogOptionKind.Danger, () =>
        {
            CloseModal(dialog);
            var ids = entries.Select(e => (e.Id, e.Name)).ToList();
            Track(RunOnBinAsync(pane, ids, id => _recycleBin!.DeletePermanently(id),
                done => done == 1 ? "1 item excluído permanentemente." : $"{done} itens excluídos permanentemente.", "Alguns itens não foram excluídos"));
        }));
        dialog.BackOption = cancel;
        dialog.FocusIndex = 0;
        PushModal(dialog);
    }

    /// <summary>Um item por vez, fora da thread de UI; falhas viram uma lista com o motivo de cada uma. A lista é recarregada no fim.</summary>
    private async Task RunOnBinAsync(PaneState pane, List<(string Id, string Name)> items, Action<string> action, Func<int, string> summary, string failureTitle)
    {
        var failures = new List<(string, string)>();
        var done = 0;
        foreach (var (id, name) in items)
        {
            try
            {
                await Task.Run(() => action(id));
                done++;
            }
            catch (FileOperationException ex)
            {
                failures.Add((name, ex.Message));
            }
        }
        pane.List.ClearSelection();
        if (pane.Location is RecycleBinLocation) await NavigateAsync(pane, RecycleBinLocation.Instance, pushHistory: false, pane.List.FocusedId);
        if (failures.Count > 0) ShowMessage(failureTitle, failures, done > 0 ? summary(done) : null);
        else SetStatus(summary(done));
    }
}
