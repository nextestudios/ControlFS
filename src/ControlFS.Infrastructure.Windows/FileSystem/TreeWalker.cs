using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ControlFS.Core.Policies;

namespace ControlFS.Infrastructure.Windows.FileSystem;

/// <summary>
/// Percurso de árvore compartilhado pela busca e pelo cálculo de tamanho: uma pasta por vez, em largura, sem
/// RecurseSubdirectories. A descida é decidida aqui (<see cref="Descends"/>), para nunca atravessar junções, links
/// simbólicos ou outros pontos de nova análise. Só lê nomes e metadados (nunca o conteúdo dos arquivos).
/// </summary>
internal static partial class TreeWalker
{
    /// <summary>Um item encontrado (<see cref="Info"/>) ou uma pasta que não pôde ser lida (<see cref="Info"/> nulo).</summary>
    /// <param name="IsTraversed">Pasta que o percurso atravessa (ou atravessaria com recursão); false para links e arquivos.</param>
    internal readonly record struct Step(string Folder, FileSystemInfo? Info, FileAttributes Attributes, bool IsTraversed = false)
    {
        public bool IsUnreadableFolder => Info is null;
        public bool IsDirectory => (Attributes & FileAttributes.Directory) != 0;
    }

    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };

    /// <summary>
    /// Pasta que o percurso atravessa: diretório comum ou pasta de arquivos na nuvem (OneDrive "sob demanda", #126).
    /// Junções, links simbólicos, pontos de montagem e qualquer outra marca de nova análise nunca são atravessados.
    /// </summary>
    public static bool Descends(string path, FileAttributes attributes)
    {
        if ((attributes & FileAttributes.Directory) == 0) return false;
        if ((attributes & FileAttributes.ReparsePoint) == 0) return true;
        return ReparseTagOf(path) is { } tag && ReparseTags.IsTraversableFolder(tag);
    }

    /// <summary>
    /// Marca de nova análise lida dos metadados da entrada (FindFirstFileEx sobre o próprio caminho): não abre a pasta
    /// nem arquivos, então nada é baixado da nuvem. Null se não der para ler (a pasta não é atravessada).
    /// </summary>
    internal static uint? ReparseTagOf(string path)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var handle = FindFirstFileExW(path, FindExInfoBasic, out var data, FindExSearchNameMatch, IntPtr.Zero, 0);
        if (handle == InvalidHandle) return null;
        FindClose(handle);
        return (data.FileAttributes & (uint)FileAttributes.ReparsePoint) != 0 ? data.Reserved0 : null;
    }

    private const int FindExInfoBasic = 1;
    private const int FindExSearchNameMatch = 0;
    private static readonly IntPtr InvalidHandle = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct Win32FindData
    {
        public uint FileAttributes;
        public uint CreationTimeLow;
        public uint CreationTimeHigh;
        public uint LastAccessTimeLow;
        public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow;
        public uint LastWriteTimeHigh;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint Reserved0;
        public uint Reserved1;
        public fixed char FileName[260];
        public fixed char AlternateFileName[14];
    }

    [LibraryImport("kernel32.dll", EntryPoint = "FindFirstFileExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [SupportedOSPlatform("windows")]
    private static partial IntPtr FindFirstFileExW(string fileName, int infoLevel, out Win32FindData findData, int searchOp, IntPtr searchFilter, uint additionalFlags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SupportedOSPlatform("windows")]
    private static partial bool FindClose(IntPtr findFile);

    /// <param name="include">Filtro opcional: itens recusados não aparecem e, se forem pastas, não são percorridos.</param>
    public static IEnumerable<Step> Walk(string root, bool recurse, CancellationToken cancellationToken, Func<FileSystemInfo, FileAttributes, bool>? include = null)
    {
        var pending = new Queue<string>();
        pending.Enqueue(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folder = pending.Dequeue();
            IEnumerator<FileSystemInfo>? items = null;
            try { items = new DirectoryInfo(folder).EnumerateFileSystemInfos("*", Options).GetEnumerator(); }
            catch (Exception ex) when (IsUnreadable(ex)) { }
            if (items is null)
            {
                yield return new Step(folder, null, 0);
                continue;
            }
            var unreadable = false;
            using (items)
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    FileSystemInfo info;
                    FileAttributes attrs;
                    try
                    {
                        if (!items.MoveNext()) break;
                        info = items.Current;
                        attrs = info.Attributes;
                    }
                    catch (Exception ex) when (IsUnreadable(ex))
                    {
                        unreadable = true;
                        break;
                    }
                    if (include is not null && !include(info, attrs)) continue;
                    var traversed = Descends(info.FullName, attrs);
                    yield return new Step(folder, info, attrs, traversed);
                    if (recurse && traversed) pending.Enqueue(info.FullName);
                }
            }
            if (unreadable) yield return new Step(folder, null, 0);
        }
    }

    private static bool IsUnreadable(Exception ex) => ex is UnauthorizedAccessException or IOException;
}
