namespace ControlFS.Core.Contracts;

/// <summary>
/// Terminal opcional (#76): abre o Windows Terminal (ou, sem ele, o Windows PowerShell) numa pasta, como ação explícita.
/// A pasta vai só como diretório de trabalho do processo; nenhum comando é passado ao terminal.
/// </summary>
public interface ITerminalLauncher
{
    /// <exception cref="ShellException">Com o motivo legível.</exception>
    void OpenTerminal(string folder);

    /// <summary>Abre o teclado virtual do Windows (osk.exe), para digitar no terminal com o controle/toque.</summary>
    /// <exception cref="ShellException"/>
    void OpenSystemKeyboard();
}
