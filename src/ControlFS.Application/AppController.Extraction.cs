using ControlFS.Application.Operations;
using ControlFS.Application.State;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Text;

namespace ControlFS.Application;

public sealed partial class AppController
{
    private readonly Dictionary<int, ExtractionPlan> _extractions = [];

    /// <summary>Parâmetros de uma extração antes de virar pedido (sem senha: ela só existe no pedido em execução).</summary>
    internal sealed record ExtractionPlan(string ArchivePath, string Destination, bool Dedicated, IReadOnlyCollection<string>? Selected, string BasePath);

    internal void PickDestinationThenExtract(string archivePath, string startFolder, IReadOnlyCollection<string>? selected, string basePath)
    {
        OpenFolderPicker("Escolha o destino da extração", startFolder,
            folder => BeginExtraction(archivePath, folder, dedicated: true, selected, basePath));
    }

    /// <summary>Mostra o resumo final (origem, destino, entradas, política de conflitos) antes de iniciar.</summary>
    internal void BeginExtraction(string archivePath, string destination, bool dedicated, IReadOnlyCollection<string>? selected, string basePath)
    {
        var plan = new ExtractionPlan(archivePath, destination, dedicated, selected, basePath);
        var dialog = new DialogModal("Extrair", []);
        void Refill()
        {
            var stem = Path.GetFileNameWithoutExtension(plan.ArchivePath);
            dialog.Lines =
            [
                ("Origem", plan.ArchivePath),
                ("Destino", plan.Dedicated ? Path.Join(plan.Destination, stem) + "  (nova pasta; se existir, \"(2)\")" : plan.Destination),
                ("Entradas", plan.Selected is null ? "todas" : $"{plan.Selected.Count} selecionada(s)" + (plan.BasePath.Length > 0 ? $" de /{plan.BasePath}" : string.Empty)),
                ("Conflitos", "perguntar a cada conflito (padrão: manter o existente)"),
                ("Segurança", "caminhos contidos no destino; links bloqueados; nada é executado"),
            ];
        }
        Refill();
        var start = new DialogOption("Extrair", DialogOptionKind.Primary, () =>
        {
            CloseModal(dialog);
            Track(StartExtractionAsync(plan));
        });
        DialogOption? dedicatedToggle = null;
        dedicatedToggle = new DialogOption(DedicatedLabel(plan.Dedicated), DialogOptionKind.Toggle, () =>
        {
            plan = plan with { Dedicated = !plan.Dedicated };
            Refill();
            dedicatedToggle!.Label = DedicatedLabel(plan.Dedicated);
            dedicatedToggle.IsChecked = plan.Dedicated;
        })
        {
            IsChecked = plan.Dedicated,
        };
        var cancel = new DialogOption("Cancelar", DialogOptionKind.Safe, () => CloseModal(dialog));
        dialog.Options.Add(start);
        dialog.Options.Add(dedicatedToggle);
        dialog.Options.Add(cancel);
        dialog.BackOption = cancel;
        PushModal(dialog);
    }

    private async Task StartExtractionAsync(ExtractionPlan plan)
    {
        // Senha só é pedida quando alguma entrada relevante é protegida.
        bool needsPassword;
        try
        {
            var info = Browser.Archive?.Info.ArchivePath == plan.ArchivePath
                ? Browser.Archive.Info
                : await _archives.InspectAsync(plan.ArchivePath, null, Limits, CancellationToken.None);
            needsPassword = info.HasEncryptedEntries;
        }
        catch (ArchiveAccessException ex)
        {
            ShowMessage("Não foi possível extrair", [("Arquivo", Path.GetFileName(plan.ArchivePath)), ("Motivo", ex.Message)]);
            return;
        }
        if (needsPassword) AskPassword(plan, retryMessage: null);
        else Enqueue(plan, password: null);
    }

    private void AskPassword(ExtractionPlan plan, string? retryMessage)
    {
        var keyboard = new VirtualKeyboard(TextFieldKind.Password, $"Senha de {Path.GetFileName(plan.ArchivePath)}");
        if (retryMessage is not null) keyboard.SetExternalError(retryMessage);
        KeyboardModal? modal = null;
        modal = new KeyboardModal(keyboard, k =>
        {
            if (k.Length == 0)
            {
                k.Reopen("Digite a senha ou cancele.");
                return Task.CompletedTask;
            }
            var secret = k.TakeSecret();
            CloseModal(modal!);
            Enqueue(plan, secret);
            return Task.CompletedTask;
        });
        PushModal(modal);
    }

    private void Enqueue(ExtractionPlan plan, string? password)
    {
        var request = new ExtractionRequest
        {
            ArchivePath = plan.ArchivePath,
            DestinationDirectory = plan.Destination,
            Mode = plan.Dedicated ? DestinationMode.CreateDedicatedFolder : DestinationMode.IntoExistingFolder,
            DedicatedFolderName = Path.GetFileNameWithoutExtension(plan.ArchivePath),
            SelectedPaths = plan.Selected,
            BaseInnerPath = plan.BasePath,
            Password = password,
            Limits = Limits,
        };
        var item = Operations.Enqueue($"Extrair {Path.GetFileName(plan.ArchivePath)}", OperationKind.Extract, async (op, ct) =>
        {
            var progress = new Progress<OperationProgress>(p => Operations.ReportProgress(op, p));
            var interaction = new UiConflictInteraction(this, op);
            return await _archives.ExtractAsync(request, interaction, progress, ct);
        });
        _extractions[item.Id] = plan;
        StatusMessage = "Extração iniciada. Você pode continuar navegando.";
        RaiseChanged();
    }

    private void OnOperationCompleted(OperationItem item)
    {
        if (!_extractions.Remove(item.Id, out var plan) || item.Result is not { } result) return;
        if (result.Error is OperationErrorKind.WrongPassword or OperationErrorKind.PasswordRequired)
        {
            AskPassword(plan, result.Error == OperationErrorKind.WrongPassword ? "Senha incorreta. Tente novamente." : null);
            return;
        }
        ShowExtractionResult(item, plan, result);
        if (Browser.Location is PhysicalLocation here && result.Destination is { } dest &&
            (string.Equals(Path.GetDirectoryName(dest), here.FullPath, StringComparison.OrdinalIgnoreCase) || string.Equals(dest, here.FullPath, StringComparison.OrdinalIgnoreCase)))
            Refresh(Browser);
    }

    private void ShowExtractionResult(OperationItem item, ExtractionPlan plan, OperationResult result)
    {
        var title = result.FinalState switch
        {
            OperationState.Completed => "Extração concluída",
            OperationState.CompletedWithWarnings => "Extração concluída com avisos",
            OperationState.Cancelled => "Extração cancelada",
            _ => "Extração falhou",
        };
        var lines = new List<(string, string)> { ("Arquivo", Path.GetFileName(plan.ArchivePath)) };
        if (result.Destination is { } destination) lines.Add(("Destino", destination));
        void Count(string label, ItemOutcome outcome)
        {
            var n = result.Count(outcome);
            if (n > 0) lines.Add((label, n.ToString()));
        }
        Count("Extraídos", ItemOutcome.Succeeded);
        Count("Mantidos ambos (renomeados)", ItemOutcome.Renamed);
        Count("Substituídos", ItemOutcome.Replaced);
        Count("Ignorados", ItemOutcome.Skipped);
        Count("Bloqueados por segurança", ItemOutcome.Blocked);
        Count("Falhas", ItemOutcome.Failed);
        Count("Não processados", ItemOutcome.NotProcessed);
        foreach (var problem in result.Items.Where(i => i.Outcome is ItemOutcome.Failed or ItemOutcome.Blocked).Take(6))
            lines.Add(("• " + problem.Name, problem.Message ?? problem.Error.ToString()));

        var dialog = new DialogModal(title, lines) { Message = result.Message };
        var close = new DialogOption("Fechar", DialogOptionKind.Safe, () => CloseModal(dialog));
        if (result.Destination is { } dest && Directory.Exists(dest))
        {
            dialog.Options.Add(new DialogOption("Abrir pasta extraída", DialogOptionKind.Primary, () =>
            {
                CloseModal(dialog);
                Screen = Screen.Browser;
                Track(NavigateAsync(Browser, new PhysicalLocation(dest), pushHistory: Browser.Location is not null));
            }));
        }
        if (result.Items.Any(i => i.Error == OperationErrorKind.WrongPasswordOrCorrupt))
            dialog.Options.Add(new DialogOption("Tentar outra senha", DialogOptionKind.Primary, () =>
            {
                CloseModal(dialog);
                AskPassword(plan, "Algumas entradas falharam: senha incorreta ou dados corrompidos.");
            }));
        dialog.Options.Add(close);
        dialog.BackOption = close;
        PushModal(dialog);
    }

    /// <summary>Diálogo de conflito. Inicia em "Pular" (preserva o existente); substituir exige confirmação.</summary>
    internal void ShowConflictDialog(ConflictInfo conflict, OperationItem op, TaskCompletionSource<ConflictDecision> answer)
    {
        var applyToRest = false;
        var lines = new List<(string, string)>
        {
            ("Existente", conflict.ExistingPath),
            ("  tamanho/data", Describe(conflict.ExistingIsDirectory ? null : conflict.ExistingSize, conflict.ExistingModified, conflict.ExistingIsDirectory)),
            ("Recebido", conflict.IncomingName),
            ("  tamanho/data", Describe(conflict.IncomingSize, conflict.IncomingModified, false)),
        };
        var dialog = new DialogModal("Já existe um item com esse nome", lines, sensitive: true);
        void Answer(ConflictChoice choice)
        {
            CloseModal(dialog);
            Operations.SetWaiting(op, false);
            answer.TrySetResult(new ConflictDecision(choice, applyToRest));
        }
        var skip = new DialogOption("Pular (manter existente)", DialogOptionKind.Safe, () => Answer(ConflictChoice.Skip));
        dialog.Options.Add(skip);
        dialog.Options.Add(new DialogOption("Manter ambos", DialogOptionKind.Primary, () => Answer(ConflictChoice.KeepBoth)));
        dialog.Options.Add(new DialogOption("Substituir…", DialogOptionKind.Danger, () => ConfirmReplace(conflict, applyToRest, () => Answer(ConflictChoice.Replace))));
        DialogOption? applyToggle = null;
        applyToggle = new DialogOption("Aplicar aos demais conflitos desta operação: não", DialogOptionKind.Toggle, () =>
        {
            applyToRest = !applyToRest;
            applyToggle!.Label = $"Aplicar aos demais conflitos desta operação: {(applyToRest ? "sim" : "não")}";
            applyToggle.IsChecked = applyToRest;
        });
        dialog.Options.Add(applyToggle);
        dialog.Options.Add(new DialogOption("Cancelar extração", DialogOptionKind.Safe, () => Answer(ConflictChoice.Cancel)));
        dialog.BackOption = skip;
        dialog.FocusIndex = 0;
        PushModal(dialog);
    }

    private void ConfirmReplace(ConflictInfo conflict, bool applyToRest, Action confirmed)
    {
        var dialog = new DialogModal("Substituir arquivo existente?", [("Será substituído", conflict.ExistingPath)], sensitive: true)
        {
            Message = applyToRest
                ? "O conteúdo existente será perdido — e a mesma escolha valerá para os demais conflitos desta operação."
                : "O conteúdo existente será perdido.",
        };
        var cancel = new DialogOption("Cancelar", DialogOptionKind.Safe, () => CloseModal(dialog));
        dialog.Options.Add(cancel);
        dialog.Options.Add(new DialogOption("Substituir", DialogOptionKind.Danger, () =>
        {
            CloseModal(dialog);
            confirmed();
        }));
        dialog.BackOption = cancel;
        dialog.FocusIndex = 0;
        PushModal(dialog);
    }

    private static string DedicatedLabel(bool dedicated) => dedicated ? "Criar pasta dedicada: sim" : "Criar pasta dedicada: não (extrair direto no destino)";

    private static string Describe(long? size, DateTimeOffset? modified, bool isDirectory) =>
        (isDirectory ? "pasta" : size is long s ? FormatBytes(s) : "tamanho desconhecido") + " · " + (modified?.LocalDateTime.ToString("g") ?? "data desconhecida");
}
