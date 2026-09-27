using ControlFS.Application.Operations;
using ControlFS.Application.State;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;

namespace ControlFS.Application;

public sealed partial class AppController
{
    private readonly IFileOperationService? _fileOps;
    private readonly Dictionary<int, FileOperationPlan> _fileOperations = [];

    internal sealed record FileOperationPlan(FileOperationKind Kind, IReadOnlyList<string> Sources, string? Destination, bool Permanent, string SourceFolder,
        IReadOnlyList<string>? LeftoverSourceFolders = null);

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

    internal void EnqueueFileOperation(FileOperationPlan plan) => EnqueueFileOperation(plan, [plan], retryOfFailed: false);

    /// <summary>Refazer (#22): o pedido original de novo, sem esvaziar a lista de refazer.</summary>
    private void EnqueueRedo(FileOperationPlan plan) => EnqueueFileOperation(plan, [plan], retryOfFailed: false, redo: true);

    /// <summary>
    /// Enfileira uma operação. <paramref name="parts"/> tem mais de um pedido só ao refazer itens com falha que iam para
    /// pastas diferentes (ex.: subpastas do destino); eles rodam em sequência como uma única operação.
    /// </summary>
    private void EnqueueFileOperation(FileOperationPlan summary, List<FileOperationPlan> parts, bool retryOfFailed, bool redo = false)
    {
        if (_fileOps is null) return;
        var count = parts.Sum(p => p.Sources.Count);
        var title = retryOfFailed
            ? $"{Verb(summary.Kind, summary.Permanent)} {count} item(ns) com falha"
            : $"{Verb(summary.Kind, summary.Permanent)} {count} item(ns)";
        var kind = summary.Kind switch
        {
            FileOperationKind.Copy => OperationKind.Copy,
            FileOperationKind.Move => OperationKind.Move,
            _ => OperationKind.Delete,
        };
        var item = Operations.Enqueue(title, kind, async (op, ct) =>
        {
            var progress = new Progress<OperationProgress>(p => Operations.ReportProgress(op, p));
            var interaction = new UiConflictInteraction(this, op);
            if (parts.Count == 1)
            {
                var single = await _fileOps.RunAsync(ToRequest(parts[0], op.PauseGate), interaction, progress, ct);
                // Desfazer uma cópia só remove o que continuar idêntico: a impressão é tirada logo ao terminar.
                if (summary.Kind == FileOperationKind.Copy && single.FinalState == OperationState.Completed && !retryOfFailed)
                    _copyFingerprints[op.Id] = await Task.Run(() => single.Placed.Select(p => UndoFingerprint.Compute(p.FinalPath)).ToList(), CancellationToken.None);
                return single;
            }
            var results = new List<OperationResult>();
            foreach (var part in parts)
            {
                if (results.LastOrDefault() is { FinalState: OperationState.Cancelled } or { FinalState: OperationState.Failed, Items.Count: > 0 })
                {
                    results.Add(new OperationResult(OperationState.Cancelled, part.Sources.Select(s => NotProcessed(s, part.Destination)).ToList()));
                    continue;
                }
                results.Add(await _fileOps.RunAsync(ToRequest(part, op.PauseGate), interaction, progress, ct));
            }
            return Merge(results, parts);
        }, pausable: true);
        item.RetryAction = () => RetryFileOperation(summary, parts, retryOfFailed);
        if (!retryOfFailed && parts.Count == 1) _undoCandidates[item.Id] = redo;
        item.Source = summary.SourceFolder;
        item.Destination = summary.Destination;
        _fileOperations[item.Id] = summary with { Sources = parts.SelectMany(p => p.Sources).ToList() };
        StatusMessage = $"{title}: iniciado. Você pode continuar navegando.";
        RaiseChanged();
    }

    private static FileOperationRequest ToRequest(FileOperationPlan plan, PauseGate? pause) => new()
    {
        Pause = pause,
        Kind = plan.Kind,
        Sources = plan.Sources,
        DestinationFolder = plan.Destination,
        Permanent = plan.Permanent,
        LeftoverSourceFolders = plan.LeftoverSourceFolders,
    };

    private static ItemResult NotProcessed(string source, string? target) =>
        new(Path.GetFileName(source), ItemOutcome.NotProcessed, Message: "Não processado.") { SourcePath = source, TargetFolder = target };

    /// <summary>Junta os resultados das partes num resultado único e honesto (o pior estado prevalece).</summary>
    private static OperationResult Merge(List<OperationResult> results, List<FileOperationPlan> parts)
    {
        var items = new List<ItemResult>();
        for (var i = 0; i < results.Count; i++)
        {
            var r = results[i];
            // Falha no planejamento (ex.: a origem sumiu): os itens da parte aparecem como falha, com o motivo.
            if (r.Items.Count == 0 && r.FinalState == OperationState.Failed)
                items.AddRange(parts[i].Sources.Select(s => new ItemResult(Path.GetFileName(s), ItemOutcome.Failed, r.Error, r.Message) { SourcePath = s, TargetFolder = parts[i].Destination }));
            else items.AddRange(r.Items);
        }
        var stop = results.FirstOrDefault(r => r.FinalState is OperationState.Cancelled || (r.FinalState is OperationState.Failed && r.Items.Count > 0));
        if (stop is not null) return new OperationResult(stop.FinalState, items, stop.Error, stop.Message);
        var warnings = items.Any(i => i.Outcome is ItemOutcome.Failed or ItemOutcome.Blocked or ItemOutcome.NotProcessed);
        return new OperationResult(warnings ? OperationState.CompletedWithWarnings : OperationState.Completed, items);
    }

    /// <summary>
    /// Tentar de novo: o motor replaneja o pedido original do zero. Itens que já não existem na origem (ex.: movidos ou
    /// excluídos na tentativa anterior) ficam de fora; o que já está no destino passa pelo fluxo normal de conflitos.
    /// </summary>
    private void RetryFileOperation(FileOperationPlan summary, IReadOnlyList<FileOperationPlan> parts, bool retryOfFailed)
    {
        var remaining = parts
            .Select(p => p with { Sources = p.Sources.Where(s => File.Exists(s) || Directory.Exists(s)).ToList() })
            .Where(p => p.Sources.Count > 0)
            .ToList();
        if (remaining.Count == 0)
        {
            ShowMessage("Nada para tentar de novo", [], "Os itens da operação não existem mais na origem.");
            return;
        }
        EnqueueFileOperation(summary, remaining, retryOfFailed);
    }

    /// <summary>
    /// Pedidos para refazer só o que falhou ou não foi processado, a partir do resultado por item. O que já deu certo
    /// nunca é refeito. Uma pasta que só falhou porque algo dentro dela falhou (mover) não é refeita inteira: refazem-se
    /// os itens de dentro, e ela é removida no fim se tiver ficado vazia.
    /// </summary>
    internal static List<FileOperationPlan> FailedItemsPlans(FileOperationPlan plan, OperationResult result)
    {
        var comparison = StringComparison.OrdinalIgnoreCase;
        var failed = result.Items.Where(i => i.NeedsRetry && i.SourcePath is not null).ToList();
        bool Contains(string folder, string path) => path.StartsWith(Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar, comparison);
        var leftovers = failed.Where(f => failed.Any(o => Contains(f.SourcePath!, o.SourcePath!))).Select(f => f.SourcePath!).ToList();
        var parts = failed
            .Where(f => !leftovers.Contains(f.SourcePath!, StringComparer.OrdinalIgnoreCase))
            .GroupBy(f => plan.Kind == FileOperationKind.Delete ? string.Empty : f.TargetFolder ?? plan.Destination ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(g => plan with
            {
                Sources = g.Select(f => f.SourcePath!).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Destination = plan.Kind == FileOperationKind.Delete ? null : g.Key,
                LeftoverSourceFolders = null,
            })
            .ToList();
        if (parts.Count > 0 && plan.Kind == FileOperationKind.Move && leftovers.Count > 0)
            parts[^1] = parts[^1] with { LeftoverSourceFolders = leftovers.OrderByDescending(l => l.Length).ToList() };
        return parts;
    }

    private void OnFileOperationCompleted(OperationItem item, FileOperationPlan plan, OperationResult result)
    {
        var retryParts = FailedItemsPlans(plan, result);
        item.RetryableItemCount = retryParts.Sum(p => p.Sources.Count);
        item.RetryFailedAction = () => RetryFileOperation(plan, retryParts, retryOfFailed: true);

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
        Count("Não processados", ItemOutcome.NotProcessed);
        foreach (var problem in result.Items.Where(i => i.Outcome is ItemOutcome.Failed or ItemOutcome.Blocked).Take(6))
            lines.Add(("• " + problem.Name, problem.Message ?? problem.Error.ToString()));
        foreach (var skipped in result.Items.Where(i => i.Outcome == ItemOutcome.Skipped && i.Error == OperationErrorKind.LinkOrSpecialBlocked).Take(3))
            lines.Add(("• " + skipped.Name, skipped.Message ?? "Ignorado."));

        var dialog = new DialogModal(title, lines) { Message = result.Message };
        var close = new DialogOption("Fechar", DialogOptionKind.Safe, () => CloseModal(dialog));
        AddRetryFailedOption(dialog, item);
        if (RegisterFileUndo(item, plan, result) is { } undo)
        {
            dialog.Options.Add(new DialogOption("Desfazer", DialogOptionKind.Primary, () =>
            {
                CloseModal(dialog);
                ConfirmUndo(undo);
            }));
            if (plan.Kind == FileOperationKind.Delete) title += " (Menu → Desfazer restaura)";
        }
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
        // Todas as abas que mostram a origem ou o destino são atualizadas, não só a ativa.
        var affected = new[] { plan.Destination, plan.SourceFolder }.Where(p => p is not null).ToList();
        foreach (var tab in _tabs.Where(t => t.Location is PhysicalLocation here && affected.Any(p => string.Equals(p, here.FullPath, StringComparison.OrdinalIgnoreCase))).ToList())
            Refresh(tab);
    }

    /// <summary>Gancho para estados que dependem do fim de uma operação (ex.: área de transferência).</summary>
    partial void OnFileOperationFinished(FileOperationPlan plan, OperationResult result);

    /// <summary>"Tentar de novo só as falhas" no resultado de uma operação, quando há itens para refazer.</summary>
    private void AddRetryFailedOption(DialogModal dialog, OperationItem item)
    {
        if (!item.CanRetryFailed) return;
        dialog.Options.Add(new DialogOption($"Tentar de novo só as falhas ({item.RetryableItemCount})", DialogOptionKind.Primary, () =>
        {
            CloseModal(dialog);
            Operations.RetryFailed(item);
        }));
    }
}
