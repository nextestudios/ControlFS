using System.ComponentModel;
using System.Diagnostics;
using ControlFS.Core.Contracts;

namespace ControlFS.Infrastructure.Windows.Shell;

/// <summary>
/// Abre um terminal externo na pasta (#76). O caminho da pasta nunca entra nos argumentos: vai só como diretório de
/// trabalho, e o Windows Terminal recebe "-d ." (sem isso ele abriria no perfil padrão; um ";" no caminho seria lido por
/// ele como separador de comandos). Os executáveis vêm de locais fixos do sistema, nunca do PATH nem da pasta aberta.
/// Nenhum comando é passado: o terminal só abre, esperando o usuário.
/// </summary>
public sealed class TerminalLauncher : ITerminalLauncher
{
    /// <summary>O que será iniciado: executável, argumentos (fixos) e diretório de trabalho.</summary>
    public sealed record LaunchPlan(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory);

    /// <summary>Windows Terminal (alias de execução do app) se instalado; senão o Windows PowerShell, que sempre existe.</summary>
    public static LaunchPlan Plan(string folder, string? windowsTerminal, string systemDirectory) =>
        windowsTerminal is not null
            ? new LaunchPlan(windowsTerminal, ["-d", "."], folder)
            : new LaunchPlan(Path.Join(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"), ["-NoLogo"], folder);

    public void OpenTerminal(string folder)
    {
        if (!OperatingSystem.IsWindows()) throw new ShellException("Disponível apenas no Windows.");
        var full = Path.GetFullPath(folder);
        if (!Directory.Exists(full)) throw new ShellException("A pasta não existe mais.");
        var alias = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "wt.exe");
        var plan = Plan(full, File.Exists(alias) ? alias : null, Environment.SystemDirectory);
        try
        {
            Start(plan);
        }
        catch (Win32Exception) when (plan.FileName == alias)
        {
            Start(Plan(full, null, Environment.SystemDirectory)); // alias quebrado (app desinstalado pela metade): PowerShell
        }
        catch (Win32Exception ex)
        {
            throw new ShellException($"O Windows não conseguiu abrir o terminal ({ex.Message}).", ex);
        }
    }

    public void OpenSystemKeyboard()
    {
        if (!OperatingSystem.IsWindows()) throw new ShellException("Disponível apenas no Windows.");
        try
        {
            // osk.exe pede uiAccess: só abre pelo Shell (CreateProcess direto falharia com "requer elevação").
            using var process = Process.Start(new ProcessStartInfo(Path.Join(Environment.SystemDirectory, "osk.exe")) { UseShellExecute = true });
        }
        catch (Win32Exception ex)
        {
            throw new ShellException($"O Windows não conseguiu abrir o teclado virtual ({ex.Message}).", ex);
        }
    }

    private static void Start(LaunchPlan plan)
    {
        var info = new ProcessStartInfo(plan.FileName) { UseShellExecute = false, WorkingDirectory = plan.WorkingDirectory };
        foreach (var argument in plan.Arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info);
    }
}
