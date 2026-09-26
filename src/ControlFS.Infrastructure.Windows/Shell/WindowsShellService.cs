using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ControlFS.Core.Contracts;

namespace ControlFS.Infrastructure.Windows.Shell;

/// <summary>
/// Abre arquivos com os programas do Windows por APIs do Shell (ShellExecuteEx, SHOpenWithDialog,
/// SHOpenFolderAndSelectItems). O caminho vai como parâmetro próprio — nunca concatenado numa linha de comando.
/// </summary>
public sealed partial class WindowsShellService : IShellService
{
    private const int ErrorNoAssociation = 1155;
    private const int OaifAllowRegistration = 0x1;
    private const int OaifExec = 0x4;

    public void Open(string path)
    {
        EnsureWindows();
        Ensure(path);
        try
        {
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorNoAssociation)
        {
            throw new ShellException("Nenhum programa do Windows está associado a este tipo de arquivo. Use \"Abrir com…\".", ex);
        }
        catch (Win32Exception ex)
        {
            throw new ShellException($"O Windows não conseguiu abrir o arquivo ({ex.Message}).", ex);
        }
    }

    public void OpenWith(string path)
    {
        EnsureWindows();
        Ensure(path);
        var file = Marshal.StringToCoTaskMemUni(path);
        try
        {
            var info = new OpenAsInfo { File = file, Class = IntPtr.Zero, Flags = OaifAllowRegistration | OaifExec };
            var hr = SHOpenWithDialog(IntPtr.Zero, ref info);
            // HRESULT_FROM_WIN32(ERROR_CANCELLED) quando o usuário fecha a caixa: não é erro.
            if (hr < 0 && hr != unchecked((int)0x800704C7)) throw new ShellException($"Não foi possível abrir a caixa \"Abrir com\" (0x{hr:X8}).");
        }
        finally
        {
            Marshal.FreeCoTaskMem(file);
        }
    }

    public void RevealInExplorer(string path)
    {
        EnsureWindows();
        Ensure(path);
        var pidl = ILCreateFromPathW(path);
        if (pidl == IntPtr.Zero) throw new ShellException("O Windows não reconheceu este caminho.");
        try
        {
            var hr = SHOpenFolderAndSelectItems(pidl, 0, IntPtr.Zero, 0);
            if (hr < 0) throw new ShellException($"Não foi possível abrir o Explorador de Arquivos (0x{hr:X8}).");
        }
        finally
        {
            ILFree(pidl);
        }
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows()) throw new ShellException("Disponível apenas no Windows.");
    }

    private static void Ensure(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) throw new ShellException("O item não existe mais.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenAsInfo
    {
        public IntPtr File;
        public IntPtr Class;
        public int Flags;
    }

    [LibraryImport("shell32.dll")]
    [SupportedOSPlatform("windows")]
    private static partial int SHOpenWithDialog(IntPtr hwndParent, ref OpenAsInfo info);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [SupportedOSPlatform("windows")]
    private static partial IntPtr ILCreateFromPathW(string path);

    [LibraryImport("shell32.dll")]
    [SupportedOSPlatform("windows")]
    private static partial void ILFree(IntPtr pidl);

    [LibraryImport("shell32.dll")]
    [SupportedOSPlatform("windows")]
    private static partial int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr apidl, uint flags);
}
