using System.Runtime.Versioning;
using ControlFS.Core.Automation;
using Microsoft.Win32;

namespace ControlFS.Infrastructure.Windows.Shell;

/// <summary>
/// Registra o esquema de URL <c>controlfs://</c> no registro do usuário atual (HKCU).
/// Garante que links executem o executável atual mesmo em versões portáteis movidas de pasta.
/// </summary>
public static class ProtocolRegistration
{
    public const string Scheme = AppProtocol.Scheme;
    private const string ClassKey = @"Software\Classes\" + Scheme;

    /// <summary>
    /// Confere se o esquema está registrado e apontando para o executável atual.
    /// Se não estiver ou se apontar para outro caminho, atualiza o registro.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static void EnsureRegistered(Action<string>? log = null)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            var command = $"\"{exe}\" \"%1\"";

            using var existing = Registry.CurrentUser.OpenSubKey(ClassKey + @"\shell\open\command");
            if (existing?.GetValue(null) is string current && string.Equals(current, command, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            using var key = Registry.CurrentUser.CreateSubKey(ClassKey, writable: true);
            key.SetValue(null, "URL:ControlFS");
            key.SetValue("URL Protocol", string.Empty);
            using (var icon = key.CreateSubKey("DefaultIcon"))
            {
                icon.SetValue(null, $"\"{exe}\",0");
            }
            using (var open = key.CreateSubKey(@"shell\open\command"))
            {
                open.SetValue(null, command);
            }
            log?.Invoke($"Protocolo: {Scheme}:// registrado para {exe}");
        }
        catch (Exception ex)
        {
            log?.Invoke($"Protocolo: não foi possível registrar {Scheme}://: {ex.Message}");
        }
    }

    /// <summary>
    /// Verifica se a chave de protocolo aponta para o executável especificado (ou o atual se omitido).
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static bool IsRegistered(string? exePath = null)
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            exePath ??= Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return false;
            var expectedCommand = $"\"{exePath}\" \"%1\"";

            using var existing = Registry.CurrentUser.OpenSubKey(ClassKey + @"\shell\open\command");
            return existing?.GetValue(null) is string current &&
                   string.Equals(current, expectedCommand, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
