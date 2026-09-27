using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;

namespace ControlFS.Application;

/// <summary>
/// Terminal opcional (#76): Norte → "Abrir terminal aqui" abre o Windows Terminal (ou o PowerShell) na pasta atual, depois
/// de um aviso de que ele fica fora da experiência com controle. Nada é executado: o terminal só abre, esperando o usuário.
/// Quem usa controle pode abrir junto o teclado virtual do Windows (o do ControlFS só funciona dentro do ControlFS).
/// </summary>
public sealed partial class AppController
{
    public ITerminalLauncher? Terminal { get; init; }

    private MenuItem? TerminalItem(PaneState pane) => Terminal is null || pane.Location is not PhysicalLocation here ? null
        : new MenuItem("Abrir terminal aqui…", () => ConfirmTerminal(here.FullPath),
            Detail: "Windows Terminal (ou PowerShell) nesta pasta. Fica fora do ControlFS e não funciona com o controle.", Icon: ActionIcon.OpenExternal, Section: "Esta pasta");

    private void ConfirmTerminal(string folder)
    {
        var dialog = new DialogModal("Abrir terminal nesta pasta?", [("Pasta", folder)])
        {
            Message = "O terminal é um programa do Windows fora do ControlFS: ele não funciona com o controle e precisa de um teclado " +
                "(físico ou o teclado virtual do Windows). Nada é executado: ele só abre nesta pasta. Volte com Alt+Tab ou o botão do sistema.",
            Icon = ActionIcon.OpenExternal,
        };
        var cancel = new DialogOption("Cancelar", DialogOptionKind.Safe, () => CloseModal(dialog), icon: ActionIcon.Cancel);
        dialog.Options.Add(cancel);
        dialog.Options.Add(new DialogOption("Abrir terminal", DialogOptionKind.Primary, () =>
        {
            CloseModal(dialog);
            LaunchTerminal(folder, withKeyboard: false);
        }, icon: ActionIcon.OpenExternal));
        dialog.Options.Add(new DialogOption("Abrir terminal e o teclado virtual do Windows", DialogOptionKind.Primary, () =>
        {
            CloseModal(dialog);
            LaunchTerminal(folder, withKeyboard: true);
        }, icon: ActionIcon.Keyboard));
        dialog.BackOption = cancel;
        dialog.FocusIndex = 0;
        PushModal(dialog);
    }

    private void LaunchTerminal(string folder, bool withKeyboard)
    {
        try
        {
            Terminal!.OpenTerminal(folder);
            if (withKeyboard) Terminal.OpenSystemKeyboard();
            SetStatus(LeavingNotice);
        }
        catch (ShellException ex)
        {
            ShowMessage("Não foi possível abrir o terminal", [("Pasta", folder)], ex.Message, icon: ActionIcon.Error);
        }
    }
}
