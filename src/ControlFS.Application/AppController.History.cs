using System.Globalization;
using ControlFS.Application.Operations;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;

namespace ControlFS.Application;

public sealed partial class AppController
{
    private readonly IOperationHistoryStore? _historyStore;
    private Task _historySaving = Task.CompletedTask;

    /// <summary>Operações concluídas desta e de sessões anteriores (#20). Limitado; nunca guarda senhas.</summary>
    public OperationHistory History { get; } = new();

    private void LoadHistory()
    {
        if (_historyStore is null) return;
        try
        {
            var loaded = _historyStore.Load();
            History.Load(loaded.Entries);
            if (loaded.Notice is { } notice) StatusMessage ??= notice;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage ??= "Não foi possível ler o histórico de operações.";
        }
    }

    private void RecordHistory(OperationItem item)
    {
        if (item.Result is not { } result || item.HistoryEntryId is not null) return;
        var entry = OperationHistoryEntry.From(item.Kind, item.Title, item.StartedAt, DateTimeOffset.Now, result, item.Source, item.Destination);
        item.HistoryEntryId = entry.Id;
        RecordHistory(entry);
    }

    private void RecordRename(string oldPath, FileEntry renamed)
    {
        var result = new OperationResult(OperationState.Completed,
            [new ItemResult(Path.GetFileName(oldPath), ItemOutcome.Succeeded, FinalPath: renamed.FullPath) { SourcePath = oldPath }]);
        var now = DateTimeOffset.Now;
        RecordHistory(OperationHistoryEntry.From(OperationKind.Rename, $"Renomear “{Path.GetFileName(oldPath)}” para “{renamed.Name}”", now, now,
            result, oldPath, renamed.FullPath));
    }

    private void RecordHistory(OperationHistoryEntry entry)
    {
        History.Add(entry);
        SaveHistory();
    }

    /// <summary>Grava uma cópia do histórico fora da thread de UI, uma gravação por vez e na ordem pedida.</summary>
    private void SaveHistory()
    {
        if (_historyStore is null) return;
        var store = _historyStore;
        var snapshot = History.Entries.ToList();
        var previous = _historySaving;
        _historySaving = Task.Run(async () =>
        {
            await previous;
            try
            {
                store.Save(snapshot);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Post(() => StatusMessage = "Não foi possível salvar o histórico de operações.");
            }
        });
        Track(_historySaving);
    }

    // ---------- Menu → Operações ----------

    /// <summary>
    /// Operações desta sessão (em andamento e concluídas, com tentar de novo) e, abaixo, as de sessões anteriores
    /// guardadas no histórico.
    /// </summary>
    private void ShowOperations()
    {
        var items = Operations.Items.Reverse().Select(op => new MenuItem(
            $"{op.Title} — {StateLabel(op.State)}",
            () => ShowOperationDetails(op),
            Detail: op.Progress is { } p && op.IsActive ? $"{p.ItemsProcessed}/{p.ItemsTotal?.ToString() ?? "?"} itens · {FormatBytes(p.BytesProcessed)}"
                : History.Find(op.HistoryEntryId) is { } done ? When(done) : null, Icon: ActionIcon.Operations)).ToList();
        var session = Operations.Items.Select(o => o.HistoryEntryId).OfType<string>().ToHashSet(StringComparer.Ordinal);
        items.AddRange(History.Entries.Reverse().Where(e => !session.Contains(e.Id)).Select(entry => new MenuItem(
            $"{entry.Title} — {StateLabel(entry.FinalState)}",
            () => ShowHistoryEntry(entry),
            Detail: When(entry), Icon: ActionIcon.Recent)));
        if (History.Entries.Count > 0)
            items.Add(new MenuItem("Limpar histórico…", ConfirmClearHistory,
                Detail: "Apaga o histórico salvo. As operações desta sessão continuam listadas até fechar o app.", Icon: ActionIcon.Erase));
        PushModal(new MenuModal("Operações", items) { Icon = ActionIcon.Operations });
    }

    private static string When(OperationHistoryEntry entry) => entry.FinishedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    private void ShowOperationDetails(OperationItem op)
    {
        var lines = new List<(string, string)> { ("Estado", StateLabel(op.State)) };
        if (op.IsActive && op.Progress is { } p)
        {
            lines.Add(("Itens", $"{p.ItemsProcessed} de {p.ItemsTotal?.ToString() ?? "?"}"));
            lines.Add(("Dados", FormatBytes(p.BytesProcessed)));
            if (p.CurrentItem is { } current) lines.Add(("Atual", current));
        }
        if (History.Find(op.HistoryEntryId) is { } entry) lines.AddRange(HistoryLines(entry));
        else if (op.Result?.Message is { } message) lines.Add(("Resultado", message));
        var dialog = new DialogModal(op.Title, lines)
        {
            Icon = op.IsActive ? ActionIcon.Operations : ResultIcon(op.State),
            Progress = op.IsActive && op.Progress is { ItemsTotal: > 0 and var total } progress ? Math.Clamp((double)progress.ItemsProcessed / total, 0, 1) : null,
        };
        var close = new DialogOption("Fechar", DialogOptionKind.Safe, () => CloseModal(dialog), icon: ActionIcon.Close);
        dialog.Options.Add(close);
        if (op.CanPause)
            dialog.Options.Add(new DialogOption("Pausar", DialogOptionKind.Primary, () =>
            {
                CloseModal(dialog);
                if (Operations.Pause(op)) StatusMessage = $"{op.Title}: pausada. Menu → Operações para continuar.";
            }, icon: ActionIcon.Pause));
        if (op.CanResume)
            dialog.Options.Add(new DialogOption("Continuar", DialogOptionKind.Primary, () =>
            {
                CloseModal(dialog);
                if (Operations.Resume(op)) StatusMessage = $"{op.Title}: continuando.";
            }, icon: ActionIcon.Resume));
        if (op.IsActive)
            dialog.Options.Add(new DialogOption("Cancelar operação", DialogOptionKind.Danger, () =>
            {
                Operations.Cancel(op);
                CloseModal(dialog);
            }, icon: ActionIcon.Cancel));
        if (op.CanRetry)
            dialog.Options.Add(new DialogOption("Tentar de novo", DialogOptionKind.Primary, () =>
            {
                CloseModal(dialog);
                Operations.Retry(op);
            }, icon: ActionIcon.Retry));
        AddRetryFailedOption(dialog, op);
        dialog.BackOption = close;
        PushModal(dialog);
    }

    /// <summary>Operação de uma sessão anterior: só o registro do que aconteceu.</summary>
    private void ShowHistoryEntry(OperationHistoryEntry entry)
    {
        var lines = new List<(string, string)> { ("Estado", StateLabel(entry.FinalState)) };
        lines.AddRange(HistoryLines(entry));
        ShowMessage(entry.Title, lines, icon: ResultIcon(entry.FinalState));
    }

    private static IEnumerable<(string, string)> HistoryLines(OperationHistoryEntry entry)
    {
        if (entry.StartedAt is { } started) yield return ("Início", started.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture));
        yield return ("Fim", entry.FinishedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture));
        if (entry.Source is { } source) yield return ("Origem", source);
        if (entry.Destination is { } destination) yield return ("Destino", destination);
        if (entry.Message is { } message) yield return ("Resultado", message);
        foreach (var (label, outcome) in OutcomeLabels)
            if (entry.Count(outcome) is > 0 and var n) yield return (label, n.ToString(CultureInfo.InvariantCulture));
        foreach (var problem in entry.Items.Where(i => i.Outcome is ItemOutcome.Failed or ItemOutcome.Blocked).Take(6))
            yield return ("• " + problem.Name, problem.Message ?? problem.Error.ToString());
    }

    private static readonly (string, ItemOutcome)[] OutcomeLabels =
    [
        ("Concluídos", ItemOutcome.Succeeded),
        ("Mantidos ambos (renomeados)", ItemOutcome.Renamed),
        ("Substituídos", ItemOutcome.Replaced),
        ("Ignorados", ItemOutcome.Skipped),
        ("Bloqueados", ItemOutcome.Blocked),
        ("Falhas", ItemOutcome.Failed),
        ("Não processados", ItemOutcome.NotProcessed),
    ];

    private void ConfirmClearHistory()
    {
        var dialog = new DialogModal("Limpar o histórico de operações?", [("Registros", History.Entries.Count.ToString(CultureInfo.InvariantCulture))])
        {
            Message = "Só o registro é apagado; nenhum arquivo é alterado.",
        };
        var cancel = new DialogOption("Cancelar", DialogOptionKind.Safe, () => CloseModal(dialog), icon: ActionIcon.Cancel);
        dialog.Options.Add(cancel);
        dialog.Options.Add(new DialogOption("Limpar histórico", DialogOptionKind.Danger, () =>
        {
            CloseModal(dialog);
            History.Clear();
            SaveHistory();
            StatusMessage = "Histórico de operações apagado.";
        }, icon: ActionIcon.Erase));
        dialog.BackOption = cancel;
        dialog.FocusIndex = 0;
        PushModal(dialog);
    }
}
