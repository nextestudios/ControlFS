using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ControlFS.Infrastructure.Windows.Diagnostics;

/// <summary>
/// Termina o processo sem a fase de descarregar as DLLs. Usado só no fim da saída, depois de tudo salvo, quando a
/// mídia do Windows foi usada na sessão: a Media Foundation deixa threads de trabalho que, no encerramento normal,
/// derrubavam o processo no renderizador de software (WARP: d3d10warp!Task::ScheduleTask, #224).
/// </summary>
[SupportedOSPlatform("windows")]
public static partial class ProcessTermination
{
    public static void TerminateCurrent(uint exitCode) => _ = TerminateProcess(GetCurrentProcess(), exitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(nint process, uint exitCode);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();
}
