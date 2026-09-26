using ControlFS.Application.State;
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
            RunShell(s => s.Open(path), external: true);
            return;
        }
        var dialog = new DialogModal("Executar este arquivo?",
        [
            ("Arquivo", entry.Name),
            ("Pasta", Path.GetDirectoryName(path) ?? "—"),
            ("Tipo", $"{Path.GetExtension(path)} — pode executar programas ou alterar o sistema"),
        ], sensitive: true)
        {
            Message = "Só continue se você confia na origem deste arquivo. O Windows pode pedir outras confirmações (SmartScreen, UAC), que ficam fora do ControlFS.",
        };
        var cancel = new DialogOption("Cancelar", DialogOptionKind.Safe, () => CloseModal(dialog));
        dialog.Options.Add(cancel);
        dialog.Options.Add(new DialogOption("Executar", DialogOptionKind.Danger, () =>
        {
            CloseModal(dialog);
            RunShell(s => s.Open(path), external: true);
        }));
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
            ShowMessage("Não foi possível abrir", [("Motivo", ex.Message)]);
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
