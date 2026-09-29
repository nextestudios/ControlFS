using ControlFS.Core.Contracts;
using Microsoft.Win32;

namespace ControlFS.Infrastructure.Archives.Creation;

/// <summary>
/// Acha o <c>Rar.exe</c> do WinRAR instalado pelo usuário. Só olha lugares que o instalador do WinRAR usa (chave
/// <c>SOFTWARE\WinRAR</c> do registro e as pastas de Arquivos de Programas), nunca varre o disco nem o PATH, e só aceita
/// um arquivo chamado <c>Rar.exe</c> com assinatura Authenticode válida da win.rar GmbH.
/// </summary>
internal static class RarLocator
{
    public const string ExecutableName = "Rar.exe";
    public const string ExpectedSigner = "win.rar GmbH";
    public const string MissingReason = "Instale o WinRAR para criar RAR.";
    private static readonly object Gate = new();
    private static RarTool? _found;

    /// <summary>O primeiro Rar.exe aceito, ou null. Um achado válido é lembrado enquanto o arquivo existir.</summary>
    public static RarTool? Find()
    {
        lock (Gate)
        {
            if (_found is { } cached && File.Exists(cached.Path)) return cached;
            _found = null;
            if (!OperatingSystem.IsWindows()) return null;
            foreach (var candidate in Candidates())
            {
                if (!IsAcceptableName(candidate) || !File.Exists(candidate)) continue;
                if (!AuthenticodeVerifier.IsSignedBy(candidate, ExpectedSigner)) continue;
                return _found = new RarTool(candidate);
            }
            return null;
        }
    }

    public static CreationAvailability Availability() =>
        Find() is null ? new CreationAvailability(false, MissingReason) : CreationAvailability.Available;

    /// <summary>Caminho completo, local (não UNC), com o nome exato <c>Rar.exe</c>.</summary>
    public static bool IsAcceptableName(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        return string.Equals(Path.GetFileName(path), ExecutableName, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> Candidates()
    {
        foreach (var dir in RegistryInstallFolders()) yield return Path.Join(dir, ExecutableName);
        foreach (var special in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var root = Environment.GetFolderPath(special);
            if (root.Length > 0) yield return Path.Join(root, "WinRAR", ExecutableName);
        }
    }

    private static IEnumerable<string> RegistryInstallFolders()
    {
        if (!OperatingSystem.IsWindows()) yield break;
        foreach (var (hive, view) in new[] { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32), (RegistryHive.CurrentUser, RegistryView.Default) })
        {
            string? exePath = null;
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(@"SOFTWARE\WinRAR");
                exePath = key?.GetValue("exe64") as string ?? key?.GetValue("ExePath") as string;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
            if (string.IsNullOrWhiteSpace(exePath) || !Path.IsPathFullyQualified(exePath)) continue;
            var dir = Path.GetDirectoryName(exePath);
            if (!string.IsNullOrEmpty(dir)) yield return dir;
        }
    }
}
