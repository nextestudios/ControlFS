namespace ControlFS.UnitTests.Support;

/// <summary>Diretório temporário controlado, exclusivo do teste e removido ao final. Nunca usa dados reais.</summary>
public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Join(System.IO.Path.GetTempPath(), "controlfs-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Sub(params string[] parts) => System.IO.Path.Join([Path, .. parts]);

    public string MakeDir(params string[] parts)
    {
        var p = Sub(parts);
        Directory.CreateDirectory(p);
        return p;
    }

    /// <summary>Todos os caminhos (relativos) existentes sob a raiz, para provar que nada foi gravado fora do destino.</summary>
    public IReadOnlyList<string> Snapshot() =>
        Directory.EnumerateFileSystemEntries(Path, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 })
            .Select(p => System.IO.Path.GetRelativePath(Path, p).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
    }
}
