using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ControlFS.Core.Policies;
using Microsoft.Win32;

namespace ControlFS.Infrastructure.Windows.Shell;

/// <summary>Unidade de rede mapeada: letra (<c>Z:\</c>), compartilhamento (quando o Windows informa) e se está conectada agora.</summary>
public sealed record MappedDrive(string Root, string? Share, bool Connected);

/// <summary>Atalho da pasta "Locais de rede" do Windows: nome mostrado e compartilhamento UNC de destino.</summary>
public sealed record NetworkShortcut(string Name, string Share);

/// <summary>
/// Locais de rede que o Windows já conhece (#27), lidos só de informação local: letras de unidade de rede
/// (GetLogicalDrives/GetDriveType), conexões lembradas (<c>HKCU\Network</c>), o estado da conexão
/// (<c>WNetGetConnection</c>, que responde com o que o Windows já sabe) e os atalhos de "Locais de rede". Nada aqui abre
/// conexão, lê o volume ou confere se o servidor responde: isso fica para quando o usuário abrir o local (fora da thread
/// de UI e cancelável). O ControlFS não guarda credenciais nem tem pilha SMB própria.
/// </summary>
public static partial class NetworkLocations
{
    /// <summary>Atalhos lidos no máximo (a pasta é do usuário; uma pasta enorme não trava o início).</summary>
    public const int MaxShortcuts = 32;

    private const int NoError = 0;
    private const int ErrorConnectionUnavailable = 1201;

    /// <summary>Pasta "Locais de rede" (Network Shortcuts) do usuário atual.</summary>
    public static string DefaultShortcutsFolder() =>
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Network Shortcuts");

    /// <summary>Unidades de rede mapeadas, conectadas ou lembradas mas desconectadas, em ordem de letra.</summary>
    public static IReadOnlyList<MappedDrive> MappedDrives(IEnumerable<string> networkRoots)
    {
        if (!OperatingSystem.IsWindows()) return [];
        var roots = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in networkRoots) roots.Add(root);
        var remembered = RememberedConnections();
        foreach (var letter in remembered.Keys) roots.Add(letter + @":\");
        var drives = new List<MappedDrive>();
        foreach (var root in roots)
        {
            var letter = char.ToUpperInvariant(root[0]);
            var (status, remote) = Connection(letter);
            var share = NetworkPathPolicy.NormalizeShare(remote) ?? (remembered.TryGetValue(letter, out var saved) ? NetworkPathPolicy.NormalizeShare(saved) : null);
            drives.Add(new MappedDrive($"{letter}:\\", share, status == NoError));
        }
        return drives;
    }

    /// <summary>
    /// Atalhos de "Locais de rede" que apontam para compartilhamentos UNC (pastas com <c>target.lnk</c>, como o assistente
    /// "Adicionar um local de rede" cria, ou arquivos .lnk soltos). Destinos WebDAV/FTP e caminhos inválidos são ignorados.
    /// Os .lnk são lidos sem resolver o atalho, numa thread STA própria, com tempo limite.
    /// </summary>
    public static IReadOnlyList<NetworkShortcut> Shortcuts(string folder, TimeSpan timeout)
    {
        if (!OperatingSystem.IsWindows()) return [];
        List<(string Name, string Link)> links;
        try
        {
            if (!Directory.Exists(folder)) return [];
            var options = new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
            links = [];
            foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", options))
            {
                if (links.Count >= MaxShortcuts) break;
                if (entry is DirectoryInfo dir && File.Exists(Path.Join(dir.FullName, "target.lnk"))) links.Add((dir.Name, Path.Join(dir.FullName, "target.lnk")));
                else if (entry is FileInfo file && file.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)) links.Add((Path.GetFileNameWithoutExtension(file.Name), file.FullName));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
        if (links.Count == 0) return [];

        var found = new List<NetworkShortcut>();
        var worker = new Thread(() =>
        {
            foreach (var (name, link) in links)
                if (OperatingSystem.IsWindows() && NetworkPathPolicy.NormalizeShare(ShortcutFiles.ReadShellLink(link)?.TargetPath) is { } share)
                    lock (found) found.Add(new NetworkShortcut(name, share));
        }) { IsBackground = true, Name = "ControlFS network shortcuts" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        worker.Join(timeout);
        lock (found)
            return [.. found.DistinctBy(s => s.Share, StringComparer.OrdinalIgnoreCase).OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>Conexões persistentes lembradas pelo Windows: letra → caminho remoto (<c>HKCU\Network\Z\RemotePath</c>).</summary>
    [SupportedOSPlatform("windows")]
    private static Dictionary<char, string> RememberedConnections()
    {
        var result = new Dictionary<char, string>();
        try
        {
            using var network = Registry.CurrentUser.OpenSubKey("Network");
            if (network is null) return result;
            foreach (var name in network.GetSubKeyNames())
            {
                if (name.Length != 1 || !char.IsAsciiLetter(name[0])) continue;
                using var key = network.OpenSubKey(name);
                if (key?.GetValue("RemotePath") is string remote) result[char.ToUpperInvariant(name[0])] = remote;
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
        }
        return result;
    }

    /// <summary>Estado da conexão da letra e o caminho remoto que o Windows associa a ela (sem contatar o servidor).</summary>
    [SupportedOSPlatform("windows")]
    private static unsafe (int Status, string? Remote) Connection(char letter)
    {
        const int capacity = 1024;
        var buffer = stackalloc char[capacity];
        var length = (uint)capacity;
        var local = $"{letter}:";
        int status;
        fixed (char* name = local) status = WNetGetConnectionW(name, buffer, &length);
        if (status is not (NoError or ErrorConnectionUnavailable)) return (status, null);
        var text = new ReadOnlySpan<char>(buffer, capacity);
        var end = text.IndexOf('\0');
        return (status, end > 0 ? new string(text[..end]) : null);
    }

    [LibraryImport("mpr.dll")]
    [SupportedOSPlatform("windows")]
    private static unsafe partial int WNetGetConnectionW(char* localName, char* remoteName, uint* length);
}
