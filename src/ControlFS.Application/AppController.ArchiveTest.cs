using ControlFS.Application.Operations;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;

namespace ControlFS.Application;

/// <summary>
/// Testar integridade de um compactado: lê todas as entradas e confere tamanho e CRC sem gravar nada. Roda na fila de
/// operações (progresso e cancelamento como uma extração). Não é antivírus.
/// </summary>
public sealed partial class AppController
{
    private readonly Dictionary<int, string> _archiveTests = [];

    internal void BeginArchiveTest(string archivePath) => Track(StartArchiveTestAsync(archivePath));

    private async Task StartArchiveTestAsync(string archivePath)
    {
        if (Browser.Archive?.Info.ArchivePath == archivePath && Browser.ArchivePassword is { } known)
        {
            EnqueueArchiveTest(archivePath, known);
            return;
        }
        bool needsPassword;
        try
        {
            var info = Browser.Archive?.Info.ArchivePath == archivePath
                ? Browser.Archive.Info
                : await _archives.InspectAsync(archivePath, null, Limits, CancellationToken.None);
            needsPassword = info.HasEncryptedEntries;
        }
        catch (ArchiveAccessException ex) when (ex.Kind is OperationErrorKind.PasswordRequired or OperationErrorKind.WrongPassword)
        {
            needsPassword = true;
        }
        catch (ArchiveAccessException ex)
        {
            ShowMessage("Não foi possível testar", [("Arquivo", Path.GetFileName(archivePath)), ("Motivo", ex.Message)], icon: ActionIcon.Error);
            return;
        }
        if (needsPassword) AskSecretFor(archivePath, null, secret => EnqueueArchiveTest(archivePath, secret));
        else EnqueueArchiveTest(archivePath, null);
    }

    private void EnqueueArchiveTest(string archivePath, string? password)
    {
        var request = new ArchiveTestRequest { ArchivePath = archivePath, Password = password, Limits = Limits };
        var item = Operations.Enqueue($"Testar {Path.GetFileName(archivePath)}", OperationKind.TestArchive, async (op, ct) =>
        {
            var progress = new Progress<OperationProgress>(p => Operations.ReportProgress(op, p));
            return await _archives.TestAsync(request, progress, ct);
        });
        item.RetryAction = () => BeginArchiveTest(archivePath);
        _archiveTests[item.Id] = archivePath;
        StatusMessage = "Teste de integridade iniciado. Nada será gravado.";
        RaiseChanged();
    }

    private void OnArchiveTestCompleted(OperationItem item, string archivePath, OperationResult result)
    {
        if (result.Error is OperationErrorKind.WrongPassword or OperationErrorKind.PasswordRequired)
        {
            AskSecretFor(archivePath, result.Error == OperationErrorKind.WrongPassword ? "Senha incorreta. Tente novamente." : null,
                secret => EnqueueArchiveTest(archivePath, secret));
            return;
        }
        ShowArchiveTestResult(item, archivePath, result);
    }

    private void ShowArchiveTestResult(OperationItem item, string archivePath, OperationResult result)
    {
        var title = result.FinalState switch
        {
            OperationState.Completed => "Integridade: nenhum problema encontrado",
            OperationState.CompletedWithWarnings => "Integridade: problemas encontrados",
            OperationState.Cancelled => "Teste de integridade cancelado",
            _ => "Teste de integridade falhou",
        };
        var ok = result.Items.Where(i => i.Outcome == ItemOutcome.Succeeded).ToList();
        var lines = new List<(string, string)> { ("Arquivo", Path.GetFileName(archivePath)) };
        void Line(string label, int n)
        {
            if (n > 0) lines.Add((label, n.ToString()));
        }
        Line("Conferidas pelo CRC", ok.Count(i => !i.NoChecksum));
        Line("Lidas sem checksum para conferir", ok.Count(i => i.NoChecksum));
        Line("Com falha", result.Count(ItemOutcome.Failed));
        Line("Não verificadas", result.Count(ItemOutcome.NotProcessed));
        foreach (var problem in result.Items.Where(i => i.Outcome == ItemOutcome.Failed).Take(6))
            lines.Add(("• " + problem.Name, problem.Message ?? problem.Error.ToString()));

        var dialog = new DialogModal(title, lines)
        {
            Icon = ResultIcon(result.FinalState),
            Message = (result.Message is { } m ? m + " " : string.Empty) +
                "O teste confere se os dados podem ser lidos e batem com o CRC; não é uma verificação de vírus.",
        };
        var close = new DialogOption("Fechar", DialogOptionKind.Safe, () => CloseModal(dialog), icon: ActionIcon.Close);
        if (item.CanRetry)
            dialog.Options.Add(new DialogOption("Testar de novo", DialogOptionKind.Primary, () =>
            {
                CloseModal(dialog);
                Operations.Retry(item);
            }, icon: ActionIcon.Retry));
        dialog.Options.Add(close);
        dialog.BackOption = close;
        ShowOperationResult(dialog);
    }
}
