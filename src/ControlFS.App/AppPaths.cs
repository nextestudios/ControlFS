namespace ControlFS.App;

/// <summary>
/// Onde o app guarda dados. Instalado (marcador ControlFS.installed ao lado do .exe): %LOCALAPPDATA%\ControlFS.
/// Portátil: pasta ControlFS_Data ao lado do .exe; se ela não aceitar escrita, usa %LOCALAPPDATA%\ControlFS e avisa.
/// Usa o caminho do processo (não AppContext.BaseDirectory), que no .exe único aponta para a pasta de extração.
/// </summary>
internal static class AppPaths
{
    public const string InstalledMarker = "ControlFS.installed";
    public const string PortableDataFolder = "ControlFS_Data";

    static AppPaths()
    {
        ExeDirectory = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        IsInstalled = File.Exists(Path.Join(ExeDirectory, InstalledMarker));
        var local = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ControlFS");
        if (IsInstalled)
        {
            DataDirectory = local;
            return;
        }
        var portable = Path.Join(ExeDirectory, PortableDataFolder);
        if (IsWritable(portable))
        {
            DataDirectory = portable;
        }
        else
        {
            DataDirectory = local;
            Notice = $"A pasta do ControlFS não permite gravação; preferências e logs ficam em {local}.";
        }
    }

    public static string ExeDirectory { get; }
    public static bool IsInstalled { get; }
    public static string DataDirectory { get; }
    public static string? Notice { get; }

    private static bool IsWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Join(directory, ".write-test-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
