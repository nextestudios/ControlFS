using System.Globalization;
using System.Reflection;

namespace ControlFS.App;

/// <summary>
/// Log local mínimo de inicialização e falhas, em %LOCALAPPDATA%\ControlFS\logs (rotação simples por tamanho).
/// Nunca contém senhas nem conteúdo de arquivos; fica só no computador do usuário.
/// </summary>
internal static class AppLog
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();

    public static string Directory { get; } = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ControlFS", "logs");

    public static void Info(string message) => Write("startup.log", "INFO", message);

    public static void Crash(Exception exception, string context)
    {
        var detail = $"{context}\n{exception.GetType().FullName} (HRESULT 0x{exception.HResult:X8}): {exception.Message}\n{exception}";
        Write("crash.log", "FATAL", detail);
        Write("startup.log", "FATAL", $"{context}: {exception.GetType().Name}: {exception.Message}");
    }

    public static void Session()
    {
        var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
        Info($"ControlFS {version} · {Environment.OSVersion.VersionString} · {(Environment.Is64BitProcess ? "x64" : "x86")} · .NET {Environment.Version}");
    }

    private static void Write(string file, string level, string message)
    {
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                var path = Path.Join(Directory, file);
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes) File.Move(path, path + ".old", overwrite: true);
                File.AppendAllText(path, $"{DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture)} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
