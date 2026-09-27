namespace ControlFS.Core.Models;

/// <summary>Um arquivo grande dentro de uma pasta analisada.</summary>
public sealed record DiskUsageFile(string Name, string FullPath, long Bytes);

/// <summary>
/// Uma pasta da análise de uso do disco (#72): totais recursivos (como <see cref="FolderSize"/>), as subpastas em ordem
/// de tamanho e os maiores arquivos que estão direto nela. Arquivos além de <see cref="MaxFiles"/> só entram nos totais
/// (<see cref="OtherFiles"/>, <see cref="OtherFilesBytes"/>).
/// </summary>
public sealed record DiskUsageNode(string Name, string FullPath, long Bytes, long Files, long Folders,
    IReadOnlyList<DiskUsageNode> Subfolders, IReadOnlyList<DiskUsageFile> LargestFiles, long OtherFiles, long OtherFilesBytes)
{
    /// <summary>Arquivos guardados por pasta, os maiores primeiro.</summary>
    public const int MaxFiles = 50;
}

/// <summary>
/// Resultado da análise: a árvore inteira, lida uma vez, para descer pelas pastas sem ler o disco de novo. Mesmas regras
/// do tamanho de pasta: junções e links nunca são seguidos (contam em <see cref="LinksNotFollowed"/>) e pastas que não
/// puderam ser lidas ficam em <see cref="Inaccessible"/>.
/// </summary>
public sealed record DiskUsage(DiskUsageNode Root, IReadOnlyList<string> Inaccessible, int LinksNotFollowed);
