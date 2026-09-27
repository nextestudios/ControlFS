using ControlFS.Application.Operations;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Text;

namespace ControlFS.Application;

/// <summary>
/// Renomear em lote (#71): itens marcados → Norte → "Renomear em lote…". O diálogo mostra a prévia (nome atual → nome
/// novo) calculada pelo mesmo plano que será aplicado; problemas bloqueiam o lote inteiro antes de tocar no disco. A
/// aplicação passa pela fila de operações, com resultado por item, e entra no Desfazer (#22).
/// </summary>
public sealed partial class AppController
{
    /// <summary>Linhas da prévia no diálogo (problemas primeiro); o resto aparece como "e mais N".</summary>
    internal const int BatchRenamePreviewLines = 12;

    private readonly Dictionary<int, BatchRenameRun> _batchRenames = [];

    private sealed class BatchRenameSession(PaneState pane, string folder, IReadOnlyList<BatchRenameSource> sources, IReadOnlyList<string> existing, DialogModal dialog)
    {
        public PaneState Pane { get; } = pane;
        public string Folder { get; } = folder;
        public IReadOnlyList<BatchRenameSource> Sources { get; } = sources;
        public IReadOnlyList<string> Existing { get; } = existing;
        public DialogModal Dialog { get; } = dialog;
        public BatchRenameOptions Options { get; set; } = new();
        public BatchRenamePlan Plan { get; set; } = new([]);
    }

    /// <summary>Um lote enfileirado: pares (caminho atual → nome novo) e a pasta, para o resultado e o Desfazer.</summary>
    internal sealed record BatchRenameRun(string Folder, IReadOnlyList<(string Path, string NewName)> Pairs, bool Redo);

    private static string ModeLabel(BatchRenameMode mode) => mode switch
    {
        BatchRenameMode.Numbering => "Numeração",
        BatchRenameMode.FindReplace => "Localizar e substituir",
        BatchRenameMode.PrefixSuffix => "Prefixo e sufixo",
        _ => "Maiúsculas e minúsculas",
    };

    private static string CaseLabel(BatchRenameCase value) => value switch
    {
        BatchRenameCase.Upper => "MAIÚSCULAS",
        BatchRenameCase.Title => "Iniciais Maiúsculas",
        _ => "minúsculas",
    };

    internal void BeginBatchRename(PaneState pane, IReadOnlyList<FileEntry> entries)
    {
        if (_fileOps is null || pane.Location is not PhysicalLocation here) return;
        var sources = entries.Where(e => e.FullPath is not null && e.Kind is EntryKind.File or EntryKind.Directory)
            .Select(e => new BatchRenameSource(e.FullPath!, e.Kind == EntryKind.File)).ToList();
        if (sources.Count == 0) return;
        Track(OpenBatchRenameAsync(pane, here.FullPath, sources));
    }

    private async Task OpenBatchRenameAsync(PaneState pane, string folder, List<BatchRenameSource> sources)
    {
        IReadOnlyList<string> existing;
        try
        {
            // Todos os nomes da pasta, inclusive ocultos: um conflito com um item oculto também bloqueia.
            var listing = await _fs.ListAsync(folder, includeHidden: true, CancellationToken.None);
            existing = [.. listing.Entries.Select(e => e.Name)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileOperationException)
        {
            ShowMessage("Não foi possível renomear em lote", [("Pasta", folder)], ex.Message, icon: ActionIcon.Error);
            return;
        }
        var dialog = new DialogModal($"Renomear {Plural.Of(sources.Count, "item", "itens")}", []) { Icon = ActionIcon.Rename };
        var session = new BatchRenameSession(pane, folder, sources, existing, dialog)
        {
            Options = new BatchRenameOptions { BaseName = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)) },
        };
        RebuildBatchRename(session);
        dialog.FocusIndex = 0;
        PushModal(dialog);
    }

    /// <summary>Recalcula o plano e remonta prévia e opções do diálogo, mantendo o foco na mesma posição.</summary>
    private void RebuildBatchRename(BatchRenameSession session)
    {
        var options = session.Options;
        var plan = BatchRenamePlan.Create(session.Sources, session.Existing, options);
        session.Plan = plan;
        var dialog = session.Dialog;

        var lines = new List<(string, string)> { ("Modo", ModeLabel(options.Mode)) };
        var parts = new List<string> { $"{plan.RenameCount} {Plural.Word(plan.RenameCount, "muda", "mudam")} de nome" };
        var unchanged = plan.Items.Count(i => i.IsUnchanged);
        if (unchanged > 0) parts.Add($"{unchanged} sem mudança");
        if (plan.ProblemCount > 0) parts.Add($"{plan.ProblemCount} com problema");
        lines.Add(("Resultado", string.Join(" · ", parts)));
        var shown = plan.Items.Where(i => i.Problem is not null).Take(BatchRenamePreviewLines).ToList();
        shown.AddRange(plan.Items.Where(i => i.Problem is null).Take(BatchRenamePreviewLines - shown.Count));
        // Na ordem da lista (a numeração fica legível); problemas sempre entram na parte visível.
        foreach (var item in plan.Items.Where(shown.Contains))
            lines.Add((item.OldName, item.Problem is { } problem ? $"⚠ {item.NewName}: {problem}" : item.IsUnchanged ? "(sem mudança)" : "→ " + item.NewName));
        if (plan.Items.Count > shown.Count) lines.Add(("…", $"e mais {Plural.Of(plan.Items.Count - shown.Count, "item", "itens")}"));
        dialog.Lines = lines;
        dialog.Message = plan.BlockedReason;

        var focus = dialog.FocusIndex;
        dialog.Options.Clear();
        void Change(Func<BatchRenameOptions, BatchRenameOptions> change)
        {
            session.Options = change(session.Options);
            RebuildBatchRename(session);
            RaiseChanged();
        }
        dialog.Options.Add(new DialogOption($"Modo: {ModeLabel(options.Mode)}", DialogOptionKind.Primary,
            () => Change(o => o with { Mode = (BatchRenameMode)(((int)o.Mode + 1) % 4) }), icon: ActionIcon.Settings));
        switch (options.Mode)
        {
            case BatchRenameMode.Numbering:
                dialog.Options.Add(TextOption(session, "Nome base", options.BaseName, (o, v) => o with { BaseName = v }));
                dialog.Options.Add(new DialogOption($"Começar em: {options.Start}", DialogOptionKind.Primary, () => EditBatchNumber(session), icon: ActionIcon.Keyboard));
                dialog.Options.Add(new DialogOption($"Dígitos: {options.Digits}", DialogOptionKind.Primary,
                    () => Change(o => o with { Digits = o.Digits % 6 + 1 }), icon: ActionIcon.Settings));
                break;
            case BatchRenameMode.FindReplace:
                dialog.Options.Add(TextOption(session, "Localizar", options.Find, (o, v) => o with { Find = v }));
                dialog.Options.Add(TextOption(session, "Substituir por", options.Replace, (o, v) => o with { Replace = v }));
                dialog.Options.Add(new DialogOption("Diferenciar maiúsculas e minúsculas", DialogOptionKind.Toggle,
                    () => Change(o => o with { MatchCase = !o.MatchCase })) { IsChecked = options.MatchCase });
                break;
            case BatchRenameMode.PrefixSuffix:
                dialog.Options.Add(TextOption(session, "Prefixo", options.Prefix, (o, v) => o with { Prefix = v }));
                dialog.Options.Add(TextOption(session, "Sufixo", options.Suffix, (o, v) => o with { Suffix = v }));
                break;
            default:
                dialog.Options.Add(new DialogOption($"Converter para: {CaseLabel(options.Case)}", DialogOptionKind.Primary,
                    () => Change(o => o with { Case = (BatchRenameCase)(((int)o.Case + 1) % 3) }), icon: ActionIcon.Settings));
                break;
        }
        var apply = new DialogOption(plan.BlockedReason is null ? $"Renomear {Plural.Of(plan.RenameCount, "item", "itens")}" : "Renomear (bloqueado)",
            DialogOptionKind.Primary, () => ApplyBatchRename(session), icon: ActionIcon.Rename);
        dialog.Options.Add(apply);
        var cancel = new DialogOption("Cancelar", DialogOptionKind.Safe, () => CloseModal(dialog), icon: ActionIcon.Cancel);
        dialog.Options.Add(cancel);
        dialog.BackOption = cancel;
        dialog.StartOption = apply;
        dialog.FocusIndex = Math.Clamp(focus, 0, dialog.Options.Count - 1);
    }

    /// <summary>Campo de texto do padrão: abre o teclado virtual; vazio é permitido (ex.: sem prefixo).</summary>
    private DialogOption TextOption(BatchRenameSession session, string label, string value, Func<BatchRenameOptions, string, BatchRenameOptions> set) =>
        new($"{label}: {(value.Length == 0 ? "(vazio)" : $"\"{value}\"")}", DialogOptionKind.Primary, () =>
        {
            var keyboard = new VirtualKeyboard(TextFieldKind.Generic, label, value, validator: ValidatePatternText, maxLength: Core.Policies.WindowsNameRules.MaxComponentLength);
            KeyboardModal? modal = null;
            modal = new KeyboardModal(keyboard, k =>
            {
                session.Options = set(session.Options, k.Text);
                CloseModal(modal!);
                RebuildBatchRename(session);
                return Task.CompletedTask;
            });
            PushModal(modal);
        }, icon: ActionIcon.Keyboard);

    /// <summary>Texto do padrão: só recusa o que nunca pode estar num nome do Windows (o resto a prévia mostra).</summary>
    private static string? ValidatePatternText(string text)
    {
        foreach (var c in text)
            if ("<>:\"/\\|?*".Contains(c, StringComparison.Ordinal)) return $"\"{c}\" não é permitido em nomes de arquivo do Windows.";
        return null;
    }

    private void EditBatchNumber(BatchRenameSession session)
    {
        var keyboard = new VirtualKeyboard(TextFieldKind.Generic, "Começar em", session.Options.Start.ToString(System.Globalization.CultureInfo.InvariantCulture),
            validator: t => int.TryParse(t, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) && n <= 999_999
                ? null : "Digite um número inteiro de 0 a 999999.", maxLength: 6);
        KeyboardModal? modal = null;
        modal = new KeyboardModal(keyboard, k =>
        {
            session.Options = session.Options with { Start = int.Parse(k.Text, System.Globalization.CultureInfo.InvariantCulture) };
            CloseModal(modal!);
            RebuildBatchRename(session);
            return Task.CompletedTask;
        });
        PushModal(modal);
    }

    private void ApplyBatchRename(BatchRenameSession session)
    {
        if (session.Plan.BlockedReason is { } reason)
        {
            SetStatus(reason);
            return;
        }
        CloseModal(session.Dialog);
        var pairs = session.Plan.Items.Where(i => i.WillRename).Select(i => (i.SourcePath, i.NewName)).ToList();
        EnqueueBatchRename(new BatchRenameRun(session.Folder, pairs, Redo: false));
    }

    private void EnqueueBatchRename(BatchRenameRun run)
    {
        if (_fileOps is null) return;
        var ops = _fileOps;
        var item = Operations.Enqueue($"Renomear {Plural.Of(run.Pairs.Count, "item", "itens")}", OperationKind.Rename, async (op, ct) =>
        {
            var results = new List<ItemResult>();
            var done = 0;
            foreach (var (path, newName) in run.Pairs)
            {
                var name = Path.GetFileName(path);
                if (ct.IsCancellationRequested)
                {
                    results.Add(new ItemResult(name, ItemOutcome.NotProcessed, Message: "Não processado.") { SourcePath = path });
                    continue;
                }
                Operations.ReportProgress(op, new OperationProgress(name, done, run.Pairs.Count, 0, null));
                try
                {
                    var renamed = await Task.Run(() => ops.Rename(path, newName), CancellationToken.None);
                    results.Add(new ItemResult(name, ItemOutcome.Succeeded, Message: $"Renomeado para \"{renamed.Name}\".",
                        FinalPath: renamed.FullPath ?? Path.Join(run.Folder, newName)) { SourcePath = path });
                }
                catch (FileOperationException ex)
                {
                    results.Add(new ItemResult(name, ItemOutcome.Failed, ex.Kind, ex.Message) { SourcePath = path });
                }
                done++;
            }
            var ok = results.All(r => r.Outcome == ItemOutcome.Succeeded);
            return new OperationResult(ok ? OperationState.Completed
                : results.All(r => r.Outcome != ItemOutcome.Succeeded) && !ct.IsCancellationRequested ? OperationState.Failed
                : ct.IsCancellationRequested ? OperationState.Cancelled : OperationState.CompletedWithWarnings, results);
        });
        item.Source = run.Folder;
        item.Destination = run.Folder;
        _batchRenames[item.Id] = run;
        StatusMessage = $"{item.Title}: iniciado.";
        RaiseChanged();
    }

    private void OnBatchRenameCompleted(OperationItem item, BatchRenameRun run, OperationResult result)
    {
        var renamed = result.Items.Where(i => i.Outcome == ItemOutcome.Succeeded && i.SourcePath is not null && i.FinalPath is not null).ToList();
        UndoRecord? undo = null;
        if (renamed.Count > 0)
        {
            // Só o que foi renomeado de fato volta no Desfazer; refazer repete exatamente esses pares.
            var steps = renamed.Select(i => new UndoStep(UndoStepKind.RenameBack, i.SourcePath!, i.FinalPath!)).ToList();
            var redoPairs = renamed.Select(i => (i.SourcePath!, Path.GetFileName(i.FinalPath!))).ToList();
            undo = new UndoRecord(item.Title, OperationKind.Rename, steps, item.StartedAt ?? DateTimeOffset.Now, DateTimeOffset.Now,
                () => EnqueueBatchRename(new BatchRenameRun(run.Folder, redoPairs, Redo: true)));
            PushUndo(undo, run.Redo);
        }
        var focus = renamed.Count > 0 ? Path.GetFileName(renamed[0].FinalPath!) : null;
        foreach (var tab in _tabs.Where(t => t.Location is PhysicalLocation here && string.Equals(here.FullPath, run.Folder, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            tab.List.ClearSelection();
            Refresh(tab, ReferenceEquals(tab, Browser) ? focus : null);
        }
        if (result.FinalState == OperationState.Completed)
        {
            SetStatus($"{Plural.Of(renamed.Count, "item renomeado", "itens renomeados")}. Menu → Desfazer volta os nomes.");
            return;
        }
        var lines = new List<(string, string)> { ("Renomeados", renamed.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)) };
        foreach (var problem in result.Items.Where(i => i.Outcome != ItemOutcome.Succeeded).Take(6))
            lines.Add(("• " + problem.Name, problem.Message ?? problem.Error.ToString()));
        var dialog = new DialogModal(result.FinalState == OperationState.Failed ? "Renomear: falhou" : "Renomear: concluído com avisos", lines)
        {
            Message = result.Message,
            Icon = ResultIcon(result.FinalState),
        };
        if (undo is not null)
            dialog.Options.Add(new DialogOption("Desfazer", DialogOptionKind.Primary, () =>
            {
                CloseModal(dialog);
                ConfirmUndo(undo);
            }, icon: ActionIcon.Undo));
        var close = new DialogOption("Fechar", DialogOptionKind.Safe, () => CloseModal(dialog), icon: ActionIcon.Close);
        dialog.Options.Add(close);
        dialog.BackOption = close;
        PushModal(dialog);
    }
}
