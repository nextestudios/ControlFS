namespace ControlFS.Core.Policies;

/// <summary>
/// Tipos que executam código ao serem abertos. Abrir um deles exige confirmação explícita, que começa em "Cancelar".
/// A lista é conservadora; a proteção do Windows (SmartScreen, Mark of the Web) continua valendo.
/// </summary>
public static class ExecutableFiles
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".scr", ".pif", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle", ".appinstaller", ".msp",
        ".bat", ".cmd", ".ps1", ".psm1", ".psd1", ".ps1xml", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta",
        ".cpl", ".msc", ".reg", ".inf", ".lnk", ".url", ".scf", ".application", ".appref-ms", ".jar", ".gadget",
        ".dll", ".sys", ".chm", ".py", ".pyw", ".sh",
    };

    public static bool IsPotentiallyExecutable(string path)
    {
        var name = Path.GetFileName(path).TrimEnd('.', ' ');
        return Extensions.Contains(Path.GetExtension(name));
    }
}
