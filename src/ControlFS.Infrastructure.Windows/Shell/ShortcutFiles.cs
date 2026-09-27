using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ControlFS.Core.Models;
using ControlFS.Core.Policies;
using Microsoft.Win32;

namespace ControlFS.Infrastructure.Windows.Shell;

/// <summary>Ícone declarado por um atalho .lnk (caminho bruto, já com variáveis expandidas) e o destino dele.</summary>
public sealed record ShellLinkInfo(string? IconLocation, int IconIndex, string? TargetPath);

/// <summary>
/// Leitura de atalhos (.url e .lnk) e resolução dos caminhos de ícone que eles declaram. Conteúdo não confiável: nada é
/// executado, a leitura tem limite de tamanho e todo caminho de ícone passa por <see cref="IconLocationPolicy"/> e por
/// <see cref="ResolveLocal"/> (unidade fixa, sem links no caminho) antes de qualquer acesso — nunca rede.
/// </summary>
public static partial class ShortcutFiles
{
    /// <summary>Arquivos .ico maiores que isso não são lidos para extrair ícone.</summary>
    public const long MaxIconFileBytes = 16L * 1024 * 1024;

    /// <summary>
    /// Arquivo que não deve ser lido sem uma ação do usuário: link/ponto de nova análise (pode apontar para a rede) ou
    /// arquivo só na nuvem (ler baixaria o conteúdo).
    /// </summary>
    private static bool IsUnsafeToRead(FileAttributes attributes) =>
        (attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline | FileAttributes.Directory)) != 0
        || ((int)attributes & (RecallOnOpen | RecallOnDataAccess)) != 0;

    private const int RecallOnOpen = 0x40000;
    private const int RecallOnDataAccess = 0x400000;

    /// <summary>Lê um .url com o limite de <see cref="InternetShortcut.MaxFileBytes"/>; <c>null</c> se não der ou não for um atalho da Internet.</summary>
    public static InternetShortcut? ReadInternetShortcut(string path) => ReadInternetShortcut(path, attributes: null);

    /// <summary>Mesmo que <see cref="ReadInternetShortcut(string)"/>, reaproveitando os atributos já lidos na listagem.</summary>
    public static InternetShortcut? ReadInternetShortcut(string path, FileAttributes? attributes)
    {
        try
        {
            if (IsUnsafeToRead(attributes ?? File.GetAttributes(path))) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.None);
            if (stream.Length > InternetShortcut.MaxFileBytes) return null;
            var buffer = new byte[InternetShortcut.MaxFileBytes + 1];
            var read = 0;
            int n;
            while (read < buffer.Length && (n = stream.Read(buffer, read, buffer.Length - read)) > 0) read += n;
            return InternetShortcut.Parse(buffer.AsSpan(0, read));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Caminho local seguro para ler: aceito pela política, expandindo antes as variáveis de ambiente (%SystemRoot%);
    /// numa unidade fixa; sem link/junção em nenhuma pasta do caminho (conferido da raiz para dentro, sem seguir
    /// nenhum); existente e não só na nuvem. <c>null</c> em qualquer outro caso.
    /// </summary>
    public static string? ResolveLocal(string? raw, bool iconFile)
    {
        if (raw is null || !OperatingSystem.IsWindows()) return null;
        var expanded = raw.Contains('%', StringComparison.Ordinal) ? Environment.ExpandEnvironmentVariables(raw) : raw;
        var path = iconFile ? IconLocationPolicy.NormalizeIconFile(expanded) : IconLocationPolicy.NormalizeLocal(expanded);
        if (path is null) return null;
        try
        {
            if (new DriveInfo(path[..3]).DriveType != DriveType.Fixed) return null;
            var current = path[..3];
            var parts = path[3..].Split('\\');
            for (var i = 0; i < parts.Length; i++)
            {
                current = Path.Join(current, parts[i]);
                var attributes = File.GetAttributes(current); // não segue links: devolve os do próprio item
                if ((attributes & FileAttributes.ReparsePoint) != 0) return null;
                if (i < parts.Length - 1 && (attributes & FileAttributes.Directory) == 0) return null;
                if (i == parts.Length - 1)
                {
                    if ((attributes & FileAttributes.Directory) != 0) return iconFile ? null : path;
                    if (IsUnsafeToRead(attributes)) return null;
                }
            }
            if (iconFile && path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) && new FileInfo(path).Length > MaxIconFileBytes) return null;
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Ícone de reserva de um jogo da Steam quando o <c>IconFile</c> do atalho não existe: o mesmo nome de arquivo
    /// (<c>&lt;hash&gt;.ico</c>) em <c>steam\games</c> da instalação local, achada por <c>HKCU\Software\Valve\Steam\SteamPath</c>.
    /// Só caminhos locais; nada é baixado.
    /// </summary>
    public static string? FindSteamGameIcon(InternetShortcut shortcut)
    {
        if (!OperatingSystem.IsWindows() || !shortcut.IsSteamGame || shortcut.IconFile is not { } declared) return null;
        var name = declared.Replace('/', '\\');
        name = name[(name.LastIndexOf('\\') + 1)..];
        if (!SteamIconName().IsMatch(name)) return null;
        var steam = SteamInstallPath();
        return steam is null ? null : ResolveLocal(Path.Join(steam, "steam", "games", name), iconFile: true);
    }

    [SupportedOSPlatform("windows")]
    private static string? SteamInstallPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            return key?.GetValue("SteamPath") is string path ? ResolveLocal(path, iconFile: false) : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^[A-Za-z0-9_\-]{1,128}\.ico$")]
    private static partial System.Text.RegularExpressions.Regex SteamIconName();

    /// <summary>
    /// Esquema da URL do atalho (ex.: "steam") quando é um nome de esquema válido; usado para conferir se há programa
    /// registrado para ele antes de pedir ao Windows para abrir.
    /// </summary>
    public static string? UrlScheme(string? url)
    {
        if (url is null) return null;
        var colon = url.IndexOf(':', StringComparison.Ordinal);
        if (colon is <= 1 or > 32) return null;
        var scheme = url[..colon];
        return char.IsAsciiLetter(scheme[0]) && scheme.All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.') ? scheme : null;
    }

    /// <summary>Há um programa registrado para o esquema (HKCR\&lt;esquema&gt; com "URL Protocol").</summary>
    [SupportedOSPlatform("windows")]
    public static bool HasUrlHandler(string scheme)
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(scheme);
            return key?.GetValue("URL Protocol") is not null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return true; // na dúvida, deixa o Windows decidir e relatar
        }
    }

    // ---------- .lnk (IShellLinkW + IPersistFile, pela vtable; chamar numa thread STA) ----------

    private static readonly Guid ClsidShellLink = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid IidShellLinkW = new("000214F9-0000-0000-C000-000000000046");
    private static readonly Guid IidPersistFile = new("0000010b-0000-0000-C000-000000000046");
    private const uint ClsctxInprocServer = 0x1;
    private const uint SlgpRawPath = 0x4;

    /// <summary>
    /// Lê o ícone declarado e o destino de um .lnk sem resolver o atalho (sem procurar o destino, sem rede): só o
    /// conteúdo do arquivo. <c>null</c> se não der para ler ou se o .lnk for um link/arquivo só na nuvem.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static unsafe ShellLinkInfo? ReadShellLink(string path)
    {
        try
        {
            if (IsUnsafeToRead(File.GetAttributes(path)) || new FileInfo(path).Length > InternetShortcut.MaxFileBytes) return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
        var clsid = ClsidShellLink;
        var iid = IidShellLinkW;
        if (CoCreateInstance(&clsid, 0, ClsctxInprocServer, &iid, out var link) != 0 || link == 0) return null;
        nint persist = 0;
        try
        {
            var vtable = *(nint**)link;
            var queryInterface = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)vtable[0];
            var persistIid = IidPersistFile;
            if (queryInterface(link, &persistIid, &persist) != 0 || persist == 0) return null;
            var load = (delegate* unmanaged[Stdcall]<nint, char*, uint, int>)(*(nint**)persist)[5];
            fixed (char* file = path)
                if (load(persist, file, 0 /* STGM_READ */) != 0) return null;

            const int capacity = 1024;
            var buffer = stackalloc char[capacity];
            var getPath = (delegate* unmanaged[Stdcall]<nint, char*, int, void*, uint, int>)vtable[3];
            buffer[0] = '\0';
            var target = getPath(link, buffer, capacity, null, SlgpRawPath) == 0 ? Text(buffer, capacity) : null;
            var getIconLocation = (delegate* unmanaged[Stdcall]<nint, char*, int, int*, int>)vtable[16];
            buffer[0] = '\0';
            int index;
            var icon = getIconLocation(link, buffer, capacity, &index) == 0 ? Text(buffer, capacity) : null;
            return new ShellLinkInfo(Expand(icon), icon is null ? 0 : index, Expand(target));
        }
        finally
        {
            if (persist != 0) _ = ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)persist)[2])(persist);
            _ = ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)link)[2])(link);
        }
    }

    private static unsafe string? Text(char* buffer, int capacity)
    {
        var text = new string(buffer, 0, new ReadOnlySpan<char>(buffer, capacity).IndexOf('\0') is var end and >= 0 ? end : capacity);
        return text.Length == 0 ? null : text;
    }

    private static string? Expand(string? value) =>
        value is null ? null : value.Contains('%', StringComparison.Ordinal) ? Environment.ExpandEnvironmentVariables(value) : value;

    [LibraryImport("ole32.dll")]
    [SupportedOSPlatform("windows")]
    private static unsafe partial int CoCreateInstance(Guid* rclsid, nint pUnkOuter, uint dwClsContext, Guid* riid, out nint ppv);
}
