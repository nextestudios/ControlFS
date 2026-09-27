using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Text;

namespace ControlFS.Application;

/// <summary>Área de transferência interna do ControlFS (independente da do Windows nesta versão).</summary>
public sealed record FileClipboard(FileOperationKind Kind, IReadOnlyList<string> Paths, string SourceFolder)
{
    public bool IsCut => Kind == FileOperationKind.Move;
}

public sealed partial class AppController
{
    public FileClipboard? Clipboard { get; private set; }

    /// <summary>Itens recortados aparecem esmaecidos até serem colados.</summary>
    public bool IsCut(FileEntry entry) =>
        Clipboard is { IsCut: true } clip && entry.FullPath is { } path && clip.Paths.Contains(path, StringComparer.OrdinalIgnoreCase);

    internal void PutOnClipboard(PaneState pane, IReadOnlyList<FileEntry> entries, FileOperationKind kind)
    {
        if (pane.Location is not PhysicalLocation here) return;
        var paths = entries.Where(e => e.FullPath is not null && e.Kind is EntryKind.File or EntryKind.Directory).Select(e => e.FullPath!).ToList();
        if (paths.Count == 0) return;
        Clipboard = new FileClipboard(kind, paths, here.FullPath);
        pane.List.ClearSelection();
        StatusMessage = kind == FileOperationKind.Move
            ? $"{Plural.Of(paths.Count, "item", "itens")} {Plural.Word(paths.Count, "recortado", "recortados")}. Vá até o destino e use Colar (menu de ações)."
            : $"{Plural.Of(paths.Count, "item", "itens")} {Plural.Word(paths.Count, "copiado", "copiados")}. Vá até o destino e use Colar (menu de ações).";
    }

    private string? PasteUnavailable(PaneState pane) =>
        FileOpsUnavailable ?? (Clipboard is null ? "Nada para colar: use Copiar ou Recortar primeiro." :
            pane.Location is not PhysicalLocation ? "Abra uma pasta do disco para colar." : null);

    private string PasteLabel => Clipboard is { } c ? $"Colar {Plural.Of(c.Paths.Count, "item", "itens")}{(c.IsCut ? " (mover)" : string.Empty)}" : "Colar";

    internal void Paste(PaneState pane)
    {
        if (Clipboard is not { } clip || pane.Location is not PhysicalLocation here) return;
        var existing = clip.Paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (existing.Count == 0)
        {
            Clipboard = null;
            ShowMessage("Nada para colar", [], "Os itens copiados ou recortados não existem mais.", icon: ActionIcon.Warning);
            return;
        }
        ConfirmTransfer(clip.Kind, existing, here.FullPath, clip.SourceFolder);
    }

    internal void ClearClipboard() => Clipboard = null;

    partial void OnFileOperationFinished(FileOperationPlan plan, OperationResult result)
    {
        // Recortar + colar: após mover, a área de transferência é esvaziada (os itens não estão mais na origem).
        if (plan.Kind == FileOperationKind.Move && Clipboard is { IsCut: true } clip &&
            plan.Sources.All(s => clip.Paths.Contains(s, StringComparer.OrdinalIgnoreCase)) &&
            result.FinalState is OperationState.Completed or OperationState.CompletedWithWarnings)
            Clipboard = null;
    }
}
