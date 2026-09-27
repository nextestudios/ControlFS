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
            if (entry.IsEncrypted) EncryptedCount++;
            if (entry.IsLinkOrSpecial) LinkCount++;
            var parent = components.Count == 1 ? string.Empty : string.Join('/', components.Take(components.Count - 1));
            ChildrenOf(parent).Add(new FileEntry(IdPrefix + path, components[^1], EntryKind.ArchiveFile, entry.Size, entry.Modified,
                IsEncrypted: entry.IsEncrypted,
                Detail: CompressionDetail(entry.Size, entry.CompressedSize),
                BlockedReason: entry.IsLinkOrSpecial ? "Link ou tipo especial: não será extraído." : null));
        }
    }

    public ArchiveInfo Info { get; }
    /// <summary>Entradas com nome recusado pela política de caminhos (aparecem na raiz, bloqueadas).</summary>
    public int BlockedCount { get; }
    public int FileCount { get; }
    public int EncryptedCount { get; }

    /// <summary>Links e tipos especiais: listados, mas bloqueados na extração.</summary>
    public int LinkCount { get; }

    /// <summary>
    /// Resumo para o cabeçalho do navegador: formato, arquivos, tamanho descompactado declarado e o que exige atenção
    /// (com senha, bloqueadas). Ex.: "ZIP · 12 arquivos · 3,4 MB · 2 com senha · 1 bloqueada".
    /// </summary>
    public string Summary
    {
        get
        {
            var parts = new List<string> { ArchiveFormats.DisplayName(Info.Format), FileCount == 1 ? "1 arquivo" : $"{FileCount} arquivos" };
            parts.Add(Info.DeclaredTotalSize is long total ? AppController.FormatBytes(total) : "tamanho desconhecido");
            if (EncryptedCount > 0) parts.Add($"{EncryptedCount} com senha");
            var blocked = BlockedCount + LinkCount;
            if (blocked > 0) parts.Add(blocked == 1 ? "1 bloqueada" : $"{blocked} bloqueadas");
            return string.Join(" · ", parts);
        }
    }

    /// <summary>"compactado: 540 KB (45%)": tamanho guardado e quanto ele representa do original.</summary>
    internal static string? CompressionDetail(long? size, long? compressed)
    {
        if (compressed is not long c) return null;
        var text = "compactado: " + AppController.FormatBytes(c);
        return size is long s && s > 0 ? $"{text} ({Math.Round(100.0 * c / s):0}%)" : text;
    }

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
