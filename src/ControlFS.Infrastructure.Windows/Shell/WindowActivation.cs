using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ControlFS.Infrastructure.Windows.Shell;

/// <summary>
/// Métodos nativos de ativação e foco de janela no Windows.
/// Garante que o app venha ao primeiro plano ao receber comandos de automação ou links externos.
/// </summary>
[SupportedOSPlatform("windows")]
public static partial class WindowActivation
{
    private const int SwRestore = 9;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hWnd, nint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hWnd, int nCmdShow);

    /// <summary>
    /// Restaura a janela se estiver minimizada e traz para a frente anexando temporariamente à fila da thread de foco.
    /// </summary>
    public static bool BringToForeground(nint hwnd)
    {
        if (!OperatingSystem.IsWindows() || hwnd == 0) return false;

        ShowWindow(hwnd, SwRestore);
        var current = GetForegroundWindow();
        if (current == hwnd) return true;

        var ourThread = GetCurrentThreadId();
        var theirThread = current == 0 ? 0 : GetWindowThreadProcessId(current, 0);
        var attached = theirThread != 0 && theirThread != ourThread && AttachThreadInput(ourThread, theirThread, true);
        try
        {
            return SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached) AttachThreadInput(ourThread, theirThread, false);
        }
    }
}
