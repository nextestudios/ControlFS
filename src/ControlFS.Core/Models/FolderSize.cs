namespace ControlFS.Core.Models;

/// <summary>
/// Tamanho recursivo de uma pasta (soma dos tamanhos dos arquivos, como o Explorador mostra em "Tamanho"). Junções,
/// links simbólicos e outros pontos de nova análise não são seguidos: contam em <see cref="LinksNotFollowed"/>.
/// Pastas que não puderam ser lidas ficam em <see cref="Inaccessible"/>, nunca somem em silêncio.
/// </summary>
public sealed record FolderSize(long Bytes, long Files, long Folders, IReadOnlyList<string> Inaccessible, int LinksNotFollowed)
{
    public static FolderSize Empty { get; } = new(0, 0, 0, [], 0);
}
