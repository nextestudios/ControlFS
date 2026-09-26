using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Windows.FileSystem;

namespace ControlFS.UnitTests.Support;

/// <summary>
/// Provedor REAL de filesystem com locais iniciais restritos ao diretório temporário do teste
/// (nunca expõe pastas reais do usuário). Opcionalmente atrasa listagens para testar respostas tardias.
/// </summary>
public sealed class TestFileSystem(string root) : IFileSystemProvider
{
    private readonly LocalFileSystemProvider _real = new();

    public Dictionary<string, TimeSpan> Delays { get; } = new(StringComparer.Ordinal);

    public IReadOnlyList<FileEntry> GetPlaces() => [new FileEntry("place:" + root, "Pasta de teste", EntryKind.KnownFolder, FullPath: root)];

    public async Task<DirectoryListing> ListAsync(string path, bool includeHidden, CancellationToken cancellationToken)
    {
        if (Delays.TryGetValue(Path.GetFullPath(path), out var delay)) await Task.Delay(delay, CancellationToken.None);
        return await _real.ListAsync(path, includeHidden, cancellationToken);
    }

    public string? GetParent(string path) => _real.GetParent(path);

    public bool DirectoryExists(string path) => _real.DirectoryExists(path);

    public FileEntry CreateDirectory(string parentPath, string name) => _real.CreateDirectory(parentPath, name);
}
