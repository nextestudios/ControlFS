using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ControlFS.Infrastructure.Windows.Shell;

/// <summary>
/// Pastas conhecidas obtidas do sistema (SHGetKnownFolderPath no Windows), nunca por caminho fixo.
/// NÃO VALIDADO em Windows neste incremento.
/// </summary>
public static partial class KnownFolders
{
    public static readonly Guid Downloads = new("374DE290-123F-4565-9164-39C4925E467B");
    public static readonly Guid Documents = new("FDD39AD0-238F-46AF-ADB4-6C85480369C7");
    public static readonly Guid Desktop = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");
    public static readonly Guid Pictures = new("33E28130-4E1E-4676-835A-98395C3BC3BB");
    public static readonly Guid Videos = new("18989B1D-99B5-455B-841C-AB7C74E4DDFC");
    public static readonly Guid Music = new("4BD8D571-6D19-48D3-BE97-422220080E43");

    public static IReadOnlyList<(string Name, string Path)> GetAll()
    {
        var list = new List<(string, string)>();
        void Add(string name, Guid id, Environment.SpecialFolder fallback)
        {
            var path = OperatingSystem.IsWindows() ? TryGetWindows(id) : null;
            path ??= fallback == Environment.SpecialFolder.UserProfile
                ? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
                : Environment.GetFolderPath(fallback);
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) list.Add((name, path));
        }
        // Fora do Windows, Downloads cai para ~/Downloads (apenas para desenvolvimento e testes).
        Add("Downloads", Downloads, Environment.SpecialFolder.UserProfile);
        Add("Documentos", Documents, Environment.SpecialFolder.MyDocuments);
        Add("Área de trabalho", Desktop, Environment.SpecialFolder.DesktopDirectory);
        Add("Imagens", Pictures, Environment.SpecialFolder.MyPictures);
        Add("Vídeos", Videos, Environment.SpecialFolder.MyVideos);
        Add("Músicas", Music, Environment.SpecialFolder.MyMusic);
        return list;
    }

    [SupportedOSPlatform("windows")]
    private static string? TryGetWindows(Guid id)
    {
        var hr = SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var pathPtr);
        try
        {
            return hr == 0 ? Marshal.PtrToStringUni(pathPtr) : null;
        }
        finally
        {
            Marshal.FreeCoTaskMem(pathPtr);
        }
    }

    [LibraryImport("shell32.dll")]
    [SupportedOSPlatform("windows")]
    private static partial int SHGetKnownFolderPath(in Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);
}
