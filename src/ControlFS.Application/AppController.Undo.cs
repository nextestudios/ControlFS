using ControlFS.Application.Operations;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Text;

namespace ControlFS.Application;

/// <summary>
/// Desfazer e refazer (#22), só para o que tem inverso documentado e testado: renomear de volta, mover de volta,
/// remover uma cópia que continua idêntica (com o original ainda no lugar) e restaurar da Lixeira. Antes de mexer em
/// qualquer coisa, tudo é conferido; se o disco mudou desde a operação, nada é feito e o motivo é mostrado. Exclusão
/// permanente, substituições e mesclagens nunca entram. Vale nesta sessão.
/// </summary>
public sealed partial class AppController
{
    private const int MaxUndo = 50;
    private readonly List<UndoRecord> _undo = [];
    private readonly List<UndoRecord> _redo = [];
    private readonly Dictionary<int, bool> _undoCandidates = [];
    private readonly Dictionary<int, List<string?>> _copyFingerprints = [];
    private readonly Dictionary<int, UndoRecord> _undoRuns = [];

    internal enum UndoStepKind
    {
        /// <summary>Renomear <see cref="UndoStep.Current"/> de volta para o nome de <see cref="UndoStep.Original"/>.</summary>
        RenameBack,
        /// <summary>Mover <see cref="UndoStep.Current"/> de volta para <see cref="UndoStep.Original"/>.</summary>
        MoveBack,
        /// <summary>Remover a cópia <see cref="UndoStep.Current"/> se continuar idêntica e o original existir.</summary>
        DeleteCopy,
        /// <summary>Restaurar da Lixeira o item excluído de <see cref="UndoStep.Original"/>.</summary>
        Restore,
    }

    internal sealed record UndoStep(UndoStepKind Kind, string Original, string? Current, string? Fingerprint = null);

    /// <summary>Uma operação que pode ser desfeita; <see cref="Redo"/> repete o pedido original depois de desfeita.</summary>
    internal sealed record UndoRecord(string Title, OperationKind Kind, IReadOnlyList<UndoStep> Steps, DateTimeOffset Started, DateTimeOffset Finished, Action Redo);

    public string? UndoTitle => _undo.Count > 0 ? _undo[^1].Title : null;
    public string? RedoTitle => _redo.Count > 0 ? _redo[^1].Title : null;

    private void PushUndo(UndoRecord record, bool redo)
    {
        _undo.Add(record);
        if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
        if (!redo) _redo.Clear(); // uma operação nova encerra o que havia para refazer
    }

    private void RegisterRenameUndo(string oldPath, string newPath, bool redo)
    {
        var now = DateTimeOffset.Now;
        PushUndo(new UndoRecord($"Renomear \"{Path.GetFileName(oldPath)}\" para \"{Path.GetFileName(newPath)}\"", OperationKind.Rename,
            [new UndoStep(UndoStepKind.RenameBack, oldPath, newPath)], now, now, () => RedoRename(oldPath, newPath)), redo);
    }

    /// <summary>Registra o inverso de uma cópia, movimentação ou envio à Lixeira concluídos sem avisos. Null: não há inverso seguro.</summary>
    private UndoRecord? RegisterFileUndo(OperationItem item, FileOperationPlan plan, OperationResult result)
    {
        var fingerprints = _copyFingerprints.Remove(item.Id, out var f) ? f : null;
        if (!_undoCandidates.Remove(item.Id, out var redo) || result.FinalState != OperationState.Completed) return null;
        if (result.Items.Any(i => i.Outcome == ItemOutcome.Replaced)) return null; // sobrescrever não se desfaz
        List<UndoStep> steps;
        switch (plan.Kind)
        {
            case FileOperationKind.Move or FileOperationKind.Copy:
                if (result.Placed.Count != plan.Sources.Count) return null; // algo pulado, mesclado ou já no lugar
                if (plan.Kind == FileOperationKind.Copy && (fingerprints is null || fingerprints.Count != result.Placed.Count || fingerprints.Any(p => p is null)))
                    return null;
                steps = [.. result.Placed.Select((p, i) => plan.Kind == FileOperationKind.Move
                    ? new UndoStep(UndoStepKind.MoveBack, p.SourcePath, p.FinalPath)
                    : new UndoStep(UndoStepKind.DeleteCopy, p.SourcePath, p.FinalPath, fingerprints![i]))];
                break;
            case FileOperationKind.Delete when !plan.Permanent && _recycleBin is not null:
                if (result.Items.Any(i => i.Outcome != ItemOutcome.Succeeded || i.SourcePath is null)) return null;
                steps = [.. result.Items.Select(i => new UndoStep(UndoStepKind.Restore, i.SourcePath!, null))];
                break;
            default:
                return null;
        }
        var originals = steps.Select(s => s.Original).ToList();
        var redoPlan = plan with { Sources = originals, LeftoverSourceFolders = null };
        var record = new UndoRecord(item.Title, item.Kind, steps, item.StartedAt ?? DateTimeOffset.Now, DateTimeOffset.Now, () => EnqueueRedo(redoPlan));
        PushUndo(record, redo);
        return record;
    }

    // ---------- Menu ----------

    private IEnumerable<MenuItem> UndoMenuItems()
    {
        yield return new MenuItem(UndoTitle is { } undo ? $"Desfazer: {undo}" : "Desfazer", () => ConfirmUndo(_undo[^1]),
            _undo.Count == 0 ? "Nada para desfazer nesta sessão." : null,
            Detail: "Renomear, mover, copiar e mandar para a Lixeira. Exclusão permanente e substituições não se desfazem.", Icon: ActionIcon.Undo, Section: "Operações");
        yield return new MenuItem(RedoTitle is { } redo ? $"Refazer: {redo}" : "Refazer", ConfirmRedo, _redo.Count == 0 ? "Nada para refazer." : null, Icon: ActionIcon.Redo, Section: "Operações");
    }

    private static string Describe(UndoRecord record)
    {
        var n = record.Steps.Count;
        return record.Steps[0].Kind switch
        {
            UndoStepKind.RenameBack => $"Volta o nome para \"{Path.GetFileName(record.Steps[0].Original)}\".",
            UndoStepKind.MoveBack => $"Move {Plural.Of(n, "item", "itens")} de volta para {Path.GetDirectoryName(record.Steps[0].Original)}.",
            UndoStepKind.DeleteCopy => $"Remove {Plural.Of(n, "cópia criada", "cópias criadas")} pela operação, só se continuarem idênticas e o original ainda existir.",
            _ => $"Restaura {Plural.Of(n, "item", "itens")} da Lixeira para o local original.",
        };
    }

    internal void ConfirmUndo(UndoRecord record)
    {
        if (!_undo.Contains(record))
        {
            SetStatus("Essa operação já não está na lista de desfazer.");
            return;
        }
        var dialog = new DialogModal($"Desfazer \"{record.Title}\"?", [("O que acontece", Describe(record))], sensitive: true)
        {
            Message = "Antes de mudar qualquer coisa, o ControlFS confere se os itens continuam como a operação deixou; se não, nada é feito.",
            Icon = ActionIcon.Undo,
        };
        var cancel = new DialogOption("Cancelar", DialogOptionKind.Safe, () => CloseModal(dialog), icon: ActionIcon.Cancel);
        dialog.Options.Add(cancel);
        dialog.Options.Add(new DialogOption("Desfazer", DialogOptionKind.Primary, () =>
        {
            CloseModal(dialog);
            StartUndo(record);
        }, icon: ActionIcon.Undo));
        dialog.BackOption = cancel;
        dialog.FocusIndex = 0;
        PushModal(dialog);
    }

    private void ConfirmRedo()
    {
        if (_redo.Count == 0) return;
        var record = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        record.Redo();
    }

    // ---------- Execução ----------

    private void StartUndo(UndoRecord record)
    {
        if (_fileOps is null || !_undo.Remove(record)) return;
        var kind = record.Steps[0].Kind switch
        {
            UndoStepKind.RenameBack => OperationKind.Rename,
            UndoStepKind.MoveBack => OperationKind.Move,
            UndoStepKind.DeleteCopy => OperationKind.Delete,
            _ => OperationKind.Move,
        };
        var item = Operations.Enqueue($"Desfazer: {record.Title}", kind, (op, ct) => RunUndoAsync(record, ct));
        item.Source = record.Steps[0].Current ?? record.Steps[0].Original;
        item.Destination = Path.GetDirectoryName(record.Steps[0].Original);
        _undoRuns[item.Id] = record;
        StatusMessage = $"Desfazendo \"{record.Title}\"…";
        RaiseChanged();
    }

    private async Task<OperationResult> RunUndoAsync(UndoRecord record, CancellationToken ct)
    {
        var ops = _fileOps!;
        // 1) Conferir tudo antes de tocar no disco.
        var (problems, recycled) = await Task.Run(() => Check(record, ct), ct);
        if (problems.Count > 0)
            return new OperationResult(OperationState.Failed,
                [.. problems.Select(p => new ItemResult(p.Name, ItemOutcome.Blocked, OperationErrorKind.AlreadyExists, p.Reason))],
                OperationErrorKind.AlreadyExists, "Nada foi desfeito: os itens mudaram desde a operação.");

        // 2) Aplicar o inverso, item por item.
        var results = new List<ItemResult>();
        foreach (var step in record.Steps)
        {
            var name = Path.GetFileName(step.Original);
            if (ct.IsCancellationRequested)
            {
                results.Add(new ItemResult(name, ItemOutcome.NotProcessed, Message: "Não processado."));
                continue;
            }
            try
            {
                results.Add(await Task.Run(async () => await ApplyAsync(ops, step, recycled, ct), CancellationToken.None));
            }
            catch (FileOperationException ex)
            {
                results.Add(new ItemResult(name, ItemOutcome.Failed, ex.Kind, ex.Message));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                results.Add(new ItemResult(name, ItemOutcome.Failed, OperationErrorKind.Unknown, ex.Message));
            }
        }
        var ok = results.All(r => r.Outcome == ItemOutcome.Succeeded);
        return new OperationResult(ok ? OperationState.Completed : ct.IsCancellationRequested ? OperationState.Cancelled : OperationState.CompletedWithWarnings, results);
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path) || new FileInfo(path).LinkTarget is not null;

    /// <summary>Confere cada passo contra o disco agora. Devolve os motivos de recusa e, para a Lixeira, qual item restaurar.</summary>
    private (List<(string Name, string Reason)> Problems, Dictionary<string, string> Recycled) Check(UndoRecord record, CancellationToken ct)
    {
        var problems = new List<(string, string)>();
        var recycled = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<RecycledItem>? bin = null;
        foreach (var step in record.Steps)
        {
            var name = Path.GetFileName(step.Original);
            var parent = Path.GetDirectoryName(step.Original) ?? string.Empty;
            string? reason = step.Kind switch
            {
                UndoStepKind.RenameBack or UndoStepKind.MoveBack when !Exists(step.Current!) => $"\"{Path.GetFileName(step.Current)}\" não está mais em {Path.GetDirectoryName(step.Current)}.",
                UndoStepKind.RenameBack or UndoStepKind.MoveBack or UndoStepKind.Restore when Exists(step.Original) => $"Já existe um item \"{name}\" em {parent}; nada é sobrescrito.",
                UndoStepKind.MoveBack or UndoStepKind.Restore when !Directory.Exists(parent) => $"A pasta de origem {parent} não existe mais.",
                UndoStepKind.MoveBack when !string.Equals(Path.GetFileName(step.Current), name, StringComparison.Ordinal) && Exists(Path.Join(parent, Path.GetFileName(step.Current))) =>
                    $"Já existe \"{Path.GetFileName(step.Current)}\" em {parent}.",
                UndoStepKind.DeleteCopy when !Exists(step.Current!) => $"A cópia \"{Path.GetFileName(step.Current)}\" não existe mais.",
                UndoStepKind.DeleteCopy when !Exists(step.Original) => "O original não existe mais: remover a cópia apagaria o único exemplar.",
                UndoStepKind.DeleteCopy when UndoFingerprint.Compute(step.Current!) != step.Fingerprint => $"A cópia \"{Path.GetFileName(step.Current)}\" foi alterada depois da operação.",
                _ => null,
            };
            if (reason is null && step.Kind == UndoStepKind.Restore)
            {
                bin ??= _recycleBin?.List(ct) ?? [];
                // O item que esta operação mandou para a Lixeira: mesmo local original, excluído durante a operação.
                var match = bin
                    .Where(r => r.Problem is null && string.Equals(r.OriginalPath, step.Original, StringComparison.OrdinalIgnoreCase) &&
                        r.DeletedAt is { } at && at >= record.Started.AddMinutes(-1) && at <= record.Finished.AddMinutes(1))
                    .OrderByDescending(r => r.DeletedAt)
                    .FirstOrDefault();
                if (match is null) reason = $"\"{name}\" não está mais na Lixeira.";
                else recycled[step.Original] = match.Id;
            }
            if (reason is not null) problems.Add((name, reason));
        }
        return (problems, recycled);
    }

    private async Task<ItemResult> ApplyAsync(IFileOperationService ops, UndoStep step, Dictionary<string, string> recycled, CancellationToken ct)
    {
        var name = Path.GetFileName(step.Original);
        var parent = Path.GetDirectoryName(step.Original)!;
        switch (step.Kind)
        {
            case UndoStepKind.RenameBack:
                ops.Rename(step.Current!, name);
                break;
            case UndoStepKind.MoveBack:
            {
                var moved = await ops.RunAsync(new FileOperationRequest { Kind = FileOperationKind.Move, Sources = [step.Current!], DestinationFolder = parent },
                    NeverOverwrite.Instance, null, ct);
                if (moved.FinalState != OperationState.Completed || moved.Placed.Count != 1)
                    return new ItemResult(name, ItemOutcome.Failed, moved.Error, moved.Message ?? (moved.Items.Count > 0 ? moved.Items[0].Message : null) ?? "Não foi possível mover de volta.");
                if (!string.Equals(moved.Placed[0].FinalPath, step.Original, StringComparison.Ordinal)) ops.Rename(moved.Placed[0].FinalPath, name);
                break;
            }
            case UndoStepKind.DeleteCopy:
            {
                // Confere de novo logo antes: a cópia continua idêntica e o original continua lá.
                if (!Exists(step.Original) || UndoFingerprint.Compute(step.Current!) != step.Fingerprint)
                    return new ItemResult(Path.GetFileName(step.Current!), ItemOutcome.Blocked, OperationErrorKind.AlreadyExists, "A cópia ou o original mudou; nada foi removido.");
                var deleted = await ops.RunAsync(new FileOperationRequest { Kind = FileOperationKind.Delete, Sources = [step.Current!], Permanent = !ops.CanRecycle(step.Current!) },
                    NeverOverwrite.Instance, null, ct);
                if (deleted.FinalState != OperationState.Completed)
                    return new ItemResult(Path.GetFileName(step.Current!), ItemOutcome.Failed, deleted.Error, (deleted.Items.Count > 0 ? deleted.Items[0].Message : null) ?? deleted.Message);
                return new ItemResult(Path.GetFileName(step.Current!), ItemOutcome.Succeeded, Message: "Cópia removida.") { SourcePath = step.Current };
            }
            case UndoStepKind.Restore:
                _recycleBin!.Restore(recycled[step.Original]);
                break;
        }
        return new ItemResult(name, ItemOutcome.Succeeded, FinalPath: step.Original) { SourcePath = step.Current };
    }

    /// <summary>Desfazer nunca sobrescreve: qualquer conflito que surja no meio vira "pular".</summary>
    private sealed class NeverOverwrite : IConflictInteraction
    {
        public static readonly NeverOverwrite Instance = new();

        public Task<ConflictDecision> ResolveConflictAsync(ConflictInfo conflict, CancellationToken cancellationToken) =>
            Task.FromResult(new ConflictDecision(ConflictChoice.Skip, ApplyToRemaining: true));
    }

    private void OnUndoCompleted(OperationItem item, UndoRecord record)
    {
        var result = item.Result!;
        if (result.FinalState == OperationState.Completed)
        {
            _redo.Add(record);
            SetStatus($"Desfeito: {record.Title}. Menu → Refazer para repetir.");
        }
        else
        {
            var lines = result.Items.Where(i => i.Outcome != ItemOutcome.Succeeded).Take(6)
                .Select(i => ("• " + i.Name, i.Message ?? i.Error.ToString())).ToList();
            var done = result.Count(ItemOutcome.Succeeded);
            if (done > 0) lines.Insert(0, ("Desfeitos", done.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            ShowMessage(result.FinalState == OperationState.Failed ? "Não foi possível desfazer" : "Desfeito em parte", lines,
                (result.Message ?? "Alguns itens não voltaram.") + " A operação saiu da lista de desfazer.", icon: result.FinalState == OperationState.Failed ? ActionIcon.Error : ActionIcon.Warning);
        }
        RefreshAfterUndo(record);
    }

    private void RefreshAfterUndo(UndoRecord record)
    {
        if (Browser.Location is not PhysicalLocation here) return;
        var affected = record.Steps.SelectMany(s => new[] { Path.GetDirectoryName(s.Original), s.Current is null ? null : Path.GetDirectoryName(s.Current) });
        if (affected.Any(p => string.Equals(p, here.FullPath, StringComparison.OrdinalIgnoreCase))) Refresh(Browser);
    }

    // ---------- Refazer renomear ----------

    private void RedoRename(string oldPath, string newPath)
    {
        if (_fileOps is null) return;
        Track(RedoRenameAsync(oldPath, newPath));
    }

    private async Task RedoRenameAsync(string oldPath, string newPath)
    {
        try
        {
            if (Exists(newPath)) throw new FileOperationException(OperationErrorKind.AlreadyExists, $"Já existe \"{Path.GetFileName(newPath)}\"; nada é sobrescrito.");
            var renamed = await Task.Run(() => _fileOps!.Rename(oldPath, Path.GetFileName(newPath)));
            RecordRename(oldPath, renamed);
            RegisterRenameUndo(oldPath, renamed.FullPath ?? newPath, redo: true);
            SetStatus($"Refeito: renomeado para \"{renamed.Name}\".");
            if (Browser.Location is PhysicalLocation here && string.Equals(here.FullPath, Path.GetDirectoryName(oldPath), StringComparison.OrdinalIgnoreCase)) Refresh(Browser);
        }
        catch (FileOperationException ex)
        {
            ShowMessage("Não foi possível refazer", [("Item", Path.GetFileName(oldPath))], ex.Message, icon: ActionIcon.Error);
        }
    }
}
