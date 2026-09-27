using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Policies;
using ControlFS.Core.Text;

namespace ControlFS.Application;

public sealed partial class AppController
{
    private const string LeavingNotice = "Aberto em outro programa do Windows — ele pode não funcionar com o controle. Volte com Alt+Tab ou o botão do sistema.";

    private string? ShellUnavailable => _shell is null ? "Abrir com o Windows não está disponível nesta compilação." : null;

    /// <summary>
    /// Abre um arquivo físico com o programa padrão do Windows. Tipos que executam código exigem confirmação explícita,
    /// que começa em "Cancelar". Nunca é chamado automaticamente (ex.: após extrair).
    /// </summary>
    internal void OpenExternally(FileEntry entry, string path)
    {
        if (_shell is null)
        {
            ShowProperties(entry);
            return;
        }
        if (!ExecutableFiles.IsPotentiallyExecutable(path))
        {
            RecordRecentFile(path);
            RunShell(s => s.Open(path), external: true);
            return;
        }
        // Jogo da Steam: continua pedindo confirmação (um .url é um arquivo comum; qualquer um pode deixar um na Área de
        // trabalho), mas mostra o que ele abre. O Windows entrega o próprio arquivo à Steam; nada é montado a partir dele.
        var game = entry is { IsSteamGame: true, Shortcut.Url: { } target } ? target : null;
        var dialog = game is not null
            ? new DialogModal("Abrir este jogo da Steam?",
            [
                ("Jogo", EntryText.DisplayName(entry)),
                ("Arquivo", entry.Name),
                ("Abre", ShortText(game)),
            ], sensitive: true)
            {
                Message = "O atalho é entregue à Steam pelo Windows. Só continue se você confia na origem deste atalho.",
            }
            : new DialogModal("Executar este arquivo?",
            [
                ("Arquivo", entry.Name),
                ("Pasta", Path.GetDirectoryName(path) ?? "—"),
                ("Tipo", $"{Path.GetExtension(path)} — pode executar programas ou alterar o sistema"),
            ], sensitive: true)
            {
                Message = "Só continue se você confia na origem deste arquivo. O Windows pode pedir outras confirmações (SmartScreen, UAC), que ficam fora do ControlFS.",
            };
        var cancel = new DialogOption("Cancelar", DialogOptionKind.Safe, () => CloseModal(dialog), icon: ActionIcon.Cancel);
        dialog.Options.Add(cancel);
        dialog.Options.Add(new DialogOption(game is not null ? "Jogar" : "Executar", DialogOptionKind.Danger, () =>
        {
            CloseModal(dialog);
            RecordRecentFile(path);
            RunShell(s => s.Open(path), external: true);
        }, icon: game is not null ? ActionIcon.Game : ActionIcon.Run));
        dialog.BackOption = cancel;
        dialog.FocusIndex = 0;
        PushModal(dialog);
    }

    private void RunShell(Action<IShellService> action, bool external)
    {
        if (_shell is null) return;
        try
        {
            action(_shell);
            if (external) StatusMessage = LeavingNotice;
        }
        catch (ShellException ex)
        {
            ShowMessage("Não foi possível abrir", [("Motivo", ex.Message)], icon: ActionIcon.Error);
        }
        RaiseChanged();
    }

    private void AskArchivePassword(PaneState pane, string archivePath, string? retryMessage)
    {
        var keyboard = new VirtualKeyboard(TextFieldKind.Password, $"Senha para abrir {Path.GetFileName(archivePath)}");
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
            Track(OpenArchiveAsync(pane, archivePath, secret));
            return Task.CompletedTask;
        });
        PushModal(modal);
    }
}
