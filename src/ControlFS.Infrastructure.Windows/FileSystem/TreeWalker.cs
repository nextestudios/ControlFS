namespace ControlFS.Infrastructure.Windows.FileSystem;

/// <summary>
/// Percurso de árvore compartilhado pela busca e pelo cálculo de tamanho: uma pasta por vez, em largura, sem
/// RecurseSubdirectories. A descida é decidida aqui (<see cref="Descends"/>), para nunca atravessar junções, links
/// simbólicos ou outros pontos de nova análise. Só lê nomes e metadados (nunca o conteúdo dos arquivos).
/// </summary>
internal static class TreeWalker
{
    /// <summary>Um item encontrado (<see cref="Info"/>) ou uma pasta que não pôde ser lida (<see cref="Info"/> nulo).</summary>
    internal readonly record struct Step(string Folder, FileSystemInfo? Info, FileAttributes Attributes)
    {
        public bool IsUnreadableFolder => Info is null;
        public bool IsDirectory => (Attributes & FileAttributes.Directory) != 0;
    }

    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };

    /// <summary>Pasta que o percurso atravessa: diretório comum, nunca um ponto de nova análise.</summary>
    public static bool Descends(FileAttributes attributes) =>
        (attributes & FileAttributes.Directory) != 0 && (attributes & FileAttributes.ReparsePoint) == 0;

    /// <param name="include">Filtro opcional: itens recusados não aparecem e, se forem pastas, não são percorridos.</param>
    public static IEnumerable<Step> Walk(string root, bool recurse, CancellationToken cancellationToken, Func<FileSystemInfo, FileAttributes, bool>? include = null)
    {
        var pending = new Queue<string>();
        pending.Enqueue(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folder = pending.Dequeue();
            IEnumerator<FileSystemInfo>? items = null;
            try { items = new DirectoryInfo(folder).EnumerateFileSystemInfos("*", Options).GetEnumerator(); }
            catch (Exception ex) when (IsUnreadable(ex)) { }
            if (items is null)
            {
                yield return new Step(folder, null, 0);
                continue;
            }
            var unreadable = false;
            using (items)
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    FileSystemInfo info;
                    FileAttributes attrs;
                    try
                    {
                        if (!items.MoveNext()) break;
                        info = items.Current;
                        attrs = info.Attributes;
                    }
                    catch (Exception ex) when (IsUnreadable(ex))
                    {
                        unreadable = true;
                        break;
                    }
                    if (include is not null && !include(info, attrs)) continue;
                    yield return new Step(folder, info, attrs);
                    if (recurse && Descends(attrs)) pending.Enqueue(info.FullName);
                }
            }
            if (unreadable) yield return new Step(folder, null, 0);
        }
    }

    private static bool IsUnreadable(Exception ex) => ex is UnauthorizedAccessException or IOException;
}
