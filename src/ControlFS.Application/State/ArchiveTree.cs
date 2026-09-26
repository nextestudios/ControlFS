using ControlFS.Core.Models;
using ControlFS.Core.Policies;

namespace ControlFS.Application.State;

/// <summary>
/// Visão hierárquica somente leitura de um compactado, montada a partir de nomes já sanitizados.
/// Entradas rejeitadas pela política aparecem na raiz como bloqueadas, com motivo, e não são navegáveis.
/// </summary>
public sealed class ArchiveTree
{
    public const string IdPrefix = "a:";
    private readonly Dictionary<string, List<FileEntry>> _children = new(StringComparer.Ordinal);

    public ArchiveTree(ArchiveInfo info, ExtractionLimits limits)
    {
        Info = info;
        _children[string.Empty] = [];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in info.Entries)
        {
            var sanitized = ArchivePathPolicy.Sanitize(entry.RawKey, limits.MaxDepth, limits.MaxRelativePathLength);
            if (!sanitized.IsAccepted)
            {
                BlockedCount++;
                var printable = new string(entry.RawKey.Select(c => char.IsControl(c) ? '�' : c).ToArray());
                _children[string.Empty].Add(new FileEntry($"blocked:{entry.Index}", printable, EntryKind.ArchiveFile, entry.Size,
                    entry.Modified, BlockedReason: sanitized.Message));
                continue;
            }
            var components = sanitized.Components;
            for (var i = 1; i < components.Count; i++) EnsureDirectory(components, i, seen);
            if (entry.IsDirectory)
            {
                EnsureDirectory(components, components.Count, seen);
                continue;
            }
            var path = sanitized.RelativePath;
            if (!seen.Add(path)) continue; // duplicata exata: o extrator reporta a colisão
            FileCount++;
            var parent = components.Count == 1 ? string.Empty : string.Join('/', components.Take(components.Count - 1));
            ChildrenOf(parent).Add(new FileEntry(IdPrefix + path, components[^1], EntryKind.ArchiveFile, entry.Size, entry.Modified,
                IsEncrypted: entry.IsEncrypted,
                Detail: entry.CompressedSize is long c ? $"compactado: {c:N0} B" : null,
                BlockedReason: entry.IsLinkOrSpecial ? "Link ou tipo especial: não será extraído." : null));
        }
    }

    public ArchiveInfo Info { get; }
    public int BlockedCount { get; }
    public int FileCount { get; }

    public bool DirectoryExists(string innerPath) => _children.ContainsKey(innerPath);

    public IReadOnlyList<FileEntry> Children(string innerPath) =>
        _children.TryGetValue(innerPath, out var list) ? list : [];

    public static string PathFromId(string id) => id.StartsWith(IdPrefix, StringComparison.Ordinal) ? id[IdPrefix.Length..].TrimEnd('/') : string.Empty;

    private void EnsureDirectory(IReadOnlyList<string> components, int count, HashSet<string> seen)
    {
        var path = string.Join('/', components.Take(count));
        if (!seen.Add(path + "/")) return;
        var parent = count == 1 ? string.Empty : string.Join('/', components.Take(count - 1));
        ChildrenOf(parent).Add(new FileEntry(IdPrefix + path + "/", components[count - 1], EntryKind.ArchiveDirectory));
        ChildrenOf(path);
    }

    private List<FileEntry> ChildrenOf(string path)
    {
        if (!_children.TryGetValue(path, out var list)) _children[path] = list = [];
        return list;
    }
}
