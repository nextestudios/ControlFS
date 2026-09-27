namespace ControlFS.Infrastructure.Archives.Security;

/// <summary>
/// Propaga a marca de procedência (Zone.Identifier) do compactado para os arquivos extraídos,
/// como o Explorer faz. Somente Windows/NTFS; em outros sistemas ou filesystems sem ADS é no-op.
/// Não remove marcas existentes. NÃO VALIDADO em Windows neste incremento.
/// </summary>
internal static class MarkOfTheWeb
{
    private const string StreamSuffix = ":Zone.Identifier";
    private const int MaxZoneLength = 4096;

    /// <summary>Marca do compactado; num dividido, a do primeiro volume que tiver uma (qualquer parte baixada marca o todo).</summary>
    public static string? ReadForArchive(string archivePath)
    {
        var parts = Inspection.VolumeSet.Find(archivePath)?.Parts ?? [];
        return Read(archivePath) ?? parts.Select(Read).FirstOrDefault(z => z is not null);
    }

    public static string? Read(string archivePath)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var info = new FileInfo(archivePath + StreamSuffix);
            if (!info.Exists || info.Length > MaxZoneLength) return null;
            return File.ReadAllText(info.FullName);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (NotSupportedException) { return null; }
    }

    public static void Apply(string filePath, string? zoneContent)
    {
        if (zoneContent is null || !OperatingSystem.IsWindows()) return;
        try { File.WriteAllText(filePath + StreamSuffix, zoneContent); }
        catch (IOException) { /* filesystem sem ADS (ex.: FAT32): documentado */ }
        catch (UnauthorizedAccessException) { }
        catch (NotSupportedException) { }
    }
}
