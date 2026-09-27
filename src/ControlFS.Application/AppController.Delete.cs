using ControlFS.Application.State;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Text;

namespace ControlFS.Application;

public sealed partial class AppController
{
    /// <summary>
    /// Excluir: Lixeira do Windows quando o local permite; exclusão permanente só com confirmação específica e nunca
    /// como fallback silencioso. Todas as confirmações começam em "Cancelar".
    /// </summary>
    internal void BeginDelete(PaneState pane, IReadOnlyList<FileEntry> entries)
    {
        if (_fileOps is null || pane.Location is not PhysicalLocation here) return;
        var sources = entries.Where(e => e.FullPath is not null && e.Kind is EntryKind.File or EntryKind.Directory).Select(e => e.FullPath!).ToList();
        if (sources.Count == 0) return;
        var names = sources.Select(Path.GetFileName).Take(3).ToList();
        var itemsText = string.Join(", ", names) + (sources.Count > 3 ? $" e mais {sources.Count - 3}" : string.Empty);
        var recyclable = sources.All(_fileOps.CanRecycle);

        if (!recyclable)
        {
            ConfirmPermanentDelete(sources, itemsText, here.FullPath,
                "Este local não tem Lixeira (ex.: pasta de rede ou unidade removível): os itens serão excluídos permanentemente e não há como desfazer.");
            return;
        }
        var dialog = new DialogModal($"Mover {Plural.Of(sources.Count, "item", "itens")} para a Lixeira?", [("Itens", itemsText), ("Pasta", here.FullPath)], sensitive: true)
        {
            Message = "Dá para restaurar depois em Início → Lixeira.",
        };
        var cancel = new DialogOption("Cancelar", DialogOptionKind.Safe, () => CloseModal(dialog));
        dialog.Options.Add(cancel);
        dialog.Options.Add(new DialogOption("Mover para a Lixeira", DialogOptionKind.Primary, () =>
        {
            CloseModal(dialog);
            EnqueueFileOperation(new FileOperationPlan(FileOperationKind.Delete, sources, null, false, here.FullPath));
        }));
        dialog.Options.Add(new DialogOption("Excluir permanentemente…", DialogOptionKind.Danger, () =>
        {
            CloseModal(dialog);
            ConfirmPermanentDelete(sources, itemsText, here.FullPath, "Os itens não irão para a Lixeira e não há como desfazer.");
        }));
        dialog.BackOption = cancel;
        dialog.FocusIndex = 0;
        PushModal(dialog);
    }

    private void ConfirmPermanentDelete(List<string> sources, string itemsText, string folder, string message)
    {
        var dialog = new DialogModal($"Excluir {Plural.Of(sources.Count, "item", "itens")} permanentemente?", [("Itens", itemsText), ("Pasta", folder)], sensitive: true)
        {
            Message = message,
        };
        var cancel = new DialogOption("Cancelar", DialogOptionKind.Safe, () => CloseModal(dialog));
        dialog.Options.Add(cancel);
        dialog.Options.Add(new DialogOption("Excluir permanentemente", DialogOptionKind.Danger, () =>
        {
            CloseModal(dialog);
            EnqueueFileOperation(new FileOperationPlan(FileOperationKind.Delete, sources, null, true, folder));
        }));
        dialog.BackOption = cancel;
        dialog.FocusIndex = 0;
        PushModal(dialog);
    }
}
