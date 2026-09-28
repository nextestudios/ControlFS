using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ControlFS.Infrastructure.Windows.Timing;

/// <summary>
/// Pede ao Windows o relógio de 1 ms enquanto a leitura rápida do controle está ligada (o padrão é ~15,6 ms, que
/// arredondaria a espera de 8 ms). Cada <see cref="Begin"/> precisa de um <see cref="End"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public static partial class TimerResolution
{
    public static void Begin() => _ = TimeBeginPeriod(1);

    public static void End() => _ = TimeEndPeriod(1);

    [LibraryImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static partial uint TimeBeginPeriod(uint period);

    [LibraryImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static partial uint TimeEndPeriod(uint period);
}
