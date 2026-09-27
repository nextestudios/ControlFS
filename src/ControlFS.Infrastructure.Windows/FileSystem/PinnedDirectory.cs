using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using Microsoft.Win32.SafeHandles;

namespace ControlFS.Infrastructure.Windows.FileSystem;

/// <summary>
/// Pasta de destino "presa" por handles: a raiz autorizada e cada subpasta até o alvo são abertas no Windows com
/// <c>FILE_FLAG_OPEN_REPARSE_POINT</c> e sem <c>FILE_SHARE_DELETE</c>. Enquanto a instância existe, nenhuma pasta da
/// cadeia pode ser renomeada, apagada ou trocada por uma junction/link por outro processo; cada uma é conferida pelo
/// handle (não é ponto de reparse, e o caminho final do handle é exatamente o esperado). Movimentações para dentro
/// dela são feitas relativas ao handle da pasta (<c>SetFileInformationByHandle(FileRenameInfo)</c>).
/// Fora do Windows cai para conferências por caminho (sem garantia contra corrida).
/// </summary>
public sealed partial class PinnedDirectory : IDisposable
{
    private const uint FileListDirectory = 0x0001;
    private const uint FileTraverse = 0x0020;
    private const uint FileReadAttributes = 0x0080;
    private const uint Delete = 0x00010000;
    private const uint Synchronize = 0x00100000;
    private const uint ShareRead = 1;
    private const uint ShareWrite = 2;
    private const uint ShareDelete = 4;
    private const uint OpenExisting = 3;
    private const uint FlagBackupSemantics = 0x02000000;
    private const uint FlagOpenReparsePoint = 0x00200000;
    private const uint AttributeDirectory = 0x10;
    private const uint AttributeReparsePoint = 0x400;
    private const int FileRenameInfoClass = 3;
    private const int ErrorFileExists = 80;
    private const int ErrorAlreadyExists = 183;
    private const int ErrorInvalidParameter = 87;
    private const int ErrorNotSupported = 50;
    private const int ErrorNotSameDevice = 17;

    private readonly List<SafeFileHandle> _chain;

    private PinnedDirectory(string path, List<SafeFileHandle> chain)
    {
        FullPath = path;
        _chain = chain;
    }

    /// <summary>Caminho da pasta presa (conferido pelo handle no Windows).</summary>
    public string FullPath { get; }

    /// <summary>Caminho final da raiz, como o Windows o resolve (identidade da raiz para comparar entre chamadas).</summary>
    public string RootIdentity { get; private init; } = string.Empty;

    /// <summary>
    /// Prende <paramref name="root"/>/<paramref name="components"/>. A raiz pode ser um link escolhido pelo usuário; nenhuma
    /// subpasta pode ser. Com <paramref name="create"/>, subpastas ausentes são criadas (uma a uma, já sob a parte presa).
    /// </summary>
    /// <exception cref="FileOperationException">Componente é link/junction, arquivo, ou mudou durante a verificação.</exception>
    public static PinnedDirectory Open(string root, IReadOnlyList<string> components, bool create, string? expectedRootIdentity = null)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return OperatingSystem.IsWindows()
            ? OpenWindows(root, components, create, expectedRootIdentity)
            : OpenPortable(root, components, create);
    }

    /// <summary>Componentes de <paramref name="folder"/> abaixo de <paramref name="root"/> (vazio se for a própria raiz).</summary>
    public static IReadOnlyList<string> Relative(string root, string folder)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(folder));
        if (relative == ".") return [];
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new FileOperationException(OperationErrorKind.PathRejected, "Caminho fora da pasta de destino autorizada.");
        return relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Move (renomeia) <paramref name="source"/> para esta pasta com o nome <paramref name="name"/>. Retorna falso se o
    /// destino já existir e <paramref name="replace"/> for falso. Links na origem são movidos como link, nunca seguidos.
    /// </summary>
    public bool MoveHere(string source, string name, bool replace)
    {
        if (name.Length == 0 || name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar) || name is "." or "..")
            throw new FileOperationException(OperationErrorKind.PathRejected, "Nome inválido para o destino.");
        if (!OperatingSystem.IsWindows()) return MovePortable(source, Path.Join(FullPath, name), replace);
        return MoveWindows(source, name, replace);
    }

    public void Dispose()
    {
        foreach (var handle in _chain) handle.Dispose();
        _chain.Clear();
    }

    // ---------------- Windows ----------------

    [SupportedOSPlatform("windows")]
    private static PinnedDirectory OpenWindows(string root, IReadOnlyList<string> components, bool create, string? expectedRootIdentity)
    {
        var chain = new List<SafeFileHandle>(components.Count + 1);
        try
        {
            // A raiz é a pasta escolhida pelo usuário: pode ser um link legítimo, então é aberta seguindo-o, mas presa.
            var rootHandle = OpenDirectory(root, followLinks: true);
            chain.Add(rootHandle);
            var rootFinal = FinalPath(rootHandle);
            if (expectedRootIdentity is not null && !string.Equals(rootFinal, expectedRootIdentity, StringComparison.OrdinalIgnoreCase))
                throw new FileOperationException(OperationErrorKind.DestinationTraversesLink, "A pasta de destino foi trocada durante a operação.");
            var current = root;
            var currentFinal = rootFinal;
            foreach (var component in components)
            {
                current = Path.Join(current, component);
                if (create && !Directory.Exists(current))
                {
                    if (File.Exists(current) || IsLinkByPath(current))
                        throw Collision(component, current);
                    Directory.CreateDirectory(current);
                }
                SafeFileHandle handle;
                try
                {
                    handle = OpenDirectory(current, followLinks: false);
                }
                catch (FileNotFoundException)
                {
                    throw new FileOperationException(OperationErrorKind.DestinationTraversesLink, $"A pasta \"{component}\" mudou durante a operação.");
                }
                catch (DirectoryNotFoundException)
                {
                    throw new FileOperationException(OperationErrorKind.DestinationTraversesLink, $"A pasta \"{component}\" mudou durante a operação.");
                }
                chain.Add(handle);
                var attributes = Attributes(handle);
                if ((attributes & AttributeReparsePoint) != 0)
                    throw new FileOperationException(OperationErrorKind.DestinationTraversesLink, $"\"{component}\" é um link ou junction; não será seguido.");
                if ((attributes & AttributeDirectory) == 0) throw Collision(component, current);
                // O handle precisa ser exatamente o filho esperado da pasta anterior (já presa).
                var final = FinalPath(handle);
                if (!string.Equals(final, currentFinal + "\\" + component, StringComparison.OrdinalIgnoreCase))
                    throw new FileOperationException(OperationErrorKind.DestinationTraversesLink, $"A pasta \"{component}\" mudou durante a operação ou aponta para outro lugar.");
                currentFinal = final;
            }
            return new PinnedDirectory(current, chain) { RootIdentity = rootFinal };
        }
        catch
        {
            foreach (var handle in chain) handle.Dispose();
            throw;
        }
    }

    private static FileOperationException Collision(string component, string path) =>
        IsLinkByPath(path)
            ? new FileOperationException(OperationErrorKind.DestinationTraversesLink, $"\"{component}\" é um link ou junction; não será seguido.")
            : new FileOperationException(OperationErrorKind.NameCollision, $"\"{component}\" já existe como arquivo; não pode ser usado como pasta.");

    [SupportedOSPlatform("windows")]
    private static SafeFileHandle OpenDirectory(string path, bool followLinks)
    {
        // Sem FILE_SHARE_DELETE: ninguém consegue renomear ou apagar a pasta enquanto o handle existir.
        var handle = CreateFileW(Extended(path), FileListDirectory | FileTraverse | FileReadAttributes | Synchronize,
            ShareRead | ShareWrite, IntPtr.Zero, OpenExisting, FlagBackupSemantics | (followLinks ? 0 : FlagOpenReparsePoint), IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw ToException(error, path);
        }
        return handle;
    }

    [SupportedOSPlatform("windows")]
    private static uint Attributes(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info)) throw ToException(Marshal.GetLastPInvokeError(), null);
        return info.FileAttributes;
    }

    [SupportedOSPlatform("windows")]
    private static unsafe string FinalPath(SafeFileHandle handle)
    {
        var buffer = new char[512];
        while (true)
        {
            uint length;
            fixed (char* p = buffer) length = GetFinalPathNameByHandleW(handle, p, (uint)buffer.Length, 0);
            if (length == 0) throw ToException(Marshal.GetLastPInvokeError(), null);
            if (length < buffer.Length) return new string(buffer, 0, (int)length).TrimEnd('\\');
            buffer = new char[length + 1];
        }
    }

    [SupportedOSPlatform("windows")]
    private bool MoveWindows(string source, string name, bool replace)
    {
        var parent = _chain[^1];
        // A origem é aberta sem seguir links: um link/junction é movido como ele mesmo.
        using var handle = CreateFileW(Extended(Path.GetFullPath(source)), Delete | FileReadAttributes | Synchronize, ShareRead | ShareWrite | ShareDelete,
            IntPtr.Zero, OpenExisting, FlagBackupSemantics | FlagOpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid) throw ToException(Marshal.GetLastPInvokeError(), source);

        var error = Rename(handle, parent, name, replace);
        if (error is ErrorInvalidParameter or ErrorNotSupported)
        {
            // Alguns sistemas de arquivos (ex.: compartilhamentos de rede) não aceitam nome relativo a um handle: usa o
            // caminho completo da pasta presa, que continua presa (a cadeia não pode ser trocada durante a chamada).
            error = Rename(handle, null, FinalPath(parent) + "\\" + name, replace);
        }
        if (error == 0) return true;
        if (error is ErrorFileExists or ErrorAlreadyExists) return false;
        if (error == ErrorNotSameDevice)
        {
            // Outro volume montado dentro do destino: o Windows copia e apaga (a cadeia continua presa).
            handle.Dispose();
            return MovePortable(source, Path.Join(FullPath, name), replace);
        }
        throw ToException(error, source);
    }

    [SupportedOSPlatform("windows")]
    private static int Rename(SafeFileHandle file, SafeFileHandle? root, string name, bool replace)
    {
        var offsetRoot = IntPtr.Size; // union {BOOLEAN; DWORD} alinhada ao ponteiro
        var offsetLength = offsetRoot + IntPtr.Size;
        var offsetName = offsetLength + sizeof(uint);
        var buffer = new byte[offsetName + (name.Length + 1) * sizeof(char)];
        buffer[0] = replace ? (byte)1 : (byte)0;
        var added = false;
        try
        {
            if (root is not null)
            {
                root.DangerousAddRef(ref added);
                var value = root.DangerousGetHandle();
                if (IntPtr.Size == 8) BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(offsetRoot), value.ToInt64());
                else BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offsetRoot), value.ToInt32());
            }
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offsetLength), (uint)(name.Length * sizeof(char)));
            MemoryMarshal.AsBytes(name.AsSpan()).CopyTo(buffer.AsSpan(offsetName));
            return SetFileInformationByHandle(file, FileRenameInfoClass, buffer, (uint)buffer.Length) ? 0 : Marshal.GetLastPInvokeError();
        }
        finally
        {
            if (added) root!.DangerousRelease();
        }
    }

    /// <summary>Prefixo de caminho estendido: o Win32 cru não aceita caminhos longos sem ele.</summary>
    private static string Extended(string path) =>
        path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal) ? path
        : path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..]
        : @"\\?\" + path;

    private static Exception ToException(int error, string? path) => error switch
    {
        2 => new FileNotFoundException("Item não encontrado.", path),
        3 => new DirectoryNotFoundException("Pasta não encontrada."),
        5 => new UnauthorizedAccessException("Permissão negada."),
        _ => new IOException($"Erro do Windows {error}.", unchecked((int)0x80070000 | error)),
    };

    // ---------------- Portátil (sem garantia contra corrida) ----------------

    private static PinnedDirectory OpenPortable(string root, IReadOnlyList<string> components, bool create)
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("A pasta de destino não existe.");
        var current = root;
        foreach (var component in components)
        {
            current = Path.Join(current, component);
            if (IsLinkByPath(current))
                throw new FileOperationException(OperationErrorKind.DestinationTraversesLink, $"\"{component}\" é um link ou junction; não será seguido.");
            if (!Directory.Exists(current))
            {
                if (File.Exists(current) || !create) throw File.Exists(current) ? Collision(component, current)
                    : new FileOperationException(OperationErrorKind.DestinationTraversesLink, $"A pasta \"{component}\" mudou durante a operação.");
                Directory.CreateDirectory(current);
            }
        }
        return new PinnedDirectory(current, []) { RootIdentity = root };
    }

    private static bool MovePortable(string source, string target, bool replace)
    {
        try
        {
            if (Directory.Exists(source) && !IsLinkByPath(source)) Directory.Move(source, target);
            else File.Move(source, target, replace);
            return true;
        }
        catch (IOException) when (!replace && (File.Exists(target) || Directory.Exists(target) || IsLinkByPath(target)))
        {
            return false;
        }
    }

    private static bool IsLinkByPath(string path)
    {
        try
        {
            if (new FileInfo(path).LinkTarget is not null) return true;
            return (File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [SupportedOSPlatform("windows")]
    private static partial SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SupportedOSPlatform("windows")]
    private static partial bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [SupportedOSPlatform("windows")]
    private static unsafe partial uint GetFinalPathNameByHandleW(SafeFileHandle file, char* path, uint length, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SupportedOSPlatform("windows")]
    private static partial bool SetFileInformationByHandle(SafeFileHandle file, int informationClass, byte[] information, uint size);
}
