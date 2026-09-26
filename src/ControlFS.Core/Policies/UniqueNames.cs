namespace ControlFS.Core.Policies;

/// <summary>Gera "nome (2).ext", "nome (3).ext"... para "manter ambos".</summary>
public static class UniqueNames
{
    public static string Next(string fileName, Func<string, bool> exists, bool isDirectory = false, int maxAttempts = 10_000)
    {
        if (!exists(fileName)) return fileName;
        var dot = isDirectory ? -1 : fileName.LastIndexOf('.');
        var stem = dot > 0 ? fileName[..dot] : fileName;
        var ext = dot > 0 ? fileName[dot..] : string.Empty;
        for (var i = 2; i <= maxAttempts; i++)
        {
            var candidate = $"{stem} ({i}){ext}";
            if (!exists(candidate)) return candidate;
        }
        throw new IOException("Não foi possível gerar um nome livre.");
    }
}
