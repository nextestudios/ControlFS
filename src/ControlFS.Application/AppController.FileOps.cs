using ControlFS.Application.Operations;
using ControlFS.Application.State;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;

namespace ControlFS.Application;

public sealed partial class AppController
{
    private readonly IFileOperationService? _fileOps;
    private readonly Dictionary<int, FileOperationPlan> _fileOperations = [];

    internal sealed record FileOperationPlan(FileOperationKind Kind, IReadOnlyList<string> Sources, string? Destination, bool Permanent, string SourceFolder);

    private string? FileOpsUnavailable => _fileOps is null ? "Operações de arquivo indisponíveis nesta compilação." : null;

    private static string Verb(FileOperationKind kind, bool permanent = false) => kind switch
    {
        FileOperationKind.Copy => "Copiar",
        FileOperationKind.Move => "Mover",
        _ => permanent ? "Excluir permanentemente" : "Mover para a Lixeira",
    };

    /// <summary>Copiar/mover para uma pasta escolhida no seletor interno.</summary>
    internal void BeginTransferTo(PaneState pane, IReadOnlyList<FileEntry> entries, FileOperationKind kind)
    {
        if (_fileOps is null || pane.Location is not PhysicalLocation here) return;
        var sources = entries.Where(e => e.FullPath is not null).Select(e => e.FullPath!).ToList();
        if (sources.Count == 0) return;
        OpenFolderPicker(kind == FileOperationKind.Copy ? "Copiar para…" : "Mover para…", here.FullPath,
            folder => ConfirmTransfer(kind, sources, folder, here.FullPath));
    }

    /// <summary>Resumo antes de executar: quantidade, origem e destino.</summary>
    internal void ConfirmTransfer(FileOperationKind kind, IReadOnlyList<string> sources, string destination, string sourceFolder, Action? onStarted = null)
    {
        var names = sources.Select(Path.GetFileName).Take(3).ToList();
        var more = sources.Count > 3 ? $" e mais {sources.Count - 3}" : string.Empty;
        var dialog = new DialogModal($"{Verb(kind)} {sources.Count} item(ns)?",
        [
            ("Itens", string.Join(", ", names) + more),
            ("De", sourceFolder),
            ("Para", destination),
            ("Conflitos", "perguntar a cada conflito (padrão: manter o existente)"),
        ]);
        var cancel = new DialogOption("Cancelar", DialogOptionKind.Safe, () => CloseModal(dialog));
        dialog.Options.Add(new DialogOption(Verb(kind), DialogOptionKind.Primary, () =>
        {
            CloseModal(dialog);
            EnqueueFileOperation(new FileOperationPlan(kind, sources, destination, false, sourceFolder));
            onStarted?.Invoke();
        }));
        dialog.Options.Add(cancel);
        dialog.BackOption = cancel;
        PushModal(dialog);
    }

    internal void EnqueueFileOperation(FileOperationPlan plan)
    {
        if (_fileOps is null) return;
        var request = new FileOperationRequest { Kind = plan.Kind, Sources = plan.Sources, DestinationFolder = plan.Destination, Permanent = plan.Permanent };
        var title = $"{Verb(plan.Kind, plan.Permanent)} {plan.Sources.Count} item(ns)";
        var item = Operations.Enqueue(title, plan.Kind == FileOperationKind.Copy ? OperationKind.Copy : plan.Kind == FileOperationKind.Move ? OperationKind.Move : OperationKind.Delete,
            async (op, ct) =>
            {
                var progress = new Progress<OperationProgress>(p => Operations.ReportProgress(op, p));
                return await _fileOps.RunAsync(request, new UiConflictInteraction(this, op), progress, ct);
            });
        item.RetryAction = () => RetryFileOperation(plan);
        _fileOperations[item.Id] = plan;
        StatusMessage = $"{title}: iniciado. Você pode continuar navegando.";
        RaiseChanged();
    }

    /// <summary>
    /// Tentar de novo: o motor replaneja o pedido original do zero. Itens que já não existem na origem (ex.: movidos ou
    /// excluídos na tentativa anterior) ficam de fora; o que já está no destino passa pelo fluxo normal de conflitos.
    /// </summary>
    private void RetryFileOperation(FileOperationPlan plan)
    {
        var remaining = plan.Sources.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (remaining.Count == 0)
        {
            ShowMessage("Nada para tentar de novo", [], "Os itens da operação não existem mais na origem.");
            return;
        }
        EnqueueFileOperation(plan with { Sources = remaining });
    }

    private void OnFileOperationCompleted(FileOperationPlan plan, OperationResult result)
    {
        var verb = Verb(plan.Kind, plan.Permanent);
        var title = result.FinalState switch
        {
            OperationState.Completed => $"{verb}: concluído",
            OperationState.CompletedWithWarnings => $"{verb}: concluído com avisos",
            OperationState.Cancelled => $"{verb}: cancelado",
            _ => $"{verb}: falhou",
        };
        var lines = new List<(string, string)>();
        if (plan.Destination is { } destination) lines.Add(("Destino", destination));
        void Count(string label, ItemOutcome outcome)
        {
            var n = result.Count(outcome);
            if (n > 0) lines.Add((label, n.ToString()));
        }
        Count("Concluídos", ItemOutcome.Succeeded);
        Count("Mantidos ambos (renomeados)", ItemOutcome.Renamed);
        Count("Substituídos", ItemOutcome.Replaced);
        Count("Ignorados", ItemOutcome.Skipped);
        Count("Bloqueados", ItemOutcome.Blocked);
        Count("Falhas", ItemOutcome.Failed);
        foreach (var problem in result.Items.Where(i => i.Outcome is ItemOutcome.Failed or ItemOutcome.Blocked).Take(6))
            lines.Add(("• " + problem.Name, problem.Message ?? problem.Error.ToString()));
        foreach (var skipped in result.Items.Where(i => i.Outcome == ItemOutcome.Skipped && i.Error == OperationErrorKind.LinkOrSpecialBlocked).Take(3))
            lines.Add(("• " + skipped.Name, skipped.Message ?? "Ignorado."));

        var dialog = new DialogModal(title, lines) { Message = result.Message };
        var close = new DialogOption("Fechar", DialogOptionKind.Safe, () => CloseModal(dialog));
        if (plan.Destination is { } dest && Directory.Exists(dest) && plan.Kind != FileOperationKind.Delete &&
            !(Browser.Location is PhysicalLocation current && string.Equals(current.FullPath, dest, StringComparison.OrdinalIgnoreCase)))
        {
            dialog.Options.Add(new DialogOption("Abrir destino", DialogOptionKind.Primary, () =>
            {
                CloseModal(dialog);
                Screen = Screen.Browser;
                Track(NavigateAsync(Browser, new PhysicalLocation(dest), pushHistory: Browser.Location is not null));
            }));
        }
        dialog.Options.Add(close);
        dialog.BackOption = close;
        OnFileOperationFinished(plan, result);
        // Operações totalmente bem-sucedidas não interrompem com diálogo: um aviso no rodapé basta.
        if (result.FinalState == OperationState.Completed && plan.Kind == FileOperationKind.Delete) StatusMessage = title;
        else PushModal(dialog);
        if (Browser.Location is PhysicalLocation here)
        {
            var affected = new[] { plan.Destination, plan.SourceFolder }.Where(p => p is not null);
            if (affected.Any(p => string.Equals(p, here.FullPath, StringComparison.OrdinalIgnoreCase))) Refresh(Browser);
        }
    }

    /// <summary>Gancho para estados que dependem do fim de uma operação (ex.: área de transferência).</summary>
    partial void OnFileOperationFinished(FileOperationPlan plan, OperationResult result);
}
