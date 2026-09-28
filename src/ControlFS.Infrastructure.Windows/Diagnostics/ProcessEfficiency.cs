using System.Runtime;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ControlFS.Infrastructure.Windows.Diagnostics;

/// <summary>
/// "Leve em segundo plano" (docs/performance.md): prioridade do processo, modo de eficiência (EcoQoS) e devolução da
/// memória livre ao Windows. Tudo é dica ao sistema: falhas são ignoradas e nada muda o comportamento do app.
/// </summary>
[SupportedOSPlatform("windows")]
public static partial class ProcessEfficiency
{
    private const uint NormalPriorityClass = 0x20;
    private const uint BelowNormalPriorityClass = 0x4000;
    private const int ProcessPowerThrottling = 4;
    private const uint PowerThrottlingCurrentVersion = 1;
    private const uint PowerThrottlingExecutionSpeed = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    /// <summary>
    /// Em segundo plano: prioridade abaixo do normal e EcoQoS (o Windows 11 põe o processo em núcleos de eficiência e
    /// frequência baixa). Em primeiro plano: prioridade normal e o Windows volta a decidir sozinho.
    /// </summary>
    public static void SetBackground(bool background)
    {
        var process = GetCurrentProcess();
        _ = SetPriorityClass(process, background ? BelowNormalPriorityClass : NormalPriorityClass);
        var state = new PowerThrottlingState
        {
            Version = PowerThrottlingCurrentVersion,
            ControlMask = background ? PowerThrottlingExecutionSpeed : 0,
            StateMask = background ? PowerThrottlingExecutionSpeed : 0,
        };
        unsafe
        {
            _ = SetProcessInformation(process, ProcessPowerThrottling, &state, (uint)sizeof(PowerThrottlingState));
        }
    }

    /// <summary>
    /// Coleta agressiva (compacta também o heap de objetos grandes e devolve os segmentos livres) e tira do conjunto de
    /// trabalho as páginas que o app não está usando. Voltar à janela as traz de volta sob demanda.
    /// </summary>
    public static void TrimMemory()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        _ = SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1);
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetPriorityClass(nint process, uint priorityClass);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool SetProcessInformation(nint process, int informationClass, void* information, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessWorkingSetSize(nint process, nint minimum, nint maximum);
}
